// SettingsViewModelTests.cs - Unit tests for SettingsViewModel
// Note: MalwLinkUpdateService is a concrete class (not interface-mocked), so update commands
// require a real instance. We test settings sync, property change handlers, and constructor logic.
using System.Collections.ObjectModel;
using Moq;
using ZUI.Models;
using ZUI.Services;
using ZUI.ViewModels;
using Microsoft.Extensions.Logging;

namespace ZUI.Tests;

public class SettingsViewModelTests
{
    private readonly Mock<IAppSettingsService> _mockSettings;
    private readonly Mock<IStrategyManager> _mockStrategyManager;
    private readonly Mock<ILogger<MalwLinkUpdateService>> _mockLogger;

    public SettingsViewModelTests()
    {
        _mockSettings = new Mock<IAppSettingsService>();
        _mockStrategyManager = new Mock<IStrategyManager>();
        _mockLogger = new Mock<ILogger<MalwLinkUpdateService>>();
    }

    private void SetupDefaultSettings()
    {
        _mockSettings.Setup(s => s.AutoProtect).Returns(false);
        _mockSettings.Setup(s => s.DefaultStrategy).Returns("auto");
        _mockSettings.Setup(s => s.RunAsAdmin).Returns(false);
        _mockSettings.Setup(s => s.DefaultDnsMode).Returns("Proxy");
        _mockSettings.Setup(s => s.DnsPort).Returns(5353);
        _mockSettings.Setup(s => s.NotificationsEnabled).Returns(true);
        _mockSettings.Setup(s => s.NotifyOnStart).Returns(true);
        _mockSettings.Setup(s => s.NotifyOnStop).Returns(false);
        _mockSettings.Setup(s => s.NotifyOnErrors).Returns(true);
        _mockSettings.Setup(s => s.AppTheme).Returns("Default");
        _mockSettings.Setup(s => s.AnimationsEnabled).Returns(true);
        _mockSettings.Setup(s => s.MinimizeToTray).Returns(true);
        _mockSettings.Setup(s => s.StartInTray).Returns(false);
        _mockSettings.Setup(s => s.ShowTrayIcon).Returns(true);
        _mockSettings.Setup(s => s.SoundEffects).Returns(true);
        _mockSettings.Setup(s => s.LogLevel).Returns("Information");
        _mockSettings.Setup(s => s.StartMinimized).Returns(false);
        _mockSettings.Setup(s => s.AutoUpdate).Returns(true);
        _mockSettings.Setup(s => s.CheckUpdatesOnStart).Returns(false);
        _mockSettings.Setup(s => s.Autostart).Returns(false);

        _mockStrategyManager.Setup(m => m.GetAvailableStrategies())
            .Returns(new List<StrategyInfo>
            {
                StrategyInfo.CreateProgrammatic("general", "General", "desc"),
            });
    }

    private SettingsViewModel CreateVm()
    {
        SetupDefaultSettings();
        var updateService = new MalwLinkUpdateService(_mockLogger.Object);
        return new SettingsViewModel(_mockSettings.Object, _mockStrategyManager.Object, updateService);
    }

    // ── Constructor null guards ─────────────────────────────────

    [Fact]
    public void Constructor_NullSettings_Throws()
    {
        var updateService = new MalwLinkUpdateService(_mockLogger.Object);
        Assert.Throws<ArgumentNullException>(() =>
            new SettingsViewModel(null!, _mockStrategyManager.Object, updateService));
    }

    [Fact]
    public void Constructor_NullStrategyManager_Throws()
    {
        var updateService = new MalwLinkUpdateService(_mockLogger.Object);
        Assert.Throws<ArgumentNullException>(() =>
            new SettingsViewModel(_mockSettings.Object, null!, updateService));
    }

    [Fact]
    public void Constructor_NullUpdateService_Throws()
    {
        SetupDefaultSettings();
        Assert.Throws<ArgumentNullException>(() =>
            new SettingsViewModel(_mockSettings.Object, _mockStrategyManager.Object, null!));
    }

