// StrategyGeneratorService.cs - Thin coordinator for DPI bypass strategy generation
// Delegates to: IspDetectionService (ISP detection), StrategyTestService (testing),
// WinwsArgsBuilder (arg building), DefaultStrategyConfigs (default data)
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using ZUI.Models;

namespace ZUI.Services;

/// <summary>
/// Service for generating DPI bypass strategies based on selected services and ISP profile.
/// Acts as a thin coordinator — delegates ISP detection, testing, and arg building
/// to specialized services.
/// </summary>
public class StrategyGeneratorService : IStrategyGeneratorService, IStrategyParamsProvider
{
    private readonly ILogger<StrategyGeneratorService> _logger;
    private readonly IAdaptiveEngine _adaptiveEngine;
    private readonly IIspDetectionService _ispDetectionService;
    private readonly IStrategyTestService _strategyTestService;
    private readonly string _zapretDir;

    private StrategyParamsConfig? _params;

    public StrategyGeneratorService(
        ILogger<StrategyGeneratorService> logger,
        IAdaptiveEngine adaptiveEngine,
        IIspDetectionService ispDetectionService,
        IStrategyTestService strategyTestService)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _adaptiveEngine = adaptiveEngine ?? throw new ArgumentNullException(nameof(adaptiveEngine));
        _ispDetectionService = ispDetectionService ?? throw new ArgumentNullException(nameof(ispDetectionService));
        _strategyTestService = strategyTestService ?? throw new ArgumentNullException(nameof(strategyTestService));

