// IspDetectionService.cs - ISP detection by public IP / ASN lookup
using System.Net.Http;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using ZUI.Models;

namespace ZUI.Services;

/// <summary>
/// Interface for ISP detection and profile loading.
/// </summary>
public interface IIspDetectionService
{
    /// <summary>
    /// Detect the user's ISP by querying public IP and matching ASN to known profiles.
    /// Falls back to the default profile if detection fails.
    /// </summary>
    Task<IspProfile> DetectIspAsync();

    /// <summary>
    /// Load ISP profiles from configuration, creating defaults if not found.
    /// </summary>
    Task<IspProfilesConfig> LoadIspProfilesAsync();
}

/// <summary>
/// Detects the user's ISP by querying public IP services and matching ASN
/// against known ISP profiles. Loads and caches ISP profiles configuration.
/// </summary>
public class IspDetectionService : IIspDetectionService
{
    private readonly ILogger<IspDetectionService> _logger;

    private IspProfilesConfig? _ispProfiles;

    // Config directories (shared with StrategyGeneratorService)
    private static readonly string LocalAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
    private static readonly string ConfigDir = Path.Combine(LocalAppData, "Z-UI", "config");

    public IspDetectionService(
        ILogger<IspDetectionService> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Resolves config file path: checks local AppData first, then bundled config.
    /// Creates the config directory if it doesn't exist.
    /// </summary>
    public static async Task<string> GetConfigPathAsync(string configName)
    {
        if (!Directory.Exists(ConfigDir))
        {
            Directory.CreateDirectory(ConfigDir);
        }

        var localPath = Path.Combine(ConfigDir, configName);

        if (File.Exists(localPath))
        {
            return localPath;
        }

        var bundledPath = Path.Combine(AppContext.BaseDirectory, "config", configName);
        if (File.Exists(bundledPath))
        {
            return bundledPath;
        }

        return localPath;
    }

    /// <inheritdoc/>
    public async Task<IspProfile> DetectIspAsync()
    {
        var profiles = await LoadIspProfilesAsync().ConfigureAwait(false);

        // Try to detect by public IP
        try
        {
            var publicIp = await GetPublicIpAsync().ConfigureAwait(false);
            _logger.LogInformation("Public IP: {Ip}", publicIp);

            var asn = await GetAsnForIpAsync(publicIp).ConfigureAwait(false);
            _logger.LogInformation("ASN: {Asn}", asn);

            // Match by ASN
            var matched = profiles.Profiles.Values
                .FirstOrDefault(p => p.Asn != null && p.Asn.Contains(asn));

            if (matched != null)
            {
                _logger.LogInformation("Detected ISP: {Name} (ASN: {Asn})", matched.Name, asn);
                return matched;
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            _logger.LogWarning(ex, "Failed to detect ISP automatically");
        }

        // Return default profile
        _logger.LogInformation("Using default ISP profile");
        return profiles.Profiles["default"];
    }

    /// <inheritdoc/>
    public async Task<IspProfilesConfig> LoadIspProfilesAsync()
    {
        if (_ispProfiles != null) return _ispProfiles;

        var configPath = await GetConfigPathAsync("isp-profiles.json").ConfigureAwait(false);

        if (!File.Exists(configPath))
        {
            _logger.LogWarning("ISP profiles not found, using defaults: {Path}", configPath);
            _ispProfiles = DefaultStrategyConfigs.CreateDefaultIspProfiles();
            await SaveConfigAsync(configPath, _ispProfiles).ConfigureAwait(false);
            return _ispProfiles;
        }

        try
        {
            var json = await File.ReadAllTextAsync(configPath).ConfigureAwait(false);
            _ispProfiles = JsonSerializer.Deserialize<IspProfilesConfig>(json);

            if (_ispProfiles == null)
            {
                _logger.LogWarning("Failed to parse ISP profiles, using defaults");
                _ispProfiles = DefaultStrategyConfigs.CreateDefaultIspProfiles();
            }
            else
            {
                _logger.LogInformation("Loaded ISP profiles v{Version}", _ispProfiles.Version);
            }

            return _ispProfiles;
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            _logger.LogError(ex, "Error loading ISP profiles");
            _ispProfiles = DefaultStrategyConfigs.CreateDefaultIspProfiles();
            return _ispProfiles;
        }
    }

    private async Task<string> GetPublicIpAsync()
    {
        using var client = new HttpClient();
        client.Timeout = TimeSpan.FromSeconds(5);

        // Try multiple IP services
        var services = new[]
        {
            "https://api.ipify.org",
            "https://icanhazip.com",
            "https://ifconfig.me/ip"
        };

        foreach (var service in services)
        {
            try
            {
                var ip = await client.GetStringAsync(new Uri(service)).ConfigureAwait(false);
                return ip.Trim();
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                _logger.LogDebug(ex, "IP service {Service} failed, trying next", service);
            }
        }

        throw new Exception("Could not determine public IP");
    }

    private async Task<string> GetAsnForIpAsync(string ip)
    {
        using var client = new HttpClient();
        client.Timeout = TimeSpan.FromSeconds(5);

        try
        {
            // Use ipinfo.io for ASN lookup
            var response = await client.GetStringAsync(new Uri($"https://ipinfo.io/{ip}/org")).ConfigureAwait(false);
            // Response format: "AS12389 Rostelecom"
            var parts = response.Trim().Split(' ');
            if (parts.Length > 0 && parts[0].StartsWith("AS"))
            {
                return parts[0].Substring(2);
            }
            return response.Trim();
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            _logger.LogDebug(ex, "Failed to get ASN for IP {Ip}", ip);
            return "";
        }
    }

    private async Task SaveConfigAsync<T>(string path, T config)
    {
        try
        {
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            var options = new JsonSerializerOptions { WriteIndented = true };
            var json = JsonSerializer.Serialize(config, options);
            await File.WriteAllTextAsync(path, json).ConfigureAwait(false);

            _logger.LogInformation("Saved config: {Path}", path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogError(ex, "Failed to save config: {Path}", path);
        }
    }
}
