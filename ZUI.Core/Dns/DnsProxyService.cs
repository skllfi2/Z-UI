// ═══════════════════════════════════════════════════════════════
// ZUI.Core / Dns / DnsProxyService.cs
// UDP DNS прокси: слушает 127.0.0.1:53, маршрутизирует запросы
// через DoH (заблокированные домены), обычный DNS (остальные),
// или Fake DNS (подмена ответов для заблокированных доменов)
//
// В отличие от ProxyManager DnsServices (где FakeDnsServer — STUB),
// здесь полная реализация: UDP listener → маршрутизация → ответ
// ═══════════════════════════════════════════════════════════════

using System.IO;
using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace ZUI.Core.Dns;

/// <summary>
/// Конфигурация DNS прокси.
/// </summary>
public sealed class DnsProxyConfig
{
    /// <summary>IP адрес для прослушивания (обычно 127.0.0.1).</summary>
    public IPAddress ListenAddress { get; init; } = IPAddress.Loopback;

    /// <summary>Порт для прослушивания (обычно 53).</summary>
    public int ListenPort { get; init; } = 53;

    /// <summary>Апстрим DNS сервер для обычных запросов.</summary>
    public IPEndPoint UpstreamDns { get; init; } = new(IPAddress.Parse("8.8.8.8"), 53);

    /// <summary>Включить DNS-over-HTTPS для заблокированных доменов.</summary>
    public bool EnableDoh { get; init; }

    /// <summary>Включить подмену DNS ответов (Fake DNS).</summary>
    public bool EnableFakeDns { get; init; }

    /// <summary>Таймаут ожидания ответа от апстрима (мс).</summary>
    public int UpstreamTimeoutMs { get; init; } = 5000;

    /// <summary>Размер буфера UDP (макс размер DNS пакета по UDP = 512 байт).</summary>
    public int UdpBufferSize { get; init; } = 4096;
}

/// <summary>
/// UDP DNS прокси сервис.
/// 
/// Маршрутизация запросов:
/// 1. Если EnableFakeDns и домен в списке подмены → FakeDnsResponder (DoH + реальный IP)
/// 2. Если EnableDoh → DoH резолвинг для всех запросов
/// 3. Иначе → прозрачное проксирование на апстрим DNS
/// 
/// Работает как UDP сервер на 127.0.0.1:53.
/// </summary>
public sealed class DnsProxyService : IAsyncDisposable
{
    private readonly ILogger _logger;
    private readonly DnsProxyConfig _config;
    private readonly DohResolver _dohResolver;
    private readonly FakeDnsResponder _fakeDnsResponder;
    private readonly DnsCache _cache;

    /// <summary>UDP сокет для приёма DNS запросов.</summary>
    private Socket? _listenSocket;

    /// <summary>UDP сокет для отправки запросов на апстрим.</summary>
    private Socket? _upstreamSocket;

    /// <summary>CancellationTokenSource для остановки.</summary>
    private CancellationTokenSource? _cts;

    /// <summary>Задача приёмника.</summary>
    private Task? _receiveTask;

    /// <summary>Сервис запущен?</summary>
    private int _isRunning;

    public bool IsRunning => Volatile.Read(ref _isRunning) == 1;

    // ── Статистика ────────────────────────────────────────

    private int _queriesReceived;
    private int _queriesForwarded;
    private int _queriesDoh;
    private int _queriesFakeDns;
    private int _queriesFailed;

    public long QueriesReceived => Volatile.Read(ref _queriesReceived);
    public long QueriesForwarded => Volatile.Read(ref _queriesForwarded);
    public long QueriesDoh => Volatile.Read(ref _queriesDoh);
    public long QueriesFakeDns => Volatile.Read(ref _queriesFakeDns);
    public long QueriesFailed => Volatile.Read(ref _queriesFailed);

    public DnsProxyService(
        DnsProxyConfig config,
        DohResolver dohResolver,
        FakeDnsResponder fakeDnsResponder,
        DnsCache cache,
        ILogger<DnsProxyService>? logger = null)
    {
        _config = config;
        _dohResolver = dohResolver;
        _fakeDnsResponder = fakeDnsResponder;
        _cache = cache;
        _logger = logger ?? NullLoggerFactory.Instance.CreateLogger<DnsProxyService>();
    }

