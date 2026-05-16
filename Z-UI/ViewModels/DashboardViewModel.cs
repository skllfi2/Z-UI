using System.Net.Http;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Dispatching;
using ZUI;
using ZUI.Services;

namespace ZUI.ViewModels;

public partial class DashboardViewModel : ViewModelBase, IDisposable
{
    private readonly IAdaptiveEngine _adaptiveEngine;
    private readonly IStrategyManager _strategyManager;
    private readonly IDashboardStatusService _statusService;
    private readonly MalwLinkUpdateService _malwLinkUpdateService;
    private readonly IWorkerServiceManager _workerServiceManager;
    private readonly IIpcClientService _ipcClientService;

    private DispatcherQueueTimer? _statusTimer;
    private bool _disposed;
    private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(30) };
    private const string VersionUrl = "https://raw.githubusercontent.com/Flowseal/zapret-discord-youtube/main/.service/version.txt";

    // ── Core state ───────────────────────────────────────────

    [ObservableProperty]
    private bool _isServiceRunning;

    [ObservableProperty]
    private string _statusText = "";

    [ObservableProperty]
    private string _currentStrategy = AppSettings.CurrentStrategy;

    [ObservableProperty]
    private string _zapretVersion = ZapretPaths.LocalVersion;

    [ObservableProperty]
    private string _appVersion = "1.0.0";

    [ObservableProperty]
    private bool _isAdmin;

    [ObservableProperty]
    private bool _setupRequired;

    [ObservableProperty]
    private bool _updateAvailable;

    [ObservableProperty]
    private string _updateVersion = "";

    [ObservableProperty]
    private string _strategyDescription = LocalizationService.Get("StrategyDescGeneral");

    [ObservableProperty]
    private int _gameFilterIndex;

    [ObservableProperty]
    private int _ipsetFilterIndex;

    [ObservableProperty]
    private string _ipsetStatusText = LocalizationService.Get("IpsetAny");

    [ObservableProperty]
    private string _serviceStatus = LocalizationService.Get("ProtectionOff");

    [ObservableProperty]
    private bool _isUpdating;

    [ObservableProperty]
    private int _updateProgress;

    [ObservableProperty]
    private string _updateStatusText = "";

    [ObservableProperty]
    private string _changelog = "";

    [ObservableProperty]
    private bool _changelogVisible;

    [ObservableProperty]
    private string _versionStatus = LocalizationService.Get("PressToCheck");

    [ObservableProperty]
    private bool _isCheckingVersion;

    // ── Dashboard {Binding} properties ───────────────────────

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private bool _isToggling;

    [ObservableProperty]
    private string _currentStrategyName = AppSettings.CurrentStrategy;

    [ObservableProperty]
    private string _toggleButtonText = LocalizationService.Get("Start");

    [ObservableProperty]
    private string _workerNotConnectedText = LocalizationService.Get("WorkerNotConnected");

    [ObservableProperty]
    private bool _isWorkerConnected = true;

    [ObservableProperty]
    private string _installWorkerButtonText = LocalizationService.Get("InstallAndStart");

    [ObservableProperty]
    private string _currentMethod = "";

    [ObservableProperty]
    private int _availableStrategiesCount;

    [ObservableProperty]
    private bool _isSecureDnsEnabled;

    [ObservableProperty]
    private bool _isProxifierRunning;

    [ObservableProperty]
    private bool _isTgProxyRunning;

    [ObservableProperty]
    private string _splitDnsStatus = LocalizationService.Get("Disabled");

    [ObservableProperty]
    private string _dnsPrimaryServer = "—";

    [ObservableProperty]
    private string _ispName = LocalizationService.Get("Detecting");

    [ObservableProperty]
    private int _passedChecks;

    [ObservableProperty]
    private int _totalChecks;

    [ObservableProperty]
    private bool _hasCriticalIssues;

    [ObservableProperty]
    private string _settingsInfoText = "";

    // ── Adaptive engine properties ──────────────────────────

    [ObservableProperty]
    private string _adaptiveStrategyName = "";

    [ObservableProperty]
    private string _dnsBypassStatusText = LocalizationService.Get("Disabled");

    [ObservableProperty]
    private bool _isDnsBypassActive;

    // ── Worker service properties ──────────────────────────

    [ObservableProperty]
    private bool _isWorkerInstalled;

    [ObservableProperty]
    private bool _isWorkerRunning;

    [ObservableProperty]
    private string _workerStatusText = "";

    [ObservableProperty]
    private bool _isWorkerInstalling;

    /// <summary>Alias — XAML binds IsProtected, VM tracks IsServiceRunning.</summary>
    public bool IsProtected => IsServiceRunning;

    public event Action? NavigateToSetup;
    public event Action? NavigateToUpdates;
    public event Action? NavigateToSettings;


    public DashboardViewModel(
        IAdaptiveEngine adaptiveEngine,
        IStrategyManager strategyManager,
        IDashboardStatusService statusService,
        MalwLinkUpdateService malwLinkUpdateService,
        IWorkerServiceManager workerServiceManager,
        IIpcClientService ipcClientService)
    {
        _adaptiveEngine = adaptiveEngine ?? throw new ArgumentNullException(nameof(adaptiveEngine));
        _strategyManager = strategyManager ?? throw new ArgumentNullException(nameof(strategyManager));
        _statusService = statusService ?? throw new ArgumentNullException(nameof(statusService));
        _malwLinkUpdateService = malwLinkUpdateService ?? throw new ArgumentNullException(nameof(malwLinkUpdateService));
        _workerServiceManager = workerServiceManager ?? throw new ArgumentNullException(nameof(workerServiceManager));
        _ipcClientService = ipcClientService ?? throw new ArgumentNullException(nameof(ipcClientService));

        _workerServiceManager.StatusChanged += status => RunOnUIThread(UpdateWorkerStatus);

        IsServiceRunning = _adaptiveEngine.IsProtected;
        CheckAdmin();
        CheckSetupRequired();
        LoadFilters();
        RefreshDashboardState();
        UpdateStatus();

        LocalizationService.LanguageChanged += () => RunOnUIThread(UpdateStatus);
        AppSettings.StrategyChanged += () => RunOnUIThread(() =>
        {
            CurrentStrategy = AppSettings.CurrentStrategy;
            CurrentStrategyName = AppSettings.CurrentStrategy;
            UpdateStatus();
        });

        UpdateChecker.UpdateFound += version =>
        {
            UpdateVersion = version;
            UpdateAvailable = true;

            if (AppSettings.AutoUpdateDownload && !IsUpdating)
            {
                _ = StartUpdateAsync();
            }
        };

        if (UpdateChecker.UpdateAvailable)
        {
            UpdateVersion = UpdateChecker.LatestVersion ?? "";
            UpdateAvailable = true;
        }

        StartStatusTimer();
    }

    /// <summary>
    /// Initialize the ViewModel — called from DashboardPage.OnNavigatedTo.
    /// Refreshes protection status and strategy list.
    /// </summary>
    public async Task InitializeAsync()
    {
        RunOnUIThread(() => IsLoading = true);
        try
        {
            await _adaptiveEngine.RefreshStatusAsync();
            RunOnUIThread(() =>
            {
                IsServiceRunning = _adaptiveEngine.IsProtected;
                RefreshDashboardState();
                UpdateStatus();
            });

            // Fire-and-forget: refresh aggregated status, then update VM properties on UI thread
            _ = RefreshStatusServiceAsync();

            // Refresh Worker service status
            await _workerServiceManager.RefreshStatusAsync();
            RunOnUIThread(UpdateWorkerStatus);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Z-UI] DashboardViewModel.InitializeAsync failed: {ex.Message}");
        }
        finally
        {
            RunOnUIThread(() => IsLoading = false);
        }
    }

    /// <summary>
    /// Fire-and-forget: refreshes IDashboardStatusService and updates VM properties on UI thread.
    /// Replaces ContinueWith — all exceptions are caught and logged.
    /// </summary>
    private async Task RefreshStatusServiceAsync()
    {
        try
        {
            await _statusService.RefreshAsync();
            RunOnUIThread(() =>
            {
                try
                {
                    IspName = _statusService.IspName;
                    PassedChecks = _statusService.PassedChecks;
                    TotalChecks = _statusService.TotalChecks;
                    HasCriticalIssues = _statusService.HasCriticalIssues;
                    RefreshDashboardState();
                }
                catch (System.Runtime.InteropServices.COMException ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[Z-UI] RefreshStatusServiceAsync UI callback COM error: {ex.Message}");
                }
                catch (InvalidOperationException ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[Z-UI] RefreshStatusServiceAsync UI callback error: {ex.Message}");
                }
            });
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Z-UI] DashboardViewModel.RefreshStatusServiceAsync failed: {ex.Message}");
        }
    }

    public new void SetDispatcherQueue(DispatcherQueue queue)
    {
        base.SetDispatcherQueue(queue);
        _statusTimer = queue.CreateTimer();
        _statusTimer.Interval = TimeSpan.FromSeconds(5);
        _statusTimer.IsRepeating = true;
        _statusTimer.Tick += StatusTimer_Tick;
        _statusTimer.Start();
    }

    private void StatusTimer_Tick(DispatcherQueueTimer sender, object e)
    {
        if (_disposed) return;
        RefreshServiceStatus();
        _ = RefreshWorkerStatusAsync();
    }

    private void RefreshServiceStatus()
    {
        var wasRunning = IsServiceRunning;
        IsServiceRunning = _adaptiveEngine.IsProtected;

        if (wasRunning != IsServiceRunning)
        {
            RunOnUIThread(() =>
            {
                RefreshDashboardState();
                UpdateStatus();
            });
        }
    }

    /// <summary>
    /// Periodically refreshes Worker service status from SCM.
    /// If status changed, updates UI properties on the UI thread.
    /// </summary>
    private async Task RefreshWorkerStatusAsync()
    {
        try
        {
            var previousStatus = _workerServiceManager.Status;
            await _workerServiceManager.RefreshStatusAsync();

            if (_workerServiceManager.Status != previousStatus)
                RunOnUIThread(UpdateWorkerStatus);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Z-UI] Worker status refresh failed: {ex.Message}");
        }
    }

    // ── State refresh ────────────────────────────────────────

    private void RefreshDashboardState()
    {
        IsWorkerConnected = _adaptiveEngine.IsWorkerConnected;
        WorkerNotConnectedText = !IsWorkerConnected ? LocalizationService.Get("WorkerNotConnected") : "";
        IsSecureDnsEnabled = _statusService.IsSecureDnsEnabled;
        IsProxifierRunning = _statusService.IsProxifierRunning;
        IsTgProxyRunning = _statusService.IsTgProxyRunning;
        SplitDnsStatus = _statusService.SplitDnsStatus;
        ToggleButtonText = IsServiceRunning ? LocalizationService.Get("Stop") : LocalizationService.Get("Start");

        // ── Adaptive engine state ──
        AdaptiveStrategyName = _adaptiveEngine.CurrentStrategy switch
        {
            AdaptiveStrategyType.AdaptiveAuto => LocalizationService.Get("AdaptiveAuto"),
            AdaptiveStrategyType.DpiBypassWorker => LocalizationService.Get("DpiBypassWorker"),
            AdaptiveStrategyType.DnsBypass => LocalizationService.Get("DnsBypass"),
            _ => ""
        };

        DnsBypassStatusText = _adaptiveEngine.DnsBypassState switch
        {
            DnsBypassState.Active => LocalizationService.Get("Active"),
            DnsBypassState.Checking => LocalizationService.Get("Checking") + "...",
            DnsBypassState.Failed => LocalizationService.Get("Failed"),
            _ => LocalizationService.Get("Disabled")
        };

        IsDnsBypassActive = _adaptiveEngine.DnsBypassState == DnsBypassState.Active;

        DnsPrimaryServer = _statusService.DnsPrimaryServer;

        try
        {
            AvailableStrategiesCount = _strategyManager.GetAvailableStrategies().Count;
            CurrentMethod = _strategyManager.GetCurrentMethod();
        }
        catch
        {
            AvailableStrategiesCount = 0;
            CurrentMethod = "—";
        }

        UpdateSettingsInfo();
        OnPropertyChanged(nameof(IsProtected));
    }

    private void UpdateStatus()
    {
        StatusText = IsServiceRunning ? LocalizationService.Get("Running") : LocalizationService.Get("Stopped");
        CurrentStrategy = AppSettings.CurrentStrategy;
        CurrentStrategyName = AppSettings.CurrentStrategy;
        ServiceStatus = IsServiceRunning ? LocalizationService.Get("DpiBypassActive") : LocalizationService.Get("ProtectionOff");
        StrategyDescription = GetStrategyDescription(CurrentStrategy);
        ToggleButtonText = IsServiceRunning ? LocalizationService.Get("Stop") : LocalizationService.Get("Start");
        OnPropertyChanged(nameof(IsProtected));
    }

    private void UpdateSettingsInfo()
    {
        var parts = new List<string>();
        if (AppSettings.AutoStartZapret) parts.Add(LocalizationService.Get("AutoStart"));
        if (AppSettings.AutoUpdateCheck) parts.Add(LocalizationService.Get("AutoUpdates"));
        SettingsInfoText = parts.Count > 0 ? string.Join(" · ", parts) : LocalizationService.Get("Default");
    }


    private static string GetStrategyDescription(string strategy) => strategy switch
    {
        "General" => LocalizationService.Get("StrategyDescGeneral"),
        "Discord" => LocalizationService.Get("StrategyDescDiscord"),
        "YouTube" => LocalizationService.Get("StrategyDescYouTube"),
        "Russia" => LocalizationService.Get("StrategyDescRussia"),
        "Gaming" => LocalizationService.Get("StrategyDescGaming"),
        _ => LocalizationService.Get("StrategyDescCustom")
    };

    private void CheckAdmin()
    {
        IsAdmin = new System.Security.Principal.WindowsPrincipal(
            System.Security.Principal.WindowsIdentity.GetCurrent())
            .IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
    }

    private void CheckSetupRequired()
    {
        SetupRequired = !File.Exists(ZapretPaths.WinwsExe);
    }

    private bool _filtersLoading = true;

    private void LoadFilters()
    {
        _filtersLoading = true;

        GameFilterIndex = AppSettings.GameFilter switch
        {
            "all" => 1,
            "tcp" => 2,
            "udp" => 3,
            _ => 0
        };

        var actualIpset = BatStrategyParser.GetCurrentIpsetMode();
        IpsetFilterIndex = actualIpset switch
        {
            "loaded" => 1,
            "none" => 2,
            _ => 0
        };
        AppSettings.IpsetFilter = actualIpset;

        var ipsetFile = Path.Combine(ZapretPaths.ListsDir, "ipset-all.txt");
        IpsetStatusText = actualIpset switch
        {
            "loaded" => File.Exists(ipsetFile)
            ? LocalizationService.Get("IpsetLoadedCount", File.ReadAllLines(ipsetFile).Length)
            : LocalizationService.Get("IpsetLoaded"),
            "none" => LocalizationService.Get("IpsetNone"),
            _ => LocalizationService.Get("IpsetAny")
        };

        _filtersLoading = false;
    }

    partial void OnGameFilterIndexChanged(int value)
    {
        if (_filtersLoading) return;
        var tag = value switch
        {
            1 => "all",
            2 => "tcp",
            3 => "udp",
            _ => "disabled"
        };
        AppSettings.GameFilter = tag;
        AppSettings.Save();
    }

    partial void OnIpsetFilterIndexChanged(int value)
    {
        if (_filtersLoading) return;
        var tag = value switch
        {
            1 => "loaded",
            2 => "none",
            _ => "any"
        };
        try { BatStrategyParser.ApplyIpsetFilter(tag); } catch { }
        AppSettings.IpsetFilter = tag;
        AppSettings.Save();
        LoadFilters();
    }

    // ── Commands ─────────────────────────────────────────────

    /// <summary>Main protection toggle — bound as ToggleProtectionCommand in XAML.</summary>
    [RelayCommand]
    private async Task ToggleProtectionAsync()
    {
        if (IsToggling) return;
        RunOnUIThread(() => IsToggling = true);

        var errorOccurred = false;
        try
        {
            if (!File.Exists(ZapretPaths.WinwsExe))
            {
                NavigateToSetup?.Invoke();
                return;
            }

            if (IsServiceRunning)
            {
                await _adaptiveEngine.StopAsync();
                ActionLogger.LogStop();
            }
            else
            {
                var strategyName = AppSettings.CurrentStrategy;
                ProtectionResult result;

                if (string.IsNullOrEmpty(strategyName) || strategyName == "auto")
                {
                    // Zero-config: let adaptive engine choose the best method
                    result = await _adaptiveEngine.StartAdaptiveAsync();
                }
                else
                {
                    var gameFilter = AppSettings.GameFilter switch
                    {
                        "all" => 1,
                        "tcp" => 2,
                        "udp" => 3,
                        _ => 0
                    };
                    result = await _adaptiveEngine.StartWithStrategyAsync(strategyName, gameFilter);
                }

                if (result.Success)
                {
                    RunOnUIThread(() =>
                    {
                        IsServiceRunning = true;
                        ActionLogger.LogStart(strategyName);
                    });
                }
                else
                {
                    RunOnUIThread(() =>
                    {
                        StatusText = $"{LocalizationService.Get("Error")}: {result.Message}";
                        ActionLogger.LogError("ToggleProtection", result.Message ?? "Unknown error");
                    });
                    errorOccurred = true;
                }
            }
        }
        catch (System.ComponentModel.Win32Exception)
        {
            RunOnUIThread(() =>
            {
                StatusText = LocalizationService.Get("AdminRequired");
                IsServiceRunning = false;
            });
            errorOccurred = true;
            ActionLogger.LogError("ToggleProtection", "AdminRequired");
        }
        catch (Exception ex)
        {
            RunOnUIThread(() =>
            {
                StatusText = $"{LocalizationService.Get("Error")}: {ex.Message}";
                IsServiceRunning = false;
            });
            errorOccurred = true;
            ActionLogger.LogError("ToggleProtection", ex.Message);
        }
        finally
        {
            RunOnUIThread(() =>
            {
                RefreshDashboardState();
                if (!errorOccurred) UpdateStatus();
                IsToggling = false;
            });
        }
    }

    /// <summary>Legacy alias — old code-behind may reference ToggleServiceCommand.</summary>
    [RelayCommand]
    private async Task ToggleServiceAsync() => await ToggleProtectionAsync();

    [RelayCommand]
    private void OpenWizard() => NavigateToSetup?.Invoke();

    [RelayCommand]
    private void OpenUpdates() => NavigateToUpdates?.Invoke();

    [RelayCommand]
    private void OpenSettings() => NavigateToSettings?.Invoke();

	[RelayCommand]
    private async Task CheckVersionAsync()
    {
        if (IsCheckingVersion) return;
        IsCheckingVersion = true;

        try
        {
            var localVersion = ZapretPaths.LocalVersion;
            var remoteVersion = await _http.GetStringAsync(VersionUrl);

            RunOnUIThread(() =>
            {
                VersionStatus = $"{localVersion} → {remoteVersion.Trim()}";
            });
        }
        catch (Exception)
        {
            RunOnUIThread(() =>
            {
                VersionStatus = LocalizationService.Get("VersionCheckError");
            });
        }
        finally
        {
            RunOnUIThread(() => IsCheckingVersion = false);
        }
    }

    [RelayCommand]
    private async Task StartUpdateAsync()
    {
        if (IsUpdating) return;

        IsUpdating = true;
        UpdateProgress = 0;
    UpdateStatusText = LocalizationService.Get("PreparingUpdate");
    Changelog = LocalizationService.Get("ChangelogLoading");
        ChangelogVisible = true;

        await UpdateDomainListsCommand.ExecuteAsync(null);
    }

    [RelayCommand]
    private void CancelUpdate()
    {
        // Update cancellation not implemented yet
    }

    /// <summary>Check for domain list updates from dns.malw.link and apply if available.</summary>
    [RelayCommand]
    private async Task UpdateDomainListsAsync(CancellationToken cancellationToken = default)
    {
        UpdateStatusText = LocalizationService.Get("PreparingUpdate");

        try
        {
            var checkResult = await _malwLinkUpdateService.CheckForUpdatesAsync().ConfigureAwait(false);
            if (!checkResult.Success)
            {
                RunOnUIThread(() =>
                {
                    UpdateStatusText = checkResult.Error ?? LocalizationService.Get("UpdateUnavailable");
                    IsUpdating = false;
                });
                return;
            }

            var currentVersion = await _malwLinkUpdateService.GetCurrentVersionAsync().ConfigureAwait(false);
            if (checkResult.NewVersion != null && checkResult.NewVersion != currentVersion)
            {
                RunOnUIThread(() =>
                {
                    UpdateProgress = 50;
                    UpdateStatusText = LocalizationService.Get("PreparingUpdate");
                });

                var updateResult = await _malwLinkUpdateService.UpdateAsync().ConfigureAwait(false);
                RunOnUIThread(() =>
                {
                    if (updateResult.Success)
                    {
                        UpdateStatusText = $"{LocalizationService.Get("UpdateComplete")} ({updateResult.NewVersion})";
                        System.Diagnostics.Debug.WriteLine($"[Z-UI] Domain lists updated to {updateResult.NewVersion}");
                    }
                    else
                    {
                        UpdateStatusText = updateResult.Error ?? LocalizationService.Get("UpdateUnavailable");
                        System.Diagnostics.Debug.WriteLine($"[Z-UI] Domain list update failed: {updateResult.Error}");
                    }
                    IsUpdating = false;
                });
            }
            else
            {
                RunOnUIThread(() =>
                {
                    UpdateStatusText = LocalizationService.Get("UpdateUnavailable");
                    IsUpdating = false;
                });
            }
        }
        catch (InvalidOperationException ex)
        {
            RunOnUIThread(() =>
            {
                UpdateStatusText = ex.Message;
                IsUpdating = false;
            });
            System.Diagnostics.Debug.WriteLine($"[Z-UI] Domain list update error: {ex.Message}");
        }
        catch (IOException ex)
        {
            RunOnUIThread(() =>
            {
                UpdateStatusText = ex.Message;
                IsUpdating = false;
            });
            System.Diagnostics.Debug.WriteLine($"[Z-UI] Domain list update IO error: {ex.Message}");
        }
        catch (TimeoutException ex)
        {
            RunOnUIThread(() =>
            {
                UpdateStatusText = ex.Message;
                IsUpdating = false;
            });
            System.Diagnostics.Debug.WriteLine($"[Z-UI] Domain list update timeout: {ex.Message}");
        }
        catch (OperationCanceledException)
        {
            RunOnUIThread(() =>
            {
                UpdateStatusText = LocalizationService.Get("UpdateUnavailable");
                IsUpdating = false;
            });
        }
    }

    private void StartStatusTimer()
    {
    }

    partial void OnIsServiceRunningChanged(bool value)
    {
        OnPropertyChanged(nameof(IsProtected));
        ToggleButtonText = value ? LocalizationService.Get("Stop") : LocalizationService.Get("Start");
    }

    // ── Worker service commands ────────────────────────────

    private void UpdateWorkerStatus()
    {
        IsWorkerInstalled = _workerServiceManager.IsInstalled;
        IsWorkerRunning = _workerServiceManager.Status == WorkerServiceStatus.Running;
        WorkerStatusText = _workerServiceManager.Status switch
        {
            WorkerServiceStatus.NotInstalled => LocalizationService.Get("WorkerNotInstalled"),
            WorkerServiceStatus.Stopped => LocalizationService.Get("WorkerStopped"),
            WorkerServiceStatus.Starting => LocalizationService.Get("WorkerStarting"),
            WorkerServiceStatus.Running => LocalizationService.Get("WorkerRunning"),
            WorkerServiceStatus.Stopping => LocalizationService.Get("WorkerStopping"),
            WorkerServiceStatus.Error => LocalizationService.Get("WorkerError"),
            _ => ""
        };
    }

    [RelayCommand]
    private async Task InstallWorkerAsync()
    {
        if (IsWorkerInstalling) return;
        IsWorkerInstalling = true;
        try
        {
            var result = await _workerServiceManager.InstallAsync();
            if (result.IsSuccess)
            {
                await _workerServiceManager.RefreshStatusAsync();
                RunOnUIThread(UpdateWorkerStatus);

                // Connect IPC to the newly installed (and auto-started) Worker
                _ = TryConnectIpcAsync();
            }
            else
            {
                RunOnUIThread(() => WorkerStatusText = $"{LocalizationService.Get("Error")}: {result.Error}");
            }
        }
        catch (Exception ex)
        {
            RunOnUIThread(() => WorkerStatusText = $"{LocalizationService.Get("Error")}: {ex.Message}");
        }
        finally
        {
            RunOnUIThread(() => IsWorkerInstalling = false);
        }
    }

    [RelayCommand]
    private async Task StartWorkerAsync()
    {
        try
        {
            var result = await _workerServiceManager.StartAsync();
            if (result.IsSuccess)
            {
                await _workerServiceManager.RefreshStatusAsync();
                RunOnUIThread(UpdateWorkerStatus);

                // Connect IPC to the started Worker
                _ = TryConnectIpcAsync();
            }
            else
            {
                RunOnUIThread(() => WorkerStatusText = $"{LocalizationService.Get("Error")}: {result.Error}");
            }
        }
        catch (Exception ex)
        {
            RunOnUIThread(() => WorkerStatusText = $"{LocalizationService.Get("Error")}: {ex.Message}");
        }
    }

    [RelayCommand]
    private async Task StopWorkerAsync()
    {
        try
        {
            var result = await _workerServiceManager.StopAsync();
            if (result.IsSuccess)
            {
                await _workerServiceManager.RefreshStatusAsync();
                RunOnUIThread(UpdateWorkerStatus);
            }
            else
            {
                RunOnUIThread(() => WorkerStatusText = $"{LocalizationService.Get("Error")}: {result.Error}");
            }
        }
        catch (Exception ex)
        {
            RunOnUIThread(() => WorkerStatusText = $"{LocalizationService.Get("Error")}: {ex.Message}");
        }
    }

    [RelayCommand]
    private async Task UninstallWorkerAsync()
    {
        if (IsWorkerInstalling) return;
        IsWorkerInstalling = true;
        try
        {
            var result = await _workerServiceManager.UninstallAsync();
            if (result.IsSuccess)
            {
                await _workerServiceManager.RefreshStatusAsync();
                RunOnUIThread(UpdateWorkerStatus);
            }
            else
            {
                RunOnUIThread(() => WorkerStatusText = $"{LocalizationService.Get("Error")}: {result.Error}");
            }
        }
        catch (Exception ex)
        {
            RunOnUIThread(() => WorkerStatusText = $"{LocalizationService.Get("Error")}: {ex.Message}");
        }
        finally
        {
            RunOnUIThread(() => IsWorkerInstalling = false);
        }
    }

    [RelayCommand]
    private async Task ReinstallWorkerAsync()
    {
        if (IsWorkerInstalling) return;
        IsWorkerInstalling = true;
        try
        {
            var result = await _workerServiceManager.ReinstallAsync();
            if (result.IsSuccess)
            {
                await _workerServiceManager.RefreshStatusAsync();
                RunOnUIThread(UpdateWorkerStatus);
            }
            else
            {
                RunOnUIThread(() => WorkerStatusText = $"{LocalizationService.Get("Error")}: {result.Error}");
            }
        }
        catch (Exception ex)
        {
            RunOnUIThread(() => WorkerStatusText = $"{LocalizationService.Get("Error")}: {ex.Message}");
        }
        finally
        {
            RunOnUIThread(() => IsWorkerInstalling = false);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _statusTimer?.Stop();
    }

    /// <summary>
    /// Best-effort IPC reconnect to Worker. Fire-and-forget — errors are caught and logged.
    /// Called after Worker install/start when the service becomes available.
    /// </summary>
    private async Task TryConnectIpcAsync()
    {
        try
        {
            if (!_ipcClientService.IsConnected)
                await _ipcClientService.ConnectAsync();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Z-UI] IPC reconnect after Worker operation failed: {ex.Message}");
        }
    }
}
