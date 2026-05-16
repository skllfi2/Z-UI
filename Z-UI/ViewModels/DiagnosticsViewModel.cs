// DiagnosticsViewModel.cs - Full diagnostics VM matching DiagnosticsPage.xaml bindings
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ZUI.Services;

namespace ZUI.ViewModels;

public partial class DiagnosticsViewModel : ViewModelBase, IDisposable
{
    private const int MaxLogLines = 500;
    private readonly IDiagnosticsService _diagnosticsService;
    private readonly IWorkerServiceManager _workerServiceManager;
    private readonly IIpcClientService _ipcClientService;

    // ── Summary ──────────────────────────────────────────────

    [ObservableProperty]
    private int _passedChecks;

    [ObservableProperty]
    private int _totalChecks;

    [ObservableProperty]
    private string _overallStatus = LocalizationService.Get("ReadyToCheck");

    [ObservableProperty]
    private string _summaryText = string.Empty;

    // ── Admin check ──────────────────────────────────────────

    [ObservableProperty]
    private bool _isAdmin;

    [ObservableProperty]
    private string _adminStatusText = LocalizationService.Get("NotChecked");

    [ObservableProperty]
    private string _adminInfoText = string.Empty;

    [ObservableProperty]
    private string _adminFixAction = string.Empty;

    // ── Worker IPC check ─────────────────────────────────────

    [ObservableProperty]
    private bool _isWorkerReachable;

    [ObservableProperty]
    private string _workerStatusText = LocalizationService.Get("NotChecked");

    [ObservableProperty]
    private string _workerInfoText = string.Empty;

    [ObservableProperty]
    private string _workerFixAction = string.Empty;

    // ── WinDivert check ──────────────────────────────────────

    [ObservableProperty]
    private bool _isWinDivertOk;

    [ObservableProperty]
    private string _winDivertStatusText = LocalizationService.Get("NotChecked");

    [ObservableProperty]
    private string _winDivertInfoText = string.Empty;

    [ObservableProperty]
    private string _winDivertFixAction = string.Empty;

    // ── Domain lists check ───────────────────────────────────

    [ObservableProperty]
    private bool _isDomainListsOk;

    [ObservableProperty]
    private string _domainListsStatusText = LocalizationService.Get("NotChecked");

    [ObservableProperty]
    private string _domainListsInfoText = string.Empty;

    [ObservableProperty]
    private string _domainListsFixAction = string.Empty;

    // ── Binary files check ───────────────────────────────────

    [ObservableProperty]
    private bool _isBinaryFilesOk;

    [ObservableProperty]
    private string _binaryFilesStatusText = LocalizationService.Get("NotChecked");

    [ObservableProperty]
    private string _binaryFilesInfoText = string.Empty;

    [ObservableProperty]
    private string _binaryFilesFixAction = string.Empty;

    // ── Network check ────────────────────────────────────────

    [ObservableProperty]
    private bool _isNetworkOk;

    [ObservableProperty]
    private string _networkStatusText = LocalizationService.Get("NotChecked");

    [ObservableProperty]
    private string _networkInfoText = string.Empty;

    [ObservableProperty]
    private string _networkFixAction = string.Empty;

    // ── Results / state ──────────────────────────────────────

    [ObservableProperty]
    private ObservableCollection<DiagnosticResult> _diagnosticResults = [];

    [ObservableProperty]
    private ObservableCollection<string> _logLines = [];

    [ObservableProperty]
    private bool _isRunning;

    /// <summary>True when at least one diagnostic run completed with results.</summary>
    public bool HasResults => DiagnosticResults.Count > 0;

    /// <summary>True when results are available for export.</summary>
    public bool CanExport => HasResults && !IsRunning;

    public DiagnosticsViewModel(
        IDiagnosticsService diagnosticsService,
        IWorkerServiceManager workerServiceManager,
        IIpcClientService ipcClientService)
    {
        _diagnosticsService = diagnosticsService ?? throw new ArgumentNullException(nameof(diagnosticsService));
        _workerServiceManager = workerServiceManager ?? throw new ArgumentNullException(nameof(workerServiceManager));
        _ipcClientService = ipcClientService ?? throw new ArgumentNullException(nameof(ipcClientService));
    }

    // ── Commands ─────────────────────────────────────────────

