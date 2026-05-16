// ═══════════════════════════════════════════════════════════════
// ZUI.Ipc / IpcPipeClient.cs
// Named Pipe клиент для UI → Worker Service (SYSTEM)
// Автопереподключение, ping/pong, корреляция запрос-ответ
// ═══════════════════════════════════════════════════════════════

using System.IO.Pipes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace ZUI.Ipc;

/// <summary>
/// Named Pipe клиент для UI → Worker IPC.
/// Формат: длина (4 байта, little-endian) + JSON байты.
/// Автопереподключение при обрыве, ping/pong для проверки связи.
/// </summary>
public sealed class IpcPipeClient : IAsyncDisposable
{
    private const string PipeName = "ZUI_IPC";
    private const int MaxMessageSize = 1024 * 1024; // 1 MB
    private const int BufferSize = 65536;
    private const int ReconnectDelayMs = 2000;
    private const int ReconnectMaxDelayMs = 60000; // Exponential backoff cap
    private const int ReconnectLogThrottleMs = 30000; // Log reconnect attempts at most every 30s
    private const int PingIntervalMs = 15000;
    private const int RequestTimeoutMs = 3000;

    private readonly ILogger _logger;
    private readonly Lock _lock = new();
    private NamedPipeClientStream? _pipe;
    private CancellationTokenSource? _cts;
    private Task? _readLoopTask;
    private Task? _pingTask;
    private int _isConnected;
    private int _reconnectAttempt; // For exponential backoff
    private DateTime _lastReconnectLog = DateTime.MinValue; // For log throttling
    private Task? _reconnectTask;

    // Корреляция запрос-ответ по MessageId
    private readonly Dictionary<Guid, TaskCompletionSource<IpcResponse>> _pendingRequests = new();
    private readonly Lock _pendingLock = new();

    /// <summary>Событие: подключено к Worker.</summary>
    public event Action? OnConnected;

    /// <summary>Событие: отключено от Worker.</summary>
    public event Action? OnDisconnected;

    /// <summary>Событие: получено событие от Worker (unsolicited).</summary>
    public event Action<IpcEvent>? OnEventReceived;

    /// <summary>Событие: получен ответ, не ожидающийся ни одним запросом.</summary>
    public event Action<IpcResponse>? OnOrphanedResponse;

    public IpcPipeClient(ILogger<IpcPipeClient>? logger = null)
    {
        _logger = logger ?? NullLoggerFactory.Instance.CreateLogger<IpcPipeClient>();
    }

    public bool IsConnected => Volatile.Read(ref _isConnected) == 1;

    // ── Подключение ────────────────────────────────────────

    /// <summary>
    /// Подключиться к Worker Service Named Pipe.
    /// Запускает фоновый цикл чтения + ping/pong.
    /// </summary>
    public async Task<Result> ConnectAsync(CancellationToken ct = default)
    {
        if (Volatile.Read(ref _isConnected) == 1)
            return Result.Failed("Already connected.");

        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);

