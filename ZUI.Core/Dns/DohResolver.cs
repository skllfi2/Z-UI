// ═══════════════════════════════════════════════════════════════
// ZUI.Core / Dns / DohResolver.cs
// DNS-over-HTTPS (DoH) резолвер
// RFC 8484: HTTPS POST с application/dns-message
// Поддержка множественных DoH серверов с fallback
// ═══════════════════════════════════════════════════════════════

using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace ZUI.Core.Dns;

/// <summary>
/// DNS-over-HTTPS резолвер.
/// Отправляет DNS запросы по HTTPS (RFC 8484).
/// Множественные серверы с fallback, кэширование результатов.
/// </summary>
public sealed class DohResolver : IAsyncDisposable
{
    private readonly ILogger _logger;
    private readonly DnsCache _cache;
    private readonly HttpClient _httpClient;
    private readonly string[] _servers;
    private int _currentServerIndex;

    /// <summary>Стандартные DoH серверы (Google, Cloudflare, NextDNS).</summary>
    public static readonly string[] DefaultServers =
    [
        "https://dns.google/dns-query",
        "https://cloudflare-dns.com/dns-query",
        "https://dns.nextdns.io/dns-query",
    ];

    /// <summary>Текущий активный сервер.</summary>
    public string CurrentServer => _servers[Volatile.Read(ref _currentServerIndex) % _servers.Length];

    /// <summary>Количество успешных запросов.</summary>
    private int _successCount;

    /// <summary>Количество неудачных запросов.</summary>
    private int _failureCount;

    public long SuccessCount => Volatile.Read(ref _successCount);
    public long FailureCount => Volatile.Read(ref _failureCount);

    public DohResolver(
        DnsCache cache,
        string[]? servers = null,
        ILogger<DohResolver>? logger = null)
    {
        _cache = cache;
        _logger = logger ?? NullLoggerFactory.Instance.CreateLogger<DohResolver>();
        _servers = servers is { Length: > 0 } ? servers : DefaultServers;

        _httpClient = new HttpClient(new SocketsHttpHandler
        {
            ConnectTimeout = TimeSpan.FromSeconds(5),
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
        })
        {
            Timeout = TimeSpan.FromSeconds(10),
        };

        _httpClient.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/dns-message"));
    }

    // ── Резолвинг ──────────────────────────────────────────

    /// <summary>
    /// Резолвить домен через DoH. Сначала проверяет кэш.
    /// Пробует серверы по порядку при неудаче (fallback).
    /// </summary>
    public async Task<Result<IPAddress>> ResolveAsync(
        string domain,
        DnsRecordType type = DnsRecordType.A,
        CancellationToken ct = default)
    {
        // 1. Проверка кэша
        var cached = _cache.Get(domain, type);
        if (cached is not null)
            return Result<IPAddress>.Success(cached);

        // 2. Формирование DNS запроса
        var transactionId = (ushort)Random.Shared.Next(1, ushort.MaxValue);
        var query = DnsPacketBuilder.BuildQuery(domain, type, transactionId);

        // 3. Отправка DoH запроса с fallback
        Exception? lastError = null;
        int serverCount = _servers.Length;

        for (int attempt = 0; attempt < serverCount; attempt++)
        {
            var serverIdx = (Volatile.Read(ref _currentServerIndex) + attempt) % serverCount;
            var server = _servers[serverIdx];

            try
            {
                var result = await QueryDohServerAsync(server, query, ct).ConfigureAwait(false);
                if (result.IsSuccess && result.Value is not null)
                {
                    // Успех — переключаемся на этот сервер
                    Volatile.Write(ref _currentServerIndex, serverIdx);
                    Interlocked.Increment(ref _successCount);

                    // Кэшируем
                    _cache.Add(domain, type, result.Value, ttl: 300); // TTL 5 мин для DoH
                    return result;
                }

                    lastError = new InvalidOperationException(result.Error);
                }
                catch (HttpRequestException ex)
        {
            _logger.LogDebug(ex, "DoH query failed for server {Server}", server);
            lastError = ex;
        }
        catch (TaskCanceledException ex)
        {
            _logger.LogDebug(ex, "DoH query failed for server {Server}", server);
            lastError = ex;
        }
        }

        Interlocked.Increment(ref _failureCount);
        _logger.LogWarning("All DoH servers failed for {Domain}: {Error}", domain, lastError?.Message);
        return Result<IPAddress>.Failed($"DoH resolution failed for {domain}: {lastError?.Message}");
    }

    /// <summary>
    /// Резолвить домен с возвратом всех A + AAAA адресов.
    /// </summary>
    public async Task<Result<IPAddress[]>> ResolveAllAsync(
        string domain,
        CancellationToken ct = default)
    {
        var results = new List<IPAddress>();

        // Параллельный запрос A и AAAA
        var aTask = ResolveAsync(domain, DnsRecordType.A, ct);
        var aaaaTask = ResolveAsync(domain, DnsRecordType.AAAA, ct);

        await Task.WhenAll(aTask, aaaaTask).ConfigureAwait(false);

        if (aTask.Result.IsSuccess && aTask.Result.Value is not null)
            results.Add(aTask.Result.Value);

        if (aaaaTask.Result.IsSuccess && aaaaTask.Result.Value is not null)
            results.Add(aaaaTask.Result.Value);

        return results.Count > 0
            ? Result<IPAddress[]>.Success(results.ToArray())
            : Result<IPAddress[]>.Failed($"Failed to resolve {domain}");
    }

    // ── DoH запрос к конкретному серверу ───────────────────

    private async Task<Result<IPAddress>> QueryDohServerAsync(
        string serverUrl,
        byte[] query,
        CancellationToken ct)
    {
        // RFC 8484: HTTP POST с Content-Type: application/dns-message
        var content = new ByteArrayContent(query);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/dns-message");

        var response = await _httpClient.PostAsync(serverUrl, content, ct).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            return Result<IPAddress>.Failed(
                $"DoH server returned {response.StatusCode}");
        }

        var responseBody = await response.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);

        // Парсим DNS ответ
        var parseResult = DnsPacketBuilder.Parse(responseBody);
        if (!parseResult.IsSuccess)
            return Result<IPAddress>.Failed($"Failed to parse DoH response: {parseResult.Error}");

        var packet = parseResult.Value!;

        // Проверяем RCODE
        if (packet.Flags.ResponseCode != DnsResponseCode.NoError)
        {
            return Result<IPAddress>.Failed(
                $"DNS server returned RCODE={packet.Flags.ResponseCode}");
        }

        // Ищем ответ с IP адресом
        foreach (var answer in packet.Answers)
        {
            if (answer.Address is not null)
            {
                _logger.LogDebug("DoH resolved: {Domain} → {Address} ({Type})",
                    answer.Name, answer.Address, answer.Type);
                return Result<IPAddress>.Success(answer.Address);
            }
        }

        return Result<IPAddress>.Failed("No IP address in DNS response");
    }

    // ── Dispose ────────────────────────────────────────────

    public ValueTask DisposeAsync()
    {
        _httpClient.Dispose();
        return ValueTask.CompletedTask;
    }
}