    // ── LoadFromSettings ────────────────────────────────────────

    [Fact]
    public void Constructor_LoadsAutoProtectFromSettings()
    {
        // Override AFTER SetupDefaultSettings() which is called by CreateVm()
        _mockSettings.Setup(s => s.AutoProtect).Returns(true);
        var updateService = new MalwLinkUpdateService(_mockLogger.Object);
        // Don't use CreateVm() since it resets the mock
        _mockStrategyManager.Setup(m => m.GetAvailableStrategies())
            .Returns(new List<StrategyInfo> { StrategyInfo.CreateProgrammatic("general", "General", "desc") });
        var vm = new SettingsViewModel(_mockSettings.Object, _mockStrategyManager.Object, updateService);
        Assert.True(vm.AutoProtect);
    }

    [Fact]
    public void Constructor_LoadsDnsMode_ProxyDefault()
    {
        _mockSettings.Setup(s => s.DefaultDnsMode).Returns("Proxy");
        var vm = CreateVm();
        Assert.Equal(0, vm.SelectedDnsModeIndex);
    }

    [Fact]
    public void Constructor_LoadsDnsMode_Doh()
    {
        SetupDefaultSettings();
        _mockSettings.Setup(s => s.DefaultDnsMode).Returns("DoH");
        var updateService = new MalwLinkUpdateService(_mockLogger.Object);
        var vm = new SettingsViewModel(_mockSettings.Object, _mockStrategyManager.Object, updateService);
        Assert.Equal(1, vm.SelectedDnsModeIndex);
    }

    [Fact]
    public void Constructor_LoadsDnsMode_None()
    {
        SetupDefaultSettings();
        _mockSettings.Setup(s => s.DefaultDnsMode).Returns("None");
        var updateService = new MalwLinkUpdateService(_mockLogger.Object);
        var vm = new SettingsViewModel(_mockSettings.Object, _mockStrategyManager.Object, updateService);
        Assert.Equal(2, vm.SelectedDnsModeIndex);
    }

    [Fact]
    public void Constructor_LoadsTheme_Light()
    {
        SetupDefaultSettings();
        _mockSettings.Setup(s => s.AppTheme).Returns("Light");
        var updateService = new MalwLinkUpdateService(_mockLogger.Object);
        var vm = new SettingsViewModel(_mockSettings.Object, _mockStrategyManager.Object, updateService);
        Assert.Equal(0, vm.SelectedThemeIndex);
    }

    [Fact]
    public void Constructor_LoadsTheme_Dark()
    {
        SetupDefaultSettings();
        _mockSettings.Setup(s => s.AppTheme).Returns("Dark");
        var updateService = new MalwLinkUpdateService(_mockLogger.Object);
        var vm = new SettingsViewModel(_mockSettings.Object, _mockStrategyManager.Object, updateService);
        Assert.Equal(1, vm.SelectedThemeIndex);
    }

    [Fact]
    public void Constructor_LoadsTheme_Default()
    {
        var vm = CreateVm();
        Assert.Equal(2, vm.SelectedThemeIndex);
    }

    [Fact]
    public void Constructor_LoadsLogLevel_Debug()
    {
        SetupDefaultSettings();
        _mockSettings.Setup(s => s.LogLevel).Returns("Debug");
        var updateService = new MalwLinkUpdateService(_mockLogger.Object);
        var vm = new SettingsViewModel(_mockSettings.Object, _mockStrategyManager.Object, updateService);
        Assert.Equal(0, vm.SelectedLogLevelIndex);
    }

    [Fact]
    public void Constructor_LoadsLogLevel_Error()
    {
        SetupDefaultSettings();
        _mockSettings.Setup(s => s.LogLevel).Returns("Error");
        var updateService = new MalwLinkUpdateService(_mockLogger.Object);
        var vm = new SettingsViewModel(_mockSettings.Object, _mockStrategyManager.Object, updateService);
        Assert.Equal(2, vm.SelectedLogLevelIndex);
    }

