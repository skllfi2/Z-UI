// IAdaptiveEngine.cs - Adaptive protection engine interface
// Smart controller: DNS-first → IPC Worker — Worker is the only DPI bypass path

namespace ZUI.Services;

/// <summary>
/// Strategy type currently active in the adaptive engine.
/// </summary>
public enum AdaptiveStrategyType
{
    /// <summary>No bypass active.</summary>
    None,

    /// <summary>DNS-only bypass (dns.malw.link for AI services).</summary>
    DnsBypass,

    /// <summary>DPI bypass via IPC to Worker service (SYSTEM).</summary>
    DpiBypassWorker,

    /// <summary>Engine selected automatically — may combine DNS + DPI.</summary>
    AdaptiveAuto
}

/// <summary>
/// State of the DNS bypass subsystem.
/// </summary>
public enum DnsBypassState
{
    /// <summary>DNS bypass is not enabled.</summary>
    Disabled,

    /// <summary>DNS bypass is being checked / initialized.</summary>
    Checking,

    /// <summary>DNS bypass is active and resolving via malw.link.</summary>
    Active,

    /// <summary>DNS bypass failed or is unreachable.</summary>
    Failed
}

/// <summary>
/// Adaptive protection engine that coordinates bypass methods:
/// <list type="number">
/// <item>DNS-first bypass via EnhancedDnsManager (dns.malw.link)</item>
/// <item>DPI bypass via IPC to Worker service (requires Worker installed &amp; running)</item>
/// </list>
/// Replaces the legacy IProtectionService with smarter strategy selection.
/// </summary>
public interface IAdaptiveEngine
{
    /// <summary>
    /// Whether any bypass method is currently active (DPI or DNS).
    /// </summary>
    bool IsProtected { get; }

    /// <summary>
    /// Whether DNS proxy (DoH + Fake DNS) is running on the Worker.
    /// </summary>
    bool IsDnsProxyRunning { get; }

    /// <summary>
    /// Whether Proxifier (per-app routing) is active.
    /// </summary>
    bool IsProxifierRunning { get; }

    /// <summary>
    /// Whether any Telegram proxy (SOCKS5→WS or MTProxy) is active.
    /// </summary>
    bool IsTgProxyRunning { get; }

    /// <summary>
    /// Whether IPC connection to Worker service is established.
    /// </summary>
    bool IsWorkerConnected { get; }

    /// <summary>
    /// Current bypass process ID (from Worker or standalone winws.exe).
    /// </summary>
    int? ProcessId { get; }

    /// <summary>
    /// Which bypass strategy is currently active.
    /// </summary>
    AdaptiveStrategyType CurrentStrategy { get; }

    /// <summary>
    /// Human-readable name of the current strategy.
    /// </summary>
    string CurrentStrategyName { get; }

    /// <summary>
    /// Current state of the DNS bypass subsystem.
    /// </summary>
    DnsBypassState DnsBypassState { get; }

    /// <summary>
    /// Start adaptive protection with automatic strategy selection.
    /// Tries DNS bypass → IPC Worker. Returns error if Worker is not connected.
    /// </summary>
    Task<ProtectionResult> StartAdaptiveAsync(CancellationToken ct = default);

    /// <summary>
    /// Start bypass with a specific strategy ID and optional game filter mode.
    /// </summary>
    Task<ProtectionResult> StartWithStrategyAsync(
        string strategyId, int gameFilterMode = 0, CancellationToken ct = default);

    /// <summary>
    /// Stop all bypass methods (DPI + DNS) and reset to idle state.
    /// </summary>
    Task<ProtectionResult> StopAsync(CancellationToken ct = default);

    /// <summary>
    /// Refresh cached status from Worker (poll IPC status endpoints).
    /// </summary>
    Task RefreshStatusAsync(CancellationToken ct = default);

    /// <summary>
    /// Force a strategy refresh: stop current bypass and restart with "auto" strategy.
    /// </summary>
    Task<bool> ForceStrategyRefreshAsync(CancellationToken ct = default);
}
