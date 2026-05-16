// ═══════════════════════════════════════════════════════════════
// ZUI.Ipc / IpcPipeServer.cs
// Named Pipe сервер для Worker Service (SYSTEM → UI)
// Поддержка: множественные клиенты, ACL, async чтение/запись
// ═══════════════════════════════════════════════════════════════

using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace ZUI.Ipc;

/// <summary>
/// Named Pipe сервер для IPC Worker → UI.
// Запускается в Worker Service (SYSTEM), принимает подключения от UI процесса.
/// Формат: длина (4 байта, little-endian) + JSON байты
/// </summary>
public sealed class IpcPipeServer : IAsyncDisposable
{
    private const string PipeName = "ZUI_IPC";
    private const int MaxMessageSize = 1024 * 1024; // 1 MB
    private const int BufferSize = 65536;

    private readonly ILogger _logger;
    private readonly List<NamedPipeServerStream> _connections = new();
    private readonly Lock _lock = new();
    private CancellationTokenSource? _cts;
    private Task? _listenTask;
    private int _isRunning;

    /// <summary>Событие: получено сообщение от клиента (UI).</summary>
    public event Func<IpcMessage, Task>? OnMessageReceived;

    /// <summary>Событие: клиент подключился.</summary>
    public event Action? OnClientConnected;

    /// <summary>Событие: клиент отключился.</summary>
    public event Action? OnClientDisconnected;

    public IpcPipeServer(ILogger<IpcPipeServer>? logger = null)
    {
        _logger = logger ?? NullLoggerFactory.Instance.CreateLogger<IpcPipeServer>();
    }

    public bool IsRunning => Volatile.Read(ref _isRunning) == 1;

    // ── Запуск сервера ──────────────────────────────────────

    /// <summary>
    /// Запустить Named Pipe сервер. Ожидает подключения клиентов.
    /// </summary>
    public Result Start(CancellationToken ct = default)
    {
        if (Interlocked.CompareExchange(ref _isRunning, 1, 0) != 0)
            return Result.Failed("IPC server is already running.");

        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _listenTask = Task.Run(() => ListenLoop(_cts.Token), _cts.Token);

        _logger.LogInformation("IPC pipe server started on {PipeName}", PipeName);
        return Result.Success();
    }

    // ── Остановка сервера ───────────────────────────────────

    public async ValueTask DisposeAsync()
    {
        Interlocked.Exchange(ref _isRunning, 0);
        _cts?.Cancel();

        if (_listenTask is not null)
        {
            try { await _listenTask; } catch (ObjectDisposedException) { /* shutdown */ } catch (IOException) { /* shutdown */ }
        }

        lock (_lock)
        {
            foreach (var conn in _connections)
            {
                try { conn.Dispose(); } catch (ObjectDisposedException) { /* cleanup */ } catch (IOException) { /* cleanup */ }
            }
            _connections.Clear();
        }

        _cts?.Dispose();
        _logger.LogInformation("IPC pipe server stopped");
    }

    // ── Отправка сообщения всем подключённым клиентам ───────

    /// <summary>
    /// Отправить сообщение всем подключённым UI клиентам.
    /// </summary>
    public async Task SendToAllAsync(IpcMessage message, CancellationToken ct = default)
    {
        var data = IpcSerializer.Serialize(message);
        var lengthBytes = BitConverter.GetBytes(data.Length);

        List<NamedPipeServerStream> connections;
        lock (_lock)
        {
            connections = _connections.ToList();
        }

        foreach (var conn in connections)
        {
            try
            {
                if (!conn.IsConnected) continue;
                await conn.WriteAsync(lengthBytes, ct).ConfigureAwait(false);
                await conn.WriteAsync(data, ct).ConfigureAwait(false);
            await conn.FlushAsync(ct).ConfigureAwait(false);
        }
        catch (IOException ex)
        {
            _logger.LogDebug(ex, "Failed to send to pipe client");
        }
        catch (ObjectDisposedException ex)
        {
            _logger.LogDebug(ex, "Failed to send to pipe client");
        }
        }
    }

    // ── Цикл ожидания подключений ───────────────────────────

    private async Task ListenLoop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var pipe = CreatePipeStream();
                await pipe.WaitForConnectionAsync(ct).ConfigureAwait(false);

                lock (_lock)
                {
                    _connections.Add(pipe);
                }

                OnClientConnected?.Invoke();
                _logger.LogInformation("IPC client connected");

