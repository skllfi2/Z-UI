// SettingsViewModel.cs - MVVM settings page bound to IAppSettingsService
using System.Collections.ObjectModel;
using System.Reflection;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.Win32;
using ZUI.Models;
using ZUI.Services;

namespace ZUI.ViewModels;

/// <summary>
/// ViewModel for the Settings page.
/// All settings are synced via IAppSettingsService (centralized persistence).
/// Strategies are loaded from IStrategyManager (real available strategies).
/// Autostart uses Windows Registry (HKCU Run key).
/// </summary>
public partial class SettingsViewModel : ObservableObject
{
    private readonly IAppSettingsService _settings;
    private readonly IStrategyManager _strategyManager;
    private readonly MalwLinkUpdateService _updateService;
    private DispatcherQueue? _dispatcherQueue;

    // ── Protection ──────────────────────────────────────────

    [ObservableProperty]
    private bool _autoProtect;

    [ObservableProperty]
    private int _selectedStrategyIndex;

    [ObservableProperty]
    private ObservableCollection<StrategyInfo> _availableStrategies = new();

    [ObservableProperty]
    private bool _runAsAdmin;

    // ── DNS ─────────────────────────────────────────────────

    [ObservableProperty]
    private int _selectedDnsModeIndex;

    // ── Notifications ───────────────────────────────────────

    [ObservableProperty]
    private bool _notificationsEnabled = true;

    [ObservableProperty]
    private bool _notifyOnStart = true;

    [ObservableProperty]
    private bool _notifyOnStop;

    [ObservableProperty]
    private bool _notifyOnErrors = true;

    // ── Appearance ──────────────────────────────────────────

    [ObservableProperty]
    private int _selectedThemeIndex = 2; // Default = "Как в системе"

    [ObservableProperty]
    private bool _animationsEnabled = true;

    // ── Language ───────────────────────────────────────────

    [ObservableProperty]
    private int _selectedLanguageIndex; // 0=Русский, 1=English

    // ── Tray ────────────────────────────────────────────────

    [ObservableProperty]
    private bool _minimizeToTray = true;

    [ObservableProperty]
    private bool _startInTray;

    [ObservableProperty]
    private bool _showTrayIcon = true;

    // ── Sound ───────────────────────────────────────────────

    [ObservableProperty]
    private bool _soundEffects = true;

    // ── Logging ─────────────────────────────────────────────

    [ObservableProperty]
    private int _selectedLogLevelIndex = 1; // Information

    // ── Startup ─────────────────────────────────────────────

    [ObservableProperty]
    private bool _autostart;

    [ObservableProperty]
    private bool _startMinimized;

    // ── Updates ─────────────────────────────────────────────

    [ObservableProperty]
    private bool _autoUpdate = true;

    [ObservableProperty]
    private bool _checkUpdatesOnStart;

    [ObservableProperty]
    private string _versionText = "";

    // ── Status ──────────────────────────────────────────────

    [ObservableProperty]
    private bool _isCheckingUpdates;

    [ObservableProperty]
    private string _updateStatusText = "";

    /// <summary>Worker Service version string (fetched from IPC).</summary>
    [ObservableProperty]
    private string _workerVersion = "";

    /// <summary>Error message when autostart registry write fails (shown in InfoBar).</summary>
    [ObservableProperty]
    private string _autostartError = "";

    /// <summary>Whether a download update is in progress.</summary>
    [ObservableProperty]
    private bool _isDownloadingUpdate;

    /// <summary>Whether autostart registry write has failed (binds to InfoBar.IsOpen).</summary>
    public bool HasAutostartError => !string.IsNullOrEmpty(AutostartError);

    partial void OnAutostartErrorChanged(string value) => OnPropertyChanged(nameof(HasAutostartError));

    /// <summary>Event to request theme change on the main window.</summary>
    public event Action<ElementTheme>? ThemeChangeRequested;

    /// <summary>Event to request a ContentDialog from the view (import/export/reset confirmations).</summary>
    public event Func<string, string, string, string, Task<bool>>? DialogRequested;

    private const string AutostartRegKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string AutostartValueName = "Z-UI";

    public SettingsViewModel(
        IAppSettingsService settings,
        IStrategyManager strategyManager,
        MalwLinkUpdateService updateService)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _strategyManager = strategyManager ?? throw new ArgumentNullException(nameof(strategyManager));
        _updateService = updateService ?? throw new ArgumentNullException(nameof(updateService));

