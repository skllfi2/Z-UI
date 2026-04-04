using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ZUI.Services;
using Microsoft.UI.Dispatching;

namespace ZUI.ViewModels;

public partial class ServicesViewModel : ViewModelBase, IDisposable
{
    private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(60) };
    
    private const string VersionUrl = "https://raw.githubusercontent.com/Flowseal/zapret-discord-youtube/main/.service/version.txt";
    private const string IpsetUrl = "https://raw.githubusercontent.com/Flowseal/zapret-discord-youtube/refs/heads/main/.service/ipset-service.txt";
    private const string HostsUrl = "https://raw.githubusercontent.com/Flowseal/zapret-discord-youtube/refs/heads/main/.service/hosts";
    private const string FlowsealApiUrl = "https://api.github.com/repos/Flowseal/zapret-discord-youtube/commits?path=.service/hosts&per_page=1";

    [ObservableProperty]
    private int _selectedPivotIndex;

    [ObservableProperty]
    private string _versionStatus = "Проверяется...";

    [ObservableProperty]
    private string _ipsetStatus = "Список заблокированных IP с GitHub";

    [ObservableProperty]
    private string _hostsStatus = "Системный файл hosts";

    [ObservableProperty]
    private string _hostsLastCheck = "";

    [ObservableProperty]
    private bool _hostsUpdateAvailable;

    [ObservableProperty]
    private bool _isCheckingVersion;

    [ObservableProperty]
    private bool _isUpdatingIpset;

    [ObservableProperty]
    private bool _isUpdatingHosts;

    [ObservableProperty]
    private string _updateLog = "";

    [ObservableProperty]
    private ObservableCollection<string> _logLines = [];

    [ObservableProperty]
    private int _logLineCount;

    [ObservableProperty]
    private bool _autoScroll = true;

    [ObservableProperty]
    private bool _textWrap = true;

    [ObservableProperty]
    private int _diagPassCount;

    [ObservableProperty]
    private int _diagFailCount;

    [ObservableProperty]
    private string _diagSubtitle = "Нажмите 'Проверить' для диагностики";

    [ObservableProperty]
    private bool _isRunningDiagnostics;

[ObservableProperty]
private ObservableCollection<CheckResultItem> _checkResults = [];

[ObservableProperty]
private bool _isDpiMode;

[ObservableProperty]
private bool _isStandardMode = true;

[ObservableProperty]
private ObservableCollection<StrategyCheckItem> _availableStrategies = [];

[ObservableProperty]
private int _selectedStrategiesCount;

[ObservableProperty]
private bool _isTestRunning;

[ObservableProperty]
private string _testProgressConfig = "";

[ObservableProperty]
private string _testProgressPhase = "";

[ObservableProperty]
private int _testProgressValue;

[ObservableProperty]
private int _testProgressTotal;

[ObservableProperty]
private ObservableCollection<string> _testLogLines = [];

 [ObservableProperty]
 private bool _showResultsTab;

 [ObservableProperty]
 private ObservableCollection<TestHistoryEntry> _testHistory = [];

 private readonly WinwsService _winwsService;
 private readonly TestHistoryService _historyService;
private ZapretTestRunner? _testRunner;
private CancellationTokenSource? _testCts;

 public ServicesViewModel(WinwsService winwsService, TestHistoryService historyService)
 {
 _winwsService = winwsService;
 _historyService = historyService;
 _winwsService.LogReceived += OnLogReceived;

 foreach (var entry in _historyService.History)
 TestHistory.Add(entry);

foreach (var line in AppState.Logs)
LogLines.Add(line);
LogLineCount = LogLines.Count;

if (UpdateChecker.UpdateAvailable)
HostsUpdateAvailable = true;

UpdateChecker.UpdateFound += _ => HostsUpdateAvailable = true;

LoadAvailableStrategies();
}

private void LoadAvailableStrategies()
{
    AvailableStrategies.Clear();
    if (!Directory.Exists(ZapretPaths.StrategiesDir)) return;

    var excludedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "service", "install", "uninstall", "run-as-service", "run-as-admin"
    };

    foreach (var file in Directory.GetFiles(ZapretPaths.StrategiesDir, "*.bat"))
    {
        var name = Path.GetFileNameWithoutExtension(file);
        if (!excludedFiles.Contains(name))
            AvailableStrategies.Add(new StrategyCheckItem { Name = name, IsChecked = true });
    }
    UpdateSelectedCount();
}

