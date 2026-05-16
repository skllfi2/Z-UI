// HostlistService.cs - Domain list management for DPI bypass
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace ZUI.Services;

/// <summary>
/// Manages blocked and user-defined domain lists for DPI bypass.
/// Downloads domain lists from GitHub, caches locally, and provides
/// efficient domain lookup with parent-domain matching.
/// </summary>
public class HostlistService : IHostlistService
{
    private readonly ILogger<HostlistService> _logger;
    private readonly IAppSettingsService _settingsService;
    private readonly object _lock = new();

    private static readonly string LocalAppData =
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
    private static readonly string CacheFilePath =
        Path.Combine(LocalAppData, "Z-UI", "cache", "hostlist-cache.json");

    private static readonly Uri[] GitHubSources =
    [
        new("https://raw.githubusercontent.com/Flowseal/zapret-discord-youtube/main/list-general.txt"),
        new("https://raw.githubusercontent.com/Flowseal/zapret-discord-youtube/main/list-google.txt")
    ];

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private static HttpClient? _httpClient;
    private static readonly object HttpClientLock = new();

    private HashSet<string> _blockedDomains = new(StringComparer.OrdinalIgnoreCase);
    private HashSet<string> _userDomains = new(StringComparer.OrdinalIgnoreCase);
    private DateTime? _lastUpdated;
    private bool _loaded;