        // Find zapret directory
        _zapretDir = FindZapretDirectory();
    }

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
            if (Directory.Exists(fullPath) && File.Exists(Path.Combine(fullPath, "winws.exe")))
                return fullPath;
        }

        return Path.Combine(baseDir, "zapret");
    }

    /// <inheritdoc/>
    public async Task<StrategyParamsConfig> LoadParametersAsync()
    {
        if (_params != null) return _params;

        var configPath = await IspDetectionService.GetConfigPathAsync("strategy-params.json").ConfigureAwait(false);

        if (!File.Exists(configPath))
        {
            _logger.LogWarning("Strategy params not found, using defaults: {Path}", configPath);
            _params = DefaultStrategyConfigs.CreateDefaultStrategyParams();
        await SaveConfigAsync(configPath, _params).ConfigureAwait(false);
        return _params;
        }

        try
        {
            var json = await File.ReadAllTextAsync(configPath).ConfigureAwait(false);
            _params = JsonSerializer.Deserialize<StrategyParamsConfig>(json);

            if (_params == null)
            {
                _logger.LogWarning("Failed to parse strategy params, using defaults");
                _params = DefaultStrategyConfigs.CreateDefaultStrategyParams();
            }
            else
            {
                _logger.LogInformation("Loaded strategy params v{Version}", _params.Version);
            }

            return _params;
        }
    catch (Exception ex) when (ex is JsonException or IOException)
    {
        _logger.LogError(ex, "Error loading strategy params");
        _params = DefaultStrategyConfigs.CreateDefaultStrategyParams();
        return _params;
    }
    }

    /// <inheritdoc/>
    public async Task<IspProfilesConfig> LoadIspProfilesAsync()
    {
        return await _ispDetectionService.LoadIspProfilesAsync().ConfigureAwait(false) ?? new IspProfilesConfig();
    }

    /// <inheritdoc/>
    public async Task<UserServicesConfig> LoadUserServicesAsync()
    {
        var configPath = await IspDetectionService.GetConfigPathAsync("user-services.json").ConfigureAwait(false);

        if (!File.Exists(configPath))
        {
            _logger.LogDebug("User services config not found, creating default: {Path}", configPath);
            var defaultConfig = new UserServicesConfig
            {
                Version = "1.0.0",
                LastModified = DateTime.UtcNow.ToString("O"),
                SelectedServices = Array.Empty<string>(),
                CustomDomains = Array.Empty<string>()
            };
        await SaveUserServicesAsync(defaultConfig).ConfigureAwait(false);
        return defaultConfig;
        }

        try
        {
            var json = await File.ReadAllTextAsync(configPath).ConfigureAwait(false);
            var config = JsonSerializer.Deserialize<UserServicesConfig>(json);

            if (config == null)
            {
                _logger.LogWarning("Failed to parse user services config, using default");
                return new UserServicesConfig
                {
                    Version = "1.0.0",
                    LastModified = DateTime.UtcNow.ToString("O")
                };
            }

            return config;
        }
    catch (Exception ex) when (ex is JsonException or IOException)
    {
        _logger.LogError(ex, "Error loading user services config");
        return new UserServicesConfig
        {
            Version = "1.0.0",
            LastModified = DateTime.UtcNow.ToString("O")
        };
    }
    }

    /// <inheritdoc/>
    public async Task SaveUserServicesAsync(UserServicesConfig config)
    {
        var configPath = await IspDetectionService.GetConfigPathAsync("user-services.json").ConfigureAwait(false);

        try
        {
            var dir = Path.GetDirectoryName(configPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            config = config with { LastModified = DateTime.UtcNow.ToString("O") };

            var options = new JsonSerializerOptions { WriteIndented = true };
            var json = JsonSerializer.Serialize(config, options);
            await File.WriteAllTextAsync(configPath, json).ConfigureAwait(false);

            _logger.LogInformation("Saved user services config: {Path}", configPath);
        }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
    {
        _logger.LogError(ex, "Failed to save user services config");
    }
    }

    /// <inheritdoc/>
    public async Task<IspProfile> DetectIspAsync()
    {
        return await _ispDetectionService.DetectIspAsync().ConfigureAwait(false) ?? new IspProfile();
    }

    /// <inheritdoc/>
    public async Task<GeneratedStrategy> GenerateAsync(
        IEnumerable<string> selectedServiceIds,
        IspProfile? profile = null,
        IEnumerable<string>? customDomains = null,
        IEnumerable<CustomServiceConfig>? customServices = null)
    {
        var parameters = await LoadParametersAsync().ConfigureAwait(false);
        profile ??= await DetectIspAsync().ConfigureAwait(false);

        var services = selectedServiceIds
            .Select(id => parameters.Services.TryGetValue(id, out var s) ? s : null)
            .Where(s => s != null)
            .Cast<ServiceConfig>()
            .ToList();

        // Add custom domains if provided
        var customDomainsList = customDomains?.ToList() ?? new List<string>();
        var customServicesList = customServices?.ToList() ?? new List<CustomServiceConfig>();

        // Validate: need either services or custom domains
        if (services.Count == 0 && customDomainsList.Count == 0 && customServicesList.Count == 0)
        {
            throw new ArgumentException("No services, custom domains, or custom services selected");
        }

        // Build args using WinwsArgsBuilder
        var args = WinwsArgsBuilder.BuildWinwsArgs(services, profile, parameters, _zapretDir, _logger, customDomainsList, customServicesList);

        // Build name
        var nameParts = new List<string>();
        if (services.Count > 0)
        {
            nameParts.Add(string.Join(" + ", services.Select(s => s.Name)));
        }
        if (customDomainsList.Count > 0)
        {
            nameParts.Add($"{customDomainsList.Count} custom domains");
        }
        if (customServicesList.Count > 0)
        {
            nameParts.Add(string.Join(" + ", customServicesList.Select(s => s.Name)));
        }

        var strategy = new GeneratedStrategy
        {
            Id = $"generated-{DateTime.UtcNow:yyyyMMddHHmmss}",
            Name = $"{string.Join(", ", nameParts)} ({profile.Name})",
            WinwsArgs = args,
            IncludedServices = selectedServiceIds.ToList(),
            Profile = profile,
            CreatedAt = DateTime.UtcNow,
            CustomDomains = customDomainsList
        };

        _logger.LogInformation("Generated strategy: {Name}", strategy.Name);
        _logger.LogDebug("Winws args: {Args}", args);

        return strategy;
    }

    /// <inheritdoc/>
    public async Task<TestResults> TestStrategyAsync(
        GeneratedStrategy strategy,
        TestLevel level = TestLevel.Quick)
    {
        return await _strategyTestService.TestStrategyAsync(strategy, level).ConfigureAwait(false) ?? new TestResults();
    }

    /// <inheritdoc/>
    public async Task<UniversalStrategyResult> GenerateUniversalStrategyAsync(CancellationToken ct = default)
    {
        _logger.LogInformation("Generating universal strategy (all services)");

        var parameters = await LoadParametersAsync().ConfigureAwait(false);
        var profile = await DetectIspAsync().ConfigureAwait(false);

        // Collect all predefined service IDs
        var allServiceIds = parameters.Services.Keys.ToList();
        var allServices = parameters.Services.Values.ToList();

        if (allServices.Count == 0)
        {
            _logger.LogWarning("No predefined services found for universal strategy");
            return UniversalStrategyResult.Failed("Нет доступных сервисов для генерации универсальной стратегии");
        }

        // Build winws args using WinwsArgsBuilder
        var args = WinwsArgsBuilder.BuildWinwsArgs(allServices, profile, parameters, _zapretDir, _logger);

        // Also collect domain list files from zapret/lists/
        var listsDir = Path.Combine(_zapretDir, "lists");
        var includedLists = new List<string>();
        if (Directory.Exists(listsDir))
        {
            foreach (var listFile in Directory.GetFiles(listsDir, "*.txt"))
            {
                var fileName = Path.GetFileName(listFile);
                includedLists.Add(fileName);

                // Append each list as a --hostlist filter rule
                args += $" --new --filter-tcp=443 --hostlist=\"{listFile}\" --dpi-desync={profile.Method}";
                if (profile.MethodParams.TryGetValue("repeats", out var repeats))
                    args += $" --dpi-desync-repeats={WinwsArgsBuilder.UnwrapValue(repeats)}";
                if (profile.MethodParams.TryGetValue("fooling", out var fooling))
                    args += $" --dpi-desync-fooling={WinwsArgsBuilder.UnwrapValue(fooling)}";
            }
        }

        // Save generated strategy to config
        var strategy = new GeneratedStrategy
        {
            Id = $"universal-{DateTime.UtcNow:yyyyMMddHHmmss}",
            Name = $"Universal ({allServices.Count} services, {includedLists.Count} lists) [{profile.Name}]",
            WinwsArgs = args,
            IncludedServices = allServiceIds,
            Profile = profile,
            CreatedAt = DateTime.UtcNow
        };

        var strategyFilePath = await IspDetectionService.GetConfigPathAsync("universal-strategy.json").ConfigureAwait(false);
        try
        {
            var dir = Path.GetDirectoryName(strategyFilePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            var options = new JsonSerializerOptions { WriteIndented = true };
            var json = JsonSerializer.Serialize(strategy, options);
            await File.WriteAllTextAsync(strategyFilePath, json, ct).ConfigureAwait(false);
            _logger.LogInformation("Universal strategy saved to: {Path}", strategyFilePath);
        }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
    {
        _logger.LogError(ex, "Failed to save universal strategy file");
    }

        _logger.LogInformation("Universal strategy generated: {Name} ({Services} services, {Lists} lists)",
            strategy.Name, allServices.Count, includedLists.Count);
        _logger.LogDebug("Winws args: {Args}", args);

        return UniversalStrategyResult.Succeeded(args, strategyFilePath, allServiceIds, includedLists);
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
