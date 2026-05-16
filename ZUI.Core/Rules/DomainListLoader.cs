// ═══════════════════════════════════════════════════════════════
// ZUI.Core / Rules / DomainListLoader.cs
// Загрузка списков доменов (hostlist) и IP-адресов (ipset)
// из текстовых файлов zapret. Thread-safe, HashSet lookup.
// ═══════════════════════════════════════════════════════════════

using System.Collections.Concurrent;
using System.Net;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ZUI.Core.Intercept;

namespace ZUI.Core.Rules;

/// <summary>
/// Загрузчик списков доменов и IP-адресов из файлов zapret.
/// Поддержка wildcard (*.example.com), CIDR подсетей, file reload.
/// Thread-safe.
/// </summary>
public sealed class DomainListLoader
{
    private readonly ILogger _logger;
    private readonly ConcurrentDictionary<string, DomainSet> _domainSets = new();
    private readonly ConcurrentDictionary<string, IpSet> _ipSets = new();
    private readonly ConcurrentDictionary<string, DateTime> _fileTimestamps = new();

    public DomainListLoader(ILogger<DomainListLoader>? logger = null)
    {
        _logger = logger ?? NullLoggerFactory.Instance.CreateLogger<DomainListLoader>();
    }

    // ── Domain lists ────────────────────────────────────────

    /// <summary>
    /// Загрузить список доменов из файла.
    /// Формат: один домен на строку, комментарии начинаются с #.
    /// Поддержка wildcard: *.example.com
    /// </summary>
    public async Task<Result> LoadDomainListAsync(string listFile, CancellationToken ct = default)
    {
        try
        {
            if (!File.Exists(listFile))
            {
                _logger.LogWarning("Domain list file not found: {File}", listFile);
                return Result.Failed($"Domain list file not found: {listFile}");
            }

            var exact = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var wildcards = new List<string>();

            var lines = await File.ReadAllLinesAsync(listFile, ct).ConfigureAwait(false);

            foreach (var line in lines)
            {
                var trimmed = line.Trim();
                if (trimmed.Length == 0 || trimmed.StartsWith('#'))
                    continue;

                if (trimmed.StartsWith("*."))
                    wildcards.Add(trimmed);
                else if (trimmed.StartsWith('*'))
                    wildcards.Add(trimmed);
                else
                    exact.Add(trimmed);
            }

            _domainSets[listFile] = new DomainSet(exact, wildcards.ToArray());
            _fileTimestamps[listFile] = File.GetLastWriteTimeUtc(listFile);

            _logger.LogInformation("Loaded {Count} domains and {Wc} wildcards from {File}",
                exact.Count, wildcards.Count, listFile);

            return Result.Success();
        }
        catch (OperationCanceledException)
        {
            return Result.Failed("Operation cancelled");
        }
        catch (IOException ex)
        {
            _logger.LogError(ex, "Failed to load domain list: {File}", listFile);
            return Result.Failed($"Failed to load domain list: {ex.Message}");
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogError(ex, "Failed to load domain list: {File}", listFile);
            return Result.Failed($"Failed to load domain list: {ex.Message}");
        }
    }

