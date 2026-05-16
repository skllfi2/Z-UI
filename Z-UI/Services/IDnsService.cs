// IDnsService.cs - Interface for DNS over HTTPS management
using ZUI.Models;

namespace ZUI.Services;

/// <summary>
/// Service for managing DNS over HTTPS (DoH) in Windows
/// </summary>
public interface IDnsService
{
    /// <summary>
    /// Check if DNS over HTTPS is enabled
    /// </summary>
    bool IsSecureDnsEnabled();

    /// <summary>
    /// Get current DNS provider name
    /// </summary>
    string? GetCurrentDnsProvider();

    /// <summary>
    /// Enable Secure DNS with specified provider
    /// </summary>
    /// <param name="providerId">Provider ID: "google", "cloudflare", "quad9"</param>
    /// <returns>True if successful</returns>
    Task<bool> EnableSecureDnsAsync(string providerId);

    /// <summary>
    /// Disable Secure DNS (reset to DHCP)
    /// </summary>
    Task<bool> DisableSecureDnsAsync();

    /// <summary>
    /// Get list of available DNS providers
    /// </summary>
    List<DnsProviderInfo> GetAvailableProviders();

    /// <summary>
    /// Check if system supports DNS over HTTPS (Windows 11)
    /// </summary>
    bool IsDohSupported();

    /// <summary>
    /// Get DNS status message for UI
    /// </summary>
    DnsStatus GetDnsStatus();
}

/// <summary>
/// DNS provider information
/// </summary>
public class DnsProviderInfo
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string DoHUrl { get; set; } = string.Empty;
    public string IpAddress { get; set; } = string.Empty;
    public string? SecondaryIp { get; set; }
    public string? Description { get; set; }
    
    /// <summary>
    /// Special provider for Russia (SNI Proxy for IP blocks)
    /// </summary>
    public bool IsForRussia { get; set; }
}

/// <summary>
/// DNS status for UI display
/// </summary>
public class DnsStatus
{
    public bool IsSecureDnsEnabled { get; set; }
    public bool IsDohSupported { get; set; }
    public string? ProviderName { get; set; }
    public string StatusMessage { get; set; } = "Checking...";
    public string? Recommendation { get; set; }
}
