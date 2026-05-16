// ═══════════════════════════════════════════════════════════════
// ZUI.Telegram / WebSocket / WsTunnelClient.cs
// WebSocket туннель: Telegram client ↔ SOCKS5 server ↔ WSS ↔ Telegram DC
// Обфусцированные MTProto пакеты через WebSocket frames
// ═══════════════════════════════════════════════════════════════

using System.Net.Sockets;
using System.Net.WebSockets;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ZUI.Core;

namespace ZUI.Telegram.WebSocket;

/// <summary>
/// WebSocket туннель-клиент для Telegram.
/// 
/// Устанавливает WSS подключение к Telegram WebSocket прокси-серверу
/// и релеит данные между клиентским TCP потоком и WebSocket.
/// 
/// Поток данных:
/// 1. Клиент (Telegram app) → SOCKS5 → Socks5Server
/// 2. Socks5Server читает MTProto init (64 байта)
/// 3. WsTunnelClient подключается к wss://kwsN.web.telegram.org/apiws
/// 4. Отправляет init-пакет через WebSocket
/// 5. Двунаправленный relay: TCP ↔ WebSocket
/// </summary>
public sealed class WsTunnelClient
{
    private readonly ILogger _logger;

    /// <summary>Текущая конфигурация.</summary>
    private WsTunnelConfig _config = WsTunnelConfig.Disabled;

    public WsTunnelClient(ILogger<WsTunnelClient>? logger = null)
    {
        _logger = logger ?? NullLoggerFactory.Instance.CreateLogger<WsTunnelClient>();
    }

    /// <summary>
    /// Обновить конфигурацию WebSocket туннеля.
    /// </summary>
    public void UpdateConfig(WsTunnelConfig config)
    {
        _config = config;
        _logger.LogDebug("WsTunnel config updated: Url={Url}, Enabled={Enabled}", config.WsUrl, config.IsEnabled);
    }

    /// <summary>
    /// Текущая конфигурация.
    /// </summary>
    public WsTunnelConfig Config => _config;

