// IDashboardStatusService.cs - Aggregated status provider for DashboardViewModel
// Replaces direct access to IDnsService, IProxifierService, ITelegramProxyService, IDiagnosticsService

namespace ZUI.Services;

/// <summary>
/// Aggregated status service that collects dashboard-relevant state
/// from DNS, Proxifier, Telegram proxy, and diagnostics subsystems.
/// </summary>
public interface IDashboardStatusService
{
    bool IsSecureDnsEnabled { get; }
    bool IsProxifierRunning { get; }
    bool IsTgProxyRunning { get; }
    string SplitDnsStatus { get; }
    string DnsPrimaryServer { get; }
    string IspName { get; }
    int PassedChecks { get; }
    int TotalChecks { get; }
    bool HasCriticalIssues { get; }

    /// <summary>
    /// Refresh all status properties from underlying services.
    /// Call this on a background thread, then read the properties on the UI thread.
    /// </summary>
    Task RefreshAsync();
}
