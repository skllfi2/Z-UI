// IStrategyParamsProvider.cs - Extracted from IStrategyGeneratorService to break circular dependency
// StrategyTestService only needs LoadParametersAsync(), not the full generator interface.
using ZUI.Models;

namespace ZUI.Services;

/// <summary>
/// Provides strategy parameters configuration.
/// Extracted from <see cref="IStrategyGeneratorService"/> to break the circular dependency:
/// StrategyGeneratorService → IStrategyTestService → IStrategyGeneratorService.
/// </summary>
public interface IStrategyParamsProvider
{
    /// <summary>
    /// Load strategy parameters from configuration
    /// </summary>
    Task<StrategyParamsConfig> LoadParametersAsync();
}
