// AdaptiveEngine.cs - Adaptive protection engine implementation
// Smart controller: DNS-first → IPC Worker (Worker is the only DPI bypass path)

using Microsoft.Extensions.Logging;
using ZUI.Ipc;

namespace ZUI.Services;

/// <summary>
/// Adaptive protection engine that coordinates bypass methods:
/// <list type="number">
/// <item>DNS-first bypass via EnhancedDnsManager (dns.malw.link)</item>
/// <item>DPI bypass via IPC to Worker service (SYSTEM)</item>
/// </list>
/// If Worker is not connected, returns an error prompting the user to install/start the Worker service.
/// Thread-safe: cached status fields guarded by <c>_statusLock</c>.
/// </summary>
public sealed class AdaptiveEngine : IAdaptiveEngine
{
    private readonly IIpcClientService _ipc;
    private readonly IAppSettingsService _settings;
    private readonly IEnhancedDnsManager? _dnsManager;
    private readonly ILogger<AdaptiveEngine> _logger;

    // ── Cached status from Worker (updated on RefreshStatusAsync or after Start/Stop) ──
    private BypassStatus? _bypassStatus;
    private ProxifierStatus? _proxifierStatus;
    private TgProxyStatus? _tgProxyStatus;
    private WorkerDnsStatus? _dnsStatus;

    // ── Adaptive engine state ──
    private AdaptiveStrategyType _currentStrategy;
    private string _currentStrategyName = string.Empty;
    private DnsBypassState _dnsBypassState;

    private readonly Lock _statusLock = new();

    // ── Public properties ──────────────────────────────────────────────

    public bool IsProtected
    {
        get
        {
            lock (_statusLock)
            {
                if (_bypassStatus?.IsRunning == true) return true;
                if (_dnsBypassState == DnsBypassState.Active) return true;
                return false;
            }
        }
    }

    public bool IsDnsProxyRunning
    {
        get { lock (_statusLock) { return _dnsStatus?.DohEnabled == true || _dnsStatus?.FakeDnsEnabled == true; } }
    }

    public bool IsProxifierRunning
    {
        get { lock (_statusLock) { return _proxifierStatus?.IsRunning ?? false; } }
    }

    public bool IsTgProxyRunning
    {
        get
        {
            lock (_statusLock)
            {
                return _tgProxyStatus?.Socks5Running == true || _tgProxyStatus?.MtProxyRunning == true;
            }
        }
    }

    public bool IsWorkerConnected => _ipc.IsConnected;

    public int? ProcessId => null; // Worker runs as SYSTEM, PID not exposed via IPC

    public AdaptiveStrategyType CurrentStrategy
    {
        get { lock (_statusLock) { return _currentStrategy; } }
    }

    public string CurrentStrategyName
    {
        get { lock (_statusLock) { return _currentStrategyName; } }
    }

    public DnsBypassState DnsBypassState
    {
        get { lock (_statusLock) { return _dnsBypassState; } }
    }

    // ── Constructor ────────────────────────────────────────────────────

    public AdaptiveEngine(
        IIpcClientService ipc,
        IAppSettingsService settings,
        ILogger<AdaptiveEngine> logger,
        IEnhancedDnsManager? dnsManager = null)
    {
        _ipc = ipc ?? throw new ArgumentNullException(nameof(ipc));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _dnsManager = dnsManager;

        // Subscribe to bypass-stopped event from Worker
        _ipc.OnBypassStopped += HandleBypassStopped;
    }

    // ── IAdaptiveEngine ────────────────────────────────────────────────

