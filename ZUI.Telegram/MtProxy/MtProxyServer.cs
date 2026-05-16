// ═══════════════════════════════════════════════════════════════
// ZUI.Telegram / MtProxy / MtProxyServer.cs
// MTProxy сервер: Telegram клиент → MTProxy → Telegram DC
// Принимает TCP подключения, деобфусцирует MTProto, релеит в DC
// ═══════════════════════════════════════════════════════════════

using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ZUI.Core;
using ZUI.Telegram.MtProto;

namespace ZUI.Telegram.MtProxy;

/// <summary>
/// MTProxy сервер для Telegram.
/// 
/// Архитектура:
/// 1. Клиент подключается к MTProxy (IP:Port)
/// 2. Клиент отправляет 64-байтовый MTProto obfuscated init
/// 3. Сервер деобфусцирует заголовок с помощью секрета
/// 4. Извлекает DC ID → определяет целевой Telegram DC
/// 5. Устанавливает TCP подключение к Telegram DC
/// 6. Пересылает оригинальный заголовок в DC
/// 7. Двунаправленный relay: клиент ↔ DC
/// 
/// Отличие от SOCKS5→WS:
/// - MTProxy работает на уровне TCP, без SOCKS5 handshake
/// - Клиент подключается напрямую к MTProxy порту
/// - Telegram клиент настроен на MTProxy (tg://proxy?server=...&secret=...)
/// </summary>
public sealed class MtProxyServer : IAsyncDisposable
{
    private readonly ILogger _logger;
    private SecretConfig _secret;

    private TcpListener? _listener;
    private CancellationTokenSource? _cts;
    private Task? _acceptTask;

    // Активные подключения
    private readonly ConcurrentDictionary<string, TcpClient> _activeConnections = new();
    private int _activeConnectionCount;

    // Конфигурация
    private int _port;

    // Состояние
    private int _state; // 0=stopped, 1=starting, 2=running, 3=stopping

    /// <summary>Порт MTProxy сервера.</summary>
    public int Port => _port;

    /// <summary>Сервер запущен.</summary>
    public bool IsRunning => Volatile.Read(ref _state) == 2;

    /// <summary>Количество активных подключений.</summary>
    public int ActiveConnectionCount => Volatile.Read(ref _activeConnectionCount);

    /// <summary>Секрет MTProxy.</summary>
    public SecretConfig Secret => _secret;

    /// <summary>Событие нового подключения.</summary>
    public event Action<int>? OnDcConnected;

#pragma warning disable CS0067 // Событие будет использоваться при интеграции с UI
    /// <summary>Событие ошибки.</summary>
    public event Action<string>? OnError;
#pragma warning restore CS0067

    public MtProxyServer(int port, SecretConfig secret, ILogger<MtProxyServer>? logger = null)
    {
        _port = port;
        _secret = secret;
        _logger = logger ?? NullLoggerFactory.Instance.CreateLogger<MtProxyServer>();
    }

    /// <summary>
    /// Запустить MTProxy сервер с указанным портом и секретом.
    /// Переопределяет порт и секрет из конструктора.
    /// </summary>
    public Task<Result> StartAsync(int port, SecretConfig secret, CancellationToken ct = default)
    {
        _port = port;
        _secret = secret; // SecretConfig is sealed class, not record — but we can reassign the field
        return StartAsync(ct);
    }

    /// <summary>
    /// Запустить MTProxy сервер.
    /// </summary>
    public Task<Result> StartAsync(CancellationToken ct = default)
    {
        if (Volatile.Read(ref _state) != 0)
            return Task.FromResult(Result.Failed($"MtProxyServer already in state: {Volatile.Read(ref _state)}"));

        Volatile.Write(ref _state, 1); // Starting

        try
        {
            _listener = new TcpListener(IPAddress.Any, _port);
            _listener.Start(100);
            _cts = new CancellationTokenSource();

            Volatile.Write(ref _state, 2); // Running

            _acceptTask = AcceptLoopAsync(_cts.Token);

            _logger.LogInformation("MTProxy server started on port {Port} (secret={SecretType})", _port, _secret.Type);
            return Task.FromResult(Result.Success());
        }
    catch (SocketException ex)
    {
        Volatile.Write(ref _state, 0);
        _logger.LogError(ex, "Failed to start MTProxy server on port {Port}", _port);
        return Task.FromResult(Result.Failed($"Failed to start MTProxy server: {ex.Message}"));
    }
    catch (IOException ex)
    {
        Volatile.Write(ref _state, 0);
        _logger.LogError(ex, "Failed to start MTProxy server on port {Port}", _port);
        return Task.FromResult(Result.Failed($"Failed to start MTProxy server: {ex.Message}"));
    }
    }

