// GeneratorViewModelTests.cs - Unit tests for GeneratorViewModel
using System.Collections.ObjectModel;
using Moq;
using ZUI.Models;
using ZUI.Services;
using ZUI.ViewModels;

namespace ZUI.Tests;

public class GeneratorViewModelTests
{
	private readonly Mock<IStrategyGeneratorService> _mockGenerator;
	private readonly Mock<IStrategyManager> _mockStrategyManager;
	private readonly Mock<IAdaptiveEngine> _mockAdaptiveEngine;

	public GeneratorViewModelTests()
	{
		_mockGenerator = new Mock<IStrategyGeneratorService>();
		_mockStrategyManager = new Mock<IStrategyManager>();
		_mockAdaptiveEngine = new Mock<IAdaptiveEngine>();
		_mockAdaptiveEngine.SetupAllProperties();
		_mockAdaptiveEngine.SetupGet(e => e.IsProtected).Returns(false);
	}

	private List<StrategyInfo> CreateTestStrategies()
	{
		return
		[
			StrategyInfo.CreateProgrammatic("general", "General", "Общая стратегия"),
			StrategyInfo.CreateProgrammatic("discord", "Discord", "Discord стратегии"),
		];
	}

	private GeneratorViewModel CreateVm()
	{
		_mockStrategyManager.Setup(m => m.GetAvailableStrategies()).Returns(CreateTestStrategies());
		_mockStrategyManager.Setup(m => m.GetCurrentStrategy()).Returns((StrategyInfo?)null);
		_mockStrategyManager.Setup(m => m.GetActiveStrategyId()).Returns("auto");
		_mockAdaptiveEngine.SetupGet(e => e.IsProtected).Returns(false);

		return new GeneratorViewModel(
			_mockGenerator.Object,
			_mockStrategyManager.Object,
			_mockAdaptiveEngine.Object);
	}

	// ── Constructor null guards ─────────────────────────────────

	[Fact]
	public void Constructor_NullGeneratorService_Throws()
	{
		Assert.Throws<ArgumentNullException>(() =>
			new GeneratorViewModel(null!, _mockStrategyManager.Object, _mockAdaptiveEngine.Object));
	}

	[Fact]
	public void Constructor_NullStrategyManager_Throws()
	{
		Assert.Throws<ArgumentNullException>(() =>
			new GeneratorViewModel(_mockGenerator.Object, null!, _mockAdaptiveEngine.Object));
	}

	[Fact]
	public void Constructor_NullAdaptiveEngine_Throws()
	{
		Assert.Throws<ArgumentNullException>(() =>
			new GeneratorViewModel(_mockGenerator.Object, _mockStrategyManager.Object, null!));
	}

	// ── Constructor default state ───────────────────────────────

	[Fact]
	public void Constructor_LoadsStrategiesWithAuto()
	{
		var vm = CreateVm();

		// 2 from manager + 1 "auto" = 3
		Assert.Equal(3, vm.AvailableStrategies.Count);
		// GeneratorViewModel uses CreateJson which prefixes with "json-"
		Assert.Equal("json-auto", vm.AvailableStrategies[0].Id);
	}

	[Fact]
	public void Constructor_SelectsAutoByDefault()
	{
		var vm = CreateVm();

		Assert.NotNull(vm.SelectedStrategy);
		Assert.Equal("json-auto", vm.SelectedStrategy!.Id);
		Assert.Equal("Auto (рекомендуется)", vm.CurrentStrategyName);
	}

	[Fact]
	public void Constructor_EmptyStrategies_StillAddsAuto()
	{
		_mockStrategyManager.Setup(m => m.GetAvailableStrategies()).Returns([]);
		_mockStrategyManager.Setup(m => m.GetCurrentStrategy()).Returns((StrategyInfo?)null);
		_mockAdaptiveEngine.SetupGet(e => e.IsProtected).Returns(false);

		var vm = new GeneratorViewModel(
			_mockGenerator.Object, _mockStrategyManager.Object, _mockAdaptiveEngine.Object);

		Assert.Single(vm.AvailableStrategies);
		Assert.Equal("json-auto", vm.AvailableStrategies[0].Id);
	}

	// ── SelectedStrategyChanged ─────────────────────────────────

	[Fact]
	public void SelectedStrategyChanged_UpdatesCurrentStrategyName()
	{
		var vm = CreateVm();
		vm.SelectedStrategy = vm.AvailableStrategies.First(s => s.Id == "general");

		Assert.Equal("General", vm.CurrentStrategyName);
	}

