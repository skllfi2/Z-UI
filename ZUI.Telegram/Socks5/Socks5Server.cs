// ═══════════════════════════════════════════════════════════════
// ZUI.Telegram / Socks5 / Socks5Server.cs
// Входящий SOCKS5 сервер для Telegram proxy
// Принимает SOCKS5 подключения → определяет Telegram DC →
// routed через WebSocket туннель или прямой TCP relay
// ═══════════════════════════════════════════════════════════════

using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ZUI.Core;
using ZUI.Telegram.MtProto;
using ZUI.Telegram.WebSocket;

namespace ZUI.Telegram.Socks5;

/// <summary>
/// SOCKS5 сервер для Telegram proxy.
/// 
/// Поток данных:
/// 1. Telegram клиент подключается к SOCKS5 (127.0.0.1:Port)
/// 2. SOCKS5 handshake → получаем адрес DC
/// 3. Если это Telegram DC:
///    a. Читаем 64-байтовый MTProto init-заголовок
///    b. Определяем DC ID
///    c. Пробуем WebSocket туннель к Telegram DC
///    d. Fallback: прямой TCP relay к DC
/// 4. Если НЕ Telegram DC: прямой TCP relay
/// </summary>
public sealed class Socks5Server : IAsyncDisposable
{
    private readonly ILogger _logger;
    private readonly Socks5Handler _handler;
    private readonly WsTunnelClient _wsTunnel;

    private TcpListener? _listener;
    private CancellationTokenSource? _cts;
    private Task? _acceptTask;

    // Активные подключения
    private readonly ConcurrentDictionary<string, TcpClient> _activeConnections = new();
    private int _activeConnectionCount;

    // Конфигурация
    private int _port;
    private readonly string? _authUsername;
    private readonly string? _authPassword;
    private WsTunnelConfig _wsConfig;

    // Состояние
    private int _state; // 0=stopped, 1=starting, 2=running, 3=stopping

    /// <summary>Порт SOCKS5 сервера.</summary>
    public int Port => Volatile.Read(ref _state) == 2 ? _port : 0;

    /// <summary>Сервер запущен.</summary>
    public bool IsRunning => Volatile.Read(ref _state) == 2;

    /// <summary>Количество активных подключений.</summary>
    public int ActiveConnectionCount => Volatile.Read(ref _activeConnectionCount);

    /// <summary>Событие нового подключения.</summary>
    public event Action<string, string>? OnConnectionEstablished;

#pragma warning disable CS0067 // Событие будет использоваться при интеграции с UI
    /// <summary>Событие ошибки.</summary>
    public event Action<string>? OnError;
#pragma warning restore CS0067

    public Socks5Server(
        int port,
        WsTunnelClient wsTunnel,
        WsTunnelConfig wsConfig,
        ILogger<Socks5Server>? logger = null)
    {
        _port = port;
        _wsTunnel = wsTunnel;
        _wsConfig = wsConfig;
        _handler = new Socks5Handler(logger: logger);
        _logger = logger ?? NullLoggerFactory.Instance.CreateLogger<Socks5Server>();
    }

    public Socks5Server(
        int port,
        WsTunnelClient wsTunnel,
        WsTunnelConfig wsConfig,
        string? authUsername,
        string? authPassword,
        ILogger<Socks5Server>? logger = null)
    {
        _port = port;
        _wsTunnel = wsTunnel;
        _wsConfig = wsConfig;
        _authUsername = authUsername;
        _authPassword = authPassword;
        _handler = new Socks5Handler(authUsername, authPassword, logger: logger);
        _logger = logger ?? NullLoggerFactory.Instance.CreateLogger<Socks5Server>();
    }

    /// <summary>
    /// Запустить SOCKS5 сервер на указанном порту.
    /// Порт можно задать при вызове (переопределяет порт из конструктора).
    /// </summary>
    public Task<Result> StartAsync(int port, CancellationToken ct = default)
    {
        _port = port;
        return StartAsync(ct);
    }

