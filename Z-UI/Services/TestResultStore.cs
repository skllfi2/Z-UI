// TestResultStore.cs - Cache for strategy test results
using System.Collections.Concurrent;
using System.IO;
using System.Text.Json;

namespace ZUI.Services;

/// <summary>
/// Cached test result for a single strategy (used by TestResultStore).
/// Distinct from <see cref="CachedStrategyResult"/> in IStrategyManager which tracks batch results.
/// </summary>
public record CachedStrategyResult(string StrategyId, bool Passed, int? LatencyMs, DateTime TestedAt);

/// <summary>
/// JSON-based cache for strategy test results in %LocalAppData%\Z-UI\cache\.
/// </summary>
public static class TestResultStore
{
    private static readonly object _lock = new();
    private static Dictionary<string, CachedStrategyResult>? _cache;
    private static readonly string CacheDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Z-UI", "cache");
    private static readonly string CacheFilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Z-UI", "cache", "test-results.json");

    private static readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = true };

    /// <summary>
    /// Try to load cached test results from disk.
    /// </summary>
    public static void TryLoadCache()
    {
        lock (_lock)
        {
            try
            {
                if (_cache is not null)
                    return; // Already loaded

                if (!File.Exists(CacheFilePath))
                {
                    _cache = new Dictionary<string, CachedStrategyResult>();
                    return;
                }

                var json = File.ReadAllText(CacheFilePath);
                _cache = JsonSerializer.Deserialize<Dictionary<string, CachedStrategyResult>>(json)
                         ?? new Dictionary<string, CachedStrategyResult>();

                System.Diagnostics.Debug.WriteLine($"[Z-UI] TestResultStore: Loaded {_cache.Count} cached test results");
            }
            catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
            {
                System.Diagnostics.Debug.WriteLine($"[Z-UI] TestResultStore: Cache load failed - {ex.Message}");
                _cache = new Dictionary<string, CachedStrategyResult>();
            }
        }
    }

    /// <summary>
    /// Save test results to cache (merges with existing).
    /// </summary>
    public static void SaveCache(Dictionary<string, CachedStrategyResult> results)
    {
        lock (_lock)
        {
            try
            {
                _cache ??= new Dictionary<string, CachedStrategyResult>();

                foreach (var kvp in results)
                    _cache[kvp.Key] = kvp.Value;

                if (!Directory.Exists(CacheDirectory))
                    Directory.CreateDirectory(CacheDirectory);

                var json = JsonSerializer.Serialize(_cache, _jsonOptions);
                File.WriteAllText(CacheFilePath, json);

                System.Diagnostics.Debug.WriteLine($"[Z-UI] TestResultStore: Saved {results.Count} test results");
            }
            catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
            {
                System.Diagnostics.Debug.WriteLine($"[Z-UI] TestResultStore: SaveCache failed - {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Try to get a cached test result for a strategy.
    /// </summary>
    public static bool TryGetResult(string strategyId, out CachedStrategyResult? result)
    {
        lock (_lock)
        {
            if (_cache is null)
                TryLoadCache();

            result = null;
            if (_cache is null)
                return false;

            if (_cache.TryGetValue(strategyId, out var cached))
            {
                result = cached;
                return true;
            }

            return false;
        }
    }

    /// <summary>
    /// Clear all cached test results.
    /// </summary>
    public static void ClearCache()
    {
        lock (_lock)
        {
            try
            {
                if (File.Exists(CacheFilePath))
                    File.Delete(CacheFilePath);

                _cache = null;
                System.Diagnostics.Debug.WriteLine("[Z-UI] TestResultStore: Cache cleared");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                System.Diagnostics.Debug.WriteLine($"[Z-UI] TestResultStore: ClearCache failed - {ex.Message}");
            }
        }
    }
}