    [RelayCommand]
    private async Task RunDiagnosticsAsync()
    {
        if (IsRunning) return;
        IsRunning = true;
        OverallStatus = LocalizationService.Get("DiagnosticsRunning");

        try
        {
            // Run individual checks in parallel for speed
            var adminTask = _diagnosticsService.CheckAdminRightsAsync();
            var workerTask = _diagnosticsService.CheckWorkerProcessAsync();
            var winDivertTask = _diagnosticsService.CheckWinDivertAsync();
            var domainListsTask = _diagnosticsService.CheckDomainListsAsync();
            var binariesTask = _diagnosticsService.CheckBinaryFilesAsync();
            var networkTask = _diagnosticsService.TestConnectivityAsync("https://www.google.com", "Google");

            await Task.WhenAll(adminTask, workerTask, winDivertTask, domainListsTask, binariesTask, networkTask);

            // Populate per-check properties
        ApplyCheckResult(adminTask.Result, LocalizationService.Get("AdminRightsCheck"),
            b => IsAdmin = b,
            t => AdminStatusText = t,
            i => AdminInfoText = i,
            f => AdminFixAction = f);

        ApplyCheckResult(workerTask.Result, "Worker Service",
            b => IsWorkerReachable = b,
            t => WorkerStatusText = t,
            i => WorkerInfoText = i,
            f => WorkerFixAction = f);

        ApplyCheckResult(winDivertTask.Result, "WinDivert",
            b => IsWinDivertOk = b,
            t => WinDivertStatusText = t,
            i => WinDivertInfoText = i,
            f => WinDivertFixAction = f);

        ApplyCheckResult(domainListsTask.Result, LocalizationService.Get("DomainListsCheck"),
            b => IsDomainListsOk = b,
            t => DomainListsStatusText = t,
            i => DomainListsInfoText = i,
            f => DomainListsFixAction = f);

        ApplyCheckResult(binariesTask.Result, LocalizationService.Get("BinaryFilesCheck"),
            b => IsBinaryFilesOk = b,
            t => BinaryFilesStatusText = t,
            i => BinaryFilesInfoText = i,
            f => BinaryFilesFixAction = f);

        ApplyCheckResult(networkTask.Result, LocalizationService.Get("NetworkConnectivity"),
            b => IsNetworkOk = b,
            t => NetworkStatusText = t,
            i => NetworkInfoText = i,
            f => NetworkFixAction = f);

            // Also get full results list for detailed view
            var allResults = await _diagnosticsService.RunAllChecksAsync();
            DiagnosticResults = new ObservableCollection<DiagnosticResult>(allResults);

            // Compute summary
            PassedChecks = allResults.Count(r => r.Success);
            TotalChecks = allResults.Count;
        SummaryText = PassedChecks == TotalChecks
            ? LocalizationService.Get("DiagnosticsPassed", PassedChecks, TotalChecks)
            : LocalizationService.Get("DiagnosticsFailed", PassedChecks, TotalChecks);
            OverallStatus = SummaryText;

            // Notify computed properties
            OnPropertyChanged(nameof(HasResults));
            OnPropertyChanged(nameof(CanExport));

            Log(LocalizationService.Get("DiagnosticsCompleted") + $": {SummaryText}");
        }
        catch (Exception ex)
        {
        OverallStatus = $"{LocalizationService.Get("Error")}: {ex.Message}";
        SummaryText = OverallStatus;
        Log($"Error: {ex.Message}");
    }
    finally
    {
        IsRunning = false;
        OnPropertyChanged(nameof(CanExport));
    }
}

/// <summary>Quick health check (used by code-behind auto-run on navigation).</summary>
[RelayCommand]
private async Task RunQuickCheckAsync()
{
    if (IsRunning) return;
    IsRunning = true;
    OverallStatus = LocalizationService.Get("QuickCheckRunning");

        try
        {
            var health = await _diagnosticsService.QuickHealthCheckAsync();
            PassedChecks = health.PassedChecks;
            TotalChecks = health.TotalChecks;
        SummaryText = health.IsHealthy
            ? LocalizationService.Get("DiagnosticsPassed", health.PassedChecks, health.TotalChecks)
            : LocalizationService.Get("DiagnosticsFailed", health.PassedChecks, health.TotalChecks);
            OverallStatus = SummaryText;

            if (health.Issues.Count > 0)
            {
                foreach (var issue in health.Issues)
                    Log($"⚠ {issue}");
            }

            Log($"Quick check: {OverallStatus}");
        }
        catch (Exception ex)
        {
        OverallStatus = $"{LocalizationService.Get("Error")}: {ex.Message}";
        SummaryText = OverallStatus;
        Log($"Error: {ex.Message}");
    }
    finally
    {
        IsRunning = false;
    }
}

[RelayCommand]
private async Task ExportReportAsync()
    {
        if (!CanExport) return;

        try
        {
            var docsFolder = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            var fileName = $"z-ui-diagnostics-{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.txt";
            var filePath = Path.Combine(docsFolder, "Z-UI", fileName);

            Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);

            using var writer = new StreamWriter(filePath, append: false);
            await writer.WriteLineAsync($"Z-UI Diagnostics Report — {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            await writer.WriteLineAsync(new string('─', 50));
            await writer.WriteLineAsync($"Status: {OverallStatus}");
            await writer.WriteLineAsync($"Passed: {PassedChecks}/{TotalChecks}");
            await writer.WriteLineAsync();

            foreach (var result in DiagnosticResults)
            {
                var glyph = result.Success ? "✓" : "✗";
                await writer.WriteLineAsync($"{glyph} {result.Name}: {result.Message}");
                if (!result.Success && result.FixAction is not null)
                    await writer.WriteLineAsync($"  → Fix: {result.FixAction}");
            }

            await writer.WriteLineAsync();
            await writer.WriteLineAsync("Log:");
            foreach (var line in LogLines)
                await writer.WriteLineAsync(line);

        Log(LocalizationService.Get("ExportedTo", filePath));
    }
    catch (Exception ex)
    {
        Log(LocalizationService.Get("ExportError", ex.Message));
        }
    }