                // Запускаем чтение для этого клиента
                _ = ReadFromClientAsync(pipe, ct);
            }
        catch (OperationCanceledException)
        {
            break;
        }
        catch (IOException ex)
        {
            _logger.LogError(ex, "Error in IPC listen loop");
            await Task.Delay(1000, ct).ConfigureAwait(false);
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogError(ex, "Error in IPC listen loop");
            await Task.Delay(1000, ct).ConfigureAwait(false);
        }
        }
    }

    // ── Чтение сообщений от клиента ─────────────────────────

    private async Task ReadFromClientAsync(NamedPipeServerStream pipe, CancellationToken ct)
    {
        var lengthBuffer = new byte[4];

        try
        {
            while (!ct.IsCancellationRequested && pipe.IsConnected)
            {
                // Читаем длину сообщения (ровно 4 байта — partial read возможен на Named Pipes)
                if (!await ReadExactAsync(pipe, lengthBuffer, ct).ConfigureAwait(false))
                    break;

                int msgLength = BitConverter.ToInt32(lengthBuffer);
                if (msgLength <= 0 || msgLength > MaxMessageSize)
                {
                    _logger.LogWarning("Invalid message length: {Length}", msgLength);
                    break;
                }

                // Читаем сообщение
                var msgBuffer = new byte[msgLength];
                int totalRead = 0;
                while (totalRead < msgLength)
                {
                    int chunkRead = await pipe.ReadAsync(msgBuffer.AsMemory(totalRead, msgLength - totalRead), ct).ConfigureAwait(false);
                    if (chunkRead == 0) break;
                    totalRead += chunkRead;
                }

                if (totalRead < msgLength) break;

                // Десериализуем
                var result = IpcSerializer.Deserialize(msgBuffer.AsSpan(0, totalRead));
                if (!result.IsSuccess)
                {
                    _logger.LogWarning("Failed to deserialize IPC message: {Error}", result.Error);
                    continue;
                }

                var message = result.Value!;

                // Обрабатываем
                if (OnMessageReceived is not null)
                {
                    await OnMessageReceived(message).ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (IOException ex)
        {
            _logger.LogError(ex, "Error reading from IPC client");
        }
        catch (ObjectDisposedException ex)
        {
            _logger.LogError(ex, "Error reading from IPC client");
        }
        finally
        {
            lock (_lock)
            {
                _connections.Remove(pipe);
            }

            try { pipe.Dispose(); } catch (ObjectDisposedException) { } catch (IOException) { }
            OnClientDisconnected?.Invoke();
            _logger.LogInformation("IPC client disconnected");
        }
    }

    // ── Создание pipe с ACL ─────────────────────────────────

    /// <summary>
    /// Читает ровно <paramref name="count"/> байт из потока.
    /// Named Pipe / TCP read может вернуть меньше запрошенного —
    /// этот метод дочитывает до полного заполнения буфера.
    /// </summary>
    private static async Task<bool> ReadExactAsync(Stream stream, byte[] buffer, CancellationToken ct)
    {
        int totalRead = 0;
        while (totalRead < buffer.Length)
        {
            int read = await stream.ReadAsync(buffer.AsMemory(totalRead, buffer.Length - totalRead), ct).ConfigureAwait(false);
            if (read == 0)
                return false; // EOF / pipe closed
            totalRead += read;
        }
        return true;
    }

    private static NamedPipeServerStream CreatePipeStream()
    {
        // ACL: restrict pipe access to local interactive users + SYSTEM only
        var security = new PipeSecurity();

        // SYSTEM (Worker service itself) — full control
        security.AddAccessRule(new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
            PipeAccessRights.FullControl,
            AccessControlType.Allow));

        // Administrators — full control (UI may run elevated)
        security.AddAccessRule(new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null),
            PipeAccessRights.FullControl,
            AccessControlType.Allow));

        // Interactive logon users only (not service accounts, not remote sessions)
        security.AddAccessRule(new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.InteractiveSid, null),
            PipeAccessRights.ReadWrite,
            AccessControlType.Allow));

        // Explicitly deny network access — prevent remote pipe exploitation
        security.AddAccessRule(new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.NetworkSid, null),
            PipeAccessRights.FullControl,
            AccessControlType.Deny));

        return NamedPipeServerStreamAcl.Create(
            PipeName,
            PipeDirection.InOut,
            NamedPipeServerStream.MaxAllowedServerInstances,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous,
            BufferSize,
            BufferSize,
            security);
    }

    // ── Внутренний Result (локальный, без зависимости ZUI.Core) ──

    public readonly struct Result
    {
        public bool IsSuccess { get; init; }
        public string? Error { get; init; }
        public static Result Success() => new() { IsSuccess = true };
        public static Result Failed(string error) => new() { IsSuccess = false, Error = error };
    }
}
