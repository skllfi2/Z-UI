// IStrategyManager.cs - Interface for managing DPI bypass strategies (Level 3 native)
// Loads JSON strategies, communicates with Worker via IPC for start/stop
using ZUI.Models;

namespace ZUI.Services;

/// <summary>
/// Manager for JSON-based DPI bypass strategies.
/// Loads from zapret/strategies/*.json (Phase 4 converted).
/// Sends strategy selection to Worker via IPC.
/// </summary>
public interface IStrategyManager
{
    /// <summary>
    /// Get all available strategies (loaded from JSON files)
    /// </summary>
    List<StrategyInfo> GetAvailableStrategies();

    /// <summary>
    /// Get current active strategy
    /// </summary>
    StrategyInfo? GetCurrentStrategy();

    /// <summary>
    /// Set strategy to use (sends to Worker on next StartBypass)
    /// </summary>
    void SetStrategy(string strategyId);

    /// <summary>
    /// Get strategy by ID
    /// </summary>
    StrategyInfo? GetStrategy(string strategyId);

    /// <summary>
    /// Update statistics for a strategy (local tracking)
    /// </summary>
    void UpdateStatistics(string strategyId, bool success);

    /// <summary>
    /// Check if custom strategy is set (from generator)
    /// </summary>
    bool HasCustomStrategy { get; }

    /// <summary>
    /// Get custom strategy method name
    /// </summary>
    string? CustomMethod { get; }

    /// <summary>
    /// Get custom strategy service list
    /// </summary>
    List<string>? CustomServices { get; }

    /// <summary>
    /// Get current DPI method display name
    /// </summary>
    string GetCurrentMethod();

    /// <summary>
    /// Set custom strategy from generator
    /// </summary>
    void SetCustomStrategy(string strategyId, string? method = null, List<string>? services = null);

    /// <summary>
    /// Detect ISP profile (from external IP + ASN lookup)
    /// </summary>
    Task<IspProfile?> DetectIspAsync();

    /// <summary>
    /// Get ISP profiles configuration
    /// </summary>
    Task<IspProfilesConfig?> GetIspProfilesAsync();

    /// <summary>
    /// Get external IP address
    /// </summary>
    Task<string> GetExternalIpAsync();

    /// <summary>
    /// Reload strategies from disk (after new JSON files are generated)
    /// </summary>
    Task ReloadStrategiesAsync();

    /// <summary>
    /// Ensures background initialization has completed.
    /// Call before accessing strategy data from async context.
    /// </summary>
    Task EnsureInitializedAsync();

    /// <summary>
    /// Get the strategy ID that will be sent to Worker on StartBypass.
    /// Resolves custom strategy / current strategy / "auto" fallback.
    /// </summary>
    string GetActiveStrategyId();
}

/// <summary>
/// Result of testing strategies
/// </summary>
public class StrategyTestResult
{
    public bool Success { get; set; }
    public string? SuccessfulStrategyId { get; set; }
    public string? SuccessfulStrategyName { get; set; }
    public List<string> TriedStrategies { get; set; } = new();
    public string? ErrorMessage { get; set; }
}