    // ── Fix commands ─────────────────────────────────────────

    [RelayCommand]
    private void FixAdmin()
    {
        try
        {
            var exePath = Environment.ProcessPath;
            if (exePath is null) return;

            var startInfo = new System.Diagnostics.ProcessStartInfo(exePath)
            {
                UseShellExecute = true,
                Verb = "runas"
            };
            System.Diagnostics.Process.Start(startInfo);
        Log(LocalizationService.Get("FixAdminRights"));
    }
    catch (Exception ex)
    {
        Log(LocalizationService.Get("FixAdminFailed", ex.Message));
        }
    }

    [RelayCommand]
    private async Task FixWorkerAsync()
    {
        try
        {
            if (!_workerServiceManager.IsInstalled)
            {
                Log(LocalizationService.Get("FixWorkerInstalling"));
                var installResult = await _workerServiceManager.InstallAsync();
                if (!installResult.IsSuccess)
                {
                    Log($"{LocalizationService.Get("Error")}: {installResult.Error}");
                    return;
                }
            }

            if (_workerServiceManager.Status != WorkerServiceStatus.Running)
            {
                Log(LocalizationService.Get("FixWorkerStarting"));
                var startResult = await _workerServiceManager.StartAsync();
                if (!startResult.IsSuccess)
                {
                    Log($"{LocalizationService.Get("Error")}: {startResult.Error}");
                    return;
                }
            }

            // Try IPC reconnect to the now-running Worker
            try
            {
                if (!_ipcClientService.IsConnected)
                    await _ipcClientService.ConnectAsync();
            }
            catch (Exception ipcEx)
            {
                Log($"IPC connect: {ipcEx.Message}");
            }

            await _workerServiceManager.RefreshStatusAsync();
            Log(_workerServiceManager.Status == WorkerServiceStatus.Running
                ? LocalizationService.Get("FixWorkerSuccess")
                : $"{LocalizationService.Get("Error")}: Worker status = {_workerServiceManager.Status}");
        }
        catch (Exception ex)
        {
            Log($"{LocalizationService.Get("Error")}: {ex.Message}");
        }
    }

    [RelayCommand]
    private void FixWinDivert()
    {
        Log("WinDivert is installed automatically on first protection start.");
    }

    [RelayCommand]
    private void FixDomainLists()
    {
        Log(LocalizationService.Get("FixDomainListsNote"));
    }

    [RelayCommand]
    private void FixBinaryFiles()
    {
        Log(LocalizationService.Get("FixBinaryFilesNote"));
    }

    [RelayCommand]
    private void FixNetwork()
    {
        Log(LocalizationService.Get("FixNetworkNote"));
    }

    // ── Clear logs ───────────────────────────────────────────

    [RelayCommand]
    private void ClearLogs()
    {
        LogLines.Clear();
    }

    // ── Helpers ──────────────────────────────────────────────

    private void ApplyCheckResult(
        DiagnosticResult result,
        string defaultName,
        Action<bool> setOk,
        Action<string> setStatus,
        Action<string> setInfo,
        Action<string> setFix)
    {
        setOk(result.Success);
        setStatus(result.Success ? "ОК" : "Ошибка");
        setInfo(string.IsNullOrEmpty(result.Message) ? defaultName : result.Message);
        setFix(result.FixAction ?? string.Empty);
    }

    private void Log(string message)
    {
        var line = $"[{DateTime.Now:HH:mm:ss}] {message}";
        LogLines.Add(line);

        while (LogLines.Count > MaxLogLines)
            LogLines.RemoveAt(0);
    }

    public void Dispose()
    {
    }
}
