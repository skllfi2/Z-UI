using System;
using System.IO;
using System.Text.Json;

namespace ZUI
{
 public static class AppSettings
 {
 private static readonly string _path = Path.Combine(
 Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
 "Z-UI", "settings.json");

 public static bool AutoStartZapret { get; set; } = false;
 public static bool AutoStartWithAdmin { get; set; } = false;
 public static bool MinimizeToTrayOnStart { get; set; } = false;
 public static bool SoundEffects { get; set; } = false;
 public static bool ToastNotifications { get; set; } = true;
 public static bool AutoUpdateCheck { get; set; } = true;
 public static bool AutoUpdateDownload { get; set; } = false;
 public static string Theme { get; set; } = "Default";
 public static string Language { get; set; } = GetSystemLanguage();
 public static string GameFilter { get; set; } = "disabled";
 public static string IpsetFilter { get; set; } = "any";
 public static bool SetupCompleted { get; set; } = false;
 public static string CurrentStrategy { get; set; } = "General";
 public static bool AnimNavIcons { get; set; } = true;
 public static bool AnimButtons { get; set; } = true;
 public static bool AnimCards { get; set; } = true;
 public static bool HotkeysEnabled { get; set; } = true;
 public static string HostsHash { get; set; } = "";
 public static DateTime HostsLastCheck { get; set; } = DateTime.MinValue;
 public static bool HostsAutoUpdate { get; set; } = false;

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
 AutoStartWithAdmin = data.AutoStartWithAdmin;
 MinimizeToTrayOnStart = data.MinimizeToTrayOnStart;
 SoundEffects = data.SoundEffects;
 ToastNotifications = data.ToastNotifications;
 AutoUpdateCheck = data.AutoUpdateCheck;
 AutoUpdateDownload = data.AutoUpdateDownload;
 Theme = data.Theme ?? "Default";
 Language = data.Language ?? GetSystemLanguage();
 GameFilter = data.GameFilter ?? "disabled";
 IpsetFilter = data.IpsetFilter ?? "any";
 SetupCompleted = data.SetupCompleted;
 CurrentStrategy = data.CurrentStrategy ?? "General";
 AnimNavIcons = data.AnimNavIcons;
 AnimButtons = data.AnimButtons;
 AnimCards = data.AnimCards;
 HotkeysEnabled = data.HotkeysEnabled;
 HostsHash = data.HostsHash ?? "";
 HostsLastCheck = data.HostsLastCheck;
 HostsAutoUpdate = data.HostsAutoUpdate;
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
 AutoStartWithAdmin = AutoStartWithAdmin,
 MinimizeToTrayOnStart = MinimizeToTrayOnStart,
 SoundEffects = SoundEffects,
 ToastNotifications = ToastNotifications,
 AutoUpdateCheck = AutoUpdateCheck,
 AutoUpdateDownload = AutoUpdateDownload,
 Theme = Theme,
 Language = Language,
 GameFilter = GameFilter,
 IpsetFilter = IpsetFilter,
 SetupCompleted = SetupCompleted,
 CurrentStrategy = CurrentStrategy,
 AnimNavIcons = AnimNavIcons,
 AnimButtons = AnimButtons,
 AnimCards = AnimCards,
 HotkeysEnabled = HotkeysEnabled,
 HostsHash = HostsHash,
 HostsLastCheck = HostsLastCheck,
 HostsAutoUpdate = HostsAutoUpdate
 };
 File.WriteAllText(_path, JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true }));
 }
 catch { }
 }

 private class SettingsData
 {
 public bool AutoStartZapret { get; set; }
 public bool AutoStartWithAdmin { get; set; }
 public bool MinimizeToTrayOnStart { get; set; }
 public bool SoundEffects { get; set; }
 public bool ToastNotifications { get; set; }
 public bool AutoUpdateCheck { get; set; } = true;
 public bool AutoUpdateDownload { get; set; } = false;
 public string? Theme { get; set; }
 public string? Language { get; set; }
 public string? GameFilter { get; set; }
 public string? IpsetFilter { get; set; }
 public bool SetupCompleted { get; set; }
 public string? CurrentStrategy { get; set; } = "General";
 public bool AnimNavIcons { get; set; } = true;
 public bool AnimButtons { get; set; } = true;
 public bool AnimCards { get; set; } = true;
 public bool HotkeysEnabled { get; set; } = true;
 public string? HostsHash { get; set; } = "";
 public DateTime HostsLastCheck { get; set; } = DateTime.MinValue;
 public bool HostsAutoUpdate { get; set; } = false;
 }

 public static string GetSystemLanguage()
 {
 var lang = System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
 return lang == "ru" ? "ru" : "en";
 }
 }
}
