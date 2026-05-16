// ═══════════════════════════════════════════════════════════════
// ZUI.Core / Intercept / DnsSniffer.cs
// WinDivert DNS-сниффер: перехват DNS-ответов (UDP 53)
// Извлекает домены и IP из DNS-ответов, заполняет DnsCache и DnsReverseCache
// ═══════════════════════════════════════════════════════════════

using System.Net;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ZUI.Core.Dns;
using ZUI.Core.WinDivert;

namespace ZUI.Core.Intercept;

/// <summary>
/// WinDivert DNS-сниффер.
/// 
/// Перехватывает DNS-ответы (UDP порт 53) через отдельный WinDivert фильтр,
/// парсит DNS-пакеты и извлекает A/AAAA записи.
/// 
/// Результаты:
/// - DnsCache: domain → IP (для DNS proxy)
/// - DnsReverseCache: IP → domain (для доменной маршрутизации в Proxifier)
/// 
/// Работает в фоновом потоке, запускается/останавливается через StartAsync/StopAsync.
/// </summary>
public sealed class DnsSniffer : IAsyncDisposable
{
    private const string DnsFilter = "outbound and udp.DstPort == 53";
    private const int MaxPacketSize = 4096;
    private const int BufferSize = 65536;

    private readonly ILogger _logger;
    private readonly DnsCache _dnsCache;
    private readonly DnsReverseCache _reverseCache;
    private SafeWinDivertHandle? _handle;

    private CancellationTokenSource? _cts;
    private Task? _sniffTask;
    private int _isRunning;
    private long _packetsSniffed;
    private long _recordsExtracted;

    public bool IsRunning => Volatile.Read(ref _isRunning) == 1;
    public long PacketsSniffed => Volatile.Read(ref _packetsSniffed);
    public long RecordsExtracted => Volatile.Read(ref _recordsExtracted);

    public DnsSniffer(
        DnsCache dnsCache,
        DnsReverseCache reverseCache,
        ILogger<DnsSniffer>? logger = null)
    {
        _dnsCache = dnsCache ?? throw new ArgumentNullException(nameof(dnsCache));
        _reverseCache = reverseCache ?? throw new ArgumentNullException(nameof(reverseCache));
        _logger = logger ?? NullLoggerFactory.Instance.CreateLogger<DnsSniffer>();
    }

    // ── Запуск ─────────────────────────────────────────────

    /// <summary>
    /// Запустить DNS-сниффер. Открывает WinDivert handle и начинает перехват.
    /// </summary>
    public Result Start(CancellationToken ct = default)
    {
        if (Interlocked.CompareExchange(ref _isRunning, 1, 0) != 0)
            return Result.Failed("DNS sniffer is already running.");

        // Открыть WinDivert для перехвата DNS-ответов
        var handle = WinDivertNative.WinDivertOpen(
            DnsFilter,
            WinDivertLayer.Network,
            0,
            0);

        if (handle == WinDivertNative.InvalidHandleValue)
        {
            var error = Marshal.GetLastWin32Error();
            _logger.LogError("Failed to open WinDivert for DNS sniffer: error {Error}", error);
            Volatile.Write(ref _isRunning, 0);
            return Result.Failed($"WinDivertOpen failed: error {error}");
        }

        _handle = new SafeWinDivertHandle(handle);

        // Установить параметры
        WinDivertNative.WinDivertSetParam(handle, WinDivertParam.QueueLength, 2048);
        WinDivertNative.WinDivertSetParam(handle, WinDivertParam.QueueTime, 100);

        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _sniffTask = Task.Run(() => SniffLoop(_cts.Token), _cts.Token);

        _logger.LogInformation("DNS sniffer started (filter: {Filter})", DnsFilter);
        return Result.Success();
    }

    // ── Остановка ──────────────────────────────────────────

    public async ValueTask StopAsync()
    {
        if (Interlocked.CompareExchange(ref _isRunning, 0, 1) != 1)
            return;

        _cts?.Cancel();

        if (_sniffTask is not null)
        {
            try { await _sniffTask.ConfigureAwait(false); }
            catch (OperationCanceledException) { }
            catch (ObjectDisposedException) { }
        }

        _handle?.Dispose();
        _logger.LogInformation("DNS sniffer stopped (sniffed {Packets}, extracted {Records})",
            Volatile.Read(ref _packetsSniffed), Volatile.Read(ref _recordsExtracted));
    }

    // ── Цикл перехвата ─────────────────────────────────────

    private void SniffLoop(CancellationToken ct)
    {
        var packet = new byte[MaxPacketSize];

        while (!ct.IsCancellationRequested)
        {
            try
            {
                var success = WinDivertNative.WinDivertRecv(
                    _handle!.DangerousGetHandle(),
                    packet,
                    (uint)packet.Length,
                    out var recvLen,
                    out var addr);

                if (!success)
                {
                    var error = Marshal.GetLastWin32Error();
                    if (error == 995) break; // Operation aborted
                    if (error == 109) break; // Pipe broken
                    _logger.LogDebug("WinDivertRecv error: {Error}", error);
                    continue;
                }

                Interlocked.Increment(ref _packetsSniffed);

                // Парсим DNS-ответ
                ParseDnsResponse(packet, (int)recvLen);

                // Re-inject packet (DNS responses should pass through)
                WinDivertNative.WinDivertSend(
                    _handle.DangerousGetHandle(),
                    packet,
                    recvLen,
                    out _,
                    ref addr);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex) when (ex is ObjectDisposedException or InvalidOperationException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "DNS sniffer loop error");
            }
        }
    }

    // ── Парсинг DNS-ответа ─────────────────────────────────

    private void ParseDnsResponse(byte[] packet, int length)
    {
        // Минимальный размер: IP header (20) + UDP header (8) + DNS header (12)
        if (length < 40) return;

        // Пропускаем IP header (20 bytes для IPv4)
        var ipHeaderLen = (packet[0] & 0x0F) * 4;
        if (ipHeaderLen < 20 || length < ipHeaderLen + 8 + 12) return;

        // Пропускаем UDP header (8 bytes)
        var dnsOffset = ipHeaderLen + 8;
        var dnsLength = length - dnsOffset;

        if (dnsLength < 12) return;

        // Парсим DNS header
        var transactionId = ReadUInt16(packet, dnsOffset);
        var flags = ReadUInt16(packet, dnsOffset + 2);
        var questionCount = ReadUInt16(packet, dnsOffset + 4);
        var answerCount = ReadUInt16(packet, dnsOffset + 6);
        var authorityCount = ReadUInt16(packet, dnsOffset + 8);
        var additionalCount = ReadUInt16(packet, dnsOffset + 10);

        // Проверяем что это ответ (QR bit = 1)
        if ((flags & 0x8000) == 0) return;

        // Проверяем что нет ошибок (RCODE = 0)
        if ((flags & 0x000F) != 0) return;

        if (answerCount == 0) return;

        // Парсим вопросы чтобы найти домен
        var offset = dnsOffset + 12;
        var domain = ReadDomainName(packet, offset, out var questionLen);
        if (string.IsNullOrEmpty(domain)) return;

        offset += questionLen;

        // Пропускаем остальные вопросы
        for (int i = 1; i < questionCount; i++)
        {
            _ = ReadDomainName(packet, offset, out var qLen);
            offset += qLen + 4; // domain + QTYPE + QCLASS
        }

        // Парсим ответы
        int recordsFound = 0;
        for (int i = 0; i < answerCount && offset < length - 10; i++)
        {
            // Пропускаем имя (может быть pointer)
            var nameLen = SkipDomainName(packet, offset);
            if (nameLen == 0) break;
            offset += nameLen;

            if (offset + 10 > length) break;

            var type = ReadUInt16(packet, offset);
            var rdataLength = ReadUInt16(packet, offset + 8);
            offset += 10;

            if (offset + rdataLength > length) break;

            // A record (type 1)
            if (type == 1 && rdataLength == 4)
            {
                var ip = new IPAddress(packet.AsSpan(offset, 4));
                var ttl = ReadUInt32(packet, offset - 6);

                _dnsCache.Add(domain, DnsRecordType.A, ip, ttl);
                _reverseCache.Add(ip, domain, ttl, "A", priority: 0);

                recordsFound++;
            }
            // AAAA record (type 28)
            else if (type == 28 && rdataLength == 16)
            {
                var ip = new IPAddress(packet.AsSpan(offset, 16));
                var ttl = ReadUInt32(packet, offset - 6);

                _dnsCache.Add(domain, DnsRecordType.AAAA, ip, ttl);
                _reverseCache.Add(ip, domain, ttl, "AAAA", priority: 0);

                recordsFound++;
            }

            offset += rdataLength;
        }

        if (recordsFound > 0)
        {
            Interlocked.Add(ref _recordsExtracted, recordsFound);
            _logger.LogDebug("DNS sniffer: {Domain} → {Records} records", domain, recordsFound);
        }
    }

    // ── DNS packet parsing helpers ─────────────────────────

    /// <summary>
    /// Прочитать доменное имя из DNS пакета.
    /// Поддерживает label sequence и DNS pointers (0xC0xx).
    /// </summary>
    private static string ReadDomainName(byte[] packet, int offset, out int totalLength)
    {
        var domain = new System.Text.StringBuilder();
        totalLength = 0;
        var jumped = false;
        var jumpOffset = 0;
        var maxJumps = 10;
        var jumps = 0;

        while (offset < packet.Length)
        {
            var labelLen = packet[offset];

            if (labelLen == 0)
            {
                totalLength = jumped ? jumpOffset + 2 : totalLength + 1;
                break;
            }

            // DNS pointer (compression)
            if ((labelLen & 0xC0) == 0xC0)
            {
                if (offset + 1 >= packet.Length) break;
                if (!jumped)
                {
                    jumpOffset = totalLength + 2;
                    jumped = true;
                }
                jumps++;
                if (jumps > maxJumps) break;

                offset = ((labelLen & 0x3F) << 8) | packet[offset + 1];
                continue;
            }

            // Regular label
            offset++;
            totalLength++;

            if (offset + labelLen > packet.Length) break;

            if (domain.Length > 0)
                domain.Append('.');

            domain.Append(System.Text.Encoding.ASCII.GetString(packet, offset, labelLen));
            offset += labelLen;
            totalLength += labelLen;
        }

        return domain.ToString();
    }

    /// <summary>
    /// Пропустить доменное имя (для навигации по пакету).
    /// Возвращает количество байт, которые нужно пропустить.
    /// </summary>
    private static int SkipDomainName(byte[] packet, int offset)
    {
        var start = offset;
        var maxJumps = 10;
        var jumps = 0;

        while (offset < packet.Length)
        {
            var labelLen = packet[offset];

            if (labelLen == 0)
                return offset - start + 1;

            // DNS pointer
            if ((labelLen & 0xC0) == 0xC0)
            {
                jumps++;
                if (jumps > maxJumps) return 0;
                return offset - start + 2;
            }

            offset++;
            if (offset + labelLen > packet.Length) return 0;
            offset += labelLen;
        }

        return 0;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ushort ReadUInt16(byte[] buffer, int offset)
    {
        return (ushort)((buffer[offset] << 8) | buffer[offset + 1]);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static uint ReadUInt32(byte[] buffer, int offset)
    {
        return (uint)((buffer[offset] << 24) | (buffer[offset + 1] << 16) |
                      (buffer[offset + 2] << 8) | buffer[offset + 3]);
    }

    // ── Dispose ────────────────────────────────────────────

    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
        _cts?.Dispose();
    }
}
