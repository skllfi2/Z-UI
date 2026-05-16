// AppSettings.cs - Static bridge for application settings
// Provides quick access to settings from ViewModels and code-behind
// without requiring DI resolution of IAppSettingsService.
namespace ZUI;

/// <summary>
/// Static bridge for application settings. Used by ViewModels and App.xaml.cs
/// for quick access without DI. Will be bridged to IAppSettingsService in a future iteration.
/// </summary>
public static class AppSettings
{
    private static string _currentStrategy = "General";
    private static string _gameFilter = "disabled";
    private static string _ipsetFilter = "any";
    private static bool _autoUpdateCheck = true;
    private static bool _autoUpdateDownload;
    private static bool _autoStartZapret;

    /// <summary>
    /// Delegate bridge to IAppSettingsService.Save().
    /// Set during App startup to connect static AppSettings to the DI-registered service.
    /// </summary>
    public static Func<bool>? SaveDelegate { get; set; }

    /// <summary>Raised when CurrentStrategy changes.</summary>
    public static event Action? StrategyChanged;

    public static string CurrentStrategy
    {
        get => _currentStrategy;
        set
        {
            if (_currentStrategy != value)
            {
                _currentStrategy = value;
                StrategyChanged?.Invoke();
            }
        }
    }

    public static string GameFilter
    {
        get => _gameFilter;
        set => _gameFilter = value;
    }

    public static string IpsetFilter
    {
        get => _ipsetFilter;
        set => _ipsetFilter = value;
    }

    public static bool AutoUpdateCheck
    {
        get => _autoUpdateCheck;
        set => _autoUpdateCheck = value;
    }

    public static bool AutoUpdateDownload
    {
        get => _autoUpdateDownload;
        set => _autoUpdateDownload = value;
    }

    public static bool AutoStartZapret
    {
        get => _autoStartZapret;
        set => _autoStartZapret = value;
    }

    /// <summary>
    /// Save settings to persistent storage via IAppSettingsService bridge.
    /// Falls back to debug logging if SaveDelegate is not configured.
    /// </summary>
    public static bool Save()
    {
        if (SaveDelegate is not null)
            return SaveDelegate.Invoke();

        System.Diagnostics.Debug.WriteLine($"[Z-UI] AppSettings.Save() (no delegate): Strategy={CurrentStrategy}, GameFilter={GameFilter}, IpsetFilter={IpsetFilter}");
        return false;
    }
}