    /// <summary>
    /// Запустить SOCKS5 сервер.
    /// </summary>
    public Task<Result> StartAsync(CancellationToken ct = default)
    {
        if (Volatile.Read(ref _state) != 0)
            return Task.FromResult(Result.Failed($"Socks5Server already in state: {Volatile.Read(ref _state)}"));

        Volatile.Write(ref _state, 1); // Starting

        try
        {
            _listener = new TcpListener(IPAddress.Loopback, _port);
            _listener.Start(50);
            _cts = new CancellationTokenSource();

            Volatile.Write(ref _state, 2); // Running

            _acceptTask = AcceptLoopAsync(_cts.Token);

            _logger.LogInformation("SOCKS5 server started on 127.0.0.1:{Port}", _port);
            return Task.FromResult(Result.Success());
        }
    catch (SocketException ex)
    {
        Volatile.Write(ref _state, 0);
        _logger.LogError(ex, "Failed to start SOCKS5 server on port {Port}", _port);
        return Task.FromResult(Result.Failed($"Failed to start SOCKS5 server: {ex.Message}"));
    }
    catch (IOException ex)
    {
        Volatile.Write(ref _state, 0);
        _logger.LogError(ex, "Failed to start SOCKS5 server on port {Port}", _port);
        return Task.FromResult(Result.Failed($"Failed to start SOCKS5 server: {ex.Message}"));
    }
    }

    /// <summary>
    /// Остановить SOCKS5 сервер.
    /// </summary>
    public async Task StopAsync()
    {
        if (Volatile.Read(ref _state) != 2)
            return;

        Volatile.Write(ref _state, 3); // Stopping
        _logger.LogInformation("Stopping SOCKS5 server...");

        _cts?.Cancel();
        _listener?.Stop();

        // Закрыть все активные подключения
        foreach (var kvp in _activeConnections)
        {
            try { kvp.Value.Close(); } catch (SocketException) { /* ignore */ } catch (ObjectDisposedException) { /* ignore */ }
        }
        _activeConnections.Clear();

        if (_acceptTask is not null)
        {
            try
            {
                await _acceptTask.ConfigureAwait(false);
            }
    catch (OperationCanceledException) { /* expected */ }
    catch (IOException ex)
    {
        _logger.LogWarning(ex, "Error during SOCKS5 server shutdown");
    }
    catch (SocketException ex)
    {
        _logger.LogWarning(ex, "Error during SOCKS5 server shutdown");
    }
        }

        _cts?.Dispose();
        _cts = null;
        _acceptTask = null;

        Volatile.Write(ref _state, 0); // Stopped
        _logger.LogInformation("SOCKS5 server stopped");
    }

    // ── Accept Loop ────────────────────────────────────────