    // ── Запуск / Остановка ────────────────────────────────

    /// <summary>
    /// Запустить DNS прокси: привязать UDP сокет к 127.0.0.1:53.
    /// Требует администраторских прав для порта 53.
    /// </summary>
    public Task<Result> StartAsync(CancellationToken ct = default)
    {
        if (Interlocked.CompareExchange(ref _isRunning, 1, 0) == 1)
            return Task.FromResult(Result.Failed("DNS proxy is already running."));

        try
        {
            _cts = new CancellationTokenSource();

            // 1. Создать UDP сокет для приёма
            _listenSocket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp)
            {
                ExclusiveAddressUse = true,
                ReceiveBufferSize = _config.UdpBufferSize * 64,
                SendBufferSize = _config.UdpBufferSize * 64,
            };

            var listenEp = new IPEndPoint(_config.ListenAddress, _config.ListenPort);
            _listenSocket.Bind(listenEp);

            // 2. Создать UDP сокет для отправки на апстрим
            _upstreamSocket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp)
            {
                ReceiveTimeout = _config.UpstreamTimeoutMs,
                SendBufferSize = _config.UdpBufferSize,
                ReceiveBufferSize = _config.UdpBufferSize,
            };

            // 3. Включить FakeDns если настроен
            _fakeDnsResponder.IsEnabled = _config.EnableFakeDns;

            // 4. Запустить приёмник
            _receiveTask = ReceiveLoopAsync(_cts.Token);

            _logger.LogInformation(
                "DNS proxy started on {Address}:{Port} (DoH={DoH}, FakeDns={FakeDns})",
                _config.ListenAddress, _config.ListenPort, _config.EnableDoh, _config.EnableFakeDns);