partial void OnIsDpiModeChanged(bool value)
{
if (value) IsStandardMode = false;
}

partial void OnIsStandardModeChanged(bool value)
{
if (value) IsDpiMode = false;
}

public void UpdateSelectedCount()
{
SelectedStrategiesCount = AvailableStrategies.Count(s => s.IsChecked);
}

[RelayCommand]
private void SelectAllStrategies()
{
foreach (var s in AvailableStrategies) s.IsChecked = true;
UpdateSelectedCount();
}

[RelayCommand]
private void ClearAllStrategies()
{
foreach (var s in AvailableStrategies) s.IsChecked = false;
UpdateSelectedCount();
}

[RelayCommand]
private async Task RunTestAsync()
{
if (IsTestRunning) return;

var selectedConfigs = AvailableStrategies
.Where(s => s.IsChecked)
.Select(s => new FileInfo(Path.Combine(ZapretPaths.StrategiesDir, s.Name + ".bat")))
.Where(f => f.Exists)
.ToList();

if (selectedConfigs.Count == 0)
{
AppendTestLog("[ОШИБКА] Не выбрана ни одна стратегия");
return;
}

_testRunner = new ZapretTestRunner(ZapretPaths.AppDir, ZapretPaths.ListsDir, ZapretPaths.StrategiesDir);
_testRunner.Log += msg => RunOnUIThread(() => AppendTestLog(msg));
_testRunner.ProgressChanged += p =>
{
    RunOnUIThread(() =>
    {
        TestProgressConfig = p.ConfigName;
        TestProgressPhase = p.Phase;
        TestProgressValue = p.ConfigIndex;
        TestProgressTotal = p.TotalConfigs;
    });
};

var mode = IsDpiMode ? TestMode.Dpi : TestMode.Standard;
var preflight = _testRunner.Preflight(mode);

if (!preflight.Ok)
{
foreach (var err in preflight.Errors)
AppendTestLog($"[ОШИБКА] {err}");
return;
}

foreach (var w in preflight.Warnings)
AppendTestLog($"[ПРЕДУПРЕЖДЕНИЕ] {w}");

IsTestRunning = true;
TestLogLines.Clear();
TestProgressValue = 0;
TestProgressTotal = selectedConfigs.Count;

AppendTestLog($" Запуск тестирования {DateTime.Now:HH:mm:ss}");
AppendTestLog($" Режим: {(mode == TestMode.Standard ? "Standard — HTTP + Ping" : "DPI — TCP 16-20 KB freeze")}");
AppendTestLog($" Стратегий: {selectedConfigs.Count}");
AppendTestLog("─────────────────────────────────────────");

_testCts = new CancellationTokenSource();

try
{
var targets = _testRunner.LoadTargets(Path.Combine(ZapretPaths.UtilsDir, "targets.txt"));
var suite = mode == TestMode.Dpi ? await new DpiTester().LoadSuiteAsync() : [];

var results = await _testRunner.RunAsync(mode, selectedConfigs, targets, suite, _testCts.Token);

 if (results.Count > 0)
 {
 var best = ZapretTestRunner.FindBest(results, mode);
 TestResultStore.Publish(results, mode);

 _testRunner.SaveResults(results, mode,
 Path.Combine(ZapretPaths.UtilsDir, "test results"));

 if (best != null)
 {
 AppendTestLog($"\\n★ Лучший конфиг: {best.ConfigName}");

 var snapshot = TestResultStore.Get(best.ConfigName);
 var entry = new TestHistoryEntry
 {
 Timestamp = DateTime.Now,
 StrategyName = best.ConfigName,
 TestMode = mode.ToString(),
 Rating = snapshot?.Rating ?? StrategyRating.Unknown,
 RatingLabel = snapshot?.RatingLabel ?? "Неизвестно",
 PassCount = results.Count(r => TestResultStore.Get(r.ConfigName)?.Rating == StrategyRating.Recommended),
 FailCount = results.Count(r => TestResultStore.Get(r.ConfigName)?.Rating == StrategyRating.NotRecommended),
 TotalTargets = results.Count,
 BestStrategy = best.ConfigName
 };
 _historyService.AddEntry(entry);
 RunOnUIThread(() =>
 {
 TestHistory.Insert(0, entry);
 if (TestHistory.Count > 50)
 TestHistory.RemoveAt(TestHistory.Count - 1);
 });
 }
 }
}
catch (OperationCanceledException)
{
AppendTestLog("[ТЕСТ] Остановлено пользователем");
}
catch (Exception ex)
{
AppendTestLog($"[ОШИБКА] {ex.Message}");
}
finally
{
IsTestRunning = false;
_testCts?.Dispose();
_testCts = null;
}
}