	// ── RefreshStrategiesCommand ────────────────────────────────

	[Fact]
	public async Task RefreshStrategies_ReloadsFromManager()
	{
		_mockStrategyManager.Setup(m => m.ReloadStrategiesAsync())
			.Returns(Task.CompletedTask);

		var vm = CreateVm();
		await vm.RefreshStrategiesCommand.ExecuteAsync(null);

		_mockStrategyManager.Verify(m => m.ReloadStrategiesAsync(), Times.Once());
		_mockStrategyManager.Verify(m => m.GetAvailableStrategies(), Times.AtLeast(2));
	}

	// ── TestStrategyCommand (Tab 1) ─────────────────────────────

	[Fact]
	public async Task TestStrategy_WhenProtected_StopsFirst()
	{
		_mockAdaptiveEngine.SetupGet(e => e.IsProtected).Returns(true);
		_mockAdaptiveEngine.Setup(e => e.StopAsync(It.IsAny<CancellationToken>()))
			.ReturnsAsync(ProtectionResult.Succeeded("auto"));
		_mockAdaptiveEngine.Setup(e => e.StartWithStrategyAsync("auto", It.IsAny<int>(), It.IsAny<CancellationToken>()))
			.ReturnsAsync(ProtectionResult.Succeeded("auto"));
		_mockStrategyManager.Setup(m => m.GetActiveStrategyId()).Returns("auto");

		var vm = CreateVm();
		await vm.TestStrategyCommand.ExecuteAsync(null);

		_mockAdaptiveEngine.Verify(e => e.StopAsync(It.IsAny<CancellationToken>()), Times.AtLeastOnce());
	}

	[Fact]
	public async Task TestStrategy_Success_SetsResult()
	{
		_mockAdaptiveEngine.SetupGet(e => e.IsProtected).Returns(false);
		_mockAdaptiveEngine.Setup(e => e.StartWithStrategyAsync("auto", It.IsAny<int>(), It.IsAny<CancellationToken>()))
			.ReturnsAsync(ProtectionResult.Succeeded("auto"));
		_mockAdaptiveEngine.Setup(e => e.StopAsync(It.IsAny<CancellationToken>()))
			.ReturnsAsync(ProtectionResult.Succeeded("auto"));
		_mockStrategyManager.Setup(m => m.GetActiveStrategyId()).Returns("auto");

		var vm = CreateVm();
		await vm.TestStrategyCommand.ExecuteAsync(null);

		Assert.Contains("✓", vm.TestResult);
	}

	[Fact]
	public async Task TestStrategy_Failure_SetsErrorResult()
	{
		_mockAdaptiveEngine.SetupGet(e => e.IsProtected).Returns(false);
		_mockAdaptiveEngine.Setup(e => e.StartWithStrategyAsync("auto", It.IsAny<int>(), It.IsAny<CancellationToken>()))
			.ReturnsAsync(ProtectionResult.Failed("Timeout"));
		_mockAdaptiveEngine.Setup(e => e.StopAsync(It.IsAny<CancellationToken>()))
			.ReturnsAsync(ProtectionResult.Succeeded("auto"));
		_mockStrategyManager.Setup(m => m.GetActiveStrategyId()).Returns("auto");

		var vm = CreateVm();
		await vm.TestStrategyCommand.ExecuteAsync(null);

		Assert.Contains("✗", vm.TestResult);
		Assert.Contains("Timeout", vm.TestResult);
	}

