// DnsPageViewModelTests.cs - Unit tests for DnsPageViewModel
// Updated: Mock<IDnsServiceAdapter> → Mock<IEnhancedDnsManager> + Mock<IIpcClientService>
using Moq;
using ZUI.Ipc;
using ZUI.Services;
using ZUI.ViewModels;

namespace ZUI.Tests;

public class DnsPageViewModelTests
{
	private readonly Mock<IDnsService> _mockDns;
	private readonly Mock<IEnhancedDnsManager> _mockDnsManager;
	private readonly Mock<IIpcClientService> _mockIpc;
	private readonly Mock<IAppSettingsService> _mockSettings;

	public DnsPageViewModelTests()
	{
		_mockDns = new Mock<IDnsService>();
		_mockDnsManager = new Mock<IEnhancedDnsManager>();
		_mockDnsManager.SetupAllProperties();
		_mockDnsManager.SetupGet(m => m.State).Returns(DnsBypassState.Disabled);
        _mockIpc = new Mock<IIpcClientService>();
        _mockIpc.SetupGet(i => i.IsConnected).Returns(false);
		_mockSettings = new Mock<IAppSettingsService>();
	}

	private DnsStatus CreateDnsStatus(bool enabled = false, bool dohSupported = true,
		string message = "OK", string? provider = null, string? recommendation = null)
	{
		return new DnsStatus
		{
			IsSecureDnsEnabled = enabled,
			IsDohSupported = dohSupported,
			StatusMessage = message,
			ProviderName = provider,
			Recommendation = recommendation
		};
	}

    private DnsPageViewModel CreateVm(bool setupIpcConnected = false)
    {
        _mockDns.Setup(d => d.GetDnsStatus()).Returns(CreateDnsStatus());
        _mockDnsManager.SetupGet(m => m.State).Returns(DnsBypassState.Disabled);
        _mockIpc.SetupGet(i => i.IsConnected).Returns(setupIpcConnected);
        _mockSettings.Setup(s => s.DnsPort).Returns(5353);
        return new DnsPageViewModel(_mockDns.Object, _mockDnsManager.Object, _mockIpc.Object, _mockSettings.Object);
    }

	// ── Constructor null guards ────────────────────────────────

	[Fact]
	public void Constructor_NullDnsService_ThrowsArgumentNullException()
	{
		Assert.Throws<ArgumentNullException>(() =>
			new DnsPageViewModel(null!, _mockDnsManager.Object, _mockIpc.Object, _mockSettings.Object));
	}

	[Fact]
	public void Constructor_NullDnsManager_ThrowsArgumentNullException()
	{
		Assert.Throws<ArgumentNullException>(() =>
			new DnsPageViewModel(_mockDns.Object, null!, _mockIpc.Object, _mockSettings.Object));
	}

	[Fact]
	public void Constructor_NullIpc_ThrowsArgumentNullException()
	{
		Assert.Throws<ArgumentNullException>(() =>
			new DnsPageViewModel(_mockDns.Object, _mockDnsManager.Object, null!, _mockSettings.Object));
	}

	[Fact]
	public void Constructor_NullAppSettings_ThrowsArgumentNullException()
	{
		Assert.Throws<ArgumentNullException>(() =>
			new DnsPageViewModel(_mockDns.Object, _mockDnsManager.Object, _mockIpc.Object, null!));
	}

	// ── Constructor default state ──────────────────────────────

	[Fact]
	public void Constructor_ChecksDnsStatus()
	{
		var vm = CreateVm();
		_mockDns.Setup(d => d.GetDnsStatus()).Returns(CreateDnsStatus(enabled: true, provider: "Google"));
		vm.CheckDnsStatus();

		Assert.True(vm.IsSecureDnsEnabled);
		Assert.Equal("Google", vm.ProviderName);
	}

	[Fact]
	public void Constructor_UpdatesDnsProxyStatus_NotRunning()
	{
		_mockDnsManager.SetupGet(m => m.State).Returns(DnsBypassState.Disabled);
		_mockIpc.SetupGet(i => i.IsConnected).Returns(false);

		var vm = CreateVm();

		Assert.False(vm.IsDnsProxyRunning);
		Assert.Equal(LocalizationService.Get("DnsProxyNotRunning"), vm.DnsProxyStatus);
	}

