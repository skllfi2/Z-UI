using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using ZUI.ViewModels;
using ZUI.Services;
using ZUI.Views;

namespace ZUI;

public partial class App : Application
{
    public static IServiceProvider Services { get; } = ConfigureServices();

    private Window? _window;
    private IntPtr _hwnd;
    private TrayIcon? _trayIcon;
    private HotkeyService? _hotkeyService;
    private WindowSubclass? _windowSubclass;

    public static TrayIcon? TrayIcon { get; private set; }
    public Window? MainWindow => _window;

    public App()
    {
        // Three-layer exception protection for unpackaged WinUI 3:
        // 1. WinUI UnhandledException — catches UI thread exceptions
        UnhandledException += OnUnhandledException;
        // 2. AppDomain.UnhandledException — catches background thread exceptions (COMException in System.Private.CoreLib)
        AppDomain.CurrentDomain.UnhandledException += OnAppDomainUnhandledException;
        // 3. TaskScheduler.UnobservedTaskException — catches fire-and-forget task failures
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
    }

    private void OnUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        // Log and suppress — COM/WinRT exceptions from background threads or fire-and-forget
        // should not crash the app. The UI gracefully degrades (e.g., toast notifications disabled).
        System.Diagnostics.Debug.WriteLine($"[Z-UI] WinUI UnhandledException suppressed: {e.Exception}");
        e.Handled = true;
    }

    private static void OnAppDomainUnhandledException(object sender, System.UnhandledExceptionEventArgs e)
    {
        // Background thread exceptions (e.g., COMException in System.Private.CoreLib from WinRT marshaler)
        // This handler prevents process crash with 0xffffffff exit code.
        var ex = e.ExceptionObject as Exception;
        System.Diagnostics.Debug.WriteLine($"[Z-UI] AppDomain UnhandledException (isTerminating={e.IsTerminating}): {ex}");
        // Note: we cannot set e.Handled here — if IsTerminating=true, the process will exit anyway.
        // But logging helps diagnose the issue, and for COMException this event gives us a chance
        // to suppress via TaskScheduler.UnobservedTaskException (which IS preventable).
    }

    private static void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        // Fire-and-forget tasks (_ = SomeAsync()) that throw exceptions reach here.
        // Without this handler, the process crashes with 0xffffffff.
        System.Diagnostics.Debug.WriteLine($"[Z-UI] Unobserved task exception suppressed: {e.Exception}");
        e.SetObserved(); // Prevent process crash
    }

    private static IServiceProvider ConfigureServices()
    {
        var services = new ServiceCollection();

        // ── Logging ──
        services.AddLogging(builder => builder.AddDebug());

        // ── Core services (order matters for DI resolution) ──
        services.AddSingleton<IIpcClientService, IpcClientService>();
        services.AddSingleton<IAppSettingsService, AppSettingsService>();

 // ── Adaptive engine (replaces ProtectionService) ──
 services.AddSingleton<IEnhancedDnsManager, EnhancedDnsManager>();
 services.AddSingleton<IHostlistService, HostlistService>();
 services.AddSingleton<IAdaptiveEngine, AdaptiveEngine>();

 // ── Strategy & DNS services ──

        services.AddSingleton<IStrategyManager, StrategyManager>();
        services.AddSingleton<IIspDetectionService, IspDetectionService>();
        services.AddSingleton<IStrategyTestService, StrategyTestService>();
        services.AddSingleton<IStrategyGeneratorService, StrategyGeneratorService>();
        services.AddSingleton<IStrategyParamsProvider>(sp => (IStrategyParamsProvider)sp.GetRequiredService<IStrategyGeneratorService>());
	services.AddSingleton<IDiagnosticsService, DiagnosticsService>();
	services.AddSingleton<IDnsService, DnsService>();
        services.AddSingleton<IProxifierService, ProxifierService>();
        services.AddSingleton<ITelegramProxyService, TelegramProxyService>();
        services.AddSingleton<ActiveBlockProber>();
        services.AddSingleton<INavigationService, NavigationService>();
        services.AddSingleton<ISoundService, SoundService>();
    services.AddSingleton<MalwLinkUpdateService>();
        services.AddSingleton<IDashboardStatusService, DashboardStatusService>();
        services.AddSingleton<IWorkerServiceManager, WorkerServiceManager>();

        // ── ViewModels ──
        services.AddSingleton<DashboardViewModel>();
        services.AddSingleton<DiagnosticsViewModel>();
        services.AddSingleton<DnsPageViewModel>();
        services.AddSingleton<GeneratorViewModel>();
        services.AddSingleton<ProxifierViewModel>();
        services.AddTransient<ProxifierPage>();
        services.AddSingleton<SettingsViewModel>();
    services.AddSingleton<TgProxyViewModel>();
        services.AddSingleton<AboutViewModel>();

        return services.BuildServiceProvider();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        try
        {
            // Initialize localization
            LocalizationService.Initialize();

            TestResultStore.TryLoadCache();

            _window = new MainWindow();
            _window.Activate();

            _hwnd = WinRT.Interop.WindowNative.GetWindowHandle(_window);
            _window.Closed += OnWindowClosed;

        try
        {
            Microsoft.UI.Xaml.ElementSoundPlayer.State = Microsoft.UI.Xaml.ElementSoundPlayerState.On;
            Microsoft.UI.Xaml.ElementSoundPlayer.SpatialAudioMode = Microsoft.UI.Xaml.ElementSpatialAudioMode.Off;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Z-UI] ElementSoundPlayer init skipped: {ex.Message}");
        }

		// Connect IPC to Worker service (best-effort, falls back to standalone mode)
            _ = TryConnectIpcAsync();

            ToastNotifier.Initialize(_hwnd);

            var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "Z-UI.ico");
            if (File.Exists(iconPath))
            {
                _trayIcon = new TrayIcon(_hwnd, iconPath, $"Z-UI — {LocalizationService.Get("Stopped")}", onShow: ShowMainWindow, onExit: ExitApp);
                TrayIcon = _trayIcon;
            }

            _hotkeyService = new HotkeyService(_hwnd);
        _hotkeyService.ToggleRequested += () =>
        {
            var engine = Services.GetRequiredService<IAdaptiveEngine>();
            _window!.DispatcherQueue.TryEnqueue(() =>
            {
                if (engine.IsProtected)
                    _ = engine.StopAsync();
                else
                    _ = TryStartProtectionAsync();
            });
        };
        _hotkeyService.ShowRequested += ShowMainWindow;
        _hotkeyService.RegisterHotkeys();

        // Install Win32 subclass to route WM_HOTKEY, WM_COMMAND, and tray messages
        if (_trayIcon is not null && _hotkeyService is not null)
            _windowSubclass = new WindowSubclass(_hwnd, _trayIcon, _hotkeyService);

        // Wire AppSettings.Save() bridge to IAppSettingsService
        AppSettings.SaveDelegate = () =>
        {
            try
            {
                Services.GetService<IAppSettingsService>()?.Save();
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Z-UI] AppSettings.SaveDelegate error: {ex.Message}");
                return false;
            }
        };

        if (AppSettings.AutoUpdateCheck)
            _ = FireAndForgetCheckUpdatesAsync();

            if (AppSettings.AutoStartZapret)
                _ = TryAutoStartProtectionAsync();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Launch error: {ex}");
            throw;
        }
    }

    private async Task TryConnectIpcAsync()
    {
        try
        {
            var ipc = Services.GetRequiredService<IIpcClientService>();
            await ipc.ConnectAsync();
        }
        catch (Exception ex)
        {
			System.Diagnostics.Debug.WriteLine($"[Z-UI] IPC connection failed (fallback to standalone mode): {ex.Message}");
        }
    }

    private void OnServiceStatusChanged(bool isRunning)
    {
        _trayIcon?.UpdateStatus(isRunning);

        if (ToastNotifier.IsEnabled)
            ToastNotifier.Show(
                LocalizationService.Get("ServiceStatus"),
                isRunning ? LocalizationService.Get("StartedMale") : LocalizationService.Get("StoppedMale"),
                isRunning ? ToastType.Success : ToastType.Informational);
    }

    private void OnWindowClosed(object sender, WindowEventArgs args)
    {
        args.Handled = true;
        var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(_hwnd);
        var appWindow = Microsoft.UI.Windowing.AppWindow.GetFromWindowId(windowId);
        appWindow.Hide();
    }

    private void ShowMainWindow()
    {
        if (_window == null) return;
        _window.DispatcherQueue.TryEnqueue(() =>
        {
            var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(_hwnd);
            var appWindow = Microsoft.UI.Windowing.AppWindow.GetFromWindowId(windowId);
            appWindow.Show();
            _window.Activate();
        });
    }

    private async Task TryAutoStartProtectionAsync()
    {
        if (!AppSettings.AutoStartZapret) return;

        try
        {
            var strategy = AppSettings.CurrentStrategy;
            if (string.IsNullOrEmpty(strategy)) return;

            var engine = Services.GetRequiredService<IAdaptiveEngine>();
            var result = await engine.StartWithStrategyAsync(strategy);
            if (result.Success)
                OnServiceStatusChanged(true);
        }
        catch (Exception ex)
        {
            if (ToastNotifier.IsEnabled)
            ToastNotifier.Show(
                "Ошибка автозапуска",
                $"Не удалось запустить сервис: {ex.Message}",
                ToastType.Error);
        }
    }

    private async Task TryStartProtectionAsync()
    {
        try
        {
            var strategy = AppSettings.CurrentStrategy;
            if (string.IsNullOrEmpty(strategy))
            {
                // Zero-config: use adaptive engine auto mode
                var engine = Services.GetRequiredService<IAdaptiveEngine>();
                var result = await engine.StartAdaptiveAsync();
                if (result.Success)
                    OnServiceStatusChanged(true);
                return;
            }

            var eng = Services.GetRequiredService<IAdaptiveEngine>();
            var res = await eng.StartWithStrategyAsync(strategy);
            if (res.Success)
                OnServiceStatusChanged(true);
        }
        catch (Exception ex)
        {
            if (ToastNotifier.IsEnabled)
            ToastNotifier.Show("Ошибка запуска", ex.Message, ToastType.Error);
        }
    }

    private async Task FireAndForgetCheckUpdatesAsync()
    {
        try
        {
            await UpdateChecker.CheckAsync();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Z-UI] Update check failed: {ex.Message}");
        }
    }

    private void ExitApp()
    {
        var engine = Services.GetRequiredService<IAdaptiveEngine>();
        _ = engine.StopAsync();
        _windowSubclass?.Dispose();
        _trayIcon?.Dispose();
        _hotkeyService?.Dispose();
        Application.Current.Exit();
    }
}