[RelayCommand]
private void StopTest()
{
_testCts?.Cancel();
}

 [RelayCommand]
 private void ClearTestLog()
 {
 TestLogLines.Clear();
 }

 [RelayCommand]
 private void ClearTestHistory()
 {
 _historyService.Clear();
 TestHistory.Clear();
 }

private void AppendTestLog(string msg)
{
TestLogLines.Add(msg);
}

    private void OnLogReceived(string message)
    {
        LogLines.Add(message);
        LogLineCount = LogLines.Count;
    }

[RelayCommand]
private async Task CheckVersionAsync()
{
    if (IsCheckingVersion) return;
    RunOnUIThread(() => IsCheckingVersion = true);

    try
    {
        var localVersion = ZapretPaths.LocalVersion;
        var remoteVersion = await _http.GetStringAsync(VersionUrl);

        RunOnUIThread(() =>
        {
            VersionStatus = $"{localVersion} → {remoteVersion.Trim()}";
            AppendUpdateLog($"✓ Версия: {localVersion}");
        });
    }
    catch (Exception ex)
    {
        RunOnUIThread(() =>
        {
            VersionStatus = "Ошибка проверки";
            AppendUpdateLog($"✗ Ошибка: {ex.Message}");
        });
    }
    finally
    {
        RunOnUIThread(() => IsCheckingVersion = false);
    }
}

[RelayCommand]
private async Task UpdateIpsetAsync()
{
    if (IsUpdatingIpset) return;
    RunOnUIThread(() => IsUpdatingIpset = true);

    try
    {
        var content = await _http.GetStringAsync(IpsetUrl);
        var ipsetFile = Path.Combine(ZapretPaths.ListsDir, "ipset-all.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(ipsetFile)!);
        await File.WriteAllTextAsync(ipsetFile, content);

        var count = content.Split('\n').Length;
        RunOnUIThread(() =>
        {
            IpsetStatus = $"Обновлено: {count} записей";
            AppendUpdateLog($"✓ ipset-all.txt обновлён ({count} записей)");
        });
    }
    catch (Exception ex)
    {
        RunOnUIThread(() =>
        {
            IpsetStatus = "Ошибка обновления";
            AppendUpdateLog($"✗ Ошибка ipset: {ex.Message}");
        });
    }
    finally
    {
        RunOnUIThread(() => IsUpdatingIpset = false);
    }
}

[RelayCommand]
private async Task UpdateHostsAsync()
{
    if (IsUpdatingHosts) return;
    RunOnUIThread(() => IsUpdatingHosts = true);

    try
    {
        var block = await BuildMergedHostsBlock();
        var hash = ComputeSHA256(block);

        if (hash == AppSettings.HostsHash)
        {
            RunOnUIThread(() =>
            {
                HostsStatus = "Hosts актуален";
                AppendUpdateLog("✓ Файл hosts актуален");
            });
            return;
        }

        var hostsFile = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.System),
            "drivers", "etc", "hosts");

        var localContent = await File.ReadAllTextAsync(hostsFile);
        var backupFile = hostsFile + ".backup_zui";
        if (!File.Exists(backupFile)) File.Copy(hostsFile, backupFile);

        var clean = localContent;
        var idx = clean.IndexOf("# ===== zapret hosts");
        if (idx >= 0) clean = clean[..idx].TrimEnd();

        await File.WriteAllTextAsync(hostsFile, clean + Environment.NewLine + Environment.NewLine + block);

        AppSettings.HostsHash = hash;
        AppSettings.HostsLastCheck = DateTime.UtcNow;
        AppSettings.Save();

        var count = block.Split('\n').Length;
        RunOnUIThread(() =>
        {
            HostsStatus = "Hosts обновлён ✓";
            HostsUpdateAvailable = false;
            AppendUpdateLog($"✓ Hosts применён ({count} строк)");
        });
    }
    catch (UnauthorizedAccessException)
    {
        RunOnUIThread(() =>
        {
            HostsStatus = "Нет прав администратора";
            AppendUpdateLog("✗ Нет прав на запись hosts");
        });
    }
    catch (Exception ex)
    {
        RunOnUIThread(() =>
        {
            HostsStatus = "Ошибка";
            AppendUpdateLog($"✗ Ошибка hosts: {ex.Message}");
        });
    }
    finally
    {
        RunOnUIThread(() => IsUpdatingHosts = false);
    }
}

    private static async Task<string> BuildMergedHostsBlock()
    {
        var flowsealContent = await _http.GetStringAsync(HostsUrl);

        var sb = new StringBuilder();
        sb.AppendLine("# ===== zapret hosts (Flowseal) =====");
        sb.Append(flowsealContent.TrimEnd());
        return sb.ToString();
    }

    private static string ComputeSHA256(string text)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(text));
        return Convert.ToHexString(bytes);
    }

