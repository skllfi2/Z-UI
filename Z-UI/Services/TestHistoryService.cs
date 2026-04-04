using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace ZUI.Services
{
    public class TestHistoryService
    {
        private static readonly string HistoryPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Z-UI", "test_history.json");

        private List<TestHistoryEntry> _history = [];

        public IReadOnlyList<TestHistoryEntry> History => _history;

        public TestHistoryService()
        {
            Load();
        }

        public void AddEntry(TestHistoryEntry entry)
        {
            _history.Insert(0, entry);
            if (_history.Count > 50)
                _history.RemoveAt(_history.Count - 1);
            Save();
        }

        public void Clear()
        {
            _history.Clear();
            Save();
        }

        private void Load()
        {
            try
            {
                if (!File.Exists(HistoryPath)) return;
                var json = File.ReadAllText(HistoryPath);
                var data = JsonSerializer.Deserialize<List<TestHistoryEntry>>(json);
                if (data != null)
                    _history = data;
            }
            catch { }
        }

        private void Save()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(HistoryPath)!);
                var json = JsonSerializer.Serialize(_history, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(HistoryPath, json);
            }
            catch { }
        }
    }

    public class TestHistoryEntry
    {
        public DateTime Timestamp { get; set; }
        public string StrategyName { get; set; } = "";
        public string TestMode { get; set; } = "";
        public StrategyRating Rating { get; set; }
        public string RatingLabel { get; set; } = "";
        public int PassCount { get; set; }
        public int FailCount { get; set; }
        public int TotalTargets { get; set; }
        public string? BestStrategy { get; set; }
    }
}