    /// <summary>
    /// Остановить MTProxy сервер.
    /// </summary>
    public async Task StopAsync()
    {
        if (Volatile.Read(ref _state) != 2)
            return;

        Volatile.Write(ref _state, 3); // Stopping
        _logger.LogInformation("Stopping MTProxy server...");

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
        _logger.LogWarning(ex, "Error during MTProxy server shutdown");
    }
    catch (SocketException ex)
    {
        _logger.LogWarning(ex, "Error during MTProxy server shutdown");
    }
        }

        _cts?.Dispose();
        _cts = null;
        _acceptTask = null;

        Volatile.Write(ref _state, 0); // Stopped
        _logger.LogInformation("MTProxy server stopped");
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
        _logger.LogWarning(ex, "Error accepting MTProxy connection");
    }
    catch (IOException ex)
    {
        _logger.LogWarning(ex, "Error accepting MTProxy connection");
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
            await using var clientStream = client.GetStream();

            // Шаг 1: Прочитать 64-байтовый MTProto init-заголовок
            var header = new byte[MtProtoPacket.HeaderSize];
            await ReadExactAsync(clientStream, header, ct).ConfigureAwait(false);

            // Шаг 2: Деобфусцировать заголовок с помощью секрета
            var decrypted = XorWithSecret(header, _secret.Key);

            // Шаг 3: Извлечь DC ID из деобфусцированного заголовка
            // В MTProxy DC ID находится в байтах 60-61 (little-endian)
            short dcIdRaw = (short)(decrypted[60] | (decrypted[61] << 8));
            int dcId = Math.Abs(dcIdRaw);

            if (dcId is < 1 or > 5)
            {
                _logger.LogWarning("MTProxy [{Conn}]: invalid DC ID {DcId}, closing", connectionId, dcId);
                return;
            }

            var dcEndpoint = MtProtoPacket.GetDcEndpoint(dcId);
            if (dcEndpoint is null)
            {
                _logger.LogWarning("MTProxy [{Conn}]: no endpoint for DC {DcId}", connectionId, dcId);
                return;
            }

            _logger.LogDebug("MTProxy [{Conn}]: client → DC{DcId} ({Host}:{Port})", connectionId, dcId, dcEndpoint.Value.Host, dcEndpoint.Value.Port);
            OnDcConnected?.Invoke(dcId);

            // Шаг 4: Подключиться к Telegram DC
            using var dcClient = new TcpClient { NoDelay = true };
            await dcClient.ConnectAsync(dcEndpoint.Value.Host, dcEndpoint.Value.Port, ct).ConfigureAwait(false);
            await using var dcStream = dcClient.GetStream();

            // Шаг 5: Переслать оригинальный заголовок в DC
            await dcStream.WriteAsync(header, ct).ConfigureAwait(false);

            // Шаг 6: Двунаправленный relay
            using var relayCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            var t1 = clientStream.CopyToAsync(dcStream, relayCts.Token);
            var t2 = dcStream.CopyToAsync(clientStream, relayCts.Token);

            await Task.WhenAny(t1, t2).ConfigureAwait(false);
            relayCts.Cancel();
        }
    catch (OperationCanceledException) { /* shutdown */ }
    catch (IOException) { /* client disconnected */ }
    catch (SocketException) { /* connection reset */ }
    catch (ObjectDisposedException ex)
    {
        _logger.LogDebug(ex, "MTProxy [{Conn}]: client handler error", connectionId);
    }
        finally
        {
            _activeConnections.TryRemove(connectionId, out _);
            Interlocked.Decrement(ref _activeConnectionCount);
            try { client.Close(); } catch (SocketException) { /* ignore */ } catch (ObjectDisposedException) { /* ignore */ }
        }
    }

    // ── XOR с секретом ─────────────────────────────────────

    /// <summary>
    /// XOR обфускация/деобфускация данных с ключом-секретом.
    /// Циклическое применение ключа: output[i] = input[i] ^ secret[i % secret.Length]
    /// </summary>
    private static byte[] XorWithSecret(byte[] data, byte[] secret)
    {
        var result = new byte[data.Length];
        for (int i = 0; i < data.Length; i++)
        {
            result[i] = (byte)(data[i] ^ secret[i % secret.Length]);
        }
        return result;
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

    /// <summary>
    /// Сгенерировать MTProxy ссылку для Telegram.
    /// Формат: tg://proxy?server=HOST&amp;port=PORT&amp;secret=SECRET
    /// </summary>
    public string GenerateTgLink(string serverHost)
    {
        return _secret.GenerateTgLink(serverHost, _port);
    }

    // ── IAsyncDisposable ──────────────────────────────────

    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
    }
}
