using System;
using System.IO;
using System.Text.Json;

namespace ZapretGUI
{
    public static class AppSettings
    {
        private static readonly string _path = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "ZapretGUI", "settings.json");

        public static bool AutoStartZapret { get; set; } = false;
        public static bool MinimizeToTrayOnStart { get; set; } = false;
        public static bool SoundEffects { get; set; } = false;
        public static bool ToastNotifications { get; set; } = true;
        public static string Theme { get; set; } = "Default";
        public static string Language { get; set; } = "ru";

        static AppSettings() => Load();

        public static void Load()
        {
            try
            {
                if (!File.Exists(_path)) return;
                var json = File.ReadAllText(_path);
                var data = JsonSerializer.Deserialize<SettingsData>(json);
                if (data == null) return;
                AutoStartZapret = data.AutoStartZapret;
                MinimizeToTrayOnStart = data.MinimizeToTrayOnStart;
                SoundEffects = data.SoundEffects;
                ToastNotifications = data.ToastNotifications;
                Theme = data.Theme ?? "Default";
                Language = data.Language ?? "ru";
            }
            catch { }
        }

        public static void Save()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
                var data = new SettingsData
                {
                    AutoStartZapret = AutoStartZapret,
                    MinimizeToTrayOnStart = MinimizeToTrayOnStart,
                    SoundEffects = SoundEffects,
                    ToastNotifications = ToastNotifications,
                    Theme = Theme,
                    Language = Language
                };
                File.WriteAllText(_path, JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch { }
        }

        private class SettingsData
        {
            public bool AutoStartZapret { get; set; }
            public bool MinimizeToTrayOnStart { get; set; }
            public bool SoundEffects { get; set; }
            public bool ToastNotifications { get; set; }
            public string? Theme { get; set; }
            public string? Language { get; set; }
        }
    }
}