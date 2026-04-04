using CommunityToolkit.Mvvm.ComponentModel;
using ZUI.Services;

namespace ZUI.ViewModels;

public partial class LocalizationViewModel : ObservableObject
{
    private static LocalizationViewModel? _instance;
    public static LocalizationViewModel Instance => _instance ??= new LocalizationViewModel();

    public LocalizationViewModel()
    {
        LocalizationService.LanguageChanged += () => OnPropertyChanged(string.Empty);
    }

    public string NavDashboard => LocalizationService.Get("NavDashboard");
    public string NavStrategies => LocalizationService.Get("NavStrategies");
    public string NavServices => LocalizationService.Get("NavServices");
    public string NavSettings => LocalizationService.Get("NavSettings");

    public string QuickActions => LocalizationService.Get("QuickActions");
    public string SetupWizard => LocalizationService.Get("SetupWizard");
    public string InstallZapret => LocalizationService.Get("InstallZapret");
    public string AdvancedFilters => LocalizationService.Get("AdvancedFilters");
    public string GameFilter => LocalizationService.Get("GameFilter");
    public string GameFilterDesc => LocalizationService.Get("GameFilterDesc");
    public string IpsetFilter => LocalizationService.Get("IpsetFilter");
    public string Changelog => LocalizationService.Get("Changelog");

    public string Administrator => LocalizationService.Get("Administrator");
    public string On => LocalizationService.Get("On");
    public string Off => LocalizationService.Get("Off");
    public string Running => LocalizationService.Get("Running");
    public string Stopped => LocalizationService.Get("Stopped");
    public string Launch => LocalizationService.Get("Launch");
    public string Stop => LocalizationService.Get("Stop");
    public string Update => LocalizationService.Get("Update");
    public string Check => LocalizationService.Get("Check");
    public string Download => LocalizationService.Get("Download");
    public string Install => LocalizationService.Get("Install");
    public string Cancel => LocalizationService.Get("Cancel");
    public string Next => LocalizationService.Get("Next");
    public string Back => LocalizationService.Get("Back");
    public string Finish => LocalizationService.Get("Finish");
    public string Skip => LocalizationService.Get("Skip");

    public string Disabled => LocalizationService.Get("Disabled");
    public string TcpAndUdp => LocalizationService.Get("TcpAndUdp");
    public string TcpOnly => LocalizationService.Get("TcpOnly");
    public string UdpOnly => LocalizationService.Get("UdpOnly");
    public string AnyIp => LocalizationService.Get("AnyIp");
    public string FromList => LocalizationService.Get("FromList");
    public string None => LocalizationService.Get("None");

    public string ZapretNotInstalled => LocalizationService.Get("ZapretNotInstalled");
    public string ZapretNotInstalledMsg => LocalizationService.Get("ZapretNotInstalledMsg");
    public string LaunchSetupWizard => LocalizationService.Get("LaunchSetupWizard");
    public string UpdateAvailable => LocalizationService.Get("UpdateAvailable");

    public string Language => LocalizationService.Get("Language");
    public string LanguageDesc => LocalizationService.Get("LanguageDesc");
    public string Theme => LocalizationService.Get("Theme");
    public string ThemeDesc => LocalizationService.Get("ThemeDesc");
    public string System => LocalizationService.Get("System");
    public string Light => LocalizationService.Get("Light");
    public string Dark => LocalizationService.Get("Dark");
    public string Russian => LocalizationService.Get("Russian");
    public string English => LocalizationService.Get("English");

    public string SoundEffects => LocalizationService.Get("SoundEffects");
    public string ToastNotifications => LocalizationService.Get("ToastNotifications");
    public string AutoStartWithWindows => LocalizationService.Get("AutoStartWithWindows");
    public string AutoStartWithWindowsDesc => LocalizationService.Get("AutoStartWithWindowsDesc");
    public string AutoStartProtection => LocalizationService.Get("AutoStartProtection");
    public string MinimizeToTray => LocalizationService.Get("MinimizeToTray");
    public string AutoStartAdmin => LocalizationService.Get("AutoStartAdmin");
    public string AutoStartAdminDesc => LocalizationService.Get("AutoStartAdminDesc");

    public string Version => LocalizationService.Get("Version");
    public string Status => LocalizationService.Get("Status");
    public string Logs => LocalizationService.Get("Logs");
    public string Clear => LocalizationService.Get("Clear");
    public string ClearLog => LocalizationService.Get("ClearLog");
    public string Save => LocalizationService.Get("Save");
    public string Diagnostics => LocalizationService.Get("Diagnostics");
    public string Results => LocalizationService.Get("Results");
    public string Testing => LocalizationService.Get("Testing");

    public string SetupWizardTitle => LocalizationService.Get("SetupWizardTitle");
    public string SetupWizardDesc => LocalizationService.Get("SetupWizardDesc");
    public string DownloadZapret => LocalizationService.Get("DownloadZapret");
    public string SelectStrategy => LocalizationService.Get("SelectStrategy");
    public string AllDone => LocalizationService.Get("AllDone");

    public string SelectStrategies => LocalizationService.Get("SelectStrategies");
    public string StrategiesSelected => LocalizationService.Get("StrategiesSelected");
    public string All => LocalizationService.Get("All");
    public string Reset => LocalizationService.Get("Reset");
    public string Apply => LocalizationService.Get("Apply");
    public string ApplyStrategy => LocalizationService.Get("ApplyStrategy");
    public string RunTest => LocalizationService.Get("RunTest");

