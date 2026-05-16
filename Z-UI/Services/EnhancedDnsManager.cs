// EnhancedDnsManager.cs - Enhanced DNS bypass manager implementation
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace ZUI.Services;

/// <summary>
/// Enhanced DNS bypass manager that checks domain support via dns.malw.link,
/// handles AI service domains blocked by GeoIP, and manages DoH/DoT configuration.
/// </summary>
public sealed class EnhancedDnsManager : IEnhancedDnsManager, IDisposable
{
    private readonly ILogger<EnhancedDnsManager> _logger;
    private readonly IAppSettingsService _settingsService;

    // Thread-safety lock for shared state
    private readonly Lock _lock = new();

    // DNS servers from dns.malw.link
    private static readonly IPAddress MalwLinkDnsPrimary = IPAddress.Parse("84.21.189.133");
    private static readonly IPAddress MalwLinkDnsSecondary = IPAddress.Parse("193.23.209.189");

    // DoH server URLs
    private static readonly string[] DoHServers =
    [
        "https://dns.malw.link/dns-query",
        "https://cloudflare-dns.com/dns-query",
        "https://dns.google/dns-query"
    ];

    // AI service domains blocked by GeoIP (from Zero-Config Plan)
    private static readonly string[] AiServiceDomains =
    [
        "openai.com",
        "chatgpt.com",
        "claude.ai",
        "anthropic.com",
        "gemini.google.com",
        "ai.google",
        "copilot.microsoft.com",
        "poe.com",
        "perplexity.ai",
        "character.ai"
    ];

    // GitHub raw URLs for domain lists
    private const string GitHubListGeneralUrl =
        "https://raw.githubusercontent.com/Flowseal/zapret-discord-youtube/main/list-general.txt";
    private const string GitHubListGoogleUrl =
        "https://raw.githubusercontent.com/Flowseal/zapret-discord-youtube/main/list-google.txt";

    // Local cache path
    private static readonly string LocalAppData =
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
    private static readonly string CacheDir = Path.Combine(LocalAppData, "Z-UI", "cache");
    private static readonly string DomainCacheFile = Path.Combine(CacheDir, "dns-bypass-domains.json");

    // Shared state (guarded by _lock)
    private HashSet<string> _supportedDomains = [];
    private HashSet<string> _userDomains = [];
    private DnsBypassState _state = DnsBypassState.Disabled;
    private string? _activeDohServer;
    private DateTime? _lastRefreshed;

    // Lazy HttpClient — avoids creating in constructor
    private readonly Lazy<HttpClient> _httpClientLazy;

