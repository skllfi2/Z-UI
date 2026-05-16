// ═══════════════════════════════════════════════════════════════
// ZUI.Proxy / TcpRelay.cs
// User-space TCP relay: app ↔ local_port ↔ proxy ↔ target
// Двунаправленная пересылка данных между двумя потоками
// ═══════════════════════════════════════════════════════════════

using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ZUI.Core;
using ZUI.Proxy.Client;
using ZUI.Proxy.Rules;
using ZUI.Proxy.Chain;
using ZUI.Core.Traffic;

namespace ZUI.Proxy;

/// <summary>
/// TCP relay: пересылает данные между локальным соединением
/// (от приложения) и удалённым соединением (через прокси).
/// 
/// Схема работы:
/// 1. TcpListener на локальном порту (dynamic)
/// 2. Приложение подключается к local_port
/// 3. TcpRelay устанавливает соединение через прокси к target
/// 4. Данные релеятся в обе стороны: app ↔ proxy ↔ target
/// 5. При закрытии любой стороны — закрывается и другая
/// </summary>
public sealed class TcpRelay : IAsyncDisposable
{
    private readonly ILogger _logger;
    private readonly Socks5Client _socks5;
    private readonly Socks4Client _socks4;
    private readonly HttpConnectClient _httpConnect;
    private readonly ChainExecutor _chainExecutor;
    private readonly TrafficMonitor _trafficMonitor;

    /// <summary>Таймаут подключения к прокси (мс).</summary>
    public int ProxyConnectTimeoutMs { get; set; } = 10_000;

    /// <summary>Размер буфера для релея данных.</summary>
    public int RelayBufferSize { get; set; } = 8192;

    public TcpRelay(
        Socks5Client socks5,
        Socks4Client socks4,
        HttpConnectClient httpConnect,
        ChainExecutor chainExecutor,
        TrafficMonitor trafficMonitor,
        ILogger<TcpRelay>? logger = null)
    {
        _socks5 = socks5;
        _socks4 = socks4;
        _httpConnect = httpConnect;
        _chainExecutor = chainExecutor;
        _trafficMonitor = trafficMonitor;
        _logger = logger ?? NullLoggerFactory.Instance.CreateLogger<TcpRelay>();
    }

    /// <summary>
    /// Установить соединение через прокси и вернуть TcpClient
    /// с установленным туннелем.
    /// </summary>
    public async Task<Result<TcpClient>> ConnectToProxyAsync(
        ProxyTarget proxy,
        string targetHost,
        int targetPort,
        CancellationToken ct = default)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(ProxyConnectTimeoutMs);

        return proxy.Type switch
        {
            ProxyType.Socks5 => await _socks5.ConnectAsync(proxy, targetHost, targetPort, cts.Token).ConfigureAwait(false),
            ProxyType.Socks4 or ProxyType.Socks4a => await _socks4.ConnectAsync(proxy, targetHost, targetPort, cts.Token).ConfigureAwait(false),
            ProxyType.HttpConnect => await _httpConnect.ConnectAsync(proxy, targetHost, targetPort, cts.Token).ConfigureAwait(false),
            _ => Result<TcpClient>.Failed($"Unsupported proxy type: {proxy.Type}"),
        };
    }

    /// <summary>
    /// Установить соединение через цепочку прокси и вернуть TcpClient.
    /// </summary>
    public async Task<Result<TcpClient>> ConnectThroughChainAsync(
        ProxyChain chain,
        string targetHost,
        int targetPort,
        CancellationToken ct = default)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(ProxyConnectTimeoutMs);

        return await _chainExecutor.ExecuteAsync(chain, targetHost, targetPort, cts.Token).ConfigureAwait(false);
    }

    /// <summary>
    /// Начать двунаправленный relay между двумя потоками.
    /// Работает до закрытия одной из сторон или CancellationToken.
    /// </summary>
    public async Task RelayAsync(
        NetworkStream source,
        NetworkStream destination,
        string connectionId,
        string direction,
        CancellationToken ct = default)
    {
        var buffer = new byte[RelayBufferSize];

        try
        {
            while (!ct.IsCancellationRequested)
            {
                int bytesRead = await source.ReadAsync(buffer, ct).ConfigureAwait(false);
                if (bytesRead == 0)
                {
                    _logger.LogDebug("Relay [{Conn}] {Dir}: source closed", connectionId, direction);
                    break;
                }

                await destination.WriteAsync(buffer.AsMemory(0, bytesRead), ct).ConfigureAwait(false);
                await destination.FlushAsync(ct).ConfigureAwait(false);

                // Учитывать трафик
                _trafficMonitor.RecordBytes(connectionId, bytesRead, direction == "upstream");
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Нормальное завершение по CancellationToken
        }
        catch (IOException ex)
        {
            _logger.LogDebug("Relay [{Conn}] {Dir}: IO error: {Error}", connectionId, direction, ex.Message);
        }
        catch (SocketException ex)
        {
            _logger.LogWarning("Relay [{Conn}] {Dir}: socket error: {Error}", connectionId, direction, ex.Message);
        }
        catch (ObjectDisposedException ex)
        {
            _logger.LogDebug("Relay [{Conn}] {Dir}: disposed: {Error}", connectionId, direction, ex.Message);
        }
    }

    /// <summary>
    /// Запустить полный relay: соединить приложение с прокси-сервером
    /// и пересылать данные в обе стороны.
    /// </summary>
    public async Task StartRelayAsync(
        TcpClient appClient,
        TcpClient proxyClient,
        string connectionId,
        CancellationToken ct = default)
    {
        var appStream = appClient.GetStream();
        var proxyStream = proxyClient.GetStream();

        _trafficMonitor.AddConnection(connectionId);

        try
        {
            // Двунаправленный relay: upstream (app→proxy) + downstream (proxy→app)
            var upstreamTask = RelayAsync(appStream, proxyStream, connectionId, "upstream", ct);
            var downstreamTask = RelayAsync(proxyStream, appStream, connectionId, "downstream", ct);

            // Ждём завершения любого направления (при закрытии одного — закроется и другое)
            await Task.WhenAny(upstreamTask, downstreamTask).ConfigureAwait(false);
        }
        finally
        {
            _trafficMonitor.RemoveConnection(connectionId);

            try { appClient.Close(); } catch (ObjectDisposedException) { /* ignore */ } catch (IOException) { /* ignore */ }
            try { proxyClient.Close(); } catch (ObjectDisposedException) { /* ignore */ } catch (IOException) { /* ignore */ }
        }
    }

    // ── IAsyncDisposable ─────────────────────────────────

    public ValueTask DisposeAsync()
    {
        return ValueTask.CompletedTask;
    }
}