    /// <summary>
    /// Попробовать установить WebSocket туннель.
    /// </summary>
    /// <param name="clientStream">TCP поток клиента (Telegram app).</param>
    /// <param name="wsHost">WSS хост (например, kws1.web.telegram.org).</param>
    /// <param name="initPacket">MTProto init-пакет (64 байта) для отправки первым.</param>
    /// <param name="ct">Токен отмены.</param>
    /// <returns>true если туннель успешно установлен и релей завершён, false если не удалось.</returns>
    public async Task<bool> TryRelayAsync(
        NetworkStream clientStream,
        string wsHost,
        byte[] initPacket,
        CancellationToken ct = default)
    {
        var ws = new ClientWebSocket();
        ws.Options.AddSubProtocol("binary");

        // Установить заголовки
        if (!string.IsNullOrWhiteSpace(_config.Origin) || !string.IsNullOrWhiteSpace(wsHost))
        {
            var origin = !string.IsNullOrWhiteSpace(_config.Origin)
                ? _config.Origin
                : $"https://{wsHost}";
            ws.Options.SetRequestHeader("Origin", origin);
        }

        if (!string.IsNullOrWhiteSpace(_config.UserAgent))
        {
            ws.Options.SetRequestHeader("User-Agent", _config.UserAgent);
        }

        // Добавить secret в query string, если есть
        var wsUrl = $"wss://{wsHost}/apiws";
        if (!string.IsNullOrWhiteSpace(_config.Secret))
        {
            wsUrl += $"?secret={_config.Secret}";
        }

        try
        {
            using var connectCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            connectCts.CancelAfter(TimeSpan.FromMilliseconds(_config.ConnectTimeoutMs));

            await ws.ConnectAsync(new Uri(wsUrl), connectCts.Token).ConfigureAwait(false);

            if (ws.State != WebSocketState.Open)
            {
                _logger.LogWarning("WebSocket connected but state is {State}, expected Open", ws.State);
                return false;
            }

            _logger.LogDebug("WebSocket tunnel established to {Host}", wsHost);

            // Отправить MTProto init-пакет первым
            await ws.SendAsync(initPacket, WebSocketMessageType.Binary, true, ct).ConfigureAwait(false);

            // Двунаправленный relay
            using var relayCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            var t1 = RelayTcpToWsAsync(clientStream, ws, relayCts.Token);
            var t2 = RelayWsToTcpAsync(ws, clientStream, relayCts.Token);

            await Task.WhenAny(t1, t2).ConfigureAwait(false);
            relayCts.Cancel();

            return true;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Shutdown — не ошибка
            return true;
        }
    catch (WebSocketException ex)
    {
        _logger.LogDebug(ex, "WebSocket tunnel failed to {Host}", wsHost);
        return false;
    }
    catch (IOException ex)
    {
        _logger.LogDebug(ex, "WebSocket tunnel error to {Host}", wsHost);
        return false;
    }
    catch (TimeoutException ex)
    {
        _logger.LogDebug(ex, "WebSocket tunnel error to {Host}", wsHost);
        return false;
    }
        finally
        {
            if (ws.State == WebSocketState.Open)
            {
                try
                {
                    await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, string.Empty, default).ConfigureAwait(false);
                }
                catch (WebSocketException) { /* ignore close errors */ }
    catch (ObjectDisposedException) { /* ignore close errors */ }
            }

            ws.Dispose();
        }
    }

    /// <summary>
    /// Установить WebSocket подключение и вернуть ClientWebSocket
    /// (для использования в MtProxyServer и других сценариях).
    /// </summary>
    public async Task<Result<ClientWebSocket>> ConnectAsync(
        string wsUrl,
        string? origin = null,
        string? userAgent = null,
        CancellationToken ct = default)
    {
        var ws = new ClientWebSocket();
        ws.Options.AddSubProtocol("binary");

        if (!string.IsNullOrWhiteSpace(origin))
            ws.Options.SetRequestHeader("Origin", origin);

        if (!string.IsNullOrWhiteSpace(userAgent ?? _config.UserAgent))
            ws.Options.SetRequestHeader("User-Agent", userAgent ?? _config.UserAgent);

        try
        {
            using var connectCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            connectCts.CancelAfter(TimeSpan.FromMilliseconds(_config.ConnectTimeoutMs));

            await ws.ConnectAsync(new Uri(wsUrl), connectCts.Token).ConfigureAwait(false);
            return Result<ClientWebSocket>.Success(ws);
        }
    catch (WebSocketException ex)
    {
        ws.Dispose();
        return Result<ClientWebSocket>.Failed($"WebSocket connection failed: {ex.Message}");
    }
    catch (IOException ex)
    {
        ws.Dispose();
        return Result<ClientWebSocket>.Failed($"WebSocket connection failed: {ex.Message}");
    }
    catch (TimeoutException ex)
    {
        ws.Dispose();
        return Result<ClientWebSocket>.Failed($"WebSocket connection failed: {ex.Message}");
    }
    }

    // ── Relay методы ────────────────────────────────────────

    /// <summary>
    /// Релей: TCP → WebSocket (клиент отправляет данные → WSS).
    /// </summary>
    private static async Task RelayTcpToWsAsync(
        NetworkStream tcpStream,
        ClientWebSocket ws,
        CancellationToken ct)
    {
        var buffer = new byte[16384];

        try
        {
            while (ws.State == WebSocketState.Open && !ct.IsCancellationRequested)
            {
                int bytesRead = await tcpStream.ReadAsync(buffer, ct).ConfigureAwait(false);
                if (bytesRead == 0) break; // Клиент закрыл соединение

                await ws.SendAsync(
                    buffer.AsMemory(0, bytesRead),
                    WebSocketMessageType.Binary,
                    true,
                    ct).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) { /* shutdown */ }
        catch (IOException) { /* client disconnected */ }
        catch (WebSocketException) { /* ws closed */ }
    }

    /// <summary>
    /// Релей: WebSocket → TCP (WSS отправляет данные → клиенту).
    /// </summary>
    private static async Task RelayWsToTcpAsync(
        ClientWebSocket ws,
        NetworkStream tcpStream,
        CancellationToken ct)
    {
        var buffer = new byte[16384];

        try
        {
            while (ws.State == WebSocketState.Open && !ct.IsCancellationRequested)
            {
                var result = await ws.ReceiveAsync(buffer, ct).ConfigureAwait(false);

                if (result.MessageType == WebSocketMessageType.Close)
                    break;

                if (result.Count > 0)
                {
                    await tcpStream.WriteAsync(buffer.AsMemory(0, result.Count), ct).ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException) { /* shutdown */ }
        catch (IOException) { /* client disconnected */ }
        catch (WebSocketException) { /* ws closed */ }
    }
}
