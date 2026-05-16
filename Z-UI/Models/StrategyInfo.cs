// StrategyInfo.cs - Model for strategy information (Level 3 native)
namespace ZUI.Models;

/// <summary>
/// Information about a DPI bypass strategy
/// Source types: "JSON" (Phase 4 converted), "Programmatic" (built-in), "Generated" (from generator)
/// </summary>
public class StrategyInfo
{
    /// <summary>
    /// Unique identifier: "json-general", "programmatic-1", "generated-abc123"
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Display name: "General", "Fake TLS Auto"
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Source type: "JSON", "Programmatic", or "Generated"
    /// </summary>
    public string Source { get; set; } = "JSON";

    /// <summary>
    /// Path to strategy file (JSON for Level 3)
    /// </summary>
    public string? FilePath { get; set; }

    /// <summary>
    /// Whether the strategy is available (file exists)
    /// </summary>
    public bool IsAvailable { get; set; } = true;

    /// <summary>
    /// Description of the strategy
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Number of successful runs
    /// </summary>
    public int SuccessCount { get; set; }

    /// <summary>
    /// Number of failed runs
    /// </summary>
    public int FailCount { get; set; }

    /// <summary>
    /// Last time this strategy was used
    /// </summary>
    public DateTime? LastUsed { get; set; }

    /// <summary>
    /// Success rate percentage
    /// </summary>
    public double SuccessRate => TotalRuns > 0 ? (double)SuccessCount / TotalRuns * 100 : 0;

    /// <summary>
    /// Total number of runs
    /// </summary>
    public int TotalRuns => SuccessCount + FailCount;

    /// <summary>
    /// Create programmatic strategy info (built-in)
    /// </summary>
    public static StrategyInfo CreateProgrammatic(string id, string name, string? description = null)
    {
        return new StrategyInfo
        {
            Id = id,
            Name = name,
            Source = "Programmatic",
            IsAvailable = true,
            Description = description
        };
    }

    /// <summary>
    /// Create JSON strategy info (Phase 4 converted from BAT)
    /// </summary>
    public static StrategyInfo CreateJson(string jsonPath, string? name = null, string? description = null)
    {
        var fileName = Path.GetFileNameWithoutExtension(jsonPath);
        return new StrategyInfo
        {
            Id = $"json-{fileName}",
            Name = name ?? fileName,
            Source = "JSON",
            FilePath = jsonPath,
            IsAvailable = File.Exists(jsonPath),
            Description = description
        };
    }

    /// <summary>
    /// Create generated strategy info (from StrategyGenerator)
    /// </summary>
    public static StrategyInfo CreateGenerated(string id, string name, string? description = null)
    {
        return new StrategyInfo
        {
            Id = id,
            Name = name,
            Source = "Generated",
            IsAvailable = true,
            Description = description
        };
    }
}