    public HostlistService(ILogger<HostlistService> logger, IAppSettingsService settingsService)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
    }

    /// <inheritdoc />
    public int BlockedDomainsCount
    {
        get
        {
            lock (_lock)
            {
                return _blockedDomains.Count;
            }
        }
    }

    /// <inheritdoc />
    public int UserDomainsCount
    {
        get
        {
            lock (_lock)
            {
                return _userDomains.Count;
            }
        }
    }

    /// <inheritdoc />
    public DateTime? LastUpdated
    {
        get
        {
            lock (_lock)
            {
                return _lastUpdated;
            }
        }
    }

    /// <inheritdoc />
    public async Task<bool> IsBlockedAsync(string domain, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(domain);

        await EnsureLoadedAsync(ct).ConfigureAwait(false);

        var normalized = domain.Trim().ToLowerInvariant();

        lock (_lock)
        {
            // Exact match
            if (_blockedDomains.Contains(normalized) || _userDomains.Contains(normalized))
            {
                return true;
            }

            // Parent domain match: "api.youtube.com" → check "youtube.com"
            var parts = normalized.Split('.');
            for (var i = 1; i < parts.Length; i++)
            {
                var parent = string.Join('.', parts[i..]);
                if (_blockedDomains.Contains(parent) || _userDomains.Contains(parent))
                {
                    return true;
                }
            }

            return false;
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<string>> GetBlockedDomainsAsync(CancellationToken ct = default)
    {
        await EnsureLoadedAsync(ct).ConfigureAwait(false);

        lock (_lock)
        {
            return _blockedDomains
                .Concat(_userDomains)
                .OrderBy(d => d, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<string>> GetUserDomainsAsync(CancellationToken ct = default)
    {
        await EnsureLoadedAsync(ct).ConfigureAwait(false);

        lock (_lock)
        {
            return _userDomains
                .OrderBy(d => d, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
    }

    /// <inheritdoc />
    public async Task RefreshFromGitHubAsync(CancellationToken ct = default)
    {
        _logger.LogInformation("Refreshing hostlist from GitHub sources");

        var merged = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var successCount = 0;

        foreach (var sourceUri in GitHubSources)
        {
            try
            {
                var client = GetHttpClient();
                var content = await client.GetStringAsync(sourceUri, ct).ConfigureAwait(false);
                var domains = ParseHostlist(content);

                foreach (var domain in domains)
                {
                    merged.Add(domain);
                }

                successCount++;
                _logger.LogDebug("Loaded {Count} domains from {Source}", domains.Count, sourceUri);
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError(ex, "Failed to download hostlist from {Source}", sourceUri);
            }
                catch (TaskCanceledException ex) when (ex.InnerException is TimeoutException)
                {
                    _logger.LogError(ex, "Timeout downloading hostlist from {Source}", sourceUri);
                }
                catch (TaskCanceledException)
                {
                    _logger.LogWarning("Hostlist refresh cancelled");
                    return;
                }
        }

        if (successCount > 0)
        {
            lock (_lock)
            {
                _blockedDomains = merged;
                _lastUpdated = DateTime.UtcNow;
                _loaded = true;
            }

            await SaveAsync(ct).ConfigureAwait(false);
            _logger.LogInformation("Hostlist refreshed: {Count} blocked domains", merged.Count);
        }
        else
        {
            _logger.LogWarning("All GitHub sources failed, retaining existing domain list");
        }
    }

    /// <inheritdoc />
    public async Task AddUserDomainAsync(string domain, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(domain);

        var normalized = domain.Trim().ToLowerInvariant();

        lock (_lock)
        {
            if (!_userDomains.Add(normalized))
            {
                return; // Already exists, no-op
            }
        }

        _logger.LogInformation("Added user domain: {Domain}", normalized);
        await SaveAsync(ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task RemoveUserDomainAsync(string domain, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(domain);

        var normalized = domain.Trim().ToLowerInvariant();

        lock (_lock)
        {
            if (!_userDomains.Remove(normalized))
            {
                return; // Didn't exist, no-op
            }
        }

        _logger.LogInformation("Removed user domain: {Domain}", normalized);
        await SaveAsync(ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task SaveAsync(CancellationToken ct = default)
    {
        try
        {
            var cache = new HostlistCache();

            lock (_lock)
            {
                cache.BlockedDomains = [.. _blockedDomains];
                cache.UserDomains = [.. _userDomains];
                cache.LastUpdated = _lastUpdated;
            }

            var dir = Path.GetDirectoryName(CacheFilePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            var json = JsonSerializer.Serialize(cache, JsonOptions);
            await File.WriteAllTextAsync(CacheFilePath, json, ct).ConfigureAwait(false);

            _logger.LogDebug("Hostlist cache saved to {Path}", CacheFilePath);
        }
        catch (IOException ex)
        {
            _logger.LogError(ex, "Failed to save hostlist cache");
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogError(ex, "Failed to save hostlist cache");
        }
    }

    /// <summary>
    /// Ensures domain data is loaded from local cache before first use.
    /// </summary>
    private async Task EnsureLoadedAsync(CancellationToken ct)
    {
        if (_loaded) return;

        await LoadFromCacheAsync(ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Loads domain lists from the local JSON cache file.
    /// </summary>
    private async Task LoadFromCacheAsync(CancellationToken ct)
    {
        try
        {
            if (!File.Exists(CacheFilePath))
            {
                _logger.LogInformation("No hostlist cache found, will load on first GitHub refresh");
                lock (_lock)
                {
                    _loaded = true;
                }
                return;
            }

            var json = await File.ReadAllTextAsync(CacheFilePath, ct).ConfigureAwait(false);
            var cache = JsonSerializer.Deserialize<HostlistCache>(json, JsonOptions);

            if (cache is null)
            {
                _logger.LogWarning("Hostlist cache file is empty or invalid");
                lock (_lock)
                {
                    _loaded = true;
                }
                return;
            }

            lock (_lock)
            {
                _blockedDomains = new HashSet<string>(
                    cache.BlockedDomains ?? [],
                    StringComparer.OrdinalIgnoreCase);

                _userDomains = new HashSet<string>(
                    cache.UserDomains ?? [],
                    StringComparer.OrdinalIgnoreCase);

                _lastUpdated = cache.LastUpdated;
                _loaded = true;
            }

            _logger.LogInformation("Loaded hostlist cache: {Blocked} blocked, {User} user domains",
                _blockedDomains.Count, _userDomains.Count);
        }
        catch (IOException ex)
        {
            _logger.LogError(ex, "Failed to load hostlist cache");
            lock (_lock)
            {
                _loaded = true;
            }
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Failed to parse hostlist cache");
            lock (_lock)
            {
                _loaded = true;
            }
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogError(ex, "Failed to load hostlist cache");
            lock (_lock)
            {
                _loaded = true;
            }
        }
    }

    /// <summary>
    /// Parses a hostlist text file into a list of domains.
    /// Supports: one domain per line, comment lines starting with #,
    /// hosts-file format (IP DOMAIN), trims whitespace, skips empties.
    /// </summary>
    internal static List<string> ParseHostlist(string content)
    {
        var domains = new List<string>();

        foreach (var rawLine in content.Split('\n'))
        {
            var line = rawLine.Trim();

            // Skip empty lines and comments
            if (string.IsNullOrWhiteSpace(line) || line.StartsWith('#'))
            {
                continue;
            }

            // Handle hosts file format: "IP DOMAIN" or "IP DOMAIN # comment"
            // Extract just the domain part
            var domain = ExtractDomainFromLine(line);
            if (!string.IsNullOrWhiteSpace(domain))
            {
                domains.Add(domain.ToLowerInvariant());
            }
        }

        return domains;
    }

    /// <summary>
    /// Extracts a domain from a single line.
    /// Handles plain domain lines and hosts-file format (IP DOMAIN).
    /// </summary>
    private static string ExtractDomainFromLine(ReadOnlySpan<char> line)
    {
        // Remove inline comments
        var commentIdx = line.IndexOf('#');
        if (commentIdx >= 0)
        {
            line = line[..commentIdx];
        }

        line = line.Trim();
        if (line.IsEmpty)
        {
            return string.Empty;
        }

        // Check if this looks like a hosts-file entry (starts with an IP address)
        var firstSpace = line.IndexOfAny(' ', '\t');
        if (firstSpace >= 0)
        {
            // Could be "IP DOMAIN" format — check if the first token looks like an IP
            var firstToken = line[..firstSpace].Trim();
            if (IsIpLike(firstToken))
            {
                // Extract the domain part after the IP
                var remainder = line[(firstSpace + 1)..].Trim();
                var nextSpace = remainder.IndexOfAny(' ', '\t');
                if (nextSpace >= 0)
                {
                    remainder = remainder[..nextSpace].Trim();
                }

                return remainder.IsEmpty ? string.Empty : remainder.ToString();
            }
        }

        // Plain domain line
        return line.ToString();
    }

    /// <summary>
    /// Quick check whether a token looks like an IPv4 or IPv6 address
    /// (starts with a digit or colon). Used to detect hosts-file format.
    /// </summary>
    private static bool IsIpLike(ReadOnlySpan<char> token)
    {
        if (token.IsEmpty)
        {
            return false;
        }

        var first = token[0];
        return char.IsDigit(first) || first == ':' || first == '[';
    }

    /// <summary>
    /// Gets or creates the shared HttpClient instance (lazy singleton).
    /// </summary>
    private static HttpClient GetHttpClient()
    {
        if (_httpClient is not null)
        {
            return _httpClient;
        }

        lock (HttpClientLock)
        {
            if (_httpClient is not null)
            {
                return _httpClient;
            }

            _httpClient = new HttpClient();
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "Z-UI/1.0");
            _httpClient.Timeout = TimeSpan.FromSeconds(30);

            return _httpClient;
        }
    }
}

/// <summary>
/// JSON-serializable cache model for hostlist persistence.
/// </summary>
internal sealed class HostlistCache
{
    public List<string> BlockedDomains { get; set; } = [];
    public List<string> UserDomains { get; set; } = [];
    public DateTime? LastUpdated { get; set; }
}
