// MalwLinkUpdateService.cs - Service for updating dns.malw.link data from GitHub
using System.Net.Http;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace ZUI.Services;

/// <summary>
/// Update result
/// </summary>
public record MalwLinkUpdateResult(bool Success, string? Error = null, string? NewVersion = null);

/// <summary>
/// Service for updating dns.malw.link configuration from GitHub
/// </summary>
public class MalwLinkUpdateService : IDisposable
{
    private readonly ILogger<MalwLinkUpdateService> _logger;
    private readonly HttpClient _httpClient;
    private bool _disposed;
    
    private const string GitHubRepo = "ImMALWARE/dns.malw.link";
    private const string GitHubApiUrl = "https://api.github.com/repos";
    private const string GitHubRawUrl = "https://raw.githubusercontent.com";
    
    // Local cache path
    private static readonly string LocalAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
    private static readonly string CacheDir = Path.Combine(LocalAppData, "Z-UI", "cache", "malwlink");
    private static readonly string VersionFile = Path.Combine(CacheDir, "version.json");
    private static readonly string HostsFile = Path.Combine(CacheDir, "hosts");
    
    public MalwLinkUpdateService(ILogger<MalwLinkUpdateService> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _httpClient = new HttpClient();
        _httpClient.DefaultRequestHeaders.Add("User-Agent", "Z-UI/1.0");
    }
    
    /// <summary>
    /// Check for updates
    /// </summary>
    public async Task<MalwLinkUpdateResult> CheckForUpdatesAsync()
    {
        try
        {
            _logger.LogInformation("Checking for dns.malw.link updates");
            
            // Get current version
		var currentVersion = await GetCurrentVersionAsync().ConfigureAwait(false);

		// Get latest version from GitHub
		var latestVersion = await GetLatestVersionFromGitHubAsync().ConfigureAwait(false);
            
            if (latestVersion == null)
            {
                return new MalwLinkUpdateResult(false, "Could not fetch latest version from GitHub");
            }
            
            if (currentVersion == latestVersion)
            {
                _logger.LogInformation("Already up to date: {Version}", currentVersion);
                return new MalwLinkUpdateResult(true, null, currentVersion);
            }
            
            _logger.LogInformation("Update available: {Current} -> {Latest}", currentVersion, latestVersion);
            return new MalwLinkUpdateResult(true, null, latestVersion);
        }
 catch (HttpRequestException ex)
 {
 _logger.LogError(ex, "Failed to check for updates");
 return new MalwLinkUpdateResult(false, ex.Message);
 }
 catch (TaskCanceledException ex)
 {
 _logger.LogError(ex, "Failed to check for updates");
 return new MalwLinkUpdateResult(false, ex.Message);
 }
 catch (IOException ex)
 {
 _logger.LogError(ex, "Failed to check for updates");
 return new MalwLinkUpdateResult(false, ex.Message);
 }
    }
    
    /// <summary>
    /// Download and apply updates
    /// </summary>
    public async Task<MalwLinkUpdateResult> UpdateAsync()
    {
        try
        {
            _logger.LogInformation("Updating dns.malw.link data");
            
            // Ensure cache directory exists
            if (!Directory.Exists(CacheDir))
            {
                Directory.CreateDirectory(CacheDir);
            }
            
            // Get latest commit SHA
            var commitSha = await GetLatestCommitShaAsync().ConfigureAwait(false);
            if (string.IsNullOrEmpty(commitSha))
            {
                return new MalwLinkUpdateResult(false, "Could not fetch latest commit");
            }
            
            // Download hosts file
            var hostsUrl = $"{GitHubRawUrl}/{GitHubRepo}/master/hosts";
		var hostsContent = await _httpClient.GetStringAsync(new Uri(hostsUrl)).ConfigureAwait(false);
            
            if (string.IsNullOrEmpty(hostsContent))
            {
                return new MalwLinkUpdateResult(false, "Could not download hosts file");
            }
            
		await File.WriteAllTextAsync(HostsFile, hostsContent).ConfigureAwait(false);
            
            // Save version info
            var versionInfo = new MalwLinkVersion
            {
                CommitSha = commitSha,
                UpdatedAt = DateTime.UtcNow,
                HostsCount = hostsContent.Split('\n').Count(l => !string.IsNullOrWhiteSpace(l) && !l.StartsWith('#'))
            };
            
		var json = JsonSerializer.Serialize(versionInfo, new JsonSerializerOptions { WriteIndented = true });
		await File.WriteAllTextAsync(VersionFile, json).ConfigureAwait(false);
            
            _logger.LogInformation("Updated to commit {Sha} with {Count} hosts entries", 
                commitSha[..8], versionInfo.HostsCount);
            
            return new MalwLinkUpdateResult(true, null, commitSha[..8]);
        }
 catch (HttpRequestException ex)
 {
 _logger.LogError(ex, "Failed to update");
 return new MalwLinkUpdateResult(false, ex.Message);
 }
 catch (TaskCanceledException ex)
 {
 _logger.LogError(ex, "Failed to update");
 return new MalwLinkUpdateResult(false, ex.Message);
 }
 catch (IOException ex)
 {
 _logger.LogError(ex, "Failed to update");
 return new MalwLinkUpdateResult(false, ex.Message);
 }
 catch (UnauthorizedAccessException ex)
 {
 _logger.LogError(ex, "Failed to update");
 return new MalwLinkUpdateResult(false, ex.Message);
 }
    }
    
