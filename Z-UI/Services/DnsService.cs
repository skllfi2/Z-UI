// DnsService.cs - DNS over HTTPS management for Windows 11
using System.ComponentModel;
using System.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace ZUI.Services;

/// <summary>
/// Manages DNS over HTTPS (DoH) configuration in Windows
/// </summary>
public class DnsService : IDnsService
{
    private readonly ILogger<DnsService> _logger;

    // Predefined DoH providers
    private static readonly List<DnsProviderInfo> _providers = new()
    {
        new DnsProviderInfo
        {
            Id = "malw",
            Name = "dns.malw.link",
            IpAddress = "84.21.189.133",
            SecondaryIp = "193.23.209.189",
            DoHUrl = "https://dns.malw.link/dns-query",
            Description = "🇷🇺 Обход IP-блокировок в России (SNI Proxy)",
            IsForRussia = true
        },
        new DnsProviderInfo
        {
            Id = "google",
            Name = "Google DNS",
            IpAddress = "8.8.8.8",
            DoHUrl = "https://dns.google/dns-query",
            Description = "Fast, reliable, widely used"
        },
        new DnsProviderInfo
        {
            Id = "cloudflare",
            Name = "Cloudflare",
            IpAddress = "1.1.1.1",
            DoHUrl = "https://cloudflare-dns.com/dns-query",
            Description = "Privacy-focused, fast"
        },
        new DnsProviderInfo
        {
            Id = "quad9",
            Name = "Quad9",
            IpAddress = "9.9.9.9",
            DoHUrl = "https://dns.quad9.net/dns-query",
            Description = "Security-focused, blocks malware"
        }
    };

