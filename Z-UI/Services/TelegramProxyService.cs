// TelegramProxyService.cs - UI-side service for Telegram proxy (SOCKS5→WS + MTProxy)
// Thin IPC wrapper — actual logic runs in Worker (ZUI.Telegram)

using Microsoft.Extensions.Logging;
using ZUI.Ipc;

namespace ZUI.Services;

/// <summary>
/// UI-side service for managing Telegram proxy.
/// Sends IPC requests to the Worker which runs the TgWsProxy and MtProxy servers.
/// </summary>
public interface ITelegramProxyService
{
    /// <summary>Whether any Telegram proxy (SOCKS5 or MTProxy) is active on the Worker.</summary>
    bool IsRunning { get; }

    /// <summary>Current Telegram proxy status.</summary>
    TgProxyStatus? Status { get; }

    /// <summary>Start SOCKS5→WebSocket proxy on the Worker.</summary>
    Task<Result> StartSocks5Async(int port, string wsUrl, string secret, CancellationToken ct = default);

    /// <summary>Stop SOCKS5→WebSocket proxy on the Worker.</summary>
    Task<Result> StopSocks5Async(CancellationToken ct = default);

    /// <summary>Start MTProxy server on the Worker.</summary>
    Task<Result> StartMtProxyAsync(int port, string secret, CancellationToken ct = default);

    /// <summary>Stop MTProxy server on the Worker.</summary>
    Task<Result> StopMtProxyAsync(CancellationToken ct = default);

    /// <summary>Stop all Telegram proxy services.</summary>
    Task<Result> StopAllAsync(CancellationToken ct = default);

    /// <summary>Refresh cached status from Worker.</summary>
    Task RefreshStatusAsync(CancellationToken ct = default);

    /// <summary>Generate tg:// proxy link for sharing.</summary>
    string GenerateProxyLink(int port, string secret, bool isMtProxy = true);
}

/// <summary>
/// Implementation of ITelegramProxyService using IPC to the Worker.
/// </summary>
public sealed class TelegramProxyService : ITelegramProxyService
{
    private readonly IIpcClientService _ipc;
    private readonly ILogger<TelegramProxyService> _logger;

    private TgProxyStatus? _status;
    private readonly object _lock = new();

    public bool IsRunning
    {
        get
        {
            lock (_lock)
            {
                return _status?.Socks5Running == true || _status?.MtProxyRunning == true;
            }
        }
    }

    public TgProxyStatus? Status
    {
        get { lock (_lock) { return _status; } }
    }

    public TelegramProxyService(IIpcClientService ipc, ILogger<TelegramProxyService> logger)
    {
        _ipc = ipc ?? throw new ArgumentNullException(nameof(ipc));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        _ipc.OnTgProxyClientConnected += HandleClientConnected;
    }

    public async Task<Result> StartSocks5Async(int port, string wsUrl, string secret, CancellationToken ct = default)
    {
        _logger.LogInformation("Starting SOCKS5→WS proxy on port {Port} via IPC", port);

        if (!_ipc.IsConnected)
            return Result.Failed("Нет связи с сервисом (Worker)");

        var result = await _ipc.StartTgWsProxyAsync(port, wsUrl, secret, ct).ConfigureAwait(false);

        if (result.IsSuccess)
        {
            await RefreshStatusAsync(ct).ConfigureAwait(false);
            _logger.LogInformation("SOCKS5→WS proxy started on port {Port}", port);
        }
        else
        {
            _logger.LogWarning("Failed to start SOCKS5→WS proxy: {Error}", result.Error);
        }

        return result;
    }

    public async Task<Result> StopSocks5Async(CancellationToken ct = default)
    {
        _logger.LogInformation("Stopping SOCKS5→WS proxy via IPC");

        if (!_ipc.IsConnected)
            return Result.Failed("Нет связи с сервисом (Worker)");

        var result = await _ipc.StopTgWsProxyAsync(ct).ConfigureAwait(false);

        if (result.IsSuccess)
        {
            await RefreshStatusAsync(ct).ConfigureAwait(false);
            _logger.LogInformation("SOCKS5→WS proxy stopped");
        }

        return result;
    }

    public async Task<Result> StartMtProxyAsync(int port, string secret, CancellationToken ct = default)
    {
        _logger.LogInformation("Starting MTProxy on port {Port} via IPC", port);

        if (!_ipc.IsConnected)
            return Result.Failed("Нет связи с сервисом (Worker)");

        var result = await _ipc.StartMtProxyAsync(port, secret, ct).ConfigureAwait(false);

        if (result.IsSuccess)
        {
            await RefreshStatusAsync(ct).ConfigureAwait(false);
            _logger.LogInformation("MTProxy started on port {Port}", port);
        }
        else
        {
            _logger.LogWarning("Failed to start MTProxy: {Error}", result.Error);
        }

        return result;
    }

    public async Task<Result> StopMtProxyAsync(CancellationToken ct = default)
    {
        _logger.LogInformation("Stopping MTProxy via IPC");

        if (!_ipc.IsConnected)
            return Result.Failed("Нет связи с сервисом (Worker)");

        var result = await _ipc.StopMtProxyAsync(ct).ConfigureAwait(false);

        if (result.IsSuccess)
        {
            await RefreshStatusAsync(ct).ConfigureAwait(false);
            _logger.LogInformation("MTProxy stopped");
        }

        return result;
    }

    public async Task<Result> StopAllAsync(CancellationToken ct = default)
    {
        _logger.LogInformation("Stopping all Telegram proxy services via IPC");

        if (!_ipc.IsConnected)
            return Result.Failed("Нет связи с сервисом (Worker)");

        var wsResult = await _ipc.StopTgWsProxyAsync(ct).ConfigureAwait(false);
        var mtResult = await _ipc.StopMtProxyAsync(ct).ConfigureAwait(false);

        await RefreshStatusAsync(ct).ConfigureAwait(false);

        if (!wsResult.IsSuccess)
            return Result.Failed($"Ошибка остановки SOCKS5: {wsResult.Error}");
        if (!mtResult.IsSuccess)
            return Result.Failed($"Ошибка остановки MTProxy: {mtResult.Error}");

        return Result.Success();
    }

    public async Task RefreshStatusAsync(CancellationToken ct = default)
    {
        if (!_ipc.IsConnected)
            return;

        var result = await _ipc.GetTgProxyStatusAsync(ct).ConfigureAwait(false);
        if (result.IsSuccess && result.Value != null)
        {
            lock (_lock) { _status = result.Value; }
        }
    }

    public string GenerateProxyLink(int port, string secret, bool isMtProxy = true)
    {
        if (isMtProxy)
        {
            return $"tg://proxy?server=YOUR_SERVER_IP&port={port}&secret={secret}";
        }
        else
        {
            return $"tg://socks?server=YOUR_SERVER_IP&port={port}&secret={secret}";
        }
    }

    private void HandleClientConnected(TgProxyClientConnectedEvent evt)
    {
        _logger.LogInformation("Telegram proxy client connected from {ClientIp}", evt.ClientIp);
    }
}