    /// <summary>
    /// Get hosts file path (local cache or bundled)
    /// </summary>
    public async Task<string> GetHostsFilePathAsync()
    {
        // Check local cache first
        if (File.Exists(HostsFile))
        {
            return HostsFile;
        }
        
        // Download on first use
		var result = await UpdateAsync().ConfigureAwait(false);
        if (result.Success)
        {
            return HostsFile;
        }
        
        // Fallback to bundled (if exists)
        var bundledPath = Path.Combine(AppContext.BaseDirectory, "config", "malwlink-hosts.txt");
        return bundledPath;
    }
    
    /// <summary>
    /// Get current cached version
    /// </summary>
    public async Task<string?> GetCurrentVersionAsync()
    {
        try
        {
            if (!File.Exists(VersionFile))
                return null;
            
		var json = await File.ReadAllTextAsync(VersionFile).ConfigureAwait(false);
		var version = JsonSerializer.Deserialize<MalwLinkVersion>(json);
		return version?.CommitSha?[..8];
	}
 catch (JsonException ex)
 {
 _logger.LogDebug(ex, "Failed to read current version");
 return null;
 }
 catch (IOException ex)
 {
 _logger.LogDebug(ex, "Failed to read current version");
 return null;
 }
    }
    
    /// <summary>
    /// Get last update time
    /// </summary>
    public async Task<DateTime?> GetLastUpdateTimeAsync()
    {
        try
        {
            if (!File.Exists(VersionFile))
                return null;
            
		var json = await File.ReadAllTextAsync(VersionFile).ConfigureAwait(false);
		var version = JsonSerializer.Deserialize<MalwLinkVersion>(json);
		return version?.UpdatedAt;
	}
 catch (JsonException ex)
 {
 _logger.LogDebug(ex, "Failed to read last update time");
 return null;
 }
 catch (IOException ex)
 {
 _logger.LogDebug(ex, "Failed to read last update time");
 return null;
 }
    }
    
    /// <summary>
    /// Check if update is needed (older than 24 hours)
    /// </summary>
    public async Task<bool> NeedsUpdateAsync()
    {
		var lastUpdate = await GetLastUpdateTimeAsync().ConfigureAwait(false);
        if (lastUpdate == null)
            return true;
        
        return (DateTime.UtcNow - lastUpdate.Value).TotalHours > 24;
    }
    
	private async Task<string?> GetLatestVersionFromGitHubAsync()
	{
		try
		{
			var commitSha = await GetLatestCommitShaAsync().ConfigureAwait(false);
			return commitSha?[..8];
		}
 catch (HttpRequestException ex)
 {
 _logger.LogDebug(ex, "Failed to get latest version from GitHub");
 return null;
 }
 catch (TaskCanceledException ex)
 {
 _logger.LogDebug(ex, "Failed to get latest version from GitHub");
 return null;
 }
    }
    
    private async Task<string?> GetLatestCommitShaAsync()
    {
        try
        {
            var url = $"{GitHubApiUrl}/{GitHubRepo}/commits?per_page=1";
		var response = await _httpClient.GetStringAsync(new Uri(url)).ConfigureAwait(false);
            
            using var doc = JsonDocument.Parse(response);
            var root = doc.RootElement;
            
            if (root.ValueKind == JsonValueKind.Array && root.GetArrayLength() > 0)
            {
                var commit = root[0];
                return commit.GetProperty("sha").GetString();
            }
            
            return null;
        }
 catch (HttpRequestException ex)
 {
 _logger.LogError(ex, "Failed to get latest commit SHA");
 return null;
 }
 catch (TaskCanceledException ex)
 {
 _logger.LogError(ex, "Failed to get latest commit SHA");
 return null;
 }
 catch (JsonException ex)
 {
 _logger.LogError(ex, "Failed to get latest commit SHA");
 return null;
 }
    }
    
    /// <summary>
    /// Auto-update if needed
    /// </summary>
    public async Task AutoUpdateIfNeededAsync()
    {
        try
        {
		if (await NeedsUpdateAsync().ConfigureAwait(false))
		{
			_logger.LogInformation("Auto-updating dns.malw.link data");
			await UpdateAsync().ConfigureAwait(false);
            }
        }
 catch (HttpRequestException ex)
 {
 _logger.LogError(ex, "Auto-update failed");
 }
 catch (IOException ex)
 {
 _logger.LogError(ex, "Auto-update failed");
 }
 catch (UnauthorizedAccessException ex)
 {
 _logger.LogError(ex, "Auto-update failed");
 }
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (_disposed) return;

        if (disposing)
        {
            _httpClient?.Dispose();
        }

        _disposed = true;
    }
}

/// <summary>
/// Version info stored locally
/// </summary>
internal class MalwLinkVersion
{
    public string? CommitSha { get; set; }
    public DateTime UpdatedAt { get; set; }
    public int HostsCount { get; set; }
}
