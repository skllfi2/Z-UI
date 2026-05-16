using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace ZUI.Models;

/// <summary>
/// DNS server configuration
/// </summary>
public sealed class DnsServer
{
    /// <summary>
    /// Unique identifier (system, malware, google, cloudflare, yandex, custom)
    /// </summary>
    public string Id { get; }

    /// <summary>
    /// Display name
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Description for tooltip/info
    /// </summary>
    public string Description { get; }

    /// <summary>
    /// Primary DNS IP address (null for DHCP)
    /// </summary>
    public string? PrimaryIp { get; }

    /// <summary>
    /// Secondary DNS IP address (optional)
    /// </summary>
    public string? SecondaryIp { get; }

    /// <summary>
    /// Whether this server is recommended
    /// </summary>
    public bool IsRecommended { get; }

    /// <summary>
    /// Whether this server is for Russian users specifically
    /// </summary>
    public bool IsRussianOptimized { get; }

    /// <summary>
    /// Constructor
    /// </summary>
    [JsonConstructor]
    public DnsServer(
        string id,
        string name,
        string description,
        string? primaryIp,
        string? secondaryIp,
        bool isRecommended = false,
        bool isRussianOptimized = false)
    {
        Id = id ?? throw new ArgumentNullException(nameof(id));
        Name = name ?? throw new ArgumentNullException(nameof(name));
        Description = description ?? "";
        PrimaryIp = primaryIp;
        SecondaryIp = secondaryIp;
        IsRecommended = isRecommended;
        IsRussianOptimized = isRussianOptimized;
    }

    /// <summary>
    /// Returns true if this is a DHCP/system DNS (no static IPs)
    /// </summary>
    [JsonIgnore]
    public bool IsDhcp => string.IsNullOrEmpty(PrimaryIp);

    /// <summary>
    /// Get formatted DNS servers string for display
    /// </summary>
    [JsonIgnore]
    public string DisplayServers
    {
        get
        {
            if (IsDhcp) return "DHCP";
            if (!string.IsNullOrEmpty(SecondaryIp))
                return $"{PrimaryIp}, {SecondaryIp}";
            return PrimaryIp!;
        }
    }

    /// <summary>
    /// Preset DNS servers
    /// </summary>
    public static readonly IReadOnlyList<DnsServer> Presets = new List<DnsServer>
    {
        new(
            id: "system",
            name: "System DNS",
            description: "Use DNS from your ISP (DHCP)",
            primaryIp: null,
            secondaryIp: null,
            isRecommended: false,
            isRussianOptimized: false),

        new(
            id: "malware",
            name: "dns.malw.link",
            description: "Bypasses IP blocks and DNS poisoning. Recommended for Russia.",
            primaryIp: "45.144.225.222",
            secondaryIp: "45.144.225.223",
            isRecommended: true,
            isRussianOptimized: true),

        new(
            id: "google",
            name: "Google DNS",
            description: "Public DNS by Google. Fast and reliable.",
            primaryIp: "8.8.8.8",
            secondaryIp: "8.8.4.4",
            isRecommended: false,
            isRussianOptimized: false),

        new(
            id: "cloudflare",
            name: "Cloudflare DNS",
            description: "Public DNS by Cloudflare. Privacy-focused.",
            primaryIp: "1.1.1.1",
            secondaryIp: "1.0.0.1",
            isRecommended: false,
            isRussianOptimized: false),

        new(
            id: "yandex",
            name: "Yandex DNS",
            description: "DNS from Yandex. Good for Russian users.",
            primaryIp: "77.88.8.8",
            secondaryIp: "77.88.8.1",
            isRecommended: false,
            isRussianOptimized: true),

        new(
            id: "adguard",
            name: "AdGuard DNS",
            description: "Blocks ads and trackers.",
            primaryIp: "94.140.14.14",
            secondaryIp: "94.140.15.15",
            isRecommended: false,
            isRussianOptimized: false),

        new(
            id: "custom",
            name: "Custom DNS",
            description: "Your own DNS servers",
            primaryIp: "",
            secondaryIp: "",
            isRecommended: false,
            isRussianOptimized: false)
    };

    /// <summary>
    /// Get preset by ID
    /// </summary>
    public static DnsServer? GetPreset(string id)
    {
        foreach (var preset in Presets)
        {
            if (preset.Id == id)
                return preset;
        }
        return null;
    }

    /// <summary>
    /// Create a custom DNS server
    /// </summary>
    public static DnsServer CreateCustom(string primaryIp, string? secondaryIp = null)
    {
        return new DnsServer(
            id: "custom",
            name: "Custom DNS",
            description: "User-defined DNS servers",
            primaryIp: primaryIp,
            secondaryIp: secondaryIp,
            isRecommended: false,
            isRussianOptimized: false);
    }
}

/// <summary>
/// DNS test result
/// </summary>
public sealed class DnsTestResult
{
    /// <summary>
    /// Whether the test passed
    /// </summary>
    public bool IsSuccess { get; }

    /// <summary>
    /// Response time in milliseconds
    /// </summary>
    public long ResponseTimeMs { get; }

    /// <summary>
    /// Error message if failed
    /// </summary>
    public string? ErrorMessage { get; }

    /// <summary>
    /// Timestamp of the test
    /// </summary>
    public DateTime Timestamp { get; }

    /// <summary>
    /// Constructor for successful test
    /// </summary>
    public DnsTestResult(long responseTimeMs)
    {
        IsSuccess = true;
        ResponseTimeMs = responseTimeMs;
        ErrorMessage = null;
        Timestamp = DateTime.Now;
    }

    /// <summary>
    /// Constructor for failed test
    /// </summary>
    public DnsTestResult(string errorMessage)
    {
        IsSuccess = false;
        ResponseTimeMs = -1;
        ErrorMessage = errorMessage;
        Timestamp = DateTime.Now;
    }

    /// <summary>
    /// Formatted response time for display
    /// </summary>
    public string DisplayResponseTime => IsSuccess ? $"{ResponseTimeMs} ms" : "Failed";
}

/// <summary>
/// DNS configuration update from remote source
/// </summary>
public sealed class DnsRemoteConfig
{
    /// <summary>
    /// Configuration version
    /// </summary>
    public string Version { get; set; } = "1.0.0";

    /// <summary>
    /// Last update timestamp
    /// </summary>
    public DateTime LastUpdated { get; set; }

    /// <summary>
    /// DNS servers from remote config
    /// </summary>
    public List<DnsServerConfig> Servers { get; set; } = new();
}

/// <summary>
/// Individual DNS server in remote config
/// </summary>
public sealed class DnsServerConfig
{
    /// <summary>
    /// Server ID
    /// </summary>
    public string Id { get; set; } = "";

    /// <summary>
    /// Primary IP
    /// </summary>
    public string PrimaryIp { get; set; } = "";

    /// <summary>
    /// Secondary IP
    /// </summary>
    public string SecondaryIp { get; set; } = "";

    /// <summary>
    /// Whether this server is currently active
    /// </summary>
    public bool IsActive { get; set; } = true;
}
