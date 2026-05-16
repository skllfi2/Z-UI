// StrategyGeneratorModels.cs - Models for strategy generator
using System.Text.Json.Serialization;

namespace ZUI.Models;

/// <summary>
/// DPI method configuration
/// </summary>
public record DpiMethod
{
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public int Stability { get; init; }
    public IReadOnlyList<string> Compatibility { get; init; } = Array.Empty<string>();
    public IReadOnlyDictionary<string, MethodParam> Params { get; init; } = new Dictionary<string, MethodParam>();
}

/// <summary>
/// Method parameter configuration
/// </summary>
public record MethodParam
{
    public object? Default { get; init; }
    public IReadOnlyList<object>? Options { get; init; }
    public int[]? Range { get; init; }
}

/// <summary>
/// Service configuration (YouTube, Discord, etc.)
/// </summary>
public record ServiceConfig
{
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Icon { get; init; } = "";
    public bool Enabled { get; init; } = true;
    public IReadOnlyList<string> Domains { get; init; } = Array.Empty<string>();
    public IReadOnlyList<int> TcpPorts { get; init; } = Array.Empty<int>();
    public IReadOnlyList<int> UdpPorts { get; init; } = Array.Empty<int>();
    public string? L7Filter { get; init; }
    public string TestUrl { get; init; } = string.Empty;
    public string TestExpect { get; init; } = string.Empty;
    public string Category { get; init; } = string.Empty;
    
    /// <summary>
    /// Voice ports for Discord-style services
    /// </summary>
    public VoicePortsConfig? VoicePorts { get; init; }
    
    /// <summary>
    /// TCP port ranges (e.g., "27015-27030" for Steam)
    /// </summary>
    public IReadOnlyList<string>? TcpPortRanges { get; init; }
    
    /// <summary>
    /// UDP port ranges (e.g., "50000-65535" for Discord voice)
    /// </summary>
    public IReadOnlyList<string>? UdpPortRanges { get; init; }
}

/// <summary>
/// Voice ports configuration
/// </summary>
public record VoicePortsConfig
{
    public IReadOnlyList<int> Tcp { get; init; } = Array.Empty<int>();
    public IReadOnlyList<int> Udp { get; init; } = Array.Empty<int>();
}

/// <summary>
/// Binary packet configuration
/// </summary>
public record BinaryPacketConfig
{
    public string Default { get; init; } = string.Empty;
    public IReadOnlyList<string> Alternatives { get; init; } = Array.Empty<string>();
}

/// <summary>
/// ISP profile for provider-specific settings
/// </summary>
public record IspProfile
{
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Description { get; init; } = "";
    public string Method { get; init; } = "fake";
    public IReadOnlyDictionary<string, object> MethodParams { get; init; } = new Dictionary<string, object>();
    public int Confidence { get; init; } = 50;
    
    /// <summary>
    /// ASN numbers for this ISP
    /// </summary>
    public IReadOnlyList<string>? Asn { get; init; }
    
    /// <summary>
    /// Geographic regions
    /// </summary>
    public IReadOnlyList<string>? Regions { get; init; }
    
    /// <summary>
    /// Notes about this profile
    /// </summary>
    public string? Notes { get; init; }
}

/// <summary>
/// Strategy parameters configuration (from strategy-params.json)
/// </summary>
public record StrategyParamsConfig
{
    public string Version { get; init; } = "1.0.0";
    public string Updated { get; init; } = string.Empty;
    public string MinAppVersion { get; init; } = "1.0.0";
    
    public IReadOnlyDictionary<string, DpiMethod> DpiMethods { get; init; } = new Dictionary<string, DpiMethod>();
    public IReadOnlyDictionary<string, ServiceConfig> Services { get; init; } = new Dictionary<string, ServiceConfig>();
    public IReadOnlyDictionary<string, BinaryPacketConfig> BinaryPackets { get; init; } = new Dictionary<string, BinaryPacketConfig>();
    
    /// <summary>
    /// Excluded domains (banking, government, etc.)
    /// </summary>
    public IReadOnlyDictionary<string, IReadOnlyList<string>>? ExcludedDomains { get; init; }
}

/// <summary>
/// ISP profiles configuration (from isp-profiles.json)
/// </summary>
public record IspProfilesConfig
{
    public string Version { get; init; } = "1.0.0";
    public string Updated { get; init; } = string.Empty;
    
