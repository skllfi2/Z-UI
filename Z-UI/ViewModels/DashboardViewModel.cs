using System;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.UI;
using ZUI.Services;
using Microsoft.UI.Dispatching;

namespace ZUI.ViewModels;

public partial class DashboardViewModel : ViewModelBase, IDisposable
{
	private readonly WinwsService _winwsService;
	private readonly UpdateService _updateService;
	private DispatcherTimer? _statusTimer;
	private bool _disposed;
	private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(30) };
	private const string VersionUrl = "https://raw.githubusercontent.com/Flowseal/zapret-discord-youtube/main/.service/version.txt";

 [ObservableProperty]
 private bool _isServiceRunning;

    [ObservableProperty]
    private string _statusText = "";

 [ObservableProperty]
 private string _currentStrategy = AppSettings.CurrentStrategy;

 [ObservableProperty]
 private string _zapretVersion = ZapretPaths.LocalVersion;

 [ObservableProperty]
 private bool _isAdmin;

 [ObservableProperty]
 private bool _setupRequired;

 [ObservableProperty]
 private bool _updateAvailable;

 [ObservableProperty]
 private string _updateVersion = "";

 [ObservableProperty]
 private string _strategyDescription = "Стандартная протекция для обхода популярных сетей";

 [ObservableProperty]
 private int _gameFilterIndex;

 [ObservableProperty]
 private int _ipsetFilterIndex;

 [ObservableProperty]
 private string _ipsetStatusText = "Любой IP адрес (any)";

 [ObservableProperty]
 private string _serviceStatus = "Защита выключена";

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
	private string _versionStatus = "Нажмите для проверки";

	[ObservableProperty]
	private bool _isCheckingVersion;

	public event Action? NavigateToSetup;
 public event Action? NavigateToStrategies;
 public event Action? NavigateToUpdates;
 public event Action? NavigateToSettings;