    [Fact]
    public void Constructor_LoadsDnsPort()
    {
        _mockSettings.Setup(s => s.DnsPort).Returns(5353);
        var vm = CreateVm();
        Assert.Equal(5353, vm.DnsPort);
    }

    // ── LoadStrategies ──────────────────────────────────────────

    [Fact]
    public void Constructor_LoadsStrategiesWithAuto()
    {
        var vm = CreateVm();
        // 1 from manager + 1 auto = 2
        Assert.Equal(2, vm.AvailableStrategies.Count);
        Assert.Equal("auto", vm.AvailableStrategies[0].Id);
    }

    // ── Property change handlers (sync to IAppSettingsService) ──

    [Fact]
    public void OnAutoProtectChanged_SyncsToSettings()
    {
        var vm = CreateVm();
        vm.AutoProtect = true;
        _mockSettings.VerifySet(s => s.AutoProtect = true, Times.Once());
    }

    [Fact]
    public void OnRunAsAdminChanged_SyncsToSettings()
    {
        var vm = CreateVm();
        vm.RunAsAdmin = true;
        _mockSettings.VerifySet(s => s.RunAsAdmin = true, Times.Once());
    }

    [Fact]
    public void OnSelectedDnsModeIndexChanged_SyncsToSettings()
    {
        var vm = CreateVm();
        vm.SelectedDnsModeIndex = 1; // DoH
        _mockSettings.VerifySet(s => s.DefaultDnsMode = "DoH", Times.Once());
    }

    [Fact]
    public void OnSelectedDnsModeIndexChanged_None()
    {
        var vm = CreateVm();
        vm.SelectedDnsModeIndex = 2;
        _mockSettings.VerifySet(s => s.DefaultDnsMode = "None", Times.Once());
    }

    [Fact]
    public void OnSelectedDnsModeIndexChanged_Proxy()
    {
        var vm = CreateVm();
        // First change to something else so 0 triggers a change
        vm.SelectedDnsModeIndex = 1; // DoH
        _mockSettings.Invocations.Clear();
        vm.SelectedDnsModeIndex = 0; // Proxy
        _mockSettings.VerifySet(s => s.DefaultDnsMode = "Proxy", Times.Once());
    }

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

    [Fact]
    public void OnNotificationsEnabledChanged_SyncsToSettings()
    {
        var vm = CreateVm();
        vm.NotificationsEnabled = false;
        _mockSettings.VerifySet(s => s.NotificationsEnabled = false, Times.Once());
    }

    [Fact]
    public void OnSelectedThemeIndexChanged_SyncsToSettings()
    {
        var vm = CreateVm();
        bool themeFired = false;
        vm.ThemeChangeRequested += theme => themeFired = true;

        vm.SelectedThemeIndex = 0; // Light

        _mockSettings.VerifySet(s => s.AppTheme = "Light", Times.Once());
        Assert.True(themeFired);
    }

    [Fact]
    public void OnAnimationsEnabledChanged_SyncsToSettings()
    {
        var vm = CreateVm();
        vm.AnimationsEnabled = false;
        _mockSettings.VerifySet(s => s.AnimationsEnabled = false, Times.Once());
    }

    [Fact]
    public void OnMinimizeToTrayChanged_SyncsToSettings()
    {
        var vm = CreateVm();
        vm.MinimizeToTray = false;
        _mockSettings.VerifySet(s => s.MinimizeToTray = false, Times.Once());
    }

    [Fact]
    public void OnSoundEffectsChanged_SyncsToSettings()
    {
        var vm = CreateVm();
        vm.SoundEffects = false;
        _mockSettings.VerifySet(s => s.SoundEffects = false, Times.Once());
    }

    [Fact]
    public void OnSelectedLogLevelIndexChanged_SyncsToSettings()
    {
        var vm = CreateVm();
        vm.SelectedLogLevelIndex = 0; // Debug
        _mockSettings.VerifySet(s => s.LogLevel = "Debug", Times.Once());
    }