    public string Open => LocalizationService.Get("Open");
    public string OpenFolder => LocalizationService.Get("OpenFolder");
    public string ReportIssue => LocalizationService.Get("ReportIssue");

    public string ProtectionStatus => LocalizationService.Get("ProtectionStatus");
    public string StrategyNotSelected => LocalizationService.Get("StrategyNotSelected");
    public string ActiveStrategy => LocalizationService.Get("ActiveStrategy");
    public string ActiveStrategyDesc => LocalizationService.Get("ActiveStrategyDesc");

    public string Export => LocalizationService.Get("Export");
    public string Import => LocalizationService.Get("Import");

    public string Main => LocalizationService.Get("Main");
    public string Appearance => LocalizationService.Get("Appearance");
    public string Updates => LocalizationService.Get("Updates");
    public string Hotkeys => LocalizationService.Get("Hotkeys");
    public string HotkeysDesc => LocalizationService.Get("HotkeysDesc");
    public string EnableHotkeys => LocalizationService.Get("EnableHotkeys");
    public string Filters => LocalizationService.Get("Filters");
    public string GameFilterLabel => LocalizationService.Get("GameFilterLabel");
    public string IpsetFilterLabel => LocalizationService.Get("IpsetFilterLabel");
    public string HostsAutoUpdate => LocalizationService.Get("HostsAutoUpdate");
    public string AutoUpdateCheck => LocalizationService.Get("AutoUpdateCheck");
    public string AutoUpdateDownload => LocalizationService.Get("AutoUpdateDownload");
    public string AutoUpdateDownloadDesc => LocalizationService.Get("AutoUpdateDownloadDesc");

    public string CollapseAll => LocalizationService.Get("CollapseAll");
    public string ExpandAll => LocalizationService.Get("ExpandAll");

    public string SearchStrategy => LocalizationService.Get("SearchStrategy");
    public string ResetTestResults => LocalizationService.Get("ResetTestResults");
    public string SelectStrategyTitle => LocalizationService.Get("SelectStrategyTitle");
    public string SelectStrategyHint => LocalizationService.Get("SelectStrategyHint");
    public string TestResult => LocalizationService.Get("TestResult");
    public string TestResultDesc => LocalizationService.Get("TestResultDesc");
    public string StrategyType => LocalizationService.Get("StrategyType");
    public string RecommendedFor => LocalizationService.Get("RecommendedFor");

    public string Mode => LocalizationService.Get("Mode");
    public string StandardMode => LocalizationService.Get("StandardMode");
    public string DpiMode => LocalizationService.Get("DpiMode");
public string TestLog => LocalizationService.Get("TestLog");

	public string ComponentChecks => LocalizationService.Get("ComponentChecks");
    public string AboutDesc => LocalizationService.Get("AboutDesc");
    public string Versions => LocalizationService.Get("Versions");
    public string AppVersion => LocalizationService.Get("AppVersion");
    public string ZapretVersion => LocalizationService.Get("ZapretVersion");
    public string ZapretVersionDesc => LocalizationService.Get("ZapretVersionDesc");
    public string WinUIVersion => LocalizationService.Get("WinUIVersion");
    public string WinUIVersionDesc => LocalizationService.Get("WinUIVersionDesc");
    public string Links => LocalizationService.Get("Links");
    public string ProjectGitHub => LocalizationService.Get("ProjectGitHub");
    public string TelegramChannel => LocalizationService.Get("TelegramChannel");
    public string OriginalZapret => LocalizationService.Get("OriginalZapret");
    public string Credits => LocalizationService.Get("Credits");
    public string CreditsFlowseal => LocalizationService.Get("CreditsFlowseal");
    public string CreditsCommunity => LocalizationService.Get("CreditsCommunity");
    public string CreditsMicrosoft => LocalizationService.Get("CreditsMicrosoft");

    public string FileCheck => LocalizationService.Get("FileCheck");
    public string AllFilesFound => LocalizationService.Get("AllFilesFound");
    public string SelectStrategyWizard => LocalizationService.Get("SelectStrategyWizard");
    public string StrategyDeterminesDpi => LocalizationService.Get("StrategyDeterminesDpi");
    public string DefaultStrategy => LocalizationService.Get("DefaultStrategy");
    public string Recommendation => LocalizationService.Get("Recommendation");
    public string LaunchSettings => LocalizationService.Get("LaunchSettings");
    public string AutoStartDesc => LocalizationService.Get("AutoStartDesc");
    public string StartProtectionOnLaunch => LocalizationService.Get("StartProtectionOnLaunch");
    public string AutoStartAdminWizard => LocalizationService.Get("AutoStartAdminWizard");
    public string AutoStartAdminWizardDesc => LocalizationService.Get("AutoStartAdminWizardDesc");

    public string UpdateTab => LocalizationService.Get("UpdateTab");
    public string TestingTab => LocalizationService.Get("TestingTab");
public string DiagnosticsTab => LocalizationService.Get("DiagnosticsTab");
	public string OperationLog => LocalizationService.Get("OperationLog");
    public string UpdateHosts => LocalizationService.Get("UpdateHosts");
    public string HostsFile => LocalizationService.Get("HostsFile");

    public string StrategiesLabel => LocalizationService.Get("StrategiesLabel");
    public string AnimNavIcons => LocalizationService.Get("AnimNavIcons");
    public string AnimButtons => LocalizationService.Get("AnimButtons");
    public string AnimCards => LocalizationService.Get("AnimCards");
}