	[Fact]
	public async Task TestStrategy_NoSelectedStrategy_DoesNothing()
	{
		var vm = CreateVm();
		vm.SelectedStrategy = null;

		await vm.TestStrategyCommand.ExecuteAsync(null);

		_mockAdaptiveEngine.Verify(e => e.StartWithStrategyAsync(
			It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never());
	}

	[Fact]
	public async Task TestStrategy_IsTestingFalse_AfterCompletion()
	{
		_mockAdaptiveEngine.SetupGet(e => e.IsProtected).Returns(false);
		_mockAdaptiveEngine.Setup(e => e.StartWithStrategyAsync("auto", It.IsAny<int>(), It.IsAny<CancellationToken>()))
			.ReturnsAsync(ProtectionResult.Succeeded("auto"));
		_mockAdaptiveEngine.Setup(e => e.StopAsync(It.IsAny<CancellationToken>()))
			.ReturnsAsync(ProtectionResult.Succeeded("auto"));
		_mockStrategyManager.Setup(m => m.GetActiveStrategyId()).Returns("auto");

		var vm = CreateVm();
		await vm.TestStrategyCommand.ExecuteAsync(null);

		Assert.False(vm.IsTesting);
	}

	// ── InitializeAsync (Tab 2) ─────────────────────────────────

	[Fact]
	public async Task InitializeAsync_LoadsServicesAndDetectsProvider()
	{
		var services = new Dictionary<string, ServiceConfig>
		{
			["youtube"] = new() { Id = "youtube", Name = "YouTube", Enabled = true, Domains = ["youtube.com"], TestUrl = "https://youtube.com" },
			["discord"] = new() { Id = "discord", Name = "Discord", Enabled = true, Domains = ["discord.com"], TestUrl = "https://discord.com" },
		};
		var paramsConfig = new StrategyParamsConfig { Services = services };
		var ispProfile = new IspProfile { Id = "rostelecom", Name = "Ростелеком", Method = "fake", Confidence = 90 };
		var profilesConfig = new IspProfilesConfig { Profiles = new Dictionary<string, IspProfile> { ["rostelecom"] = ispProfile } };

		_mockGenerator.Setup(g => g.LoadParametersAsync()).ReturnsAsync(paramsConfig);
		_mockGenerator.Setup(g => g.LoadIspProfilesAsync()).ReturnsAsync(profilesConfig);
		_mockGenerator.Setup(g => g.DetectIspAsync()).ReturnsAsync(ispProfile);

		var vm = CreateVm();
		await vm.InitializeAsync();

		Assert.Equal(2, vm.AvailableServices.Count);
		Assert.Equal("Ростелеком", vm.DetectedProviderName);
		Assert.Contains("fake", vm.DetectedProviderInfo);
	}

	[Fact]
	public async Task InitializeAsync_SkipIfAlreadyInitialized()
	{
		var paramsConfig = new StrategyParamsConfig();
		_mockGenerator.Setup(g => g.LoadParametersAsync()).ReturnsAsync(paramsConfig);
		_mockGenerator.Setup(g => g.LoadIspProfilesAsync()).ReturnsAsync(new IspProfilesConfig());
		_mockGenerator.Setup(g => g.DetectIspAsync()).ReturnsAsync(new IspProfile { Id = "test", Name = "Test" });

		var vm = CreateVm();
		await vm.InitializeAsync();
		await vm.InitializeAsync(); // Second call should be skipped

		_mockGenerator.Verify(g => g.LoadParametersAsync(), Times.Once());
	}

	[Fact]
	public async Task InitializeAsync_DetectError_SetsErrorMessage()
	{
		_mockGenerator.Setup(g => g.LoadParametersAsync()).ReturnsAsync(new StrategyParamsConfig());
		_mockGenerator.Setup(g => g.LoadIspProfilesAsync()).ReturnsAsync(new IspProfilesConfig());
		// DetectProviderAsync catches InvalidOperationException, IOException, TimeoutException
		// Plain Exception is NOT caught and would propagate to InitializeAsync which also
		// catches those same three types. Use InvalidOperationException to match the catch blocks.
		_mockGenerator.Setup(g => g.DetectIspAsync()).ThrowsAsync(new InvalidOperationException("Network error"));

		var vm = CreateVm();
		await vm.InitializeAsync();

		// DetectProviderAsync catch block sets "Ошибка" for caught exceptions
		Assert.Equal("Ошибка", vm.DetectedProviderName);
	}

	[Fact]
	public async Task InitializeAsync_NoProfile_SetsNotDetected()
	{
		_mockGenerator.Setup(g => g.LoadParametersAsync()).ReturnsAsync(new StrategyParamsConfig());
		_mockGenerator.Setup(g => g.LoadIspProfilesAsync()).ReturnsAsync(new IspProfilesConfig());
		_mockGenerator.Setup(g => g.DetectIspAsync()).ReturnsAsync((IspProfile)null!);

		var vm = CreateVm();
		await vm.InitializeAsync();

		Assert.Equal("Не определён", vm.DetectedProviderName);
	}

	// ── CustomDomains ───────────────────────────────────────────

	[Fact]
	public void AddCustomDomain_AddsDomain()
	{
		var vm = CreateVm();
		vm.AddCustomDomain("example.com");

		Assert.Contains("example.com", vm.CustomDomains);
		Assert.True(vm.HasCustomDomains);
	}

	[Fact]
	public void AddCustomDomain_TrimmedAndLowercased()
	{
		var vm = CreateVm();
		vm.AddCustomDomain(" EXAMPLE.COM ");

		Assert.Contains("example.com", vm.CustomDomains);
	}

	[Fact]
	public void AddCustomDomain_IgnoresEmpty()
	{
		var vm = CreateVm();
		vm.AddCustomDomain("");
		vm.AddCustomDomain(" ");

		Assert.Empty(vm.CustomDomains);
		Assert.False(vm.HasCustomDomains);
	}

	[Fact]
	public void AddCustomDomain_IgnoresDuplicate()
	{
		var vm = CreateVm();
		vm.AddCustomDomain("example.com");
		vm.AddCustomDomain("example.com");

		Assert.Single(vm.CustomDomains);
	}

	[Fact]
	public void RemoveCustomDomain_RemovesDomain()
	{
		var vm = CreateVm();
		vm.AddCustomDomain("example.com");
		vm.RemoveCustomDomain("example.com");

		Assert.Empty(vm.CustomDomains);
		Assert.False(vm.HasCustomDomains);
	}

	// ── CanRunTest / CanApply ───────────────────────────────────

	[Fact]
	public void CanRunTest_False_WhenNoServicesSelected()
	{
		var vm = CreateVm();
		vm.SelectedServices = new ObservableCollection<object>();

		Assert.False(vm.CanRunTest);
	}

	[Fact]
	public void CanApply_False_WhenNoGeneratedStrategy()
	{
		var vm = CreateVm();
		Assert.False(vm.CanApply);
	}

	// ── ServiceTestResultDisplay ────────────────────────────────

	[Fact]
	public void ServiceTestResultDisplay_ServiceName_MapsKnownIds()
	{
		var display = new ServiceTestResultDisplay("youtube", true, 50);
		Assert.Equal("YouTube", display.ServiceName);
	}

	[Fact]
	public void ServiceTestResultDisplay_ServiceName_UnknownId_ReturnsId()
	{
		var display = new ServiceTestResultDisplay("unknown-service", true, null);
		Assert.Equal("unknown-service", display.ServiceName);
	}

	[Fact]
	public void ServiceTestResultDisplay_ServiceName_CustomPrefix()
	{
		var display = new ServiceTestResultDisplay("custom:MyApp", true, null);
		Assert.Equal("MyApp", display.ServiceName);
	}

	[Fact]
	public void ServiceTestResultDisplay_LatencyText_NullPassed()
	{
		var display = new ServiceTestResultDisplay("test", true, null);
		Assert.Equal("OK", display.LatencyText);
	}

	[Fact]
	public void ServiceTestResultDisplay_LatencyText_NullFailed()
	{
		var display = new ServiceTestResultDisplay("test", false, null);
		Assert.Equal("—", display.LatencyText);
	}

	[Fact]
	public void ServiceTestResultDisplay_LatencyText_Under100ms()
	{
		var display = new ServiceTestResultDisplay("test", true, 50);
		Assert.Equal("50ms", display.LatencyText);
	}

	[Fact]
	public void ServiceTestResultDisplay_LatencyText_Over1s()
	{
		var display = new ServiceTestResultDisplay("test", true, 2500);
		// F1 format uses current culture decimal separator (',' in Russian locale)
		Assert.True(display.LatencyText is "2.5s" or "2,5s");
	}

	[Theory]
	[InlineData("youtube", "YouTube")]
	[InlineData("discord", "Discord")]
	[InlineData("telegram", "Telegram")]
	[InlineData("whatsapp", "WhatsApp")]
	[InlineData("instagram", "Instagram")]
	[InlineData("twitter", "Twitter/X")]
	[InlineData("facebook", "Facebook")]
	[InlineData("tiktok", "TikTok")]
	[InlineData("poe2", "Path of Exile 2")]
	[InlineData("steam", "Steam")]
	[InlineData("twitch", "Twitch")]
	public void ServiceTestResultDisplay_ServiceName_AllMappings(string id, string expectedName)
	{
		var display = new ServiceTestResultDisplay(id, true, null);
		Assert.Equal(expectedName, display.ServiceName);
	}
}
