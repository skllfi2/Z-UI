// Services/TestResultStore.cs
// Singleton-хранилище результатов тестирования.
// ServicesPage.Testing.cs пишет сюда после теста,
// StrategiesPage читает и отображает значки.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ZUI.Services
{
    public enum StrategyRating
    {
        Unknown,
        Recommended,
        Acceptable,
        NotRecommended
    }

    public class StrategyTestSnapshot
    {
        public string ConfigName { get; set; } = "";
        public StrategyRating Rating { get; set; } = StrategyRating.Unknown;
        public bool IsBest { get; set; }
        public int HttpOk { get; set; }
        public int HttpErr { get; set; }
        public int HttpUnsup { get; set; }
        public int PingOk { get; set; }
        public int DpiOk { get; set; }
        public int DpiBlocked { get; set; }
        public TestMode Mode { get; set; }
        public DateTime TestedAt { get; set; }

        [JsonIgnore]
        public string RatingLabel => Rating switch
        {
            StrategyRating.Recommended => "Рекомендуется",
            StrategyRating.Acceptable => "Приемлемо",
            StrategyRating.NotRecommended => "Не рекомендуется",
            _ => "Требуется тестирование"
        };

        [JsonIgnore]
        public string RatingEmoji => Rating switch
        {
            StrategyRating.Recommended => "✓",
            StrategyRating.Acceptable => "~",
            StrategyRating.NotRecommended => "✗",
            _ => "?"
        };

        [JsonIgnore]
        public string Summary => Mode == TestMode.Standard
            ? $"OK {HttpOk} ERR {HttpErr} Ping {PingOk} — {TestedAt:dd.MM HH:mm}"
            : $"DPI OK {DpiOk} BLOCKED {DpiBlocked} — {TestedAt:dd.MM HH:mm}";
    }

    public static class TestResultStore
    {
        public static event Action? ResultsUpdated;

        private static readonly Dictionary<string, StrategyTestSnapshot> _results = new();
        private static readonly object _lock = new();

        private static string CachePath => Path.Combine(ZapretPaths.UtilsDir, "test_results_cache.json");

        public static void Publish(IReadOnlyList<ConfigResult> results, TestMode mode)
        {
            if (results.Count == 0) return;

            var best = ZapretTestRunner.FindBest(results, mode);

            lock (_lock)
            {
                foreach (var r in results)
                {
                    int total = mode == TestMode.Standard
                        ? r.Standard.SelectMany(t => t.Http).Count()
                        : r.Dpi.SelectMany(d => d.Lines).Count();
                    int ok = mode == TestMode.Standard ? r.HttpOk : r.DpiOk;

                    double ratio = total == 0 ? 0 : (double)ok / total;

                    var configName = r.ConfigName;
                    if (configName.EndsWith(".bat", StringComparison.OrdinalIgnoreCase))
                        configName = configName.Substring(0, configName.Length - 4);

                    var bestName = best?.ConfigName;
                    if (bestName != null && bestName.EndsWith(".bat", StringComparison.OrdinalIgnoreCase))
                        bestName = bestName.Substring(0, bestName.Length - 4);

                    var rating = configName == bestName
                        ? StrategyRating.Recommended
                        : ratio >= 0.8
                            ? StrategyRating.Recommended
                            : ratio >= 0.5
                                ? StrategyRating.Acceptable
                                : StrategyRating.NotRecommended;

                    _results[configName] = new StrategyTestSnapshot
                    {
                        ConfigName = configName,
                        Rating = rating,
                        IsBest = configName == bestName,
                        HttpOk = r.HttpOk,
                        HttpErr = r.HttpErr,
                        HttpUnsup = r.HttpUnsup,
                        PingOk = r.PingOk,
                        DpiOk = r.DpiOk,
                        DpiBlocked = r.DpiBlocked,
                        Mode = mode,
                        TestedAt = DateTime.Now
                    };
                }
            }

            TrySaveCache();
            ResultsUpdated?.Invoke();
        }

        public static StrategyTestSnapshot? Get(string configName)
        {
            lock (_lock)
            {
                _results.TryGetValue(configName, out var snap);
                return snap;
            }
        }

        public static IReadOnlyDictionary<string, StrategyTestSnapshot> GetAll()
        {
            lock (_lock) { return new Dictionary<string, StrategyTestSnapshot>(_results); }
        }

        public static bool HasAnyResults()
        {
            lock (_lock) { return _results.Count > 0; }
        }

        public static void TryLoadCache()
        {
            try
            {
                if (!File.Exists(CachePath)) return;
                var json = File.ReadAllText(CachePath);
                var list = JsonSerializer.Deserialize<List<StrategyTestSnapshot>>(json);
                if (list is null) return;
                lock (_lock)
                {
                    foreach (var s in list)
                        _results[s.ConfigName] = s;
                }
            }
            catch { }
        }

        private static void TrySaveCache()
        {
            try
            {
                List<StrategyTestSnapshot> list;
                lock (_lock) { list = new List<StrategyTestSnapshot>(_results.Values); }
                var json = JsonSerializer.Serialize(list, new JsonSerializerOptions { WriteIndented = false });
                File.WriteAllText(CachePath, json);
            }
            catch { }
        }

        public static void ClearCache()
        {
            lock (_lock) { _results.Clear(); }
            try { File.Delete(CachePath); } catch { }
            ResultsUpdated?.Invoke();
        }
    }
}
