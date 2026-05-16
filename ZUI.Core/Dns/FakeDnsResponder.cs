// ═══════════════════════════════════════════════════════════════
// ZUI.Core / Dns / FakeDnsResponder.cs
// Поддельные DNS ответы для заблокированных доменов
// Возвращает реальный IP домена через DoH, минуя блокировки
// В отличие от ProxyManager (где SendFakeResponse — STUB),
// здесь формируется полный IP+UDP+DNS пакет
// ═══════════════════════════════════════════════════════════════

using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace ZUI.Core.Dns;

/// <summary>
/// Поддельный DNS резолвер.
/// Для заблокированных доменов возвращает реальный IP через DoH,
/// а не IP провайдерского заглушки.
/// Поддерживает списки доменов (hostlist) для определения,
/// какие домены нуждаются в подмене.
/// </summary>
public sealed class FakeDnsResponder : IAsyncDisposable
{
    private readonly ILogger _logger;
    private readonly DohResolver _dohResolver;
    private readonly DnsCache _cache;

    /// <summary>Домены, для которых применяется Fake DNS (реальный IP через DoH).</summary>
    private readonly ConcurrentDictionary<string, bool> _fakeDomains = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Домены, исключённые из Fake DNS.</summary>
    private readonly ConcurrentDictionary<string, bool> _excludeDomains = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Количество поддельных ответов.</summary>
    private int _fakeResponsesSent;

    public long FakeResponsesSent => Volatile.Read(ref _fakeResponsesSent);

    /// <summary>Включён ли Fake DNS.</summary>
    public bool IsEnabled { get; set; }

    public FakeDnsResponder(
        DohResolver dohResolver,
        DnsCache cache,
        ILogger<FakeDnsResponder>? logger = null)
    {
        _dohResolver = dohResolver;
        _cache = cache;
        _logger = logger ?? NullLoggerFactory.Instance.CreateLogger<FakeDnsResponder>();
    }

    // ── Управление списками доменов ────────────────────────

    /// <summary>
    /// Загрузить список доменов для подмены DNS.
    /// </summary>
    public async Task<Result> LoadFakeDomainListAsync(
        string filePath,
        CancellationToken ct = default)
    {
        if (!File.Exists(filePath))
            return Result.Failed($"Domain list not found: {filePath}");

        try
        {
            var lines = await File.ReadAllLinesAsync(filePath, ct).ConfigureAwait(false);
            int added = 0;

            foreach (var line in lines)
            {
                var domain = line.Trim();
                if (string.IsNullOrEmpty(domain) || domain.StartsWith('#'))
                    continue;

                // Поддержка формата: domain.com или .domain.com (wildcard)
                var d = domain.StartsWith('.') ? domain[1..] : domain;
                _fakeDomains[d] = true;
                added++;
            }

            _logger.LogInformation("Loaded {Count} fake DNS domains from {File}", added, filePath);
            return Result.Success();
        }
        catch (IOException ex)
        {
            return Result.Failed($"Failed to load fake domain list: {ex.Message}");
        }
        catch (UnauthorizedAccessException ex)
        {
            return Result.Failed($"Failed to load fake domain list: {ex.Message}");
        }
    }

    /// <summary>
    /// Загрузить список исключений (не подменять DNS для этих доменов).
    /// </summary>
    public async Task<Result> LoadExcludeDomainListAsync(
        string filePath,
        CancellationToken ct = default)
    {
        if (!File.Exists(filePath))
            return Result.Failed($"Exclude list not found: {filePath}");

        try
        {
            var lines = await File.ReadAllLinesAsync(filePath, ct).ConfigureAwait(false);
            int added = 0;

            foreach (var line in lines)
            {
                var domain = line.Trim();
                if (string.IsNullOrEmpty(domain) || domain.StartsWith('#'))
                    continue;

                _excludeDomains[domain] = true;
                added++;
            }

            _logger.LogInformation("Loaded {Count} exclude DNS domains from {File}", added, filePath);
            return Result.Success();
        }
        catch (IOException ex)
        {
            return Result.Failed($"Failed to load exclude domain list: {ex.Message}");
        }
        catch (UnauthorizedAccessException ex)
        {
            return Result.Failed($"Failed to load exclude domain list: {ex.Message}");
        }
    }

    // ── Проверка домена ────────────────────────────────────

    /// <summary>
    /// Нужно ли подменять DNS ответ для этого домена?
    /// True если домен в списке подмены и не в исключениях.
    /// </summary>
    public bool ShouldFakeDns(string domain)
    {
        if (!IsEnabled)
            return false;

        if (_excludeDomains.ContainsKey(domain))
            return false;

        // Точное совпадение
        if (_fakeDomains.ContainsKey(domain))
            return true;

        // Wildcard: проверяем родительские домены
        // ".google.com" в списке → "www.google.com" тоже подменяется
        var parts = domain.Split('.');
        for (int i = 1; i < parts.Length; i++)
        {
            var parent = string.Join('.', parts[i..]);
            if (_fakeDomains.ContainsKey(parent))
                return true;
        }

        return false;
    }

    // ── Формирование поддельного DNS ответа ────────────────