public DashboardViewModel(WinwsService winwsService, UpdateService updateService)
{
_winwsService = winwsService ?? throw new ArgumentNullException(nameof(winwsService));
_updateService = updateService ?? throw new ArgumentNullException(nameof(updateService));

_winwsService.StatusChanged += OnStatusChanged;
IsServiceRunning = _winwsService.IsRunning;
CheckAdmin();
CheckSetupRequired();
LoadFilters();
UpdateStatus();

LocalizationService.LanguageChanged += () => RunOnUIThread(UpdateStatus);
AppSettings.StrategyChanged += () => RunOnUIThread(() =>
{
CurrentStrategy = AppSettings.CurrentStrategy;
UpdateStatus();
});

_updateService.DownloadProgress += progress =>
{
RunOnUIThread(() => UpdateProgress = progress);
};

 _updateService.StatusChanged += status =>
 {
 RunOnUIThread(() => UpdateStatusText = status);
 };

 _updateService.UpdateCompleted += () =>
 {
 RunOnUIThread(() =>
 {
 IsUpdating = false;
 ZapretVersion = ZapretPaths.LocalVersion;
 UpdateAvailable = false;
 });
 };

 _updateService.UpdateFailed += error =>
 {
 RunOnUIThread(() => IsUpdating = false);
 };

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

 public new void SetDispatcherQueue(DispatcherQueue queue)
 {
 base.SetDispatcherQueue(queue);
 _statusTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
 _statusTimer.Tick += StatusTimer_Tick;
 _statusTimer.Start();
 }

 private void StatusTimer_Tick(object? sender, object e)
 {
 if (_disposed) return;
 RefreshServiceStatus();
 }

 private void RefreshServiceStatus()
 {
 var wasRunning = IsServiceRunning;
 IsServiceRunning = _winwsService.IsRunning;
 if (wasRunning != IsServiceRunning)
 {
 UpdateStatus();
 }
 }

 private void OnStatusChanged(bool running)
 {
 IsServiceRunning = running;
 UpdateStatus();
 }

    private void UpdateStatus()
    {
        StatusText = IsServiceRunning ? LocalizationService.Get("Running") : LocalizationService.Get("Stopped");
        CurrentStrategy = AppSettings.CurrentStrategy;
        ServiceStatus = IsServiceRunning ? LocalizationService.Get("DpiBypassActive") : LocalizationService.Get("ProtectionOff");
        StrategyDescription = GetStrategyDescription(CurrentStrategy);
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
 ? $"Загружен список: {File.ReadAllLines(ipsetFile).Length} записей"
 : "Загружен список IP",
 "none" => "Фильтрация по IP отключена",
 _ => "Любой IP адрес (any)"
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

 [RelayCommand]
 private async Task ToggleServiceAsync()
 {
 if (!File.Exists(ZapretPaths.WinwsExe))
 {
 NavigateToSetup?.Invoke();
 return;
 }

 if (IsServiceRunning)
 {
 _winwsService.Stop();
 ActionLogger.LogStop();
 }
 else
 {
 try
 {
 var strategyName = CurrentStrategy;
 var batFile = Path.Combine(ZapretPaths.StrategiesDir, strategyName + ".bat");

 if (File.Exists(batFile))
 {
 var arguments = BatStrategyParser.ParseStrategy(batFile);
 if (arguments != null)
 {
 await _winwsService.StartAsync(arguments);
 ActionLogger.LogStart(strategyName);
 return;
 }
 }

 var listsP = ZapretPaths.ListsDir + "\\";
 var binP = ZapretPaths.WinwsDir + "\\";
 var args =
 "--wf-tcp=80,443,2053,2083,2087,2096,8443 --wf-udp=443,19294-19344,50000-50100 " +
 "--filter-udp=443 --hostlist=\"" + listsP + "list-general.txt\" --hostlist-exclude=\"" + listsP + "list-exclude.txt\" --ipset-exclude=\"" + listsP + "ipset-exclude.txt\" --dpi-desync=fake --dpi-desync-repeats=6 --dpi-desync-fake-quic=\"" + binP + "quic_initial_www_google_com.bin\" --new " +
 "--filter-udp=19294-19344,50000-50100 --filter-l7=discord,stun --dpi-desync=fake --dpi-desync-repeats=6 --new " +
 "--filter-tcp=80,443 --hostlist=\"" + listsP + "list-general.txt\" --hostlist-exclude=\"" + listsP + "list-exclude.txt\" --dpi-desync=multisplit --dpi-desync-split-seqovl=568 --dpi-desync-split-pos=1 --dpi-desync-split-seqovl-pattern=\"" + binP + "tls_clienthello_4pda_to.bin\"";

 await _winwsService.StartAsync(args);
 ActionLogger.LogStart("General");
 }
 catch (System.ComponentModel.Win32Exception)
 {
 StatusText = "Требуются права администратора";
 IsServiceRunning = false;
 ActionLogger.LogError("ToggleService", "Требуются права администратора");
 }
 catch (Exception ex)
 {
 StatusText = $"Ошибка: {ex.Message}";
 IsServiceRunning = false;
 ActionLogger.LogError("ToggleService", ex.Message);
 }
 }
 }

 [RelayCommand]
 private void OpenWizard() => NavigateToSetup?.Invoke();

 [RelayCommand]
 private void ChangeStrategy() => NavigateToStrategies?.Invoke();

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
				VersionStatus = "Ошибка проверки";
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
 UpdateStatusText = "Подготовка к обновлению...";
 Changelog = await _updateService.FetchChangelogAsync(ZapretPaths.LocalVersion, UpdateVersion) ?? "Список изменений недоступен";
 ChangelogVisible = true;

 _ = Task.Run(async () =>
 {
 await _updateService.DownloadAndInstallAsync();
 });
 }

 [RelayCommand]
 private void CancelUpdate()
 {
 // Update cancellation not implemented in UpdateService yet
 }

 private void StartStatusTimer()
 {
 }

 public void Dispose()
 {
 if (_disposed) return;
 _disposed = true;
 _winwsService.StatusChanged -= OnStatusChanged;
 _statusTimer?.Stop();
 }
}
