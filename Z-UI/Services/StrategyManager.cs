// StrategyManager.cs - JSON-based strategy manager (Level 3 native)
// Loads strategies from zapret/strategies/*.json files
// No more BAT parsing, no more winws.exe args building
// Strategy ID is sent to Worker via IPC — Worker loads and applies the JSON

using System.Text.Json;
using Microsoft.Extensions.Logging;
using ZUI.Models;

namespace ZUI.Services;

/// <summary>
/// Manages JSON-based DPI bypass strategies.
/// In Level 3, strategies are JSON files (Phase 4 converted from BAT).
/// The Worker reads the same JSON files and applies the rules natively.
/// This manager handles: loading, selection, statistics, ISP detection.
/// </summary>
public sealed class StrategyManager : IStrategyManager
{
    private readonly IIpcClientService _ipc;
    private readonly ILogger<StrategyManager> _logger;
    private readonly string _zapretDir;

    private StrategyInfo? _currentStrategy;
    private readonly Dictionary<string, StrategyInfo> _allStrategies = new();
    private readonly List<StrategyInfo> _strategiesList = new();

    // Custom strategy from generator
    private string? _customStrategyId;
    private bool _hasCustomStrategy;
    private string? _customMethod;
    private List<string>? _customServices;

    // Initialization task — awaited by EnsureInitializedAsync
    private readonly Task _initTask;

    public bool HasCustomStrategy => _hasCustomStrategy;
    public string? CustomMethod => _customMethod;
    public List<string>? CustomServices => _customServices;

public StrategyManager(IIpcClientService ipc, ILogger<StrategyManager> logger)
{
    _ipc = ipc ?? throw new ArgumentNullException(nameof(ipc));
    _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    _zapretDir = FindZapretDirectory();

    // Load JSON strategies asynchronously to avoid blocking constructor
    _initTask = InitializeAsync();
}