    public DnsService(ILogger<DnsService> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc/>
    public string? GetCurrentDnsProvider()
    {
        try
        {
            // Get current DNS servers
            var output = RunPowerShellCommand(
                "Get-DnsClientServerAddress -AddressFamily IPv4 | " +
                "Where-Object {$_.ServerAddresses} | " +
                "Select-Object -First 1 -ExpandProperty ServerAddresses");

            if (string.IsNullOrWhiteSpace(output))
                return null;

            var servers = output.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(s => s.Trim())
                .FirstOrDefault();

            if (string.IsNullOrEmpty(servers))
                return null;

            // Match to known providers
            foreach (var provider in _providers)
            {
                if (servers.Contains(provider.IpAddress))
                {
                    _logger.LogDebug("Matched DNS provider: {Name}", provider.Name);
                    return provider.Name;
                }
            }

            return servers;
        }
        catch (SecurityException ex)
        {
            _logger.LogError(ex, "Error getting DNS provider");
            return null;
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogError(ex, "Error getting DNS provider");
            return null;
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogError(ex, "Error getting DNS provider");
            return null;
        }
    }

    /// <inheritdoc/>
    public async Task<bool> EnableSecureDnsAsync(string providerId)
    {
        try
        {
            var provider = _providers.FirstOrDefault(p => p.Id == providerId);
            if (provider == null)
            {
                _logger.LogWarning("Provider not found: {Id}", providerId);
                return false;
            }

            _logger.LogInformation("Enabling DoH with: {Name}", provider.Name);

            // Get active network adapter
            var adapterName = await GetActiveAdapterNameAsync().ConfigureAwait(false);
            if (string.IsNullOrEmpty(adapterName))
            {
                _logger.LogError("Could not get active adapter name");
                return false;
            }

            // Set DNS server and enable DoH
            var setDnsCmd = $"Set-DnsClientServerAddress -InterfaceAlias '{adapterName}' -ServerAddresses '{provider.IpAddress}'";
            var enableDohCmd = $"Set-DnsClientDohServerAddress -ServerAddress '{provider.IpAddress}' -DohTemplate '{provider.DoHUrl}' -EnableDoH $true";

            RunPowerShellCommand(setDnsCmd, true);
            RunPowerShellCommand(enableDohCmd, true);

            _logger.LogInformation("DoH enabled successfully with {Name}", provider.Name);
            return true;
        }
        catch (Win32Exception ex)
        {
            _logger.LogError(ex, "Error enabling DoH");
            return false;
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogError(ex, "Error enabling DoH");
            return false;
        }
        catch (SecurityException ex)
        {
            _logger.LogError(ex, "Error enabling DoH");
            return false;
        }
    }

    /// <inheritdoc/>
    public async Task<bool> DisableSecureDnsAsync()
    {
        try
        {
            _logger.LogInformation("Disabling DoH");

            var adapterName = await GetActiveAdapterNameAsync().ConfigureAwait(false);
            if (string.IsNullOrEmpty(adapterName))
            {
                _logger.LogError("Could not get active adapter name");
                return false;
            }

            // Reset to DHCP
            RunPowerShellCommand(
                $"Set-DnsClientServerAddress -InterfaceAlias '{adapterName}' -ResetServerAddresses",
                true);

            _logger.LogInformation("DoH disabled, DNS reset to DHCP");
            return true;
        }
        catch (Win32Exception ex)
        {
            _logger.LogError(ex, "Error disabling DoH");
            return false;
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogError(ex, "Error disabling DoH");
            return false;
        }
        catch (SecurityException ex)
        {
            _logger.LogError(ex, "Error disabling DoH");
            return false;
        }
    }

    /// <inheritdoc/>
    public List<DnsProviderInfo> GetAvailableProviders()
    {
        return _providers.ToList();
    }

    /// <inheritdoc/>
    public bool IsDohSupported()
    {
        try
        {
            // Check Windows version (Windows 11 = 10.0.22000+)
            var version = Environment.OSVersion.Version;
            var isWindows11 = version.Major >= 10 && version.Build >= 22000;

            if (!isWindows11)
            {
                // Try to check via registry (might work on Win10 21H2+)
                using var key = Registry.LocalMachine.OpenSubKey(
                    @"SOFTWARE\Microsoft\Windows NT\CurrentVersion");

                var buildStr = key?.GetValue("CurrentBuild")?.ToString();
                if (int.TryParse(buildStr, out var build) && build >= 19041)
                {
                    _logger.LogDebug("DoH supported on Windows 10 build {Build}", build);
                    return true;
                }
            }

            _logger.LogDebug("DoH support check: Windows {Major}.{Minor} build {Build}",
                version.Major, version.Minor, version.Build);

            return isWindows11 || version.Build >= 19041;
        }
        catch (SecurityException ex)
        {
            _logger.LogError(ex, "Error checking DoH support");
            return false;
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogError(ex, "Error checking DoH support");
            return false;
        }
        catch (IOException ex)
        {
            _logger.LogError(ex, "Error checking DoH support");
            return false;
        }
    }

    /// <inheritdoc/>
    public DnsStatus GetDnsStatus()
    {
        var status = new DnsStatus
        {
            IsDohSupported = IsDohSupported(),
            IsSecureDnsEnabled = IsSecureDnsEnabled(),
            ProviderName = GetCurrentDnsProvider()
        };

        if (!status.IsDohSupported)
        {
            status.StatusMessage = "⚠️ DNS over HTTPS не поддерживается";
            status.Recommendation = "Требуется Windows 10 (19041+) или Windows 11";
        }
        else if (status.IsSecureDnsEnabled)
        {
            status.StatusMessage = $"✓ Secure DNS: {status.ProviderName ?? "настроен"}";
        }
        else
        {
            status.StatusMessage = "⚠️ Secure DNS выключен";
            status.Recommendation = "Рекомендуется включить для работы YouTube и Discord";
        }

        return status;
    }

    /// <inheritdoc/>
    public bool IsSecureDnsEnabled()
    {
        try
        {
            var output = RunPowerShellCommand(
                "Get-DnsClientDohServerAddress | Where-Object {$_.Enabled} | " +
                "Select-Object -First 1 -ExpandProperty ServerAddress");

            return !string.IsNullOrWhiteSpace(output);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Error checking DoH status");
            return false;
        }
    }

    private string RunPowerShellCommand(string command, bool asAdmin = false)
    {
        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = $"-NoProfile -Command \"{command}\"",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                }
            };

            if (asAdmin)
            {
                process.StartInfo.Verb = "runas";
                process.StartInfo.UseShellExecute = true;
                process.StartInfo.RedirectStandardOutput = false;
                process.StartInfo.RedirectStandardError = false;
            }

            process.Start();

            if (!asAdmin)
            {
                var output = process.StandardOutput.ReadToEnd();
                process.WaitForExit();
                return output;
            }
            else
            {
                process.WaitForExit();
                return process.ExitCode == 0 ? "success" : "";
            }
        }
        catch (Win32Exception ex)
        {
            _logger.LogError(ex, "PowerShell command failed: {Command}", command);
            return "";
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogError(ex, "PowerShell command failed: {Command}", command);
            return "";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "PowerShell command failed: {Command}", command);
            return "";
        }
    }

    private async Task<string?> GetActiveAdapterNameAsync()
    {
        try
        {
            var output = RunPowerShellCommand(
                "Get-NetAdapter | Where-Object {$_.Status -eq 'Up'} | " +
                "Select-Object -First 1 -ExpandProperty Name");

            return output?.Trim();
        }
        catch (Win32Exception ex)
        {
            _logger.LogError(ex, "Error getting adapter name");
            return null;
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogError(ex, "Error getting adapter name");
            return null;
        }
    }
}