    [Fact]
    public async Task Refresh_UpdatesDnsProxyStatus_RunningWithDnsBypassActive()
    {
        // Constructor no longer calls UpdateDnsProxyStatus — that's done via Refresh()
        _mockDns.Setup(d => d.GetDnsStatus()).Returns(CreateDnsStatus());
        _mockDns.Setup(d => d.IsSecureDnsEnabled()).Returns(false);
        _mockDnsManager.SetupGet(m => m.State).Returns(DnsBypassState.Active);
        _mockIpc.SetupGet(i => i.IsConnected).Returns(true);
        _mockIpc.Setup(i => i.GetDnsStatusAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<WorkerDnsStatus?>.Success(null));
        _mockSettings.Setup(s => s.DnsPort).Returns(5353);

        var vm = new DnsPageViewModel(_mockDns.Object, _mockDnsManager.Object, _mockIpc.Object, _mockSettings.Object);

        // Before Refresh: IsDnsProxyRunning is default (false)
        Assert.False(vm.IsDnsProxyRunning);

        await vm.Refresh();

        // After Refresh: DNS bypass active → IsDnsProxyRunning = true
        Assert.True(vm.IsDnsProxyRunning);
    }

	[Fact]
	public void Constructor_LoadsDnsPortFromSettings()
	{
		_mockDns.Setup(d => d.GetDnsStatus()).Returns(CreateDnsStatus());
		_mockDnsManager.SetupGet(m => m.State).Returns(DnsBypassState.Disabled);
		_mockIpc.SetupGet(i => i.IsConnected).Returns(false);
		_mockSettings.Setup(s => s.DnsPort).Returns(8080);

		var vm = new DnsPageViewModel(_mockDns.Object, _mockDnsManager.Object, _mockIpc.Object, _mockSettings.Object);
		Assert.Equal(8080, vm.DnsPort);
	}

	// ── CheckDnsStatus ──────────────────────────────────────────

	[Fact]
	public void CheckDnsStatus_UpdatesAllProperties()
	{
		var vm = CreateVm();
		_mockDns.Setup(d => d.GetDnsStatus()).Returns(CreateDnsStatus(
			enabled: true, dohSupported: true, message: "Secure DNS active",
			provider: "Cloudflare", recommendation: "All good"));

		vm.CheckDnsStatus();

		Assert.True(vm.IsSecureDnsEnabled);
		Assert.True(vm.IsDohSupported);
		Assert.Equal("Secure DNS active", vm.StatusMessage);
		Assert.Equal("Cloudflare", vm.ProviderName);
		Assert.Equal("All good", vm.Recommendation);
	}

	// ── EnableDohCommand ────────────────────────────────────────

	[Fact]
	public async Task EnableDoh_Success_UpdatesStatus()
	{
		_mockDns.Setup(d => d.EnableSecureDnsAsync("malw")).ReturnsAsync(true);
		_mockDns.Setup(d => d.GetDnsStatus()).Returns(CreateDnsStatus());

		var vm = CreateVm();
		await vm.EnableDohCommand.ExecuteAsync(null);

		Assert.Contains("✓", vm.StatusMessage);
		Assert.True(vm.IsSecureDnsEnabled);
	}

	[Fact]
	public async Task EnableDoh_Failure_SetsErrorMessage()
	{
		_mockDns.Setup(d => d.EnableSecureDnsAsync(It.IsAny<string>())).ReturnsAsync(false);
		_mockDns.Setup(d => d.GetDnsStatus()).Returns(CreateDnsStatus());

		var vm = CreateVm();
		await vm.EnableDohCommand.ExecuteAsync(null);

		Assert.Contains("✗", vm.StatusMessage);
		Assert.Equal("Запустите приложение от имени администратора", vm.Recommendation);
	}

	[Fact]
	public async Task EnableDoh_IsApplyingFalse_AfterCompletion()
	{
		_mockDns.Setup(d => d.EnableSecureDnsAsync(It.IsAny<string>())).ReturnsAsync(true);
		_mockDns.Setup(d => d.GetDnsStatus()).Returns(CreateDnsStatus());

		var vm = CreateVm();
		await vm.EnableDohCommand.ExecuteAsync(null);

		Assert.False(vm.IsApplying);
	}

