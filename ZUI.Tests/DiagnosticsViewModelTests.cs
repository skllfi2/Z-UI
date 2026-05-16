// DiagnosticsViewModelTests.cs - Unit tests for DiagnosticsViewModel
using System.Collections.ObjectModel;
using Moq;
using ZUI.Services;
using ZUI.ViewModels;

namespace ZUI.Tests;

public class DiagnosticsViewModelTests
{
    private readonly Mock<IDiagnosticsService> _mockDiagnostics;
    private readonly Mock<IWorkerServiceManager> _mockWorkerServiceManager;
    private readonly Mock<IIpcClientService> _mockIpcClientService;

    public DiagnosticsViewModelTests()
    {
        _mockDiagnostics = new Mock<IDiagnosticsService>();
        _mockWorkerServiceManager = new Mock<IWorkerServiceManager>();
        _mockWorkerServiceManager.SetupGet(w => w.IsInstalled).Returns(false);
        _mockWorkerServiceManager.SetupGet(w => w.Status).Returns(WorkerServiceStatus.NotInstalled);
        _mockIpcClientService = new Mock<IIpcClientService>();
        _mockIpcClientService.SetupGet(i => i.IsConnected).Returns(false);
    }

    private DiagnosticsViewModel CreateVm()
    {
        return new DiagnosticsViewModel(_mockDiagnostics.Object, _mockWorkerServiceManager.Object, _mockIpcClientService.Object);
    }

    // ── Constructor ────────────────────────────────────────────