        try
        {
            await ConnectPipeAsync(_cts.Token).ConfigureAwait(false);

            _readLoopTask = ReadLoopAsync(_cts.Token);
            _pingTask = PingLoopAsync(_cts.Token);

            return Result.Success();
        }
        catch (IOException ex)
        {
            _logger.LogError(ex, "Failed to connect to IPC server");
            return Result.Failed($"Connection failed: {ex.Message}");
        }
        catch (TimeoutException ex)
        {
            _logger.LogError(ex, "Failed to connect to IPC server");
            return Result.Failed($"Connection failed: {ex.Message}");
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogError(ex, "Failed to connect to IPC server");
            return Result.Failed($"Connection failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Подключиться с автопереподключением (exponential backoff).
    /// </summary>
    public async Task ConnectWithRetryAsync(CancellationToken ct = default)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _reconnectAttempt = 0;

        while (!_cts.Token.IsCancellationRequested)
        {
            try
            {
                await ConnectPipeAsync(_cts.Token).ConfigureAwait(false);

                // Запускаем циклы
                _readLoopTask = ReadLoopAsync(_cts.Token);
                _pingTask = PingLoopAsync(_cts.Token);

                _reconnectAttempt = 0; // Reset on successful connect
                return;
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex) when (ex is IOException or TimeoutException or UnauthorizedAccessException)
            {
                _reconnectAttempt++;
                var backoffShift = Math.Min(_reconnectAttempt - 1, 5);
                var delay = Math.Min(ReconnectDelayMs * (1 << backoffShift), ReconnectMaxDelayMs);

                // Throttle: log at most once every ReconnectLogThrottleMs
                var now = DateTime.UtcNow;
                if ((now - _lastReconnectLog).TotalMilliseconds >= ReconnectLogThrottleMs || _reconnectAttempt <= 2)
                {
                    _logger.LogDebug(ex, "IPC connection attempt {Attempt} failed, retrying in {Delay}ms", _reconnectAttempt, delay);
                    _lastReconnectLog = now;
                }

                try
                {
                    await Task.Delay(delay, _cts.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
        }
    }
    }

    // ── Отправка запроса с ожиданием ответа ────────────────

    /// <summary>
    /// Отправить запрос Worker и дождаться ответа (с таймаутом).
    /// Корреляция по MessageId.
    /// </summary>
    public async Task<Result<IpcResponse>> SendRequestAsync(IpcRequest request, CancellationToken ct = default)
    {
        if (!IsConnected)
            return Result<IpcResponse>.Failed("Not connected to Worker.");

        var tcs = new TaskCompletionSource<IpcResponse>(TaskCreationOptions.RunContinuationsAsynchronously);

        lock (_pendingLock)
        {
            _pendingRequests[request.MessageId] = tcs;
        }

        try
        {
            // Отправляем запрос
            var sendResult = SendMessageAsync(request, ct);
            var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(RequestTimeoutMs);

            // Ждём ответ с таймаутом
            using var timeoutReg = timeoutCts.Token.Register(() => tcs.TrySetResult(
                new ErrorResponse("Request timed out") { RequestId = request.MessageId }));

            var response = await tcs.Task.ConfigureAwait(false);

            // Проверяем тип ответа
            if (response is ErrorResponse err)
                return Result<IpcResponse>.Failed(err.Message);

            return Result<IpcResponse>.Success(response);
        }
        catch (OperationCanceledException)
        {
            return Result<IpcResponse>.Failed("Request cancelled.");
        }
        finally
        {
            lock (_pendingLock)
            {
                _pendingRequests.Remove(request.MessageId);
            }
        }
    }

    // ── Отправка сообщения без ожидания ответа ─────────────

    /// <summary>
    /// Отправить сообщение Worker без ожидания ответа (fire-and-forget).
    /// </summary>
    public async Task<Result> SendMessageAsync(IpcMessage message, CancellationToken ct = default)
    {
        if (!IsConnected)
            return Result.Failed("Not connected to Worker.");

        NamedPipeClientStream pipe;
        lock (_lock)
        {
            pipe = _pipe!;
        }

        if (pipe is null || !pipe.IsConnected)
            return Result.Failed("Pipe is not connected.");

        try
        {
            var data = IpcSerializer.Serialize(message);
            var lengthBytes = BitConverter.GetBytes(data.Length);

            await pipe.WriteAsync(lengthBytes, ct).ConfigureAwait(false);
            await pipe.WriteAsync(data, ct).ConfigureAwait(false);
            await pipe.FlushAsync(ct).ConfigureAwait(false);

            return Result.Success();
        }
        catch (IOException ex)
        {
            _logger.LogError(ex, "Failed to send IPC message");
            SetDisconnected();
            return Result.Failed($"Send failed: {ex.Message}");
        }
        catch (ObjectDisposedException ex)
        {
            _logger.LogError(ex, "Failed to send IPC message");
            SetDisconnected();
            return Result.Failed($"Send failed: {ex.Message}");
        }
        catch (TimeoutException ex)
        {
            _logger.LogError(ex, "Failed to send IPC message");
            SetDisconnected();
            return Result.Failed($"Send failed: {ex.Message}");
        }
    }

    // ── Отключение ─────────────────────────────────────────

    public async ValueTask DisposeAsync()
    {
        Volatile.Write(ref _isConnected, 0);
        _cts?.Cancel();

        if (_readLoopTask is not null)
        {
            try { await _readLoopTask.ConfigureAwait(false); } catch (ObjectDisposedException) { /* shutdown */ } catch (IOException) { /* shutdown */ }
        }
        if (_pingTask is not null)
        {
            try { await _pingTask.ConfigureAwait(false); } catch (ObjectDisposedException) { /* shutdown */ } catch (IOException) { /* shutdown */ }
        }
        if (_reconnectTask is not null)
        {
            try { await _reconnectTask.ConfigureAwait(false); } catch (ObjectDisposedException) { /* shutdown */ } catch (IOException) { /* shutdown */ }
        }

        lock (_lock)
        {
            _pipe?.Dispose();
            _pipe = null;
        }

        // Cancel all pending requests
        CancelPendingRequests();

        _cts?.Dispose();
        _logger.LogInformation("IPC pipe client disposed");
    }

    // ── Внутренние методы ──────────────────────────────────

    private async Task ConnectPipeAsync(CancellationToken ct)
    {
        _logger.LogInformation("Connecting to IPC pipe server...");

        var newPipe = new NamedPipeClientStream(
            ".", PipeName,
            PipeDirection.InOut,
            PipeOptions.Asynchronous);

        await newPipe.ConnectAsync(2000, ct).ConfigureAwait(false);

        lock (_lock)
        {
            _pipe?.Dispose();
            _pipe = newPipe;
        }

        Volatile.Write(ref _isConnected, 1);
        OnConnected?.Invoke();
        _logger.LogInformation("Connected to IPC pipe server");
    }

    /// <summary>
    /// Цикл чтения сообщений от Worker.
    /// Обрабатывает ответы (по RequestId) и события.
    /// </summary>
    private async Task ReadLoopAsync(CancellationToken ct)
    {
        var lengthBuffer = new byte[4];

        try
        {
            while (!ct.IsCancellationRequested && IsConnected)
            {
                NamedPipeClientStream pipe;
                lock (_lock)
                {
                    pipe = _pipe!;
                }

                if (pipe is null || !pipe.IsConnected)
                {
                    SetDisconnected();
                    break;
                }

                // Читаем длину (ровно 4 байта — partial read возможен на Named Pipes)
                if (!await ReadExactAsync(pipe, lengthBuffer, ct).ConfigureAwait(false))
                {
                    _logger.LogWarning("IPC pipe closed (length prefix read failed)");
                    SetDisconnected();
                    break;
                }

                int msgLength = BitConverter.ToInt32(lengthBuffer);
                if (msgLength <= 0 || msgLength > MaxMessageSize)
                {
                    _logger.LogWarning("Invalid message length: {Length}", msgLength);
                    SetDisconnected();
                    break;
                }

                // Читаем сообщение
                var msgBuffer = new byte[msgLength];
                int totalRead = 0;
                while (totalRead < msgLength)
                {
                    int chunkRead = await pipe.ReadAsync(msgBuffer.AsMemory(totalRead, msgLength - totalRead), ct).ConfigureAwait(false);
                    if (chunkRead == 0)
                    {
                        SetDisconnected();
                        break;
                    }
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

                // Маршрутизация: Response → pending request, Event → event handler
                switch (message)
                {
                    case IpcResponse response:
                        HandleResponse(response);
                        break;
                    case IpcEvent evt:
                        OnEventReceived?.Invoke(evt);
                        break;
                    default:
                        _logger.LogDebug("Received unexpected message type: {Type}", message.GetType().Name);
                        break;
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (IOException ex)
        {
            _logger.LogError(ex, "Error in IPC read loop");
            SetDisconnected();
        }
        catch (ObjectDisposedException ex)
        {
            _logger.LogError(ex, "Error in IPC read loop");
            SetDisconnected();
        }
    }

    /// <summary>
    /// Обработать ответ от Worker: найти ожидающий запрос по RequestId.
    /// </summary>
    private void HandleResponse(IpcResponse response)
    {
        TaskCompletionSource<IpcResponse>? tcs;
        lock (_pendingLock)
        {
            _pendingRequests.TryGetValue(response.RequestId, out tcs);
            if (tcs is not null)
                _pendingRequests.Remove(response.RequestId);
        }

        if (tcs is not null)
        {
            tcs.TrySetResult(response);
        }
        else
        {
            // Ответ без ожидающего запроса (orphaned)
            _logger.LogDebug("Received response without pending request: {Type}, RequestId={RequestId}",
                response.GetType().Name, response.RequestId);
            OnOrphanedResponse?.Invoke(response);
        }
    }

    /// <summary>
    /// Цикл ping для поддержания связи.
    /// Использует fire-and-forget (SendMessageAsync) — любой ответ от Worker
    /// (даже ErrorResponse) доказывает, что соединение живо.
    /// ReadLoopAsync обработает PongResponse, если он придёт.
    /// </summary>
    private async Task PingLoopAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested && IsConnected)
            {
                await Task.Delay(PingIntervalMs, ct).ConfigureAwait(false);

                if (!IsConnected) break;

                var ping = new PingRequest();
                var sendResult = await SendMessageAsync(ping, ct).ConfigureAwait(false);

                if (!sendResult.IsSuccess)
                {
                    _logger.LogWarning("Ping send failed: {Error}", sendResult.Error);
                    SetDisconnected();
                    break;
                }
                // Send succeeded → connection is alive.
                // Response (PongResponse or ErrorResponse) is handled by ReadLoopAsync.
            }
        }
        catch (OperationCanceledException) { }
        catch (IOException)
        {
            SetDisconnected();
        }
        catch (ObjectDisposedException)
        {
            SetDisconnected();
        }
    }

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

    /// <summary>
    /// Observe a task to prevent unobserved exceptions.
    /// Swallows all expected exceptions (IOException, ObjectDisposedException, OperationCanceledException).
    /// </summary>
    private static async Task ObserveTaskAsync(Task task)
    {
        try
        {
            await task.ConfigureAwait(false);
        }
        catch (OperationCanceledException) { }
        catch (IOException) { }
        catch (ObjectDisposedException) { }
    }

    private void SetDisconnected()
    {
        if (Interlocked.Exchange(ref _isConnected, 0) == 1)
        {
            // Cancel all pending requests — responses will never arrive
            CancelPendingRequests();

            OnDisconnected?.Invoke();
            _logger.LogInformation("Disconnected from IPC pipe server");

            // Auto-reconnect only if CTS is still active (not disposed)
            if (_cts is not null && !_cts.IsCancellationRequested)
            {
                // Fire-and-forget auto-reconnect (exponential backoff)
                Volatile.Write(ref _reconnectTask, ReconnectLoopAsync());
            }
        }
    }

    /// <summary>
    /// Cancel all pending request TCS — called on disconnect/dispose.
    /// </summary>
    private void CancelPendingRequests()
    {
        lock (_pendingLock)
        {
            foreach (var tcs in _pendingRequests.Values)
            {
                tcs.TrySetCanceled();
            }
            _pendingRequests.Clear();
        }
    }

    /// <summary>
    /// Автопереподключение после обрыва связи.
    /// Запускается из SetDisconnected() как fire-and-forget.
    /// Экспоненциальная задержка (2s → 60s), throttled логирование.
    /// </summary>
    private async Task ReconnectLoopAsync()
    {
        _reconnectAttempt = 0;

        var ct = _cts?.Token ?? CancellationToken.None;

        while (!ct.IsCancellationRequested)
        {
            // Clean up old pipe, read loop, ping loop
            lock (_lock)
            {
                _pipe?.Dispose();
                _pipe = null;
            }

            _reconnectAttempt++;
            var backoffShift = Math.Min(_reconnectAttempt - 1, 5);
            var delay = Math.Min(ReconnectDelayMs * (1 << backoffShift), ReconnectMaxDelayMs);

            // Throttle: log at most once every ReconnectLogThrottleMs
            var now = DateTime.UtcNow;
            if ((now - _lastReconnectLog).TotalMilliseconds >= ReconnectLogThrottleMs || _reconnectAttempt <= 2)
            {
                _logger.LogDebug("IPC auto-reconnect attempt {Attempt}, next in {Delay}ms", _reconnectAttempt, delay);
                _lastReconnectLog = now;
            }

            // Wait with exponential backoff
            try
            {
                await Task.Delay(delay, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }

        // Try to connect
        try
        {
            await ConnectPipeAsync(ct).ConfigureAwait(false);

            // Success — start new read + ping loops (old tasks already completed since pipe was disposed)
            var oldRead = _readLoopTask;
            var oldPing = _pingTask;

            _readLoopTask = ReadLoopAsync(ct);
            _pingTask = PingLoopAsync(ct);
            _reconnectAttempt = 0;
            Volatile.Write(ref _reconnectTask, null);

            // Observe old tasks to prevent unobserved exceptions
            if (oldRead is not null) _ = ObserveTaskAsync(oldRead);
            if (oldPing is not null) _ = ObserveTaskAsync(oldPing);

            return;
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex) when (ex is IOException or TimeoutException or UnauthorizedAccessException)
            {
                // Log throttled above; loop again
            }
        }

        // Cancelled or disposed
        Volatile.Write(ref _reconnectTask, null);
    }

    // ── Внутренний Result (локальный, без зависимости ZUI.Core) ──

    public readonly struct Result
    {
        public bool IsSuccess { get; init; }
        public string? Error { get; init; }
        public static Result Success() => new() { IsSuccess = true };
        public static Result Failed(string error) => new() { IsSuccess = false, Error = error };
    }

    public readonly struct Result<T>
    {
        public bool IsSuccess { get; init; }
        public T? Value { get; init; }
        public string? Error { get; init; }
        public static Result<T> Success(T value) => new() { IsSuccess = true, Value = value };
        public static Result<T> Failed(string error) => new() { IsSuccess = false, Error = error };
    }
}