        try
        {
            _dispatcherQueue = DispatcherQueue.GetForCurrentThread();
        }
        catch (InvalidOperationException) { /* Not on UI thread */ }

        _settings.SettingChanged += OnExternalSettingChanged;

        LoadFromSettings();
        LoadStrategies();
        LoadVersion();
        // LoadWorkerVersionAsync deferred until SetDispatcherQueue is called
        ReadAutostartFromRegistry();
    }

    /// <summary>
    /// Set the DispatcherQueue for UI thread marshalling.
    /// Called from SettingsPage.OnNavigatedTo. Triggers deferred loading.
    /// </summary>
    public void SetDispatcherQueue(Microsoft.UI.Dispatching.DispatcherQueue queue)
    {
        if (_dispatcherQueue != null) return;
        _dispatcherQueue = queue;
        _ = LoadWorkerVersionAsync();
    }

    // ── Load from centralized settings ──────────────────────

    private void LoadFromSettings()
    {
        AutoProtect = _settings.AutoProtect;
        RunAsAdmin = _settings.RunAsAdmin;

        SelectedDnsModeIndex = _settings.DefaultDnsMode switch
        {
            "DoH" => 1,
            "None" => 2,
            _ => 0
        };
        NotificationsEnabled = _settings.NotificationsEnabled;
        NotifyOnStart = _settings.NotifyOnStart;
        NotifyOnStop = _settings.NotifyOnStop;
        NotifyOnErrors = _settings.NotifyOnErrors;

        SelectedThemeIndex = _settings.AppTheme switch
        {
            "Light" => 0,
            "Dark" => 1,
            _ => 2
        };
        AnimationsEnabled = _settings.AnimationsEnabled;

        SelectedLanguageIndex = _settings.Language switch
        {
            "en" => 1,
            _ => 0
        };

        MinimizeToTray = _settings.MinimizeToTray;
        StartInTray = _settings.StartInTray;
        ShowTrayIcon = _settings.ShowTrayIcon;

        SoundEffects = _settings.SoundEffects;

        SelectedLogLevelIndex = _settings.LogLevel switch
        {
            "Debug" => 0,
            "Error" => 2,
            "None" => 3,
            _ => 1
        };

        StartMinimized = _settings.StartMinimized;

        AutoUpdate = _settings.AutoUpdate;
        CheckUpdatesOnStart = _settings.CheckUpdatesOnStart;
    }

    private void LoadStrategies()
    {
        AvailableStrategies.Clear();

        var autoStrategy = StrategyInfo.CreateProgrammatic("auto", LocalizationService.Get("AutoRecommended"), LocalizationService.Get("AutoRecommendedDesc"));
        AvailableStrategies.Add(autoStrategy);

        var strategies = _strategyManager.GetAvailableStrategies();
        foreach (var strategy in strategies)
        {
            AvailableStrategies.Add(strategy);
        }

        var currentId = _settings.DefaultStrategy;
        SelectedStrategyIndex = 0;
        for (int i = 0; i < AvailableStrategies.Count; i++)
        {
            if (AvailableStrategies[i].Id == currentId)
            {
                SelectedStrategyIndex = i;
                break;
            }
        }
    }

    private void LoadVersion()
    {
        try
        {
            var version = Assembly.GetExecutingAssembly().GetName().Version;
        VersionText = version != null
            ? LocalizationService.Get("CurrentVersion", $"{version.Major}.{version.Minor}.{version.Build}")
            : LocalizationService.Get("CurrentVersionUnknown");
    }
    catch
    {
        VersionText = LocalizationService.Get("CurrentVersionUnknown");
    }
    }

    private async Task LoadWorkerVersionAsync()
    {
        try
        {
            // Worker version from MalwLinkUpdateService
            var currentVersion = await _updateService.GetCurrentVersionAsync().ConfigureAwait(false);
        WorkerVersion = !string.IsNullOrEmpty(currentVersion)
            ? LocalizationService.Get("WorkerVersion", currentVersion)
            : LocalizationService.Get("WorkerVersionUnknown");
    }
    catch (IOException)
    {
        WorkerVersion = LocalizationService.Get("WorkerNotConnected");
    }
    catch (TimeoutException)
    {
        WorkerVersion = LocalizationService.Get("WorkerNotConnected");
    }
    catch (InvalidOperationException)
    {
        WorkerVersion = LocalizationService.Get("WorkerNotConnected");
    }
    }

    // ── Registry-based autostart ────────────────────────────

    private void ReadAutostartFromRegistry()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(AutostartRegKey, writable: false);
            var value = key?.GetValue(AutostartValueName) as string;
            Autostart = !string.IsNullOrEmpty(value);
        }
        catch
        {
            Autostart = false;
        }
    }

    private void WriteAutostartToRegistry(bool enable)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(AutostartRegKey, writable: true);
            if (key == null)
            {
                AutostartError = LocalizationService.Get("RegistryOpenFailed");
                return;
            }

            if (enable)
            {
                var exePath = Environment.ProcessPath;
                if (!string.IsNullOrEmpty(exePath))
                {
                    var args = StartMinimized ? " --minimized" : "";
                    key.SetValue(AutostartValueName, $"\"{exePath}\"{args}");
                }
            }
            else
            {
                key.DeleteValue(AutostartValueName, throwOnMissingValue: false);
            }

            AutostartError = "";
        }
    catch (IOException ex)
    {
        AutostartError = LocalizationService.Get("RegistryError", ex.Message);
        System.Diagnostics.Debug.WriteLine($"Failed to write autostart registry: {ex.Message}");
    }
    catch (UnauthorizedAccessException ex)
    {
        AutostartError = LocalizationService.Get("RegistryError", ex.Message);
        System.Diagnostics.Debug.WriteLine($"Failed to write autostart registry: {ex.Message}");
    }
    }

    // ── Property changed handlers (sync to IAppSettingsService) ──

    partial void OnAutoProtectChanged(bool value) => _settings.AutoProtect = value;

    partial void OnSelectedStrategyIndexChanged(int value)
    {
        if (value >= 0 && value < AvailableStrategies.Count)
            _settings.DefaultStrategy = AvailableStrategies[value].Id;
    }

    partial void OnRunAsAdminChanged(bool value) => _settings.RunAsAdmin = value;

    partial void OnSelectedDnsModeIndexChanged(int value)
    {
        _settings.DefaultDnsMode = value switch
        {
            1 => "DoH",
            2 => "None",
            _ => "Proxy"
        };
    }

    partial void OnNotificationsEnabledChanged(bool value) => _settings.NotificationsEnabled = value;
    partial void OnNotifyOnStartChanged(bool value) => _settings.NotifyOnStart = value;
    partial void OnNotifyOnStopChanged(bool value) => _settings.NotifyOnStop = value;
    partial void OnNotifyOnErrorsChanged(bool value) => _settings.NotifyOnErrors = value;

    partial void OnSelectedThemeIndexChanged(int value)
    {
        var theme = value switch
        {
            0 => "Light",
            1 => "Dark",
            _ => "Default"
        };
        _settings.AppTheme = theme;

        var elementTheme = theme switch
        {
            "Light" => ElementTheme.Light,
            "Dark" => ElementTheme.Dark,
            _ => ElementTheme.Default
        };
        ThemeChangeRequested?.Invoke(elementTheme);
    }

    partial void OnAnimationsEnabledChanged(bool value) => _settings.AnimationsEnabled = value;

    partial void OnSelectedLanguageIndexChanged(int value)
    {
        _settings.Language = value switch
        {
            1 => "en",
            _ => "ru"
        };
    }
    partial void OnMinimizeToTrayChanged(bool value) => _settings.MinimizeToTray = value;
    partial void OnStartInTrayChanged(bool value) => _settings.StartInTray = value;
    partial void OnShowTrayIconChanged(bool value) => _settings.ShowTrayIcon = value;
    partial void OnSoundEffectsChanged(bool value) => _settings.SoundEffects = value;

    partial void OnSelectedLogLevelIndexChanged(int value)
    {
        _settings.LogLevel = value switch
        {
            0 => "Debug",
            2 => "Error",
            3 => "None",
            _ => "Information"
        };
    }

    partial void OnAutostartChanged(bool value)
    {
        _settings.Autostart = value;
        WriteAutostartToRegistry(value);
    }

    partial void OnStartMinimizedChanged(bool value)
    {
        _settings.StartMinimized = value;
        // Update registry entry if autostart is enabled
        if (Autostart)
            WriteAutostartToRegistry(true);
    }

    partial void OnAutoUpdateChanged(bool value) => _settings.AutoUpdate = value;
    partial void OnCheckUpdatesOnStartChanged(bool value) => _settings.CheckUpdatesOnStart = value;

    // ── Commands ────────────────────────────────────────────

    [RelayCommand]
    private async Task CheckUpdatesAsync(CancellationToken ct)
    {
        if (IsCheckingUpdates) return;

        IsCheckingUpdates = true;
        UpdateStatusText = LocalizationService.Get("CheckingUpdates");

        try
        {
            var result = await _updateService.CheckForUpdatesAsync().ConfigureAwait(false);

            await RunOnUIThreadAsync(() =>
            {
        UpdateStatusText = result.Success
            ? LocalizationService.Get("UpdatesAvailable")
            : LocalizationService.Get("UpdateCheckError", result.Error ?? LocalizationService.Get("UnknownError"));
            });
        }
    catch (InvalidOperationException ex)
    {
        await RunOnUIThreadAsync(() => UpdateStatusText = LocalizationService.Get("UpdateCheckError", ex.Message));
    }
    catch (IOException ex)
    {
        await RunOnUIThreadAsync(() => UpdateStatusText = LocalizationService.Get("UpdateCheckError", ex.Message));
    }
    catch (TimeoutException ex)
    {
        await RunOnUIThreadAsync(() => UpdateStatusText = LocalizationService.Get("UpdateCheckError", ex.Message));
    }
    finally
        {
            await RunOnUIThreadAsync(() => IsCheckingUpdates = false);
        }
    }

    [RelayCommand]
    private async Task DownloadUpdateAsync(CancellationToken ct)
    {
        if (IsDownloadingUpdate) return;

        IsDownloadingUpdate = true;
        UpdateStatusText = LocalizationService.Get("DownloadingUpdate");

        try
        {
            var result = await _updateService.UpdateAsync().ConfigureAwait(false);

            await RunOnUIThreadAsync(() =>
            {
        UpdateStatusText = result.Success
            ? LocalizationService.Get("UpdatesDownloaded", result.NewVersion ?? "")
            : LocalizationService.Get("UpdateError", result.Error ?? LocalizationService.Get("UnknownError"));
            });
        }
    catch (InvalidOperationException ex)
    {
        await RunOnUIThreadAsync(() => UpdateStatusText = LocalizationService.Get("UpdateError", ex.Message));
    }
    catch (IOException ex)
    {
        await RunOnUIThreadAsync(() => UpdateStatusText = LocalizationService.Get("UpdateError", ex.Message));
    }
    catch (TimeoutException ex)
    {
        await RunOnUIThreadAsync(() => UpdateStatusText = LocalizationService.Get("UpdateError", ex.Message));
    }
    finally
        {
            await RunOnUIThreadAsync(() => IsDownloadingUpdate = false);
        }
    }

    [RelayCommand]
    private async Task ExportSettingsAsync(CancellationToken ct)
    {
        try
        {
            var picker = new Windows.Storage.Pickers.FileSavePicker();
            picker.FileTypeChoices.Add("JSON Settings", new[] { ".json" });
            picker.SuggestedFileName = "zui-settings-backup";

            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(MainWindow.Instance);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

            var file = await picker.PickSaveFileAsync().AsTask(ct);
            if (file != null)
            {
                _settings.Save();
                var json = System.IO.File.ReadAllText(
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                        "Z-UI", "config", "settings.json"));
                await Windows.Storage.FileIO.WriteTextAsync(file, json);
            }
        }
    catch (OperationCanceledException) { }
    catch (IOException ex)
    {
        System.Diagnostics.Debug.WriteLine($"Export failed: {ex.Message}");
    }
    catch (UnauthorizedAccessException ex)
    {
        System.Diagnostics.Debug.WriteLine($"Export failed: {ex.Message}");
    }
    catch (System.Text.Json.JsonException ex)
    {
        System.Diagnostics.Debug.WriteLine($"Export failed: {ex.Message}");
    }
    }

    [RelayCommand]
    private async Task ImportSettingsAsync(CancellationToken ct)
    {
        try
        {
            var picker = new Windows.Storage.Pickers.FileOpenPicker();
            picker.FileTypeFilter.Add(".json");

            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(MainWindow.Instance);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

            var file = await picker.PickSingleFileAsync().AsTask(ct);
            if (file != null)
            {
                var json = await Windows.Storage.FileIO.ReadTextAsync(file);
                var imported = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object>>(json);
                if (imported != null)
                {
                    foreach (var kvp in imported)
                        _settings.SetSetting(kvp.Key, kvp.Value);

                    LoadFromSettings();
                    LoadStrategies();
                }
            }
        }
    catch (OperationCanceledException) { }
    catch (IOException ex)
    {
        System.Diagnostics.Debug.WriteLine($"Import failed: {ex.Message}");
    }
    catch (UnauthorizedAccessException ex)
    {
        System.Diagnostics.Debug.WriteLine($"Import failed: {ex.Message}");
    }
    catch (System.Text.Json.JsonException ex)
    {
        System.Diagnostics.Debug.WriteLine($"Import failed: {ex.Message}");
    }
    }

    [RelayCommand]
    private async Task ResetSettingsAsync(CancellationToken ct)
    {
    var confirmed = DialogRequested != null
        ? await DialogRequested.Invoke(
            LocalizationService.Get("ResetTitle"),
            LocalizationService.Get("ResetMessage"),
            LocalizationService.Get("ResetConfirm"),
            LocalizationService.Get("Cancel"))
        : false;

        if (!confirmed) return;

        var defaults = new Dictionary<string, object>
        {
            ["AutoProtect"] = false,
            ["DefaultStrategy"] = "auto",
            ["RunAsAdmin"] = false,
            ["DefaultDnsMode"] = "Proxy",
            ["DnsPort"] = 5353,
            ["NotificationsEnabled"] = true,
            ["NotifyOnStart"] = true,
            ["NotifyOnStop"] = false,
            ["NotifyOnErrors"] = true,
            ["AppTheme"] = "Default",
            ["AnimationsEnabled"] = true,
            ["Language"] = "ru",
            ["MinimizeToTray"] = true,
            ["StartInTray"] = false,
            ["ShowTrayIcon"] = true,
            ["SoundEffects"] = true,
            ["LogLevel"] = "Information",
            ["Autostart"] = false,
            ["StartMinimized"] = false,
            ["AutoUpdate"] = true,
            ["CheckUpdatesOnStart"] = false
        };

        foreach (var kvp in defaults)
            _settings.SetSetting(kvp.Key, kvp.Value);

        // Remove autostart from registry
        WriteAutostartToRegistry(false);

        LoadFromSettings();
        LoadStrategies();
    }

    [RelayCommand]
    private async Task OpenLogsFolderAsync(CancellationToken ct)
    {
        try
        {
            var logsPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Z-UI", "logs");

            if (!Directory.Exists(logsPath))
                Directory.CreateDirectory(logsPath);

            await Windows.System.Launcher.LaunchFolderPathAsync(logsPath);
        }
    catch (IOException ex)
    {
        System.Diagnostics.Debug.WriteLine($"Failed to open logs folder: {ex.Message}");
    }
    catch (UnauthorizedAccessException ex)
    {
        System.Diagnostics.Debug.WriteLine($"Failed to open logs folder: {ex.Message}");
    }
    }

    // ── External setting change handler ─────────────────────

    private void OnExternalSettingChanged(string key, object value)
    {
        RunOnUIThreadAsync(() =>
        {
            switch (key)
            {
                case "AutoProtect": AutoProtect = _settings.AutoProtect; break;
                case "DefaultStrategy": LoadStrategies(); break;
                case "RunAsAdmin": RunAsAdmin = _settings.RunAsAdmin; break;
            case "AppTheme":
                SelectedThemeIndex = _settings.AppTheme switch
                {
                    "Light" => 0,
                    "Dark" => 1,
                    _ => 2
                };
                break;
            case "Language":
                SelectedLanguageIndex = _settings.Language switch
                {
                    "en" => 1,
                    _ => 0
                };
                break;
            }
        });
    }

    // ── UI Thread Helper ────────────────────────────────────

    private Task RunOnUIThreadAsync(Action action)
    {
        if (_dispatcherQueue == null || _dispatcherQueue.HasThreadAccess)
        {
            action();
            return Task.CompletedTask;
        }

        var tcs = new TaskCompletionSource<bool>();
        _dispatcherQueue.TryEnqueue(() =>
        {
        try
        {
            action();
            tcs.SetResult(true);
        }
catch (ObjectDisposedException ex)
            {
                tcs.SetException(ex);
            }
            catch (InvalidOperationException ex)
            {
                tcs.SetException(ex);
            }
        });
        return tcs.Task;
    }
}