[RelayCommand]
private async Task RunDiagnosticsAsync()
{
    if (IsRunningDiagnostics) return;
    RunOnUIThread(() =>
    {
        IsRunningDiagnostics = true;
        CheckResults.Clear();
        DiagPassCount = 0;
        DiagFailCount = 0;
        DiagSubtitle = "Выполняется проверка...";
    });

    var results = await Task.Run(RunAllChecks);

    int passed = results.Count(r => r.Ok);
    RunOnUIThread(() =>
    {
        DiagPassCount = passed;
        DiagFailCount = results.Count - passed;
        DiagSubtitle = passed == results.Count
            ? $"Все проверки пройдены · {results.Count} компонентов"
            : $"{results.Count - passed} проблем из {results.Count}";

        foreach (var r in results)
            CheckResults.Add(r);

        IsRunningDiagnostics = false;
    });
}

    private static List<CheckResultItem> RunAllChecks()
    {
        var r = new List<CheckResultItem>();

        FileCheck(r, "winws.exe", ZapretPaths.WinwsExe);
        FileCheck(r, "WinDivert.dll", Path.Combine(ZapretPaths.WinwsDir, "WinDivert.dll"));
        FileCheck(r, "WinDivert64.sys", Path.Combine(ZapretPaths.WinwsDir, "WinDivert64.sys"));
        FileCheck(r, "cygwin1.dll", Path.Combine(ZapretPaths.WinwsDir, "cygwin1.dll"));

        var lg = Path.Combine(ZapretPaths.ListsDir, "list-general.txt");
        r.Add(new CheckResultItem("list-general.txt", File.Exists(lg),
            File.Exists(lg) ? $"{CountLines(lg)} доменов" : $"Не найден: {lg}"));

        var ip = Path.Combine(ZapretPaths.ListsDir, "ipset-all.txt");
        r.Add(new CheckResultItem("ipset-all.txt", File.Exists(ip),
            File.Exists(ip) ? $"{CountLines(ip)} записей" : $"Не найден: {ip}"));

        FileCheck(r, "list-exclude.txt", Path.Combine(ZapretPaths.ListsDir, "list-exclude.txt"));

        return r;
    }

    private static void FileCheck(List<CheckResultItem> r, string name, string path)
    {
        r.Add(new CheckResultItem(name, File.Exists(path),
            File.Exists(path) ? "OK" : $"Не найден: {path}"));
    }

    private static int CountLines(string path)
    {
        try { return File.ReadAllLines(path).Length; }
        catch { return 0; }
    }

    [RelayCommand]
    private void ClearLog()
    {
        AppState.Logs.Clear();
        LogLines.Clear();
        LogLineCount = 0;
    }

[RelayCommand]
private void CopyLog()
{
var text = string.Join("\n", LogLines);
var package = new Windows.ApplicationModel.DataTransfer.DataPackage();
package.SetText(text);
Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(package);
}

    private void AppendUpdateLog(string line)
    {
        UpdateLog += line + "\n";
    }

public void Dispose()
{
_winwsService.LogReceived -= OnLogReceived;
}
}

public partial class StrategyCheckItem : ObservableObject
{
public string Name { get; init; } = "";
public bool IsChecked { get; set => SetProperty(ref field, value); }
}

public partial class CheckResultItem : ObservableObject
{
    public string Name { get; init; } = "";
    public bool Ok { get; init; }
    public string Detail { get; init; } = "";

    public CheckResultItem(string name, bool ok, string detail)
    {
        Name = name;
        Ok = ok;
        Detail = detail;
    }
}
