// IHostlistService.cs - Interface for domain list management
namespace ZUI.Services;

/// <summary>
/// Manages blocked and user-defined domain lists for DPI bypass.
/// Provides domain lookup, GitHub refresh, and local caching.
/// </summary>
public interface IHostlistService
{
    /// <summary>
    /// Number of domains loaded from GitHub sources.
    /// </summary>
    int BlockedDomainsCount { get; }

    /// <summary>
    /// Number of user-defined domains.
    /// </summary>
    int UserDomainsCount { get; }

    /// <summary>
    /// Timestamp of the last successful GitHub refresh or cache load.
    /// </summary>
    DateTime? LastUpdated { get; }

    /// <summary>
    /// Checks whether a domain is in the blocked or user-defined lists.
    /// Performs exact match and parent domain match (e.g., "api.youtube.com" matches "youtube.com").
    /// </summary>
    Task<bool> IsBlockedAsync(string domain, CancellationToken ct = default);

    /// <summary>
    /// Returns the combined list of blocked domains and user-defined domains.
    /// </summary>
    Task<IReadOnlyList<string>> GetBlockedDomainsAsync(CancellationToken ct = default);

    /// <summary>
    /// Returns the list of user-defined domains only.
    /// </summary>
    Task<IReadOnlyList<string>> GetUserDomainsAsync(CancellationToken ct = default);

    /// <summary>
    /// Downloads the latest domain lists from GitHub sources and updates the local cache.
    /// </summary>
    Task RefreshFromGitHubAsync(CancellationToken ct = default);

    /// <summary>
    /// Adds a domain to the user-defined list and auto-saves.
    /// </summary>
    Task AddUserDomainAsync(string domain, CancellationToken ct = default);

    /// <summary>
    /// Removes a domain from the user-defined list and auto-saves.
    /// </summary>
    Task RemoveUserDomainAsync(string domain, CancellationToken ct = default);

    /// <summary>
    /// Persists both blocked and user domain sets to the local cache file.
    /// </summary>
    Task SaveAsync(CancellationToken ct = default);
}