    public IReadOnlyDictionary<string, IspProfile> Profiles { get; init; } = new Dictionary<string, IspProfile>();
    public IReadOnlyList<DetectionRule> DetectionRules { get; init; } = Array.Empty<DetectionRule>();
}

/// <summary>
/// ISP detection rule
/// </summary>
public record DetectionRule
{
    public IReadOnlyList<string>? Asn { get; init; }
    public IReadOnlyList<string>? IpRanges { get; init; }
    public string ProfileId { get; init; } = string.Empty;
}

/// <summary>
/// Generated strategy result
/// </summary>
public record GeneratedStrategy
{
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string WinwsArgs { get; init; } = string.Empty;
    public IReadOnlyList<string> IncludedServices { get; init; } = Array.Empty<string>();
    public IspProfile? Profile { get; init; }
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;

    /// <summary>
    /// Custom domains included in this strategy
    /// </summary>
    public IReadOnlyList<string> CustomDomains { get; init; } = Array.Empty<string>();
}

/// <summary>
/// Test level for strategy testing
/// </summary>
public enum TestLevel
{
    /// <summary>
    /// DNS + TCP test (5 seconds)
    /// </summary>
    Quick,
    
    /// <summary>
    /// HTTP + WebSocket test (30 seconds)
    /// </summary>
    Standard,
    
    /// <summary>
    /// Real usage test (2-5 minutes)
    /// </summary>
    Full
}

/// <summary>
/// Service test result
/// </summary>
public record ServiceTestResult
{
    public string ServiceId { get; init; } = string.Empty;
    public bool Passed { get; init; }
    public int? LatencyMs { get; init; }
    public string? Details { get; init; }
}

/// <summary>
/// Overall test results
/// </summary>
public record TestResults
{
    public bool Success { get; init; }
    public IReadOnlyDictionary<string, ServiceTestResult> ServiceResults { get; init; } = new Dictionary<string, ServiceTestResult>();
    public TimeSpan Duration { get; init; }
    public string? ErrorMessage { get; init; }
}

/// <summary>
/// User services configuration
/// </summary>
public record UserServicesConfig
{
    public string Version { get; init; } = "1.0.0";
    public string LastModified { get; init; } = string.Empty;
    
    public IReadOnlyList<string> SelectedServices { get; init; } = Array.Empty<string>();
    public string? DetectedProfile { get; init; }
    public string? ManualProfileOverride { get; init; }
    
    public GeneratedStrategy? GeneratedStrategy { get; init; }
    
    /// <summary>
    /// Custom domains added by user
    /// </summary>
    public IReadOnlyList<string> CustomDomains { get; init; } = Array.Empty<string>();
    
    /// <summary>
    /// Custom services added by user
    /// </summary>
    public IReadOnlyList<CustomServiceConfig> CustomServices { get; init; } = Array.Empty<CustomServiceConfig>();
    
    public DnsConfig? DnsConfig { get; init; }
}

/// <summary>
/// Custom service configuration (user-defined)
/// </summary>
public record CustomServiceConfig
{
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public IReadOnlyList<string> Domains { get; init; } = Array.Empty<string>();
    public IReadOnlyList<int> TcpPorts { get; init; } = Array.Empty<int>();
    public IReadOnlyList<int> UdpPorts { get; init; } = Array.Empty<int>();
    public string? Notes { get; init; }
}

/// <summary>
/// DNS configuration
/// </summary>
public record DnsConfig
{
    public bool UseMalwLink { get; init; }
    public string? PrimaryDns { get; init; }
    public string? SecondaryDns { get; init; }
    public string? DohUrl { get; init; }
}

/// <summary>
/// Result of universal strategy generation.
/// </summary>
public record UniversalStrategyResult
{
    public bool Success { get; init; }
    public string? WinwsArgs { get; init; }
    public string? StrategyFilePath { get; init; }
    public string? Error { get; init; }
    public IReadOnlyList<string> IncludedServices { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> IncludedLists { get; init; } = Array.Empty<string>();

    public static UniversalStrategyResult Succeeded(string winwsArgs, string filePath, IReadOnlyList<string> services, IReadOnlyList<string> lists) =>
        new() { Success = true, WinwsArgs = winwsArgs, StrategyFilePath = filePath, IncludedServices = services, IncludedLists = lists };

    public static UniversalStrategyResult Failed(string error) =>
        new() { Success = false, Error = error };
}