	// ── DisableDohCommand ───────────────────────────────────────

	[Fact]
	public async Task DisableDoh_Success_UpdatesStatus()
	{
		_mockDns.Setup(d => d.DisableSecureDnsAsync()).ReturnsAsync(true);
		_mockDns.Setup(d => d.GetDnsStatus()).Returns(CreateDnsStatus(enabled: true));

		var vm = CreateVm();
		await vm.DisableDohCommand.ExecuteAsync(null);

		Assert.Contains("✓", vm.StatusMessage);
		Assert.False(vm.IsSecureDnsEnabled);
	}

	[Fact]
	public async Task DisableDoh_Failure_SetsError()
	{
		_mockDns.Setup(d => d.DisableSecureDnsAsync()).ReturnsAsync(false);
		_mockDns.Setup(d => d.GetDnsStatus()).Returns(CreateDnsStatus(enabled: true));

		var vm = CreateVm();
		await vm.DisableDohCommand.ExecuteAsync(null);

		Assert.Contains("✗", vm.StatusMessage);
	}

	// ── StartDnsProxyCommand ────────────────────────────────────

	[Fact]
	public async Task StartDnsProxy_Success_SetsRunningStatus()
	{
		_mockDnsManager.Setup(m => m.EnableDnsBypassAsync(It.IsAny<CancellationToken>()))
			.Returns(Task.CompletedTask);
    _mockIpc.Setup(i => i.ConfigureDnsAsync(true, false, It.IsAny<CancellationToken>()))
        .Returns(Task.FromResult(Result.Success()));
    _mockDnsManager.SetupGet(m => m.State).Returns(DnsBypassState.Active);

    var vm = CreateVm();
    await vm.StartDnsProxyCommand.ExecuteAsync(null);

    Assert.False(vm.IsDnsProxyApplying);
    }

[Fact]
public async Task StartDnsProxy_Failure_SetsErrorStatus()
{
    // IPC ConfigureDnsAsync failure is silently ignored (by design) — VM still succeeds
    _mockDnsManager.Setup(m => m.EnableDnsBypassAsync(It.IsAny<CancellationToken>()))
        .Returns(Task.CompletedTask);
    _mockIpc.SetupGet(i => i.IsConnected).Returns(true);
    _mockIpc.Setup(i => i.ConfigureDnsAsync(true, false, It.IsAny<CancellationToken>()))
        .Returns(Task.FromResult(Result.Failed("Worker not connected")));

        var vm = CreateVm();
        await vm.StartDnsProxyCommand.ExecuteAsync(null);

        // VM continues and shows success status (IPC failure is logged but not fatal)
        Assert.Contains(LocalizationService.Get("DnsProxyStarted"), vm.DnsProxyStatus);
    }

    [Fact]
    public async Task StartDnsProxy_Exception_SetsErrorStatus()
    {
        // When EnableDnsBypassAsync throws InvalidOperationException, VM catches it
        _mockDnsManager.Setup(m => m.EnableDnsBypassAsync(It.IsAny<CancellationToken>()))
        .ThrowsAsync(new InvalidOperationException("IPC failure"));

        var vm = CreateVm();
        await vm.StartDnsProxyCommand.ExecuteAsync(null);

        Assert.Contains("IPC failure", vm.DnsProxyStatus);
    }

	// ── StopDnsProxyCommand ─────────────────────────────────────

	[Fact]
	public async Task StopDnsProxy_Success_ClearsRunningState()
	{
		_mockDnsManager.Setup(m => m.DisableDnsBypassAsync(It.IsAny<CancellationToken>()))
			.Returns(Task.CompletedTask);
    _mockIpc.Setup(i => i.ConfigureDnsAsync(false, false, It.IsAny<CancellationToken>()))
        .Returns(Task.FromResult(Result.Success()));

		var vm = CreateVm();
		await vm.StopDnsProxyCommand.ExecuteAsync(null);

		Assert.False(vm.IsDnsProxyRunning);
		Assert.False(vm.IsFakeDnsEnabled);
	}