    private async Task AcceptLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var client = await _listener!.AcceptTcpClientAsync(ct).ConfigureAwait(false);
                _ = HandleClientAsync(client, ct);
            }
    catch (OperationCanceledException) when (ct.IsCancellationRequested)
    {
        break;
    }
    catch (SocketException ex)
    {
        _logger.LogWarning(ex, "Error accepting SOCKS5 connection");
    }
    catch (IOException ex)
    {
        _logger.LogWarning(ex, "Error accepting SOCKS5 connection");
    }
        }
    }

    // ── Client Handler ─────────────────────────────────────

    private async Task HandleClientAsync(TcpClient client, CancellationToken ct)
    {
        client.NoDelay = true;
        var connectionId = Guid.NewGuid().ToString("N")[..8];
        Interlocked.Increment(ref _activeConnectionCount);
        _activeConnections[connectionId] = client;

        try
        {
            await using var stream = client.GetStream();

            // Шаг 1: SOCKS5 handshake
            var handshakeResult = await _handler.HandleHandshakeAsync(stream, ct).ConfigureAwait(false);
            if (!handshakeResult.IsSuccess)
            {
                _logger.LogDebug("SOCKS5 handshake failed: {Error}", handshakeResult.Error);
                return;
            }

            var target = handshakeResult.Value;
            _logger.LogDebug("SOCKS5 [{Conn}]: CONNECT → {Host}:{Port}", connectionId, target.TargetHost, target.TargetPort);

            // Шаг 2: Это Telegram DC?
            if (MtProtoPacket.IsTelegramDcIp(target.TargetHost))
            {
                await HandleTelegramDcAsync(stream, target, connectionId, ct).ConfigureAwait(false);
            }
            else
            {
                // Не Telegram DC — прямой TCP relay
                _logger.LogDebug("SOCKS5 [{Conn}]: Non-Telegram target, direct TCP relay", connectionId);
                await RelayDirectTcpAsync(stream, target.TargetHost, target.TargetPort, ct: ct).ConfigureAwait(false);
            }
        }
    catch (OperationCanceledException) { /* shutdown */ }
    catch (IOException) { /* client disconnected */ }
    catch (SocketException) { /* connection reset */ }
    catch (ObjectDisposedException ex)
    {
        _logger.LogDebug(ex, "SOCKS5 [{Conn}]: client handler error", connectionId);
    }
        finally
        {
            _activeConnections.TryRemove(connectionId, out _);
            Interlocked.Decrement(ref _activeConnectionCount);
            try { client.Close(); } catch (SocketException) { /* ignore */ } catch (ObjectDisposedException) { /* ignore */ }
        }
    }

    /// <summary>
    /// Обработать подключение к Telegram DC:
    /// 1. Прочитать 64-байтовый MTProto init
    /// 2. Извлечь DC ID
    /// 3. Попробовать WebSocket туннель
    /// 4. Fallback: прямой TCP relay
    /// </summary>
    private async Task HandleTelegramDcAsync(
        NetworkStream clientStream,
        Socks5HandshakeResult target,
        string connectionId,
        CancellationToken ct)
    {
        // Прочитать MTProto init-заголовок (64 байта)
        var initPacket = new byte[MtProtoPacket.HeaderSize];
        try
        {
            await ReadExactAsync(clientStream, initPacket, ct).ConfigureAwait(false);
        }
        catch (IOException)
        {
            _logger.LogDebug("SOCKS5 [{Conn}]: client disconnected before MTProto init", connectionId);
            return;
        }

        // Извлечь DC ID
        int dcId = MtProtoPacket.ExtractDcId(initPacket);
        _logger.LogDebug("SOCKS5 [{Conn}]: Telegram DC ID = {DcId}", connectionId, dcId);

        // Попробовать WebSocket туннель (если DC ID определён и WSS хост доступен)
        if (dcId > 0 && _wsConfig.IsEnabled)
        {
            var wsHost = MtProtoPacket.GetWsHost(dcId);
            if (wsHost is not null)
            {
                _logger.LogInformation("SOCKS5 [{Conn}]: attempting WebSocket tunnel to {WsHost}", connectionId, wsHost);
                OnConnectionEstablished?.Invoke("Telegram", $"DC{dcId} ({wsHost})");

                var wsResult = await _wsTunnel.TryRelayAsync(
                    clientStream, wsHost, initPacket, ct).ConfigureAwait(false);

                if (wsResult)
                {
                    _logger.LogDebug("SOCKS5 [{Conn}]: WebSocket tunnel succeeded", connectionId);
                    return;
                }

                _logger.LogWarning("SOCKS5 [{Conn}]: WebSocket tunnel failed, fallback to direct TCP", connectionId);
            }
        }

        // Fallback: прямой TCP relay с уже прочитанным initPacket
        OnConnectionEstablished?.Invoke("Telegram", $"DC{dcId} (direct TCP)");
        await RelayDirectTcpAsync(clientStream, target.TargetHost, target.TargetPort, initPacket, ct).ConfigureAwait(false);
    }

    // ── TCP Relay ──────────────────────────────────────────

    private static async Task RelayDirectTcpAsync(
        NetworkStream clientStream,
        string targetHost,
        int targetPort,
        byte[]? prefixData = null,
        CancellationToken ct = default)
    {
        using var remoteClient = new TcpClient { NoDelay = true };
        await remoteClient.ConnectAsync(targetHost, targetPort, ct).ConfigureAwait(false);
        await using var remoteStream = remoteClient.GetStream();

        // Отправить prefix data (MTProto init header), если есть
        if (prefixData is not null && prefixData.Length > 0)
        {
            await remoteStream.WriteAsync(prefixData, ct).ConfigureAwait(false);
        }

        // Двунаправленный relay
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var t1 = clientStream.CopyToAsync(remoteStream, cts.Token);
        var t2 = remoteStream.CopyToAsync(clientStream, cts.Token);

        await Task.WhenAny(t1, t2).ConfigureAwait(false);
        cts.Cancel();
    }

    // ── Вспомогательные ──────────────────────────────────

    private static async Task ReadExactAsync(NetworkStream stream, byte[] buffer, CancellationToken ct)
    {
        int offset = 0;
        while (offset < buffer.Length)
        {
            int read = await stream.ReadAsync(buffer.AsMemory(offset, buffer.Length - offset), ct).ConfigureAwait(false);
            if (read == 0)
                throw new IOException("Connection closed prematurely");
            offset += read;
        }
    }

    // ── IAsyncDisposable ──────────────────────────────────

    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
    }
}