    public async Task<ProtectionResult> StartAdaptiveAsync(CancellationToken ct = default)
    {
        _logger.LogInformation("Starting adaptive protection (auto strategy)");

        // ── Phase 1: Try DNS-first bypass via EnhancedDnsManager ──
        lock (_statusLock)
        {
            _dnsBypassState = DnsBypassState.Checking;
        }

        if (_dnsManager is not null)
        {
            try
            {
                await _dnsManager.EnableDnsBypassAsync(ct).ConfigureAwait(false);

                lock (_statusLock)
                {
                    _dnsBypassState = _dnsManager.State;
                }

                if (_dnsBypassState == DnsBypassState.Active)
                {
                    _logger.LogInformation("DNS bypass active — domains resolve via dns.malw.link");
                }
                else
                {
                    _logger.LogInformation("DNS bypass not available (state={State}), proceeding to DPI bypass", _dnsBypassState);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "DNS bypass failed, proceeding to DPI bypass");
                lock (_statusLock)
                {
                    _dnsBypassState = DnsBypassState.Failed;
                }
            }
        }
        else
        {
            _logger.LogInformation("EnhancedDnsManager not available — skipping DNS-first phase");
            lock (_statusLock)
            {
                _dnsBypassState = DnsBypassState.Disabled;
            }
        }

        // ── Phase 2: IPC Worker (only DPI bypass path) ──
        if (_ipc.IsConnected)
        {
            return await StartViaIpcAsync("auto", gameFilterMode: 0, ct).ConfigureAwait(false);
        }

        _logger.LogError("Cannot start DPI bypass: Worker not connected via IPC");
        return ProtectionResult.Failed(
            "Нет связи с сервисом Worker. Установите и запустите службу Worker для работы DPI обхода.");
    }

    public async Task<ProtectionResult> StartWithStrategyAsync(
        string strategyId, int gameFilterMode = 0, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(strategyId);

        _logger.LogInformation("Starting adaptive protection with strategy {StrategyId} (gameFilter={GameFilter})", 
            strategyId, gameFilterMode);

        // ── DNS bypass: attempt if EnhancedDnsManager is available ──
        if (_dnsManager is not null)
        {
            try
            {
                await _dnsManager.EnableDnsBypassAsync(ct).ConfigureAwait(false);
                lock (_statusLock)
                {
                    _dnsBypassState = _dnsManager.State;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "DNS bypass failed for strategy start");
                lock (_statusLock)
                {
                    _dnsBypassState = DnsBypassState.Failed;
                }
            }
        }
        else
        {
            lock (_statusLock)
            {
                _dnsBypassState = DnsBypassState.Disabled;
            }
        }

        // ── IPC Worker path (only DPI bypass path) ──
        if (_ipc.IsConnected)
        {
            return await StartViaIpcAsync(strategyId, gameFilterMode, ct).ConfigureAwait(false);
        }

        return ProtectionResult.Failed(
            "Нет связи с сервисом Worker. Установите и запустите службу Worker для работы DPI обхода.");
    }