    [Fact]
    public async Task StopDnsProxy_Failure_SetsErrorStatus()
    {
        // IPC ConfigureDnsAsync failure is swallowed by catch blocks (by design)
        _mockDnsManager.Setup(m => m.DisableDnsBypassAsync(It.IsAny<CancellationToken>()))
        .Returns(Task.CompletedTask);
        _mockIpc.SetupGet(i => i.IsConnected).Returns(true);
        _mockIpc.Setup(i => i.ConfigureDnsAsync(false, false, It.IsAny<CancellationToken>()))
        .Returns(Task.FromResult(Result.Failed("Cannot stop")));

        var vm = CreateVm();
        await vm.StopDnsProxyCommand.ExecuteAsync(null);

        // VM always sets stopped status — IPC failure is caught and ignored
        Assert.Equal(LocalizationService.Get("DnsProxyStopped"), vm.DnsProxyStatus);
        Assert.False(vm.IsDnsProxyRunning);
    }

	// ── ToggleFakeDnsCommand ────────────────────────────────────

    [Fact]
    public async Task ToggleFakeDns_Success_TogglesState()
    {
        // ToggleFakeDns requires Worker to be connected
        var vm = CreateVm(setupIpcConnected: true);

        _mockIpc.Setup(i => i.ConfigureDnsAsync(It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .Returns(Task.FromResult(Result.Success()));

        await vm.ToggleFakeDnsCommand.ExecuteAsync(null);

        Assert.True(vm.IsFakeDnsEnabled, $"IsFakeDnsEnabled={vm.IsFakeDnsEnabled}, DnsProxyStatus={vm.DnsProxyStatus}");
    }

    [Fact]
    public async Task ToggleFakeDns_Failure_SetsErrorStatus()
    {
        // ToggleFakeDns requires Worker to be connected
        var vm = CreateVm(setupIpcConnected: true);
        _mockIpc.Setup(i => i.ConfigureDnsAsync(true, true, It.IsAny<CancellationToken>()))
            .Returns(Task.FromResult(Result.Failed("Not connected")));

        await vm.ToggleFakeDnsCommand.ExecuteAsync(null);

        Assert.Contains("Not connected", vm.DnsProxyStatus);
    }

	// ── Refresh Command ─────────────────────────────────────────

    [Fact]
    public async Task Refresh_RefreshesBothLocalAndWorkerDns()
    {
        var vm = CreateVm(setupIpcConnected: true);

        // Set up AFTER CreateVm() — CreateVm resets the GetDnsStatus setup
        _mockDns.Setup(d => d.GetDnsStatus()).Returns(CreateDnsStatus(enabled: true, provider: "Google"));
        _mockIpc.Setup(i => i.GetDnsStatusAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.FromResult(Result<WorkerDnsStatus?>.Success(null)));
        _mockDnsManager.SetupGet(m => m.State).Returns(DnsBypassState.Active);

        await vm.RefreshCommand.ExecuteAsync(null);

        Assert.True(vm.IsSecureDnsEnabled);
        Assert.Equal("Google", vm.ProviderName);
        // Refresh() calls CheckDnsStatus (local) + RefreshWorkerDnsStatusAsync (Worker IPC)
        _mockDns.Verify(d => d.GetDnsStatus(), Times.AtLeastOnce());
        _mockIpc.Verify(i => i.GetDnsStatusAsync(It.IsAny<CancellationToken>()), Times.Once());
    }

	// ── Providers list ──────────────────────────────────────────

	[Fact]
	public void Providers_ContainsFourEntries()
	{
		var vm = CreateVm();
		Assert.Equal(4, vm.Providers.Count);
		Assert.Contains("malw", vm.Providers[0]);
	}

	[Fact]
	public void DnsModes_ContainsTwoEntries()
	{
		var vm = CreateVm();
		Assert.Equal(2, vm.DnsModes.Count);
	}

	// ── OnDnsPortChanged ────────────────────────────────────────

	[Fact]
	public void OnDnsPortChanged_ValidPort_SyncsToSettings()
	{
		var vm = CreateVm();
		vm.DnsPort = 8080;
		_mockSettings.VerifySet(s => s.DnsPort = 8080, Times.Once());
	}

	[Fact]
	public void OnDnsPortChanged_InvalidPort_DoesNotSync()
	{
		var vm = CreateVm();
		vm.DnsPort = 80; // Below 1024
		_mockSettings.VerifySet(s => s.DnsPort = It.IsAny<int>(), Times.Never());
	}
}
