// DashboardStatusService.cs - Aggregated status provider for DashboardViewModel
// Collects DNS, Proxifier, Telegram proxy, ISP, and diagnostics state in one place

using ZUI.Models;

namespace ZUI.Services;

/// <summary>
/// Aggregates dashboard-relevant status from multiple subsystems
/// so DashboardViewModel depends on a single service instead of four.
/// </summary>
public sealed class DashboardStatusService : IDashboardStatusService
{
    private readonly IDnsService _dnsService;
    private readonly IProxifierService _proxifierService;
    private readonly ITelegramProxyService _tgProxyService;
    private readonly IDiagnosticsService _diagnosticsService;
    private readonly IAdaptiveEngine _adaptiveEngine;
    private readonly IStrategyManager _strategyManager;

    public DashboardStatusService(
        IDnsService dnsService,
        IProxifierService proxifierService,
        ITelegramProxyService tgProxyService,
        IDiagnosticsService diagnosticsService,
        IAdaptiveEngine adaptiveEngine,
        IStrategyManager strategyManager)
    {
        _dnsService = dnsService ?? throw new ArgumentNullException(nameof(dnsService));
        _proxifierService = proxifierService ?? throw new ArgumentNullException(nameof(proxifierService));
        _tgProxyService = tgProxyService ?? throw new ArgumentNullException(nameof(tgProxyService));
        _diagnosticsService = diagnosticsService ?? throw new ArgumentNullException(nameof(diagnosticsService));
        _adaptiveEngine = adaptiveEngine ?? throw new ArgumentNullException(nameof(adaptiveEngine));
        _strategyManager = strategyManager ?? throw new ArgumentNullException(nameof(strategyManager));
    }

    public bool IsSecureDnsEnabled { get; private set; }
    public bool IsProxifierRunning { get; private set; }
    public bool IsTgProxyRunning { get; private set; }
    public string SplitDnsStatus { get; private set; } = "";
    public string DnsPrimaryServer { get; private set; } = "—";
    public string IspName { get; private set; } = "";
    public int PassedChecks { get; private set; }
    public int TotalChecks { get; private set; }
    public bool HasCriticalIssues { get; private set; }

    public async Task RefreshAsync()
    {
        // ── Synchronous / fast properties (from adaptive engine) ──
        IsProxifierRunning = _adaptiveEngine.IsProxifierRunning;
        IsTgProxyRunning = _adaptiveEngine.IsTgProxyRunning;
        SplitDnsStatus = _adaptiveEngine.IsDnsProxyRunning
            ? LocalizationService.Get("Active")
            : LocalizationService.Get("Disabled");

        // ── DNS state ──
        IsSecureDnsEnabled = _dnsService.IsSecureDnsEnabled();
        var provider = _dnsService.GetCurrentDnsProvider();
        DnsPrimaryServer = provider ?? (IsSecureDnsEnabled ? LocalizationService.Get("Configured") : "—");

        // ── ISP detection (network call, fire-and-forget style) ──
        try
        {
            var profile = await _strategyManager.DetectIspAsync().ConfigureAwait(false);
            IspName = profile?.Name ?? LocalizationService.Get("NotDetected");
        }
        catch
        {
            IspName = LocalizationService.Get("NotDetected");
        }

        // ── Diagnostics quick health check ──
        try
        {
            var health = await _diagnosticsService.QuickHealthCheckAsync().ConfigureAwait(false);
            PassedChecks = health.PassedChecks;
            TotalChecks = health.TotalChecks;
            HasCriticalIssues = !health.IsHealthy;
        }
        catch
        {
            PassedChecks = 0;
            TotalChecks = 0;
            HasCriticalIssues = false;
        }
    }
}