    private async Task InitializeAsync()
    {
        try
        {
            await LoadJsonStrategiesAsyncCore().ConfigureAwait(false);

            // Set default strategy (first available, or "auto")
            _currentStrategy = _strategiesList.FirstOrDefault();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "StrategyManager background initialization failed");
        }
    }

    /// <summary>
    /// Ensures background initialization has completed before accessing strategy data.
    /// Safe to call multiple times — awaits the same completed task (no overhead).
    /// </summary>
    public Task EnsureInitializedAsync() => _initTask;

    /// <inheritdoc/>
    public List<StrategyInfo> GetAvailableStrategies()
    {
        return _strategiesList.ToList();
    }

    /// <inheritdoc/>
    public StrategyInfo? GetCurrentStrategy()
    {
        return _currentStrategy;
    }

    /// <inheritdoc/>
    public void SetStrategy(string strategyId)
    {
        if (_allStrategies.TryGetValue(strategyId, out var strategy))
        {
            _currentStrategy = strategy;
            _logger.LogInformation("Set strategy: {Name} ({Source})", strategy.Name, strategy.Source);
        }
        else
        {
            _logger.LogWarning("Strategy not found: {Id}", strategyId);
        }
    }

    /// <inheritdoc/>
    public StrategyInfo? GetStrategy(string strategyId)
    {
        return _allStrategies.TryGetValue(strategyId, out var strategy) ? strategy : null;
    }

    /// <inheritdoc/>
    public void UpdateStatistics(string strategyId, bool success)
    {
        if (_allStrategies.TryGetValue(strategyId, out var strategy))
        {
            if (success)
                strategy.SuccessCount++;
            else
                strategy.FailCount++;

            strategy.LastUsed = DateTime.Now;
        }
    }

    /// <inheritdoc/>
    public string GetCurrentMethod()
    {
        if (_hasCustomStrategy && !string.IsNullOrEmpty(_customMethod))
            return _customMethod;

        if (_currentStrategy == null)
            return "fake";

        // Extract method from JSON strategy name or default
        return ExtractMethodFromStrategyName(_currentStrategy.Name);
    }

    /// <inheritdoc/>
    public void SetCustomStrategy(string strategyId, string? method = null, List<string>? services = null)
    {
        _customStrategyId = strategyId;
        _hasCustomStrategy = !string.IsNullOrWhiteSpace(strategyId);
        _customMethod = method;
        _customServices = services;
        _logger.LogInformation("Custom strategy set: {Id}, Has={Has}", strategyId, _hasCustomStrategy);
    }

    /// <inheritdoc/>
    public string GetActiveStrategyId()
    {
        // Priority: custom strategy from generator
        if (_hasCustomStrategy && !string.IsNullOrWhiteSpace(_customStrategyId))
            return _customStrategyId;

        // Current selected strategy
        if (_currentStrategy != null)
            return _currentStrategy.Id;

        // Fallback: auto
        return "auto";
    }

    /// <inheritdoc/>
    public async Task ReloadStrategiesAsync()
    {
        await EnsureInitializedAsync().ConfigureAwait(false);

        _allStrategies.Clear();
        _strategiesList.Clear();
        await LoadJsonStrategiesAsyncCore().ConfigureAwait(false);

        // Re-validate current strategy
        if (_currentStrategy != null && !_allStrategies.ContainsKey(_currentStrategy.Id))
        {
            _currentStrategy = _strategiesList.FirstOrDefault();
        }
    }

    /// <inheritdoc/>
    public async Task<IspProfile?> DetectIspAsync()
    {
        try
        {
            var ip = await GetExternalIpAsync().ConfigureAwait(false);
            if (string.IsNullOrEmpty(ip))
                return null;

            using var client = new System.Net.Http.HttpClient();
            client.Timeout = TimeSpan.FromSeconds(5);

            var response = await client.GetStringAsync(new Uri($"https://ipinfo.io/{ip}/org")).ConfigureAwait(false);
            var asn = response.Trim().Split(' ').FirstOrDefault()?.Replace("AS", "") ?? "";

            var profiles = GetDefaultIspProfiles();
            foreach (var profile in profiles.Profiles.Values)
            {
                if (profile.Asn != null && profile.Asn.Contains(asn))
                    return profile;
            }
        }
 catch (HttpRequestException ex)
 {
 _logger.LogWarning(ex, "Failed to detect ISP");
 }
 catch (TaskCanceledException ex)
 {
 _logger.LogWarning(ex, "Failed to detect ISP");
 }
 catch (IOException ex)
 {
 _logger.LogWarning(ex, "Failed to detect ISP");
 }

        return null;
    }

    /// <inheritdoc/>
    public async Task<IspProfilesConfig?> GetIspProfilesAsync()
    {
        return await Task.FromResult(GetDefaultIspProfiles()).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<string> GetExternalIpAsync()
    {
        try
        {
            using var client = new System.Net.Http.HttpClient();
            client.Timeout = TimeSpan.FromSeconds(5);

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
                catch (HttpRequestException ex) { _logger.LogDebug(ex, "IP service {Service} failed, trying next", service); }
            }
        }
 catch (HttpRequestException ex)
 {
 _logger.LogWarning(ex, "Failed to get external IP");
 }
 catch (TaskCanceledException ex)
 {
 _logger.LogWarning(ex, "Failed to get external IP");
 }

        return "";
    }

    // ── Private helpers ──────────────────────────────────────

    private async Task LoadJsonStrategiesAsyncCore()
    {
        var strategiesPath = Path.Combine(_zapretDir, "strategies");

        if (!Directory.Exists(strategiesPath))
        {
            _logger.LogWarning("Strategies directory not found: {Path}", strategiesPath);
            return;
        }

        // Load JSON strategy files (Phase 4 converted)
        foreach (var jsonFile in Directory.GetFiles(strategiesPath, "*.json"))
        {
            try
            {
                var name = Path.GetFileNameWithoutExtension(jsonFile);
                var strategyId = $"json-{name}";

                // Try to read the JSON to extract display info
                var strategyName = name;
                string? description = null;
                string method = "fake";

                try
                {
                    var json = await File.ReadAllTextAsync(jsonFile).ConfigureAwait(false);
                    using var doc = JsonDocument.Parse(json);
                    var root = doc.RootElement;

                    if (root.TryGetProperty("name", out var nameProp))
                        strategyName = nameProp.GetString() ?? name;

                    if (root.TryGetProperty("description", out var descProp))
                        description = descProp.GetString();

                    // Extract primary method from first rule
                    if (root.TryGetProperty("rules", out var rulesProp) && rulesProp.GetArrayLength() > 0)
                    {
                        var firstRule = rulesProp.EnumerateArray().First();
                        if (firstRule.TryGetProperty("desyncMethod", out var methodProp))
                            method = methodProp.GetString() ?? "fake";
                    }
                }
 catch (JsonException ex)
 {
 _logger.LogDebug(ex, "Failed to parse JSON strategy metadata: {Path}", jsonFile);
 }
 catch (IOException ex)
 {
 _logger.LogDebug(ex, "Failed to parse JSON strategy metadata: {Path}", jsonFile);
 }

                var strategy = new StrategyInfo
                {
                    Id = strategyId,
                    Name = strategyName,
                    Source = "JSON",
                    FilePath = jsonFile,
                    IsAvailable = true,
                    Description = description ?? GetMethodDisplayName(method)
                };

                _allStrategies[strategyId] = strategy;
                _strategiesList.Add(strategy);
            }
 catch (IOException ex)
 {
 _logger.LogDebug(ex, "Failed to load JSON strategy: {Path}", jsonFile);
 }
 catch (UnauthorizedAccessException ex)
 {
 _logger.LogDebug(ex, "Failed to load JSON strategy: {Path}", jsonFile);
 }
 catch (JsonException ex)
 {
 _logger.LogDebug(ex, "Failed to load JSON strategy: {Path}", jsonFile);
 }
        }

        _logger.LogInformation("Loaded {Count} JSON strategies from: {Path}", _strategiesList.Count, strategiesPath);
    }

    private static string ExtractMethodFromStrategyName(string name)
    {
        if (name.Contains("fake", StringComparison.OrdinalIgnoreCase))
            return "fake";
        if (name.Contains("multisplit", StringComparison.OrdinalIgnoreCase))
            return "multisplit";
        if (name.Contains("fakedsplit", StringComparison.OrdinalIgnoreCase))
            return "fakedsplit";
        if (name.Contains("split", StringComparison.OrdinalIgnoreCase))
            return "multisplit";
        return "fake";
    }

    private static string GetMethodDisplayName(string method) => method switch
    {
        "fake" => "Fake TLS/QUIC",
        "multisplit" => "Multi Split",
        "fakedsplit" => "Fake + Split",
        "split" => "Split",
        _ => method
    };

    private static string FindZapretDirectory()
    {
        var baseDir = AppContext.BaseDirectory;
        var candidates = new[]
        {
            Path.Combine(baseDir, "zapret"),
            Path.Combine(baseDir, "..", "zapret"),
        };

        foreach (var dir in candidates)
        {
            var fullPath = Path.GetFullPath(dir);
            if (Directory.Exists(fullPath))
                return fullPath;
        }

        return Path.Combine(baseDir, "zapret");
    }

    private IspProfilesConfig GetDefaultIspProfiles()
    {
        return new IspProfilesConfig
        {
            Version = "1.0.0",
            Profiles = new Dictionary<string, IspProfile>
            {
                ["default"] = new IspProfile
                {
                    Id = "default",
                    Name = "Универсальный",
                    Method = "fake",
                    Asn = new List<string>(),
                    Confidence = 50
                },
                ["rtk"] = new IspProfile
                {
                    Id = "rtk",
                    Name = "Ростелеком",
                    Asn = new List<string> { "12389", "25490" },
                    Method = "fake",
                    Confidence = 90
                },
                ["mgts"] = new IspProfile
                {
                    Id = "mgts",
                    Name = "МГТС/МТС",
                    Asn = new List<string> { "25513", "8359" },
                    Method = "fake",
                    Confidence = 85
                },
                ["beeline"] = new IspProfile
                {
                    Id = "beeline",
                    Name = "Билайн",
                    Asn = new List<string> { "8402", "3216" },
                    Method = "multisplit",
                    Confidence = 95
                },
                ["megafon"] = new IspProfile
                {
                    Id = "megafon",
                    Name = "Мегафон",
                    Asn = new List<string> { "31133", "25159" },
                    Method = "fake",
                    Confidence = 80
                }
            }
        };
    }
}
