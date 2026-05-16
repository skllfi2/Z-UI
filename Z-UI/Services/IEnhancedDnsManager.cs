// IEnhancedDnsManager.cs - Enhanced DNS bypass manager interface
// DnsBypassState is defined in IAdaptiveEngine.cs (same namespace)

namespace ZUI.Services;

/// <summary>
/// Method used for DNS bypass resolution
/// </summary>
public enum DnsBypassMethod
{
    /// <summary>No bypass method applied</summary>
    None,
    /// <summary>Resolved via dns.malw.link servers</summary>
    MalwLink,
    /// <summary>Resolved via DNS over HTTPS</summary>
    DoH,
    /// <summary>Resolved via system default DNS</summary>
    SystemDns
}

/// <summary>
/// Result of a DNS bypass attempt for a single domain
/// </summary>
public record DnsBypassResult
{
    /// <summary>Whether the bypass attempt succeeded</summary>
    public bool Success { get; init; }

    /// <summary>Resolved IP address, if available</summary>
    public string? ResolvedIp { get; init; }

    /// <summary>Human-readable reason for success or failure</summary>
    public string? Reason { get; init; }

    /// <summary>The DNS bypass method that was used</summary>
    public DnsBypassMethod Method { get; init; }

    /// <summary>Time taken for the DNS resolution</summary>
    public TimeSpan Latency { get; init; }
}

/// <summary>
/// Snapshot of the current DNS bypass system status
/// </summary>
public record DnsBypassInfo
{
    /// <summary>Current state of the DNS bypass system</summary>
    public DnsBypassState State { get; init; }

    /// <summary>Number of domains in the supported domains list</summary>
    public int SupportedDomainsCount { get; init; }

    /// <summary>URL of the active DoH server, if configured</summary>
    public string? ActiveDohServer { get; init; }

    /// <summary>When the supported domains list was last refreshed</summary>
    public DateTime? LastRefreshed { get; init; }
}

/// <summary>
/// Enhanced DNS manager for bypassing GeoIP blocks via dns.malw.link
/// and handling AI service domains (ChatGPT, Claude, Gemini).
/// Coordinates DoH/DoT configuration and falls back to system DNS
/// when bypass is not needed.
/// </summary>
public interface IEnhancedDnsManager
{
    /// <summary>
    /// Current state of the DNS bypass system
    /// </summary>
    DnsBypassState State { get; }

    /// <summary>
    /// Whether DNS bypass is currently available and active
    /// </summary>
    bool IsDnsBypassAvailable { get; }

    /// <summary>
    /// Attempt to bypass DNS for a specific domain.
    /// Tests resolution via custom DNS servers and returns result with latency.
    /// </summary>
    /// <param name="domain">Domain name to resolve</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Result indicating success, resolved IP, method used, and latency</returns>
    Task<DnsBypassResult> TryBypassDomainAsync(string domain, CancellationToken ct = default);

    /// <summary>
    /// Check if a domain is supported for DNS bypass
    /// (present in supported domains list or AI services list)
    /// </summary>
    /// <param name="domain">Domain name to check</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>True if the domain can be bypassed via DNS</returns>
    Task<bool> IsDomainSupportedAsync(string domain, CancellationToken ct = default);

    /// <summary>
    /// Enable DNS bypass by configuring DoH (preferred) or standard DNS.
    /// Tests connectivity before switching. Pending integration with IDnsService.
    /// </summary>
    /// <param name="ct">Cancellation token</param>
    Task EnableDnsBypassAsync(CancellationToken ct = default);

    /// <summary>
    /// Disable DNS bypass and restore system DNS configuration.
    /// Pending integration with IDnsService.
    /// </summary>
    /// <param name="ct">Cancellation token</param>
    Task DisableDnsBypassAsync(CancellationToken ct = default);

    /// <summary>
    /// Refresh the list of supported domains from local cache and GitHub.
    /// Loads from Flowseal/zapret-discord-youtube list files.
    /// </summary>
    /// <param name="ct">Cancellation token</param>
    Task RefreshSupportedDomainsAsync(CancellationToken ct = default);

    /// <summary>
    /// Get current DNS bypass status information
    /// </summary>
    /// <returns>Snapshot of current state, domain count, and server info</returns>
    DnsBypassInfo GetStatus();
}