    /// <summary>
    /// Проверить, входит ли домен в список.
    /// Сначала точное совпадение, потом wildcard.
    /// </summary>
    public bool IsDomainInList(string listFile, string domain)
    {
        if (!_domainSets.TryGetValue(listFile, out var set))
            return false;

        if (set.Exact.Contains(domain))
            return true;

        var wildcards = set.Wildcards;
        for (int i = 0; i < wildcards.Length; i++)
        {
            if (SniParser.MatchSni(domain, wildcards[i]))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Перезагрузить файл, если он изменился (по дате модификации).
    /// </summary>
    public async Task<Result> ReloadIfChangedAsync(string listFile, CancellationToken ct = default)
    {
        if (!File.Exists(listFile))
            return Result.Failed($"File not found: {listFile}");

        if (!_fileTimestamps.TryGetValue(listFile, out var lastTime))
            return await LoadDomainListAsync(listFile, ct).ConfigureAwait(false);

        var currentTime = File.GetLastWriteTimeUtc(listFile);
        if (currentTime > lastTime)
            return await LoadDomainListAsync(listFile, ct).ConfigureAwait(false);

        return Result.Success();
    }

    // ── IP sets ─────────────────────────────────────────────

    /// <summary>
    /// Загрузить IP set из файла.
    /// Формат: один IP или CIDR на строку, комментарии начинаются с #.
    /// </summary>
    public async Task<Result> LoadIpsetAsync(string listFile, CancellationToken ct = default)
    {
        try
        {
            if (!File.Exists(listFile))
            {
                _logger.LogWarning("IP set file not found: {File}", listFile);
                return Result.Failed($"IP set file not found: {listFile}");
            }

            var exactIps = new HashSet<uint>();
            var subnets = new List<CidrEntry>();

            var lines = await File.ReadAllLinesAsync(listFile, ct).ConfigureAwait(false);

            foreach (var line in lines)
            {
                var trimmed = line.Trim();
                if (trimmed.Length == 0 || trimmed.StartsWith('#'))
                    continue;

                if (trimmed.Contains('/'))
                {
                    // CIDR notation: 142.250.0.0/16
                    var parts = trimmed.Split('/');
                    if (parts.Length == 2 && uint.TryParse(parts[1], out var prefixLen) && prefixLen <= 32)
                    {
                        if (TryParseIPv4(parts[0], out var ip))
                        {
                            uint mask = prefixLen == 0 ? 0 : ~((1u << (32 - (int)prefixLen)) - 1);
                            subnets.Add(new CidrEntry(ip & mask, mask));
                        }
                    }
                }
                else
                {
                    if (TryParseIPv4(trimmed, out var ip))
                        exactIps.Add(ip);
                }
            }

            _ipSets[listFile] = new IpSet(exactIps, subnets.ToArray());
            _fileTimestamps[listFile] = File.GetLastWriteTimeUtc(listFile);

            _logger.LogInformation("Loaded {Count} IPs and {SubnetCount} subnets from {File}",
                exactIps.Count, subnets.Count, listFile);

            return Result.Success();
        }
        catch (OperationCanceledException)
        {
            return Result.Failed("Operation cancelled");
        }
        catch (IOException ex)
        {
            _logger.LogError(ex, "Failed to load IP set: {File}", listFile);
            return Result.Failed($"Failed to load IP set: {ex.Message}");
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogError(ex, "Failed to load IP set: {File}", listFile);
            return Result.Failed($"Failed to load IP set: {ex.Message}");
        }
    }

    /// <summary>
    /// Проверить, входит ли IP-адрес в IP set.
    /// Поддержка точного совпадения и CIDR подсетей (IPv4).
    /// </summary>
    public bool IsIpInList(string listFile, IPAddress ip)
    {
        if (!_ipSets.TryGetValue(listFile, out var set))
            return false;

        if (ip.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork)
            return false; // IPv6 not supported in ipset files

        uint ipValue = IpAddressToUint(ip);
        if (set.ExactIps.Contains(ipValue))
            return true;

        var subnets = set.Subnets;
        for (int i = 0; i < subnets.Length; i++)
        {
            if ((ipValue & subnets[i].Mask) == subnets[i].Network)
                return true;
        }

        return false;
    }

    /// <summary>
    /// Перезагрузить IP set, если файл изменился.
    /// </summary>
    public async Task<Result> ReloadIpsetIfChangedAsync(string listFile, CancellationToken ct = default)
    {
        if (!File.Exists(listFile))
            return Result.Failed($"File not found: {listFile}");

        if (!_fileTimestamps.TryGetValue(listFile, out var lastTime))
            return await LoadIpsetAsync(listFile, ct).ConfigureAwait(false);

        var currentTime = File.GetLastWriteTimeUtc(listFile);
        if (currentTime > lastTime)
            return await LoadIpsetAsync(listFile, ct).ConfigureAwait(false);

        return Result.Success();
    }

    /// <summary>
    /// Загружен ли список доменов.
    /// </summary>
    public bool IsDomainListLoaded(string listFile) => _domainSets.ContainsKey(listFile);

    /// <summary>
    /// Загружен ли IP set.
    /// </summary>
    public bool IsIpsetLoaded(string listFile) => _ipSets.ContainsKey(listFile);

    // ── Helpers ─────────────────────────────────────────────

    private static uint IpAddressToUint(IPAddress ip)
    {
        var bytes = ip.GetAddressBytes();
        return (uint)((bytes[0] << 24) | (bytes[1] << 16) | (bytes[2] << 8) | bytes[3]);
    }

    private static bool TryParseIPv4(string text, out uint ip)
    {
        ip = 0;
        var parts = text.Split('.');
        if (parts.Length != 4)
            return false;

        uint result = 0;
        for (int i = 0; i < 4; i++)
        {
            if (!byte.TryParse(parts[i], out var b))
                return false;
            result = (result << 8) | b;
        }

        ip = result;
        return true;
    }

    // ── Internal data structures ────────────────────────────

    private sealed class DomainSet(HashSet<string> exact, string[] wildcards)
    {
        public HashSet<string> Exact { get; } = exact;
        public string[] Wildcards { get; } = wildcards;
    }

    private sealed class IpSet(HashSet<uint> exactIps, CidrEntry[] subnets)
    {
        public HashSet<uint> ExactIps { get; } = exactIps;
        public CidrEntry[] Subnets { get; } = subnets;
    }

    private readonly record struct CidrEntry(uint Network, uint Mask);
}
