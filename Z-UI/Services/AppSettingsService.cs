// AppSettingsService.cs - Centralized settings management
using System.Text.Json;
using Microsoft.UI.Xaml;

namespace ZUI.Services;

/// <summary>
/// Centralized application settings service
/// Syncs settings across all pages and handles persistence
/// </summary>
public interface IAppSettingsService
{
    // Protection
    bool AutoProtect { get; set; }
    string DefaultStrategy { get; set; }
    bool RunAsAdmin { get; set; }

    // DNS
    string DefaultDnsMode { get; set; }
    int DnsPort { get; set; }

    // Notifications
    bool NotificationsEnabled { get; set; }
    bool NotifyOnStart { get; set; }
    bool NotifyOnStop { get; set; }
    bool NotifyOnErrors { get; set; }

    // Appearance
    string AppTheme { get; set; }
    bool AnimationsEnabled { get; set; }
    string Language { get; set; }

    // Tray
    bool MinimizeToTray { get; set; }
    bool StartInTray { get; set; }
    bool ShowTrayIcon { get; set; }

    // Sound
    bool SoundEffects { get; set; }

    // Logging
    string LogLevel { get; set; }

    // Startup
    bool Autostart { get; set; }
    bool StartMinimized { get; set; }

    // Updates
    bool AutoUpdate { get; set; }
    bool CheckUpdatesOnStart { get; set; }

    // Current state (not persisted)
    string CurrentStrategy { get; set; }
    string CurrentDnsMode { get; set; }

    // Events
    event Action<string, object>? SettingChanged;

    void Load();
    void Save();
    T GetSetting<T>(string key, T defaultValue);
    void SetSetting(string key, object value);
}

public class AppSettingsService : IAppSettingsService
{
    private readonly string _configPath;
    private Dictionary<string, object> _settings = new();

    public event Action<string, object>? SettingChanged;

    // Protection
    public bool AutoProtect { get => GetSetting("AutoProtect", false); set => SetSetting("AutoProtect", value); }
    public string DefaultStrategy { get => GetSetting("DefaultStrategy", "auto"); set => SetSetting("DefaultStrategy", value); }
    public bool RunAsAdmin { get => GetSetting("RunAsAdmin", false); set => SetSetting("RunAsAdmin", value); }

    // DNS
    public string DefaultDnsMode { get => GetSetting("DefaultDnsMode", "Proxy"); set => SetSetting("DefaultDnsMode", value); }
    public int DnsPort { get => GetSetting("DnsPort", 5353); set => SetSetting("DnsPort", value); }

    // Notifications
    public bool NotificationsEnabled { get => GetSetting("NotificationsEnabled", true); set => SetSetting("NotificationsEnabled", value); }
    public bool NotifyOnStart { get => GetSetting("NotifyOnStart", true); set => SetSetting("NotifyOnStart", value); }
    public bool NotifyOnStop { get => GetSetting("NotifyOnStop", false); set => SetSetting("NotifyOnStop", value); }
    public bool NotifyOnErrors { get => GetSetting("NotifyOnErrors", true); set => SetSetting("NotifyOnErrors", value); }

    // Appearance
    public string AppTheme { get => GetSetting("AppTheme", "Default"); set => SetSetting("AppTheme", value); }
    public bool AnimationsEnabled { get => GetSetting("AnimationsEnabled", true); set => SetSetting("AnimationsEnabled", value); }
    public string Language { get => GetSetting("Language", "ru"); set => SetSetting("Language", value); }

    // Tray
    public bool MinimizeToTray { get => GetSetting("MinimizeToTray", true); set => SetSetting("MinimizeToTray", value); }
    public bool StartInTray { get => GetSetting("StartInTray", false); set => SetSetting("StartInTray", value); }
    public bool ShowTrayIcon { get => GetSetting("ShowTrayIcon", true); set => SetSetting("ShowTrayIcon", value); }

    // Sound
    public bool SoundEffects { get => GetSetting("SoundEffects", true); set => SetSetting("SoundEffects", value); }

    // Logging
    public string LogLevel { get => GetSetting("LogLevel", "Information"); set => SetSetting("LogLevel", value); }

    // Startup
    public bool Autostart { get => GetSetting("Autostart", false); set => SetSetting("Autostart", value); }
    public bool StartMinimized { get => GetSetting("StartMinimized", false); set => SetSetting("StartMinimized", value); }

    // Updates
    public bool AutoUpdate { get => GetSetting("AutoUpdate", true); set => SetSetting("AutoUpdate", value); }
    public bool CheckUpdatesOnStart { get => GetSetting("CheckUpdatesOnStart", false); set => SetSetting("CheckUpdatesOnStart", value); }

    // Current state (runtime, not persisted to file but in memory)
    public string CurrentStrategy { get; set; } = "auto";
    public string CurrentDnsMode { get; set; } = "Proxy";

    public AppSettingsService()
    {
        _configPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Z-UI",
            "config",
            "settings.json");

        Load();
    }

    public void Load()
    {
        try
        {
            if (File.Exists(_configPath))
            {
                var json = File.ReadAllText(_configPath);
                _settings = JsonSerializer.Deserialize<Dictionary<string, object>>(json)
                    ?? new Dictionary<string, object>();
            }

            // Try ApplicationData if available (packaged mode)
            TryLoadFromApplicationData();

            // Initialize current state from defaults
            CurrentStrategy = DefaultStrategy;
            CurrentDnsMode = DefaultDnsMode;
        }
 catch (IOException)
 {
 _settings = new Dictionary<string, object>();
 }
 catch (UnauthorizedAccessException)
 {
 _settings = new Dictionary<string, object>();
 }
    }

    public void Save()
    {
        try
        {
            var dir = Path.GetDirectoryName(_configPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            var json = JsonSerializer.Serialize(_settings, new JsonSerializerOptions
            {
                WriteIndented = true
            });
            File.WriteAllText(_configPath, json);

            TrySaveToApplicationData();
        }
 catch (IOException) { }
 catch (UnauthorizedAccessException) { }
 }

 public T GetSetting<T>(string key, T defaultValue)
 {
 if (_settings.TryGetValue(key, out var value))
 {
 try
 {
 // Handle JsonElement from System.Text.Json
 if (value is JsonElement element)
 {
 var jsonValue = element.ToString();
 return (T)Convert.ChangeType(jsonValue, typeof(T));
 }
 return (T)Convert.ChangeType(value, typeof(T));
 }
 catch (InvalidCastException) { }
 catch (FormatException) { }
 catch (OverflowException) { }
        }
        return defaultValue;
    }

    public void SetSetting(string key, object value)
    {
        _settings[key] = value;
        Save();
        SettingChanged?.Invoke(key, value);
    }

    private void TryLoadFromApplicationData()
    {
        try
        {
            // ApplicationData may not be available in unpackaged mode
            var localSettings = Windows.Storage.ApplicationData.Current?.LocalSettings;
            if (localSettings == null)
                return;

            foreach (var key in _settings.Keys.ToList())
            {
                if (localSettings.Values.TryGetValue(key, out var value))
                    _settings[key] = value;
            }
        }
 catch (UnauthorizedAccessException) { }
 catch (InvalidOperationException) { }
 }

 private void TrySaveToApplicationData()
 {
 try
 {
 var localSettings = Windows.Storage.ApplicationData.Current?.LocalSettings;
 if (localSettings == null)
 return;

 foreach (var kvp in _settings)
 localSettings.Values[kvp.Key] = kvp.Value;
 }
 catch (UnauthorizedAccessException) { }
 catch (InvalidOperationException) { }
    }
}