    /// <summary>
    /// Резолвить домен через DoH и построить DNS ответ.
    /// Возвращает полный DNS ответ (bytes), готовый к отправке,
    /// или null если не удалось резолвить.
    /// </summary>
    public async Task<byte[]?> BuildFakeResponseAsync(
        byte[] originalQuery,
        CancellationToken ct = default)
    {
        // 1. Извлечь домен из запроса
        var domainResult = DnsPacketBuilder.ExtractDomainFromQuery(originalQuery);
        if (!domainResult.IsSuccess)
        {
            _logger.LogDebug("Cannot extract domain from DNS query: {Error}", domainResult.Error);
            return null;
        }

        var domain = domainResult.Value!;
        var transactionId = DnsPacketBuilder.GetTransactionId(originalQuery);

        // 2. Определить тип записи из запроса
        var type = ExtractQueryType(originalQuery);

        // 3. Резолвить через DoH
        var resolveResult = await _dohResolver.ResolveAsync(domain, type, ct).ConfigureAwait(false);
        if (!resolveResult.IsSuccess || resolveResult.Value is null)
        {
            _logger.LogDebug("DoH resolution failed for {Domain}: {Error}", domain, resolveResult.Error);
            return null;
        }

        var ip = resolveResult.Value;

        // 4. Построить DNS ответ
        byte[] response;
        if (ip.AddressFamily == AddressFamily.InterNetwork)
        {
            response = DnsPacketBuilder.BuildAResponse(transactionId, domain, ip, ttl: 300);
        }
        else if (ip.AddressFamily == AddressFamily.InterNetworkV6)
        {
            response = DnsPacketBuilder.BuildAaaaResponse(transactionId, domain, ip, ttl: 300);
        }
        else
        {
            _logger.LogDebug("Unsupported address family for {Domain}: {Family}", domain, ip.AddressFamily);
            return null;
        }

        Interlocked.Increment(ref _fakeResponsesSent);
        _logger.LogDebug("Fake DNS: {Domain} → {Address}", domain, ip);

        return response;
    }

    /// <summary>
    /// Резолвить домен через DoH без исходного запроса (для построения
    /// полного UDP пакета для инъекции через WinDivert).
    /// </summary>
    public async Task<Result<byte[]>> BuildFakeUdpPacketAsync(
        string domain,
        IPEndPoint srcEp,
        IPEndPoint dstEp,
        CancellationToken ct = default)
    {
        var resolveResult = await _dohResolver.ResolveAsync(domain, DnsRecordType.A, ct).ConfigureAwait(false);
        if (!resolveResult.IsSuccess || resolveResult.Value is null)
            return Result<byte[]>.Failed($"Cannot resolve {domain}: {resolveResult.Error}");

        var ip = resolveResult.Value;
        var transactionId = (ushort)Random.Shared.Next(1, ushort.MaxValue);
        var dnsPayload = DnsPacketBuilder.BuildAResponse(transactionId, domain, ip, ttl: 300);

        // Формируем полный UDP пакет: IP + UDP + DNS
        var udpPacket = BuildUdpDnsPacket(dnsPayload, srcEp, dstEp);
        return Result<byte[]>.Success(udpPacket);
    }

    // ── Вспомогательные ────────────────────────────────────

    /// <summary>
    /// Извлечь тип записи из DNS запроса (после QNAME).
    /// </summary>
    private static DnsRecordType ExtractQueryType(byte[] query)
    {
        if (query.Length < 14)
            return DnsRecordType.A;

        // Пропускаем заголовок (12) + QNAME (variable length)
        int pos = 12;
        while (pos < query.Length)
        {
            byte len = query[pos];
            if (len == 0)
            {
                pos++; // Корневой label
                break;
            }
            pos += 1 + len;
        }

        if (pos + 2 <= query.Length)
        {
            var qtype = (ushort)((query[pos] << 8) | query[pos + 1]);
            return (DnsRecordType)qtype;
        }

        return DnsRecordType.A;
    }

    /// <summary>
    /// Построить полный UDP пакет (IPv4 + UDP + DNS payload).
    /// для инъекции через WinDivert.
    /// </summary>
    private static byte[] BuildUdpDnsPacket(byte[] dnsPayload, IPEndPoint srcEp, IPEndPoint dstEp)
    {
        // UDP header: 8 байт
        var udpLength = (ushort)(8 + dnsPayload.Length);

        // IPv4 header: 20 байт (без опций)
        var totalLength = (ushort)(20 + udpLength);

        using var ms = new MemoryStream();
        var writer = new BinaryWriter(ms);

        // IPv4 header
        writer.Write((byte)(0x45));      // Version=4, IHL=5
        writer.Write((byte)0);           // DSCP/ECN
        writer.WriteBe(totalLength);     // Total Length
        writer.WriteBe((ushort)0);       // Identification
        writer.WriteBe((ushort)0x4000);  // Flags: Don't Fragment
        writer.Write((byte)64);          // TTL
        writer.Write((byte)17);          // Protocol: UDP
        writer.WriteBe((ushort)0);       // Header Checksum (0 = let WinDivert calc)
        writer.Write(srcEp.Address.GetAddressBytes()); // Source IP
        writer.Write(dstEp.Address.GetAddressBytes()); // Destination IP

        // UDP header
        writer.WriteBe((ushort)srcEp.Port);   // Source Port
        writer.WriteBe((ushort)dstEp.Port);   // Destination Port
        writer.WriteBe(udpLength);            // UDP Length
        writer.WriteBe((ushort)0);            // UDP Checksum (0 = let WinDivert calc)

        // DNS payload
        writer.Write(dnsPayload);

        return ms.ToArray();
    }

    // ── Dispose ────────────────────────────────────────────

    public ValueTask DisposeAsync()
    {
        _logger.LogInformation("FakeDnsResponder disposed ({Count} fake responses sent)", _fakeResponsesSent);
        return ValueTask.CompletedTask;
    }
}

// ── BinaryWriter Big-Endian расширения (локальные) ─────────

file static class BinaryWriterExtensions
{
    public static void WriteBe(this BinaryWriter writer, ushort value)
    {
        writer.Write((byte)(value >> 8));
        writer.Write((byte)(value & 0xFF));
    }
}
