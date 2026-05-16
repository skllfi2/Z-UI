// IStrategyGeneratorService.cs - Interface for strategy generator
using ZUI.Models;

namespace ZUI.Services;

/// <summary>
/// Service for generating DPI bypass strategies
/// </summary>
public interface IStrategyGeneratorService
{
    /// <summary>
    /// Load strategy parameters from configuration
    /// </summary>
    Task<StrategyParamsConfig> LoadParametersAsync();
    
    /// <summary>
    /// Load ISP profiles from configuration
    /// </summary>
    Task<IspProfilesConfig> LoadIspProfilesAsync();
    
    /// <summary>
    /// Load user services configuration
    /// </summary>
    Task<UserServicesConfig> LoadUserServicesAsync();
    
    /// <summary>
    /// Save user services configuration
    /// </summary>
    Task SaveUserServicesAsync(UserServicesConfig config);
    
    /// <summary>
    /// Detect ISP by IP or ASN
    /// </summary>
    Task<IspProfile> DetectIspAsync();
    
    /// <summary>
    /// Generate winws arguments for selected services
    /// </summary>
    Task<GeneratedStrategy> GenerateAsync(
        IEnumerable<string> selectedServiceIds,
        IspProfile? profile = null,
        IEnumerable<string>? customDomains = null,
        IEnumerable<CustomServiceConfig>? customServices = null);
    
    /// <summary>
    /// Test strategy
    /// </summary>
    Task<TestResults> TestStrategyAsync(
        GeneratedStrategy strategy,
        TestLevel level = TestLevel.Quick);
    
    /// <summary>
    /// Generate a universal strategy combining all services into one winws.exe invocation.
    /// Uses all domain lists from zapret/lists/ and all predefined services.
    /// </summary>
    Task<UniversalStrategyResult> GenerateUniversalStrategyAsync(CancellationToken ct = default);
}