    [Fact]
    public void Constructor_NullDiagnosticsService_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() =>
        new DiagnosticsViewModel(null!, _mockWorkerServiceManager.Object, _mockIpcClientService.Object));
    }

    [Fact]
    public void Constructor_NullWorkerServiceManager_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() =>
        new DiagnosticsViewModel(_mockDiagnostics.Object, null!, _mockIpcClientService.Object));
    }

    [Fact]
    public void Constructor_NullIpcClientService_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() =>
        new DiagnosticsViewModel(_mockDiagnostics.Object, _mockWorkerServiceManager.Object, null!));
    }

    [Fact]
    public void Constructor_Defaults_PassedChecksZero()
    {
        var vm = CreateVm();
        Assert.Equal(0, vm.PassedChecks);
        Assert.Equal(0, vm.TotalChecks);
        Assert.False(vm.HasResults);
        Assert.False(vm.IsRunning);
    }

    [Fact]
    public void Constructor_DefaultOverallStatus()
    {
        var vm = CreateVm();
        Assert.Equal("Готово к проверке", vm.OverallStatus);
    }

    // ── RunDiagnosticsCommand ───────────────────────────────────

    [Fact]
    public async Task RunDiagnostics_UpdatesPassedAndTotalChecks()
    {
        _mockDiagnostics.Setup(d => d.CheckAdminRightsAsync())
            .ReturnsAsync(new DiagnosticResult { Name = "Admin Rights", Success = true, Message = "OK" });
        _mockDiagnostics.Setup(d => d.CheckWorkerProcessAsync())
            .ReturnsAsync(new DiagnosticResult { Name = "Worker IPC", Success = true, Message = "OK" });
        _mockDiagnostics.Setup(d => d.CheckWinDivertAsync())
            .ReturnsAsync(new DiagnosticResult { Name = "WinDivert", Success = true, Message = "OK" });
        _mockDiagnostics.Setup(d => d.CheckDomainListsAsync())
            .ReturnsAsync(new DiagnosticResult { Name = "Domain Lists", Success = true, Message = "OK" });
        _mockDiagnostics.Setup(d => d.CheckBinaryFilesAsync())
            .ReturnsAsync(new DiagnosticResult { Name = "Binary Files", Success = false, Message = "Missing" });
        _mockDiagnostics.Setup(d => d.TestConnectivityAsync(It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(new DiagnosticResult { Name = "Network", Success = true, Message = "OK" });
        _mockDiagnostics.Setup(d => d.RunAllChecksAsync())
            .ReturnsAsync(new List<DiagnosticResult>
            {
                new() { Name = "Admin", Success = true, Message = "OK" },
                new() { Name = "Worker", Success = true, Message = "OK" },
                new() { Name = "WinDivert", Success = true, Message = "OK" },
                new() { Name = "Domain Lists", Success = true, Message = "OK" },
                new() { Name = "Binary Files", Success = false, Message = "Missing" },
                new() { Name = "Network", Success = true, Message = "OK" },
            });

        var vm = CreateVm();
        await vm.RunDiagnosticsCommand.ExecuteAsync(null);

        Assert.Equal(5, vm.PassedChecks);
        Assert.Equal(6, vm.TotalChecks);
        Assert.Contains("5/6", vm.SummaryText);
    }

    [Fact]
    public async Task RunDiagnostics_SetsHasResults_AfterFullCheck()
    {
        _mockDiagnostics.Setup(d => d.CheckAdminRightsAsync())
            .ReturnsAsync(new DiagnosticResult { Name = "Admin", Success = true, Message = "OK" });
        _mockDiagnostics.Setup(d => d.CheckWorkerProcessAsync())
            .ReturnsAsync(new DiagnosticResult { Name = "Worker", Success = true, Message = "OK" });
        _mockDiagnostics.Setup(d => d.CheckWinDivertAsync())
            .ReturnsAsync(new DiagnosticResult { Name = "WinDivert", Success = true, Message = "OK" });
        _mockDiagnostics.Setup(d => d.CheckDomainListsAsync())
            .ReturnsAsync(new DiagnosticResult { Name = "Domain Lists", Success = true, Message = "OK" });
        _mockDiagnostics.Setup(d => d.CheckBinaryFilesAsync())
            .ReturnsAsync(new DiagnosticResult { Name = "Binary Files", Success = true, Message = "OK" });
        _mockDiagnostics.Setup(d => d.TestConnectivityAsync(It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(new DiagnosticResult { Name = "Network", Success = true, Message = "OK" });
        _mockDiagnostics.Setup(d => d.RunAllChecksAsync())
            .ReturnsAsync(new List<DiagnosticResult>
            {
                new() { Name = "Admin Rights", Success = true, Message = "OK" }
            });

        var vm = CreateVm();
        await vm.RunDiagnosticsCommand.ExecuteAsync(null);

        Assert.True(vm.HasResults);
        Assert.Single(vm.DiagnosticResults);
    }

    [Fact]
    public async Task RunDiagnostics_UpdatesAdminCard()
    {
        _mockDiagnostics.Setup(d => d.CheckAdminRightsAsync())
            .ReturnsAsync(new DiagnosticResult { Name = "Admin Rights", Success = true, Message = "Has admin" });
        _mockDiagnostics.Setup(d => d.CheckWorkerProcessAsync())
            .ReturnsAsync(new DiagnosticResult { Name = "Worker IPC", Success = true, Message = "OK" });
        _mockDiagnostics.Setup(d => d.CheckWinDivertAsync())
            .ReturnsAsync(new DiagnosticResult { Name = "WinDivert", Success = true, Message = "OK" });
        _mockDiagnostics.Setup(d => d.CheckDomainListsAsync())
            .ReturnsAsync(new DiagnosticResult { Name = "Domain Lists", Success = true, Message = "OK" });
        _mockDiagnostics.Setup(d => d.CheckBinaryFilesAsync())
            .ReturnsAsync(new DiagnosticResult { Name = "Binary Files", Success = true, Message = "OK" });
        _mockDiagnostics.Setup(d => d.TestConnectivityAsync(It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(new DiagnosticResult { Name = "Network", Success = true, Message = "OK" });
        _mockDiagnostics.Setup(d => d.RunAllChecksAsync())
            .ReturnsAsync(new List<DiagnosticResult>());

        var vm = CreateVm();
        await vm.RunDiagnosticsCommand.ExecuteAsync(null);

        Assert.True(vm.IsAdmin);
        // ApplyCheckResult: success → "ОК", info → result.Message ("Has admin")
        Assert.Equal("ОК", vm.AdminStatusText);
        Assert.Equal("Has admin", vm.AdminInfoText);
    }

    [Fact]
    public async Task RunDiagnostics_UpdatesWorkerCard_Failure()
    {
        _mockDiagnostics.Setup(d => d.CheckAdminRightsAsync())
            .ReturnsAsync(new DiagnosticResult { Name = "Admin", Success = true, Message = "OK" });
        _mockDiagnostics.Setup(d => d.CheckWorkerProcessAsync())
            .ReturnsAsync(new DiagnosticResult { Name = "Worker IPC", Success = false, Message = "Not connected", FixAction = "Start Worker" });
        _mockDiagnostics.Setup(d => d.CheckWinDivertAsync())
            .ReturnsAsync(new DiagnosticResult { Name = "WinDivert", Success = true, Message = "OK" });
        _mockDiagnostics.Setup(d => d.CheckDomainListsAsync())
            .ReturnsAsync(new DiagnosticResult { Name = "Domain Lists", Success = true, Message = "OK" });
        _mockDiagnostics.Setup(d => d.CheckBinaryFilesAsync())
            .ReturnsAsync(new DiagnosticResult { Name = "Binary Files", Success = true, Message = "OK" });
        _mockDiagnostics.Setup(d => d.TestConnectivityAsync(It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(new DiagnosticResult { Name = "Network", Success = true, Message = "OK" });
        _mockDiagnostics.Setup(d => d.RunAllChecksAsync())
            .ReturnsAsync(new List<DiagnosticResult>());

        var vm = CreateVm();
        await vm.RunDiagnosticsCommand.ExecuteAsync(null);

        Assert.False(vm.IsWorkerReachable);
        Assert.Equal("Ошибка", vm.WorkerStatusText);
        Assert.Equal("Not connected", vm.WorkerInfoText);
        Assert.Equal("Start Worker", vm.WorkerFixAction);
    }

    [Fact]
    public async Task RunDiagnostics_UpdatesWinDivertCard()
    {
        _mockDiagnostics.Setup(d => d.CheckAdminRightsAsync())
            .ReturnsAsync(new DiagnosticResult { Name = "Admin", Success = true, Message = "OK" });
        _mockDiagnostics.Setup(d => d.CheckWorkerProcessAsync())
            .ReturnsAsync(new DiagnosticResult { Name = "Worker", Success = true, Message = "OK" });
        _mockDiagnostics.Setup(d => d.CheckWinDivertAsync())
            .ReturnsAsync(new DiagnosticResult { Name = "WinDivert Driver", Success = true, Message = "Loaded" });
        _mockDiagnostics.Setup(d => d.CheckDomainListsAsync())
            .ReturnsAsync(new DiagnosticResult { Name = "Domain Lists", Success = true, Message = "OK" });
        _mockDiagnostics.Setup(d => d.CheckBinaryFilesAsync())
            .ReturnsAsync(new DiagnosticResult { Name = "Binary Files", Success = true, Message = "OK" });
        _mockDiagnostics.Setup(d => d.TestConnectivityAsync(It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(new DiagnosticResult { Name = "Network", Success = true, Message = "OK" });
        _mockDiagnostics.Setup(d => d.RunAllChecksAsync())
            .ReturnsAsync(new List<DiagnosticResult>());

        var vm = CreateVm();
        await vm.RunDiagnosticsCommand.ExecuteAsync(null);

        Assert.True(vm.IsWinDivertOk);
        Assert.Equal("ОК", vm.WinDivertStatusText);
    }

    [Fact]
    public async Task RunDiagnostics_Exception_SetsErrorSummary()
    {
        _mockDiagnostics.Setup(d => d.CheckAdminRightsAsync())
            .ThrowsAsync(new InvalidOperationException("IPC failure"));

        var vm = CreateVm();
        await vm.RunDiagnosticsCommand.ExecuteAsync(null);

        Assert.Contains("IPC failure", vm.SummaryText);
    }

    [Fact]
    public async Task RunDiagnostics_IsRunningFalse_AfterCompletion()
    {
        _mockDiagnostics.Setup(d => d.CheckAdminRightsAsync())
            .ReturnsAsync(new DiagnosticResult { Name = "Admin", Success = true, Message = "OK" });
        _mockDiagnostics.Setup(d => d.CheckWorkerProcessAsync())
            .ReturnsAsync(new DiagnosticResult { Name = "Worker", Success = true, Message = "OK" });
        _mockDiagnostics.Setup(d => d.CheckWinDivertAsync())
            .ReturnsAsync(new DiagnosticResult { Name = "WinDivert", Success = true, Message = "OK" });
        _mockDiagnostics.Setup(d => d.CheckDomainListsAsync())
            .ReturnsAsync(new DiagnosticResult { Name = "Domain Lists", Success = true, Message = "OK" });
        _mockDiagnostics.Setup(d => d.CheckBinaryFilesAsync())
            .ReturnsAsync(new DiagnosticResult { Name = "Binary Files", Success = true, Message = "OK" });
        _mockDiagnostics.Setup(d => d.TestConnectivityAsync(It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(new DiagnosticResult { Name = "Network", Success = true, Message = "OK" });
        _mockDiagnostics.Setup(d => d.RunAllChecksAsync())
            .ReturnsAsync(new List<DiagnosticResult>());

        var vm = CreateVm();
        await vm.RunDiagnosticsCommand.ExecuteAsync(null);

        Assert.False(vm.IsRunning);
    }

    // ── RunQuickCheckCommand ────────────────────────────────────

    [Fact]
    public async Task RunQuickCheck_AllPass_SetsSummary()
    {
        var healthStatus = new DiagnosticHealthStatus
        {
            IsHealthy = true,
            PassedChecks = 3,
            TotalChecks = 3,
            Summary = "OK"
        };
        _mockDiagnostics.Setup(d => d.QuickHealthCheckAsync())
            .ReturnsAsync(healthStatus);

        var vm = CreateVm();
        await vm.RunQuickCheckCommand.ExecuteAsync(null);

        Assert.Equal(3, vm.PassedChecks);
        Assert.Equal(3, vm.TotalChecks);
        Assert.Contains("3/3", vm.SummaryText);
    }

    [Fact]
    public async Task RunQuickCheck_SomeFail_SetsPartialPass()
    {
        var healthStatus = new DiagnosticHealthStatus
        {
            IsHealthy = false,
            PassedChecks = 2,
            TotalChecks = 3,
            Summary = "2/3"
        };
        _mockDiagnostics.Setup(d => d.QuickHealthCheckAsync())
            .ReturnsAsync(healthStatus);

        var vm = CreateVm();
        await vm.RunQuickCheckCommand.ExecuteAsync(null);

        Assert.Equal(2, vm.PassedChecks);
        Assert.Equal(3, vm.TotalChecks);
        Assert.Contains("2/3", vm.SummaryText);
    }

    [Fact]
    public async Task RunQuickCheck_Exception_SetsErrorSummary()
    {
        _mockDiagnostics.Setup(d => d.QuickHealthCheckAsync())
            .ThrowsAsync(new Exception("Service error"));

        var vm = CreateVm();
        await vm.RunQuickCheckCommand.ExecuteAsync(null);

        Assert.Contains("Ошибка", vm.SummaryText);
    }

    [Fact]
    public async Task RunQuickCheck_IsRunningFalse_AfterCompletion()
    {
        _mockDiagnostics.Setup(d => d.QuickHealthCheckAsync())
            .ReturnsAsync(new DiagnosticHealthStatus { IsHealthy = true, PassedChecks = 1, TotalChecks = 1, Summary = "OK" });

        var vm = CreateVm();
        await vm.RunQuickCheckCommand.ExecuteAsync(null);

        Assert.False(vm.IsRunning);
    }

    // ── ClearLogsCommand ────────────────────────────────────────

    [Fact]
    public async Task ClearLogs_ClearsLogLines()
    {
        var vm = CreateVm();
        // Run diagnostics to add some log lines
        _mockDiagnostics.Setup(d => d.CheckAdminRightsAsync())
            .ReturnsAsync(new DiagnosticResult { Name = "Admin", Success = true, Message = "OK" });
        _mockDiagnostics.Setup(d => d.CheckWorkerProcessAsync())
            .ReturnsAsync(new DiagnosticResult { Name = "Worker", Success = true, Message = "OK" });
        _mockDiagnostics.Setup(d => d.CheckWinDivertAsync())
            .ReturnsAsync(new DiagnosticResult { Name = "WinDivert", Success = true, Message = "OK" });
        _mockDiagnostics.Setup(d => d.CheckDomainListsAsync())
            .ReturnsAsync(new DiagnosticResult { Name = "Domain Lists", Success = true, Message = "OK" });
        _mockDiagnostics.Setup(d => d.CheckBinaryFilesAsync())
            .ReturnsAsync(new DiagnosticResult { Name = "Binary Files", Success = true, Message = "OK" });
        _mockDiagnostics.Setup(d => d.TestConnectivityAsync(It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(new DiagnosticResult { Name = "Network", Success = true, Message = "OK" });
        _mockDiagnostics.Setup(d => d.RunAllChecksAsync())
            .ReturnsAsync(new List<DiagnosticResult>());

        await vm.RunDiagnosticsCommand.ExecuteAsync(null);

        // After running, there should be log lines
        Assert.NotEmpty(vm.LogLines);

        vm.ClearLogsCommand.Execute(null);

        Assert.Empty(vm.LogLines);
    }

    // ── HasResults / CanExport ──────────────────────────────────

    [Fact]
    public void HasResults_False_WhenNoResults()
    {
        var vm = CreateVm();
        Assert.False(vm.HasResults);
        Assert.False(vm.CanExport);
    }

    // ── Dispose ─────────────────────────────────────────────────

    [Fact]
    public void Dispose_DoesNotThrow()
    {
        var vm = CreateVm();
        vm.Dispose();
    }
}