    [Fact]
    public void OnAutoUpdateChanged_SyncsToSettings()
    {
        var vm = CreateVm();
        vm.AutoUpdate = false;
        _mockSettings.VerifySet(s => s.AutoUpdate = false, Times.Once());
    }

    [Fact]
    public void OnCheckUpdatesOnStartChanged_SyncsToSettings()
    {
        var vm = CreateVm();
        vm.CheckUpdatesOnStart = true;
        _mockSettings.VerifySet(s => s.CheckUpdatesOnStart = true, Times.Once());
    }

    // ── ResetDnsPortCommand ─────────────────────────────────────

    [Fact]
    public void ResetDnsPort_SetsTo5353()
    {
        var vm = CreateVm();
        vm.DnsPort = 8080;
        vm.ResetDnsPortCommand.Execute(null);
        Assert.Equal(5353, vm.DnsPort);
    }

    // ── ResetSettingsCommand ────────────────────────────────────

    [Fact]
    public async Task ResetSettings_WithoutDialogConfirmed_DoesNothing()
    {
        var vm = CreateVm();
        // DialogRequested is null → confirmed = false → should not reset
        await vm.ResetSettingsCommand.ExecuteAsync(null);

        _mockSettings.Verify(s => s.SetSetting(It.IsAny<string>(), It.IsAny<object>()), Times.Never());
    }

    [Fact]
    public async Task ResetSettings_WithDialogDenied_DoesNothing()
    {
        var vm = CreateVm();
        vm.DialogRequested += (_, _, _, _) => Task.FromResult(false);

        await vm.ResetSettingsCommand.ExecuteAsync(null);

        _mockSettings.Verify(s => s.SetSetting(It.IsAny<string>(), It.IsAny<object>()), Times.Never());
    }

    [Fact]
    public async Task ResetSettings_WithDialogConfirmed_AppliesDefaults()
    {
        var vm = CreateVm();
        vm.DialogRequested += (_, _, _, _) => Task.FromResult(true);

        await vm.ResetSettingsCommand.ExecuteAsync(null);

        _mockSettings.Verify(s => s.SetSetting("AutoProtect", false), Times.Once());
        _mockSettings.Verify(s => s.SetSetting("DefaultStrategy", "auto"), Times.Once());
        _mockSettings.Verify(s => s.SetSetting("DefaultDnsMode", "Proxy"), Times.Once());
        _mockSettings.Verify(s => s.SetSetting("DnsPort", 5353), Times.Once());
    }

    // ── ThemeChangeRequested event ──────────────────────────────

    [Fact]
    public void OnSelectedThemeIndexChanged_FiresThemeChangeRequested()
    {
        var vm = CreateVm();
        Microsoft.UI.Xaml.ElementTheme? requestedTheme = null;
        vm.ThemeChangeRequested += theme => requestedTheme = theme;

        vm.SelectedThemeIndex = 1; // Dark

        Assert.Equal(Microsoft.UI.Xaml.ElementTheme.Dark, requestedTheme);
    }

    // ── SettingChanged event handler ────────────────────────────

    [Fact]
    public void OnExternalSettingChanged_AutoProtect_UpdatesProperty()
    {
        var vm = CreateVm();
        // After CreateVm, override the mock for the event handler to read
        _mockSettings.Setup(s => s.AutoProtect).Returns(true);

        _mockSettings.Raise(s => s.SettingChanged += null, "AutoProtect", true);

        Assert.True(vm.AutoProtect);
    }

    [Fact]
    public void OnExternalSettingChanged_AppTheme_UpdatesSelectedThemeIndex()
    {
        var vm = CreateVm();
        // Override AFTER CreateVm() so the event handler reads the correct value
        _mockSettings.Setup(s => s.AppTheme).Returns("Dark");

        _mockSettings.Raise(s => s.SettingChanged += null, "AppTheme", "Dark");

        Assert.Equal(1, vm.SelectedThemeIndex);
    }
}