    // Cached serializer options — avoid allocating per-call (CA1869)
    private static readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = true };

    private bool _disposed;

    public EnhancedDnsManager(
        ILogger<EnhancedDnsManager> logger,
        IAppSettingsService settingsService)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));

        _httpClientLazy = new Lazy<HttpClient>(() =>
        {
            var client = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(15)
            };
            client.DefaultRequestHeaders.Add("User-Agent", "Z-UI/1.0");
            return client;
        });

        // Seed AI domains into supported list
        foreach (var domain in AiServiceDomains)
        {
            _supportedDomains.Add(domain);
        }

        _logger.LogInformation("EnhancedDnsManager initialized with {Count} AI service domains",
            AiServiceDomains.Length);
    }

    /// <inheritdoc/>
    public DnsBypassState State
    {
        get
        {
            lock (_lock)
            {
                return _state;
            }
        }
    }

    /// <inheritdoc/>
    public bool IsDnsBypassAvailable
    {
        get
        {
            lock (_lock)
            {
                return _state == DnsBypassState.Active;
            }
        }
    }

    /// <inheritdoc/>
    public async Task<DnsBypassResult> TryBypassDomainAsync(string domain, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(domain);

        _logger.LogDebug("Attempting DNS bypass for domain: {Domain}", domain);

        var sw = Stopwatch.StartNew();

        try
        {
            // Check if domain is supported first
            var isSupported = await IsDomainSupportedAsync(domain, ct).ConfigureAwait(false);
            if (!isSupported)
            {
                return new DnsBypassResult
                {
                    Success = false,
                    Reason = "Domain is not in supported DNS bypass list",
                    Method = DnsBypassMethod.None
                };
            }

            // Try resolving via dns.malw.link DNS servers
            var resolvedIp = await ResolveViaMalwLinkAsync(domain, ct).ConfigureAwait(false);
            sw.Stop();

            if (resolvedIp is not null)
            {
                _logger.LogInformation(
                    "DNS bypass succeeded for {Domain} via malw.link → {Ip} ({Latency}ms)",
                    domain, resolvedIp, sw.ElapsedMilliseconds);

                return new DnsBypassResult
                {
                    Success = true,
                    ResolvedIp = resolvedIp,
                    Reason = "Resolved via dns.malw.link",
                    Method = DnsBypassMethod.MalwLink,
                    Latency = sw.Elapsed
                };
            }

            // Fallback: try system DNS
            sw.Restart();
            var systemIp = await ResolveViaSystemDnsAsync(domain, ct).ConfigureAwait(false);
            sw.Stop();

            if (systemIp is not null)
            {
                _logger.LogDebug(
                    "DNS bypass fallback: {Domain} resolved via system DNS → {Ip} ({Latency}ms)",
                    domain, systemIp, sw.ElapsedMilliseconds);

                return new DnsBypassResult
                {
                    Success = true,
                    ResolvedIp = systemIp,
                    Reason = "Resolved via system DNS (fallback)",
                    Method = DnsBypassMethod.SystemDns,
                    Latency = sw.Elapsed
                };
            }

            sw.Stop();

            return new DnsBypassResult
            {
                Success = false,
                Reason = "Domain did not resolve via any DNS method",
                Method = DnsBypassMethod.None,
                Latency = sw.Elapsed
            };
        }
        catch (SocketException ex)
        {
            sw.Stop();
            _logger.LogWarning(ex, "DNS resolution failed for {Domain}", domain);

            return new DnsBypassResult
            {
                Success = false,
                Reason = $"DNS resolution error: {ex.Message}",
                Method = DnsBypassMethod.None,
                Latency = sw.Elapsed
            };
        }
        catch (OperationCanceledException)
        {
            sw.Stop();
            _logger.LogDebug("DNS bypass cancelled for {Domain}", domain);

            return new DnsBypassResult
            {
                Success = false,
                Reason = "Operation was cancelled",
                Method = DnsBypassMethod.None,
                Latency = sw.Elapsed
            };
        }
        catch (Exception ex)
        {
            sw.Stop();
            _logger.LogError(ex, "Unexpected error during DNS bypass for {Domain}", domain);

            return new DnsBypassResult
            {
                Success = false,
                Reason = $"Unexpected error: {ex.Message}",
                Method = DnsBypassMethod.None,
                Latency = sw.Elapsed
            };
        }
    }

    /// <inheritdoc/>
    public Task<bool> IsDomainSupportedAsync(string domain, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(domain);

        lock (_lock)
        {
            // Check exact match
            if (_supportedDomains.Contains(domain) || _userDomains.Contains(domain))
            {
                return Task.FromResult(true);
            }

            // Check suffix match (e.g., "api.openai.com" matches "openai.com")
            foreach (var supported in _supportedDomains)
            {
                if (domain.EndsWith($".{supported}", StringComparison.OrdinalIgnoreCase))
                {
                    return Task.FromResult(true);
                }
            }

            foreach (var userDomain in _userDomains)
            {
                if (domain.EndsWith($".{userDomain}", StringComparison.OrdinalIgnoreCase))
                {
                    return Task.FromResult(true);
                }
            }
        }

        return Task.FromResult(false);
    }

    /// <inheritdoc/>
    public async Task EnableDnsBypassAsync(CancellationToken ct = default)
    {
        _logger.LogInformation("Enabling DNS bypass");

        lock (_lock)
        {
            _state = DnsBypassState.Checking;
        }

        try
        {
            // Test connectivity to DoH servers
            var dohAvailable = await TestDohConnectivityAsync(ct).ConfigureAwait(false);

            if (dohAvailable)
            {
                _logger.LogInformation("DoH connectivity verified, DNS bypass can be enabled");

                lock (_lock)
                {
                    _state = DnsBypassState.Active;
                    _activeDohServer = DoHServers[0]; // malw.link as primary
                }

                // DNS bypass configuration pending integration with IDnsService
                _logger.LogInformation(
                    "DNS bypass configuration pending integration with IDnsService. " +
                    "DoH server: {Server}", DoHServers[0]);
            }
            else
            {
                _logger.LogWarning("DoH connectivity test failed, attempting standard DNS fallback");

                var dnsAvailable = await TestMalwLinkDnsAsync(ct).ConfigureAwait(false);

                lock (_lock)
                {
                    if (dnsAvailable)
                    {
                        _state = DnsBypassState.Active;
                        _activeDohServer = null; // Using standard DNS, not DoH

                        _logger.LogInformation(
                            "DNS bypass configuration pending integration with IDnsService. " +
                            "Standard DNS: {Primary}, {Secondary}",
                            MalwLinkDnsPrimary, MalwLinkDnsSecondary);
                    }
                    else
                    {
                        _state = DnsBypassState.Failed;

                        _logger.LogError(
                            "DNS bypass configuration pending integration with IDnsService. " +
                            "Neither DoH nor standard DNS is available");
                    }
                }
            }
        }
        catch (SocketException ex)
        {
            _logger.LogError(ex, "Network error while enabling DNS bypass");

            lock (_lock)
            {
                _state = DnsBypassState.Failed;
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogDebug("DNS bypass enable cancelled");

            lock (_lock)
            {
                _state = DnsBypassState.Disabled;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error while enabling DNS bypass");

            lock (_lock)
            {
                _state = DnsBypassState.Failed;
            }
        }
    }

    /// <inheritdoc/>
    public async Task DisableDnsBypassAsync(CancellationToken ct = default)
    {
        _logger.LogInformation("Disabling DNS bypass");

        // DNS bypass configuration pending integration with IDnsService
        _logger.LogInformation(
            "DNS bypass configuration pending integration with IDnsService. " +
            "System DNS will be restored once integrated.");

        lock (_lock)
        {
            _state = DnsBypassState.Disabled;
            _activeDohServer = null;
        }

        await Task.CompletedTask.ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task RefreshSupportedDomainsAsync(CancellationToken ct = default)
    {
        _logger.LogInformation("Refreshing supported DNS bypass domains");

        var refreshedDomains = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Always include AI service domains
        foreach (var domain in AiServiceDomains)
        {
            refreshedDomains.Add(domain);
        }

        // Load from local cache first
        var cachedDomains = await LoadFromLocalCacheAsync(ct).ConfigureAwait(false);
        if (cachedDomains is not null)
        {
            foreach (var domain in cachedDomains)
            {
                refreshedDomains.Add(domain);
            }

            _logger.LogDebug("Loaded {Count} domains from local cache", cachedDomains.Count);
        }

        // Try to fetch from GitHub (non-blocking — use cached if GitHub is unreachable)
        try
        {
            var githubDomains = await FetchFromGitHubAsync(ct).ConfigureAwait(false);
            if (githubDomains.Count > 0)
            {
                foreach (var domain in githubDomains)
                {
                    refreshedDomains.Add(domain);
                }

                _logger.LogInformation("Loaded {Count} domains from GitHub", githubDomains.Count);

                // Save to local cache for offline use
                await SaveToLocalCacheAsync(refreshedDomains, ct).ConfigureAwait(false);
            }
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Failed to fetch domains from GitHub, using cached data");
        }
        catch (TaskCanceledException ex)
        {
            _logger.LogWarning(ex, "GitHub fetch timed out, using cached data");
        }

        // Update shared state
        lock (_lock)
        {
            _supportedDomains = refreshedDomains;
            _lastRefreshed = DateTime.UtcNow;
        }

        _logger.LogInformation("DNS bypass domains refreshed: {Count} total domains", refreshedDomains.Count);
    }

    /// <inheritdoc/>
    public DnsBypassInfo GetStatus()
    {
        lock (_lock)
        {
            return new DnsBypassInfo
            {
                State = _state,
                SupportedDomainsCount = _supportedDomains.Count + _userDomains.Count,
                ActiveDohServer = _activeDohServer,
                LastRefreshed = _lastRefreshed
            };
        }
    }

    // ──────────────────────────────────────────────────────────────
    // Internal DNS resolution methods
    // ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Resolve a domain via dns.malw.link DNS servers using UDP DNS query.
    /// Uses System.Net.Dns — no third-party libraries.
    /// </summary>
    private async Task<string?> ResolveViaMalwLinkAsync(string domain, CancellationToken ct)
    {
        try
        {
            // System.Net.Dns.GetHostAddressesAsync uses the system's configured DNS.
            // To use custom DNS servers, we need a manual UDP DNS query.
            var result = await ManualDnsQueryAsync(domain, MalwLinkDnsPrimary, ct).ConfigureAwait(false);
            if (result is not null)
            {
                return result;
            }

            // Try secondary
            return await ManualDnsQueryAsync(domain, MalwLinkDnsSecondary, ct).ConfigureAwait(false);
        }
        catch (SocketException ex)
        {
            _logger.LogDebug(ex, "Malw.link DNS resolution failed for {Domain}", domain);
            return null;
        }
        catch (OperationCanceledException)
        {
            _logger.LogDebug("Malw.link DNS query cancelled for {Domain}", domain);
            return null;
        }
    }

    /// <summary>
    /// Resolve a domain via system default DNS
    /// </summary>
    private static async Task<string?> ResolveViaSystemDnsAsync(string domain, CancellationToken ct)
    {
        try
        {
            var addresses = await Dns.GetHostAddressesAsync(domain, ct).ConfigureAwait(false);
            var ipv4 = addresses.FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork);
            return ipv4?.ToString();
        }
        catch (SocketException)
        {
            return null;
        }
    }

    /// <summary>
    /// Manual UDP DNS query to a specific DNS server.
    /// Builds a raw DNS packet for A record lookup and parses the response.
    /// </summary>
    private static async Task<string?> ManualDnsQueryAsync(string domain, IPAddress dnsServer, CancellationToken ct)
    {
        using var udpClient = new UdpClient();
        udpClient.Client.ReceiveTimeout = 5000;
        udpClient.Client.SendTimeout = 3000;

        var dnsEndpoint = new IPEndPoint(dnsServer, 53);

        // Build DNS query packet for A record
        var queryPacket = BuildDnsQueryPacket(domain, recordType: 1); // 1 = A record

        // UdpClient.SendAsync in .NET: connect to endpoint then send
        udpClient.Connect(dnsEndpoint);
        _ = await udpClient.SendAsync(queryPacket, ct).ConfigureAwait(false);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(5));

        // ReceiveAsync returns ValueTask<UdpReceiveResult>; convert to Task for WhenAny
        var receiveTask = udpClient.ReceiveAsync(CancellationToken.None).AsTask();
        var completedTask = await Task.WhenAny(receiveTask, Task.Delay(5000, cts.Token)).ConfigureAwait(false);

        if (completedTask != receiveTask)
        {
            return null; // Timed out
        }

        var response = await receiveTask.ConfigureAwait(false);
        return ParseDnsResponse(response.Buffer);
    }

    /// <summary>
    /// Build a minimal DNS query packet for the given domain and record type
    /// </summary>
    private static byte[] BuildDnsQueryPacket(string domain, ushort recordType)
    {
        var packet = new List<byte>();

        // Transaction ID (random)
        var transactionId = (ushort)Random.Shared.Next(1, ushort.MaxValue);
        packet.Add((byte)(transactionId >> 8));
        packet.Add((byte)(transactionId & 0xFF));

        // Flags: standard query, recursion desired
        packet.Add(0x01); // QR=0, Opcode=0, RD=1
        packet.Add(0x00); // RA=0, Z=0, RCODE=0

        // Questions count
        packet.Add(0x00);
        packet.Add(0x01);

        // Answer, Authority, Additional RRs count = 0
        packet.Add(0x00);
        packet.Add(0x00);
        packet.Add(0x00);
        packet.Add(0x00);
        packet.Add(0x00);
        packet.Add(0x00);

        // Query name (domain encoded as labels)
        foreach (var label in domain.Split('.'))
        {
            var labelBytes = System.Text.Encoding.ASCII.GetBytes(label);
            packet.Add((byte)labelBytes.Length);
            packet.AddRange(labelBytes);
        }
        packet.Add(0x00); // Null terminator

        // Query type
        packet.Add((byte)(recordType >> 8));
        packet.Add((byte)(recordType & 0xFF));

        // Query class: IN (1)
        packet.Add(0x00);
        packet.Add(0x01);

        return packet.ToArray();
    }

    /// <summary>
    /// Parse DNS response buffer and extract the first A record IP address
    /// </summary>
    private static string? ParseDnsResponse(byte[] buffer)
    {
        if (buffer.Length < 12)
        {
            return null;
        }

        // Check response code (bits 3-0 of byte 3)
        var rcode = buffer[3] & 0x0F;
        if (rcode != 0)
        {
            return null; // DNS error response
        }

        var answerCount = (buffer[6] << 8) | buffer[7];
        if (answerCount == 0)
        {
            return null;
        }

        // Skip header (12 bytes) and question section
        var offset = 12;

        // Skip question name
        while (offset < buffer.Length && buffer[offset] != 0)
        {
            var labelLen = buffer[offset];
            if (labelLen >= 0xC0)
            {
                // Compression pointer
                offset += 2;
                break;
            }
            offset += labelLen + 1;
        }

        if (offset < buffer.Length && buffer[offset] == 0)
        {
            offset++; // Skip null terminator
        }

        offset += 4; // Skip QTYPE and QCLASS

        // Parse answer records
        for (var i = 0; i < answerCount && offset < buffer.Length - 10; i++)
        {
            // Skip name (could be compressed pointer)
            if ((buffer[offset] & 0xC0) == 0xC0)
            {
                offset += 2;
            }
            else
            {
                while (offset < buffer.Length && buffer[offset] != 0)
                {
                    offset++;
                }
                offset++; // Skip null terminator
            }

            if (offset + 10 > buffer.Length)
            {
                break;
            }

            var type = (ushort)((buffer[offset] << 8) | buffer[offset + 1]);
            // var cls = (ushort)((buffer[offset + 2] << 8) | buffer[offset + 3]);
            // var ttl = (buffer[offset + 4] << 24) | (buffer[offset + 5] << 16) | (buffer[offset + 6] << 8) | buffer[offset + 7];
            var dataLength = (ushort)((buffer[offset + 8] << 8) | buffer[offset + 9]);
            offset += 10;

            if (offset + dataLength > buffer.Length)
            {
                break;
            }

            if (type == 1 && dataLength == 4) // A record with IPv4 address
            {
                return $"{buffer[offset]}.{buffer[offset + 1]}.{buffer[offset + 2]}.{buffer[offset + 3]}";
            }

            offset += dataLength;
        }

        return null;
    }

    // ──────────────────────────────────────────────────────────────
    // Connectivity testing
    // ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Test DoH connectivity by making a test query to the primary DoH server
    /// </summary>
    private async Task<bool> TestDohConnectivityAsync(CancellationToken ct)
    {
        try
        {
            var httpClient = _httpClientLazy.Value;

            // Try DNS-over-HTTPS query to malw.link
            var dohUrl = $"{DoHServers[0]}?name=google.com&type=A";
            var request = new HttpRequestMessage(HttpMethod.Get, dohUrl);
            request.Headers.Add("Accept", "application/dns-json");

            using var response = await httpClient.SendAsync(request, ct).ConfigureAwait(false);

            if (response.IsSuccessStatusCode)
            {
                _logger.LogDebug("DoH connectivity test passed: {Server}", DoHServers[0]);
                return true;
            }

            _logger.LogDebug("DoH connectivity test failed with status: {Status}", response.StatusCode);
            return false;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogDebug(ex, "DoH connectivity test failed");
            return false;
        }
        catch (TaskCanceledException ex)
        {
            _logger.LogDebug(ex, "DoH connectivity test timed out");
            return false;
        }
    }

    /// <summary>
    /// Test connectivity to dns.malw.link DNS servers via UDP
    /// </summary>
    private async Task<bool> TestMalwLinkDnsAsync(CancellationToken ct)
    {
        try
        {
            var result = await ManualDnsQueryAsync("google.com", MalwLinkDnsPrimary, ct).ConfigureAwait(false);
            if (result is not null)
            {
                _logger.LogDebug("Malw.link DNS connectivity test passed");
                return true;
            }

            _logger.LogDebug("Malw.link DNS connectivity test failed: no response");
            return false;
        }
        catch (SocketException ex)
        {
            _logger.LogDebug(ex, "Malw.link DNS connectivity test failed");
            return false;
        }
    }

    // ──────────────────────────────────────────────────────────────
    // Domain list management
    // ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Load domains from local JSON cache file
    /// </summary>
    private async Task<HashSet<string>?> LoadFromLocalCacheAsync(CancellationToken ct)
    {
        try
        {
            if (!File.Exists(DomainCacheFile))
            {
                return null;
            }

            var json = await File.ReadAllTextAsync(DomainCacheFile, ct).ConfigureAwait(false);
            var domains = JsonSerializer.Deserialize<List<string>>(json);

            if (domains is null || domains.Count == 0)
            {
                return null;
            }

            return new HashSet<string>(domains, StringComparer.OrdinalIgnoreCase);
        }
        catch (JsonException ex)
        {
            _logger.LogDebug(ex, "Failed to parse local DNS domain cache");
            return null;
        }
        catch (IOException ex)
        {
            _logger.LogDebug(ex, "Failed to read local DNS domain cache");
            return null;
        }
    }

    /// <summary>
    /// Save domains to local JSON cache file
    /// </summary>
    private async Task SaveToLocalCacheAsync(HashSet<string> domains, CancellationToken ct)
    {
        try
        {
            if (!Directory.Exists(CacheDir))
            {
                Directory.CreateDirectory(CacheDir);
            }

        var json = JsonSerializer.Serialize(domains.ToList(), _jsonOptions);

            await File.WriteAllTextAsync(DomainCacheFile, json, ct).ConfigureAwait(false);

            _logger.LogDebug("Saved {Count} domains to local cache", domains.Count);
        }
        catch (IOException ex)
        {
            _logger.LogWarning(ex, "Failed to save DNS domain cache");
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogWarning(ex, "Failed to save DNS domain cache (access denied)");
        }
    }

    /// <summary>
    /// Fetch domain lists from GitHub (Flowseal/zapret-discord-youtube)
    /// Parses .txt hostlist files (one domain per line, # comments, whitespace trimmed)
    /// </summary>
    private async Task<HashSet<string>> FetchFromGitHubAsync(CancellationToken ct)
    {
        var domains = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var urls = new[] { GitHubListGeneralUrl, GitHubListGoogleUrl };
        var httpClient = _httpClientLazy.Value;

        foreach (var url in urls)
        {
            try
            {
                var content = await httpClient.GetStringAsync(new Uri(url), ct).ConfigureAwait(false);
                var parsed = ParseHostlist(content);
                foreach (var domain in parsed)
                {
                    domains.Add(domain);
                }

                _logger.LogDebug("Fetched {Count} domains from {Url}", parsed.Count, url);
            }
            catch (HttpRequestException ex)
            {
                _logger.LogDebug(ex, "Failed to fetch domain list from {Url}", url);
            }
            catch (TaskCanceledException ex)
            {
                _logger.LogDebug(ex, "Timeout fetching domain list from {Url}", url);
            }
        }

        return domains;
    }

    /// <summary>
    /// Parse a .txt hostlist file: one domain per line, # comments, whitespace trimmed.
    /// Lines starting with # are ignored. Empty lines are skipped.
    /// Supports both plain domain lines and /ipset/ domain lines.
    /// </summary>
    internal static HashSet<string> ParseHostlist(string content)
    {
        var domains = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var rawLine in content.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var line = rawLine.Trim();

            // Skip comments and empty lines
            if (string.IsNullOrWhiteSpace(line) || line.StartsWith('#'))
            {
                continue;
            }

            // Handle ipset-style lines: /domain/ip1,ip2
            if (line.StartsWith('/'))
            {
                var endSlash = line.IndexOf('/', 1);
                if (endSlash > 1)
                {
                    var domainPart = line[1..endSlash].Trim();
                    if (IsValidDomain(domainPart))
                    {
                        domains.Add(domainPart);
                    }
                }

                continue;
            }

            // Plain domain line
            var trimmed = line.Split('#')[0].Trim(); // Remove inline comments
            if (IsValidDomain(trimmed))
            {
                domains.Add(trimmed);
            }
        }

        return domains;
    }

    /// <summary>
    /// Basic validation: domain must contain at least one dot and valid characters
    /// </summary>
    private static bool IsValidDomain(string domain)
    {
        if (string.IsNullOrWhiteSpace(domain))
        {
            return false;
        }

        // Must contain at least one dot (e.g., "example.com")
        if (!domain.Contains('.'))
        {
            return false;
        }

        // Must not contain spaces or invalid characters
        if (domain.Any(c => char.IsWhiteSpace(c) || c is ':' or '[' or ']' or '/'))
        {
            return false;
        }

        return true;
    }

    // ──────────────────────────────────────────────────────────────
    // IDisposable
    // ──────────────────────────────────────────────────────────────

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    private void Dispose(bool disposing)
    {
        if (_disposed)
        {
            return;
        }

        if (disposing && _httpClientLazy.IsValueCreated)
        {
            _httpClientLazy.Value.Dispose();
        }

        _disposed = true;
    }
}