            return Task.FromResult(Result.Success());
        }
        catch (SocketException ex) when (ex.SocketErrorCode == SocketError.AddressAlreadyInUse)
        {
            Interlocked.Exchange(ref _isRunning, 0);
            return Task.FromResult(Result.Failed(
                $"Port {_config.ListenPort} is already in use. Another DNS server may be running. Error: {ex.Message}"));
        }
        catch (SocketException ex)
        {
            Interlocked.Exchange(ref _isRunning, 0);
            CleanupSockets();
            return Task.FromResult(Result.Failed($"Failed to start DNS proxy: {ex.Message}"));
        }
        catch (IOException ex)
        {
            Interlocked.Exchange(ref _isRunning, 0);
            CleanupSockets();
            return Task.FromResult(Result.Failed($"Failed to start DNS proxy: {ex.Message}"));
        }
    }

    /// <summary>
    /// Остановить DNS прокси.
    /// </summary>
    public async Task StopAsync()
    {
        if (Interlocked.CompareExchange(ref _isRunning, 0, 1) == 0)
            return; // Уже остановлен

        _logger.LogInformation("Stopping DNS proxy...");

        _cts?.Cancel();

        if (_receiveTask is not null)
        {
            try
            {
                await _receiveTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Ожидаемо
            }
            catch (SocketException ex)
            {
                _logger.LogDebug(ex, "DNS proxy receive loop ended with error");
            }
            catch (IOException ex)
            {
                _logger.LogDebug(ex, "DNS proxy receive loop ended with error");
            }
        }

        CleanupSockets();
        _logger.LogInformation("DNS proxy stopped. Stats: {Received} received, {Forwarded} forwarded, {Doh} DoH, {Fake} Fake, {Failed} failed",
            QueriesReceived, QueriesForwarded, QueriesDoh, QueriesFakeDns, QueriesFailed);
    }

    /// <summary>
    /// Обновить конфигурацию DNS прокси на лету.
    /// </summary>
    public void UpdateConfig(bool enableDoh, bool enableFakeDns)
    {
        _fakeDnsResponder.IsEnabled = enableFakeDns;
        _logger.LogInformation("DNS proxy config updated: DoH={DoH}, FakeDns={FakeDns}", enableDoh, enableFakeDns);
    }

    // ── Основной цикл приёма ──────────────────────────────

    private async Task ReceiveLoopAsync(CancellationToken ct)
    {
        var buffer = new byte[_config.UdpBufferSize];
        var endpoint = new IPEndPoint(IPAddress.Any, 0);

        _logger.LogDebug("DNS proxy receive loop started");

        while (!ct.IsCancellationRequested)
        {
            try
            {
                // Приём UDP пакета
                int bytesReceived;
                try
                {
                    var recvResult = await _listenSocket!.ReceiveFromAsync(buffer, SocketFlags.None, endpoint, ct).ConfigureAwait(false);
                    bytesReceived = recvResult.ReceivedBytes;
                }
                catch (SocketException ex) when (ex.SocketErrorCode == SocketError.OperationAborted)
                {
                    break; // Сокет закрыт — выход
                }

                if (bytesReceived <= 0)
                    continue;

                Interlocked.Increment(ref _queriesReceived);

                // Копировать данные (buffer переиспользуется)
                var queryData = buffer[..bytesReceived];
                var clientEndpoint = (IPEndPoint)endpoint;

                // Проверить, что это DNS запрос
                if (!DnsPacketBuilder.LooksLikeDnsQuery(queryData))
                {
                    _logger.LogDebug("Received non-DNS UDP packet from {Endpoint}", clientEndpoint);
                    continue;
                }

                // Обработать запрос (fire-and-forget с error handling)
                _ = HandleQueryAsync(queryData.ToArray(), clientEndpoint, ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (SocketException ex)
            {
                _logger.LogDebug(ex, "Error in DNS proxy receive loop");
            }
            catch (IOException ex)
            {
                _logger.LogDebug(ex, "Error in DNS proxy receive loop");
            }
        }

        _logger.LogDebug("DNS proxy receive loop ended");
    }

    // ── Маршрутизация запроса ─────────────────────────────

    private async Task HandleQueryAsync(
        byte[] queryData,
        IPEndPoint clientEndpoint,
        CancellationToken ct)
    {
        try
        {
            // 1. Извлечь домен из запроса
            var domainResult = DnsPacketBuilder.ExtractDomainFromQuery(queryData);
            if (!domainResult.IsSuccess)
            {
                _logger.LogDebug("Cannot extract domain from DNS query: {Error}", domainResult.Error);
                await ForwardToUpstreamAsync(queryData, clientEndpoint, ct).ConfigureAwait(false);
                return;
            }

            var domain = domainResult.Value!;

            // 2. Определить тип записи
            var queryType = ExtractQueryType(queryData);
            var transactionId = DnsPacketBuilder.GetTransactionId(queryData);

            // 3. Маршрутизация
            byte[]? response = null;

            // Приоритет 1: Fake DNS (если домен в списке подмены)
            if (_config.EnableFakeDns && _fakeDnsResponder.ShouldFakeDns(domain))
            {
                Interlocked.Increment(ref _queriesFakeDns);
                response = await _fakeDnsResponder.BuildFakeResponseAsync(queryData, ct).ConfigureAwait(false);

                if (response is not null)
                {
                    _logger.LogDebug("Fake DNS: {Domain} → real IP via DoH", domain);
                }
                else
                {
                    _logger.LogDebug("Fake DNS failed for {Domain}, falling back to DoH", domain);
                }
            }

            // Приоритет 2: DoH (если включён или Fake DNS не сработал)
            if (response is null && _config.EnableDoh)
            {
                Interlocked.Increment(ref _queriesDoh);
                response = await ResolveViaDohAsync(domain, queryType, transactionId, ct).ConfigureAwait(false);
            }

            // Приоритет 3: Прозрачное проксирование на апстрим
            if (response is null)
            {
                Interlocked.Increment(ref _queriesForwarded);
                response = await ForwardAndReceiveAsync(queryData, ct).ConfigureAwait(false);
            }

            // 4. Отправить ответ клиенту
            if (response is not null)
            {
                await SendResponseAsync(response, clientEndpoint, ct).ConfigureAwait(false);
            }
            else
            {
                Interlocked.Increment(ref _queriesFailed);
            }
        }
        catch (OperationCanceledException)
        {
            // Ожидаемо при остановке
        }
        catch (SocketException ex)
        {
            Interlocked.Increment(ref _queriesFailed);
            _logger.LogDebug(ex, "Error handling DNS query");
        }
        catch (IOException ex)
        {
            Interlocked.Increment(ref _queriesFailed);
            _logger.LogDebug(ex, "Error handling DNS query");
        }
    }

    // ── Резолвинг через DoH ───────────────────────────────

    /// <summary>
    /// Резолвить домен через DoH и построить DNS ответ.
    /// </summary>
    private async Task<byte[]?> ResolveViaDohAsync(
        string domain,
        DnsRecordType type,
        ushort transactionId,
        CancellationToken ct)
    {
        var resolveResult = await _dohResolver.ResolveAsync(domain, type, ct).ConfigureAwait(false);
        if (!resolveResult.IsSuccess || resolveResult.Value is null)
        {
            _logger.LogDebug("DoH resolution failed for {Domain}: {Error}", domain, resolveResult.Error);
            return null;
        }

        var ip = resolveResult.Value;

        if (ip.AddressFamily == AddressFamily.InterNetwork)
        {
            return DnsPacketBuilder.BuildAResponse(transactionId, domain, ip, ttl: 300);
        }
        else if (ip.AddressFamily == AddressFamily.InterNetworkV6)
        {
            return DnsPacketBuilder.BuildAaaaResponse(transactionId, domain, ip, ttl: 300);
        }

        return null;
    }

    // ── Прозрачное проксирование ──────────────────────────

    /// <summary>
    /// Перенаправить DNS запрос на апстрим сервер и получить ответ.
    /// </summary>
    private async Task<byte[]?> ForwardAndReceiveAsync(
        byte[] queryData,
        CancellationToken ct)
    {
        try
        {
            // Отправить запрос на апстрим
            var upstreamEp = _config.UpstreamDns;
                    await _upstreamSocket!.SendToAsync(queryData, SocketFlags.None, upstreamEp, ct).ConfigureAwait(false);

            // Получить ответ
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(_config.UpstreamTimeoutMs);

            var responseBuffer = new byte[_config.UdpBufferSize];
            var remoteEp = new IPEndPoint(IPAddress.Any, 0);

                    var recvResult = await _upstreamSocket.ReceiveFromAsync(responseBuffer, SocketFlags.None, remoteEp, timeoutCts.Token).ConfigureAwait(false);
            int received = recvResult.ReceivedBytes;

            if (received > 0)
                return responseBuffer[..received];

            return null;
        }
        catch (OperationCanceledException)
        {
            _logger.LogDebug("Upstream DNS timeout");
            return null;
        }
        catch (SocketException ex)
        {
            _logger.LogDebug(ex, "Failed to forward DNS query to upstream");
            return null;
        }
        catch (IOException ex)
        {
            _logger.LogDebug(ex, "Failed to forward DNS query to upstream");
            return null;
        }
    }

    /// <summary>
    /// Перенаправить запрос и отправить ответ клиенту (полный цикл forwarding).
    /// </summary>
    private async Task ForwardToUpstreamAsync(
        byte[] queryData,
        IPEndPoint clientEndpoint,
        CancellationToken ct)
    {
        var response = await ForwardAndReceiveAsync(queryData, ct).ConfigureAwait(false);
        if (response is not null)
        {
            await SendResponseAsync(response, clientEndpoint, ct).ConfigureAwait(false);
        }
        else
        {
            Interlocked.Increment(ref _queriesFailed);
        }
    }

    // ── Отправка ответа ───────────────────────────────────

    private async Task SendResponseAsync(
        byte[] response,
        IPEndPoint clientEndpoint,
        CancellationToken ct)
    {
        try
        {
                    await _listenSocket!.SendToAsync(response, SocketFlags.None, clientEndpoint, ct).ConfigureAwait(false);
        }
        catch (SocketException ex)
        {
            _logger.LogDebug(ex, "Failed to send DNS response to {Endpoint}", clientEndpoint);
        }
        catch (IOException ex)
        {
            _logger.LogDebug(ex, "Failed to send DNS response to {Endpoint}", clientEndpoint);
        }
    }

    // ── Вспомогательные ───────────────────────────────────

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
                pos++;
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

    private void CleanupSockets()
    {
        try { _listenSocket?.Close(); } catch (ObjectDisposedException) { } catch (SocketException) { }
        try { _upstreamSocket?.Close(); } catch (ObjectDisposedException) { } catch (SocketException) { }
        _listenSocket = null;
        _upstreamSocket = null;
    }

    // ── Dispose ───────────────────────────────────────────

    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
        _cts?.Dispose();
        _logger.LogInformation("DNS proxy service disposed");
    }
}