    public async Task<ProtectionResult> StopAsync(CancellationToken ct = default)
    {
        _logger.LogInformation("Stopping adaptive protection");

        var dpiStopped = true;
        var dnsStopped = true;

        // ── Stop DPI bypass via Worker ──
        if (_ipc.IsConnected)
        {
            try
            {
                var result = await _ipc.StopBypassAsync(ct).ConfigureAwait(false);
                if (!result.IsSuccess)
                {
                    _logger.LogWarning("Failed to stop DPI bypass via IPC: {Error}", result.Error);
                    dpiStopped = false;
                }
            }
            catch (IOException ex)
            {
                _logger.LogWarning(ex, "IPC error stopping DPI bypass");
                dpiStopped = false;
            }
            catch (TimeoutException ex)
            {
                _logger.LogWarning(ex, "IPC timeout stopping DPI bypass");
                dpiStopped = false;
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogWarning(ex, "IPC error stopping DPI bypass");
                dpiStopped = false;
            }

            // ── Disable DNS on Worker ──
            try
            {
                await _ipc.ConfigureDnsAsync(
                    enableDoh: false, enableFakeDns: false, ct: ct).ConfigureAwait(false);
                _logger.LogInformation("DNS disabled on Worker");
            }
            catch (IOException ex)
            {
                _logger.LogWarning(ex, "Failed to disable DNS on Worker");
                dnsStopped = false;
            }
            catch (TimeoutException ex)
            {
                _logger.LogWarning(ex, "Failed to disable DNS on Worker");
                dnsStopped = false;
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogWarning(ex, "Failed to disable DNS on Worker");
                dnsStopped = false;
            }
        }

        // ── Stop DNS bypass via EnhancedDnsManager ──
        if (_dnsManager is not null)
        {
            try
            {
                await _dnsManager.DisableDnsBypassAsync(ct).ConfigureAwait(false);
                _logger.LogInformation("DNS bypass disabled");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to disable DNS bypass (non-critical)");
                dnsStopped = false;
            }
        }

        // ── Reset adaptive state ──
        lock (_statusLock)
        {
            _currentStrategy = AdaptiveStrategyType.None;
            _currentStrategyName = string.Empty;
            _dnsBypassState = DnsBypassState.Disabled;
        }

        // ── Refresh cached status ──
        try
        {
            await RefreshStatusAsync(ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to refresh status after stop (non-critical)");
        }

        if (dpiStopped && dnsStopped)
        {
            return ProtectionResult.Succeeded(
                "Защита остановлена\n• DPI bypass остановлен\n• DNS отключён");
        }

        if (dpiStopped)
        {
            return ProtectionResult.Succeeded(
                "DPI bypass остановлен, но не удалось отключить DNS на Worker");
        }

        return ProtectionResult.Failed("Не удалось полностью остановить защиту");
    }

    public async Task RefreshStatusAsync(CancellationToken ct = default)
    {
        if (!_ipc.IsConnected)
            return;

        try
        {
            // Poll all status endpoints in parallel
            var bypassTask = _ipc.GetBypassStatusAsync(ct);
            var proxifierTask = _ipc.GetProxifierStatusAsync(ct);
            var tgTask = _ipc.GetTgProxyStatusAsync(ct);
            var dnsTask = _ipc.GetDnsStatusAsync(ct);

            await Task.WhenAll(bypassTask, proxifierTask, tgTask, dnsTask).ConfigureAwait(false);

            var bypassResult = await bypassTask.ConfigureAwait(false);
            var proxifierResult = await proxifierTask.ConfigureAwait(false);
            var tgResult = await tgTask.ConfigureAwait(false);
            var dnsResult = await dnsTask.ConfigureAwait(false);

            lock (_statusLock)
            {
                if (bypassResult.IsSuccess) _bypassStatus = bypassResult.Value;
                if (proxifierResult.IsSuccess) _proxifierStatus = proxifierResult.Value;
                if (tgResult.IsSuccess) _tgProxyStatus = tgResult.Value;
                if (dnsResult.IsSuccess) _dnsStatus = dnsResult.Value;
            }
        }
        catch (IOException ex)
        {
            _logger.LogDebug(ex, "Failed to refresh status from Worker");
        }
        catch (TimeoutException ex)
        {
            _logger.LogDebug(ex, "Failed to refresh status from Worker");
        }
        catch (System.Runtime.InteropServices.COMException ex)
        {
            _logger.LogDebug(ex, "Failed to refresh status from Worker (COM)");
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogDebug(ex, "Failed to refresh status from Worker");
        }
    }

    public async Task<bool> ForceStrategyRefreshAsync(CancellationToken ct = default)
    {
        _logger.LogInformation("Force strategy refresh: stopping and restarting with auto strategy");

        var stopResult = await StopAsync(ct).ConfigureAwait(false);
        if (!stopResult.Success)
        {
            _logger.LogWarning("Force strategy refresh failed: could not stop current bypass");
            return false;
        }

        var startResult = await StartAdaptiveAsync(ct).ConfigureAwait(false);
        return startResult.Success;
    }

    // ── Private helpers ────────────────────────────────────────────────

    /// <summary>
    /// Start bypass via IPC to Worker service. Configures DNS first, then starts DPI bypass.
    /// </summary>
    private async Task<ProtectionResult> StartViaIpcAsync(
        string strategyId, int gameFilterMode, CancellationToken ct)
    {
        _logger.LogInformation("Starting protection via IPC (strategy={Strategy}, gameFilter={GameFilter})",
            strategyId, gameFilterMode);

        // 1. Configure DNS on Worker if enabled
        try
        {
            var dnsResult = await _ipc.ConfigureDnsAsync(
                enableDoh: true, enableFakeDns: true, ct: ct).ConfigureAwait(false);

            if (!dnsResult.IsSuccess)
            {
                _logger.LogWarning("Failed to configure DNS on Worker: {Error}", dnsResult.Error);
                // Continue anyway — DNS is optional
            }
            else
            {
                _logger.LogInformation("DNS configured on Worker");
            }
        }
        catch (IOException ex)
        {
            _logger.LogWarning(ex, "Failed to configure DNS, continuing with DPI bypass only");
        }
        catch (TimeoutException ex)
        {
            _logger.LogWarning(ex, "Failed to configure DNS, continuing with DPI bypass only");
        }

        // 2. Start DPI bypass
        try
        {
            var result = await _ipc.StartBypassAsync(
                strategyId: strategyId,
                gameFilterMode: gameFilterMode,
                ct: ct).ConfigureAwait(false);

            if (result.IsSuccess)
            {
                _logger.LogInformation("DPI bypass started via IPC");

                // Refresh cached status
                try
                {
                    await RefreshStatusAsync(ct).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Failed to refresh status after start (non-critical)");
                }

                // Update adaptive state
                lock (_statusLock)
                {
                    _currentStrategy = strategyId == "auto"
                        ? AdaptiveStrategyType.AdaptiveAuto
                        : AdaptiveStrategyType.DpiBypassWorker;
                    _currentStrategyName = strategyId;
                }

                // Build result message
                var message = "Защита активирована";
                if (IsProtected) message += "\n• DPI bypass: запущен";
                if (IsDnsProxyRunning) message += "\n• DNS: DoH + Fake DNS";

                return ProtectionResult.Succeeded(message);
            }

            _logger.LogError("Failed to start DPI bypass via IPC: {Error}", result.Error);

            // Partial success — DNS may have started even if bypass failed
            if (IsDnsProxyRunning)
            {
                lock (_statusLock)
                {
                    _currentStrategy = AdaptiveStrategyType.DnsBypass;
                    _currentStrategyName = "DNS-only (DPI failed)";
                }

                return ProtectionResult.Failed(
                    $"DPI bypass не запущен: {result.Error}\nНо DNS настроен — сайты могут открываться.");
            }

            return ProtectionResult.Failed(result.Error ?? "Не удалось запустить защиту");
        }
        catch (IOException ex)
        {
            _logger.LogError(ex, "IPC error starting DPI bypass");
            return ProtectionResult.Failed($"IPC ошибка: {ex.Message}");
        }
        catch (TimeoutException ex)
        {
            _logger.LogError(ex, "IPC timeout starting DPI bypass");
            return ProtectionResult.Failed($"IPC таймаут: {ex.Message}");
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogError(ex, "IPC error starting DPI bypass");
            return ProtectionResult.Failed($"IPC ошибка: {ex.Message}");
        }
    }

    /// <summary>
    /// Handle bypass-stopped event from Worker — update cached state.
    /// </summary>
    private void HandleBypassStopped(BypassStoppedEvent evt)
    {
        _logger.LogWarning("Bypass stopped unexpectedly on Worker: {Reason}", evt.Reason);

        lock (_statusLock)
        {
            _bypassStatus = new BypassStatus { IsRunning = false };
            _currentStrategy = AdaptiveStrategyType.None;
            _currentStrategyName = string.Empty;
        }
    }
}
