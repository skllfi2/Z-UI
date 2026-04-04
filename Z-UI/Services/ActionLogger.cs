using System;
using System.Collections.Concurrent;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ZUI.Services
{
    public class ActionLogger
    {
        private static readonly string LogPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Z-UI", "actions.log");

        private static readonly ConcurrentQueue<LogEntry> _queue = new();
        private static readonly Timer _timer;
        private static readonly object _lock = new();

        static ActionLogger()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
            _timer = new Timer(Flush, null, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5));
        }

        public static void Log(string action, string? details = null)
        {
            var entry = new LogEntry
            {
                Timestamp = DateTime.Now,
                Action = action,
                Details = details
            };
            _queue.Enqueue(entry);
        }

        public static void LogStart(string strategy)
        {
            Log("START", $"Запущена стратегия: {strategy}");
        }

        public static void LogStop()
        {
            Log("STOP", "Сервис остановлен");
        }

        public static void LogStrategyApply(string strategy)
        {
            Log("STRATEGY", $"Применена стратегия: {strategy}");
        }

        public static void LogTestRun(string mode, int count)
        {
            Log("TEST", $"Запущено тестирование: {mode}, стратегий: {count}");
        }

        public static void LogTestComplete(string bestStrategy)
        {
            Log("TEST_COMPLETE", $"Лучший конфиг: {bestStrategy}");
        }

        public static void LogSettingsChange(string setting, string value)
        {
            Log("SETTINGS", $"{setting} = {value}");
        }

        public static void LogError(string context, string error)
        {
            Log("ERROR", $"{context}: {error}");
        }

        private static void Flush(object? state)
        {
            if (_queue.IsEmpty) return;

            lock (_lock)
            {
                var sb = new StringBuilder();
                while (_queue.TryDequeue(out var entry))
                {
                    sb.AppendLine($"[{entry.Timestamp:yyyy-MM-dd HH:mm:ss}] [{entry.Action}] {entry.Details}");
                }

                if (sb.Length > 0)
                {
                    try
                    {
                        File.AppendAllText(LogPath, sb.ToString());
                    }
                    catch { }
                }
            }
        }

        public static string[] GetRecentLogs(int count = 100)
        {
            Flush(null);
            try
            {
                if (!File.Exists(LogPath)) return [];
                var lines = File.ReadAllLines(LogPath);
                return lines.Length <= count ? lines : lines[^count..];
            }
            catch
            {
                return [];
            }
        }

        public static void Clear()
        {
            lock (_lock)
            {
                while (_queue.TryDequeue(out _)) { }
                try { File.Delete(LogPath); } catch { }
            }
        }

        private class LogEntry
        {
            public DateTime Timestamp { get; set; }
            public string Action { get; set; } = "";
            public string? Details { get; set; }
        }
    }
}
