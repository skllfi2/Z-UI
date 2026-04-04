using System;
using System.IO;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using ZUI.ViewModels;
using ZUI.Services;

namespace ZUI;

public partial class App : Application
{
	public static IServiceProvider Services { get; } = ConfigureServices();

	private Window? _window;
	private IntPtr _hwnd;
	private TrayIcon? _trayIcon;
	private HotkeyService? _hotkeyService;

	public static TrayIcon? TrayIcon { get; private set; }
	public Window? MainWindow => _window;

private static IServiceProvider ConfigureServices()
{
    var services = new ServiceCollection();

    services.AddSingleton<WinwsService>();
 services.AddSingleton<TestHistoryService>();
 services.AddSingleton<UpdateService>();

    services.AddSingleton<DashboardViewModel>();
    services.AddSingleton<StrategiesViewModel>();
    services.AddSingleton<ServicesViewModel>();
    services.AddSingleton<SettingsViewModel>();
    services.AddSingleton<SetupWizardViewModel>();
    services.AddSingleton<DiagnosticsViewModel>();
    services.AddSingleton<UpdatesViewModel>();
    services.AddSingleton<ServiceViewModel>();
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

			Microsoft.UI.Xaml.ElementSoundPlayer.State = Microsoft.UI.Xaml.ElementSoundPlayerState.On;
			Microsoft.UI.Xaml.ElementSoundPlayer.SpatialAudioMode = Microsoft.UI.Xaml.ElementSpatialAudioMode.Off;

			var winwsService = Services.GetRequiredService<WinwsService>();
			winwsService.SetDispatcherQueue(_window.DispatcherQueue);

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
				var ws = Services.GetRequiredService<WinwsService>();
				_window!.DispatcherQueue.TryEnqueue(() =>
				{
					if (ws.IsRunning)
						ws.Stop();
					else
						_ = TryStartZapretAsync();
				});
			};
			_hotkeyService.ShowRequested += ShowMainWindow;
			_hotkeyService.RegisterHotkeys();

			winwsService.StatusChanged += OnServiceStatusChanged;

			if (AppSettings.AutoUpdateCheck)
				_ = UpdateChecker.CheckAsync();

			if (AppSettings.AutoStartZapret)
				_ = TryAutoStartZapretAsync();
		}
		catch (Exception ex)
		{
			System.Diagnostics.Debug.WriteLine($"Launch error: {ex}");
			throw;
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

 private async Task TryAutoStartZapretAsync()
 {
 if (!AppSettings.AutoStartZapret) return;

 try
 {
 var strategy = ServiceManager.GetInstalledStrategy();
 if (string.IsNullOrEmpty(strategy)) return;

 var winwsService = Services.GetRequiredService<WinwsService>();
 await winwsService.StartAsync($"--discord-youtube={strategy}");
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

 private async Task TryStartZapretAsync()
 {
 try
 {
 var strategy = ServiceManager.GetInstalledStrategy();
 if (string.IsNullOrEmpty(strategy))
 {
 if (ToastNotifier.IsEnabled)
 ToastNotifier.Show("Ошибка", "Стратегия не установлена", ToastType.Error);
 return;
 }

 var winwsService = Services.GetRequiredService<WinwsService>();
 await winwsService.StartAsync($"--discord-youtube={strategy}");
 }
 catch (Exception ex)
 {
 if (ToastNotifier.IsEnabled)
 ToastNotifier.Show("Ошибка запуска", ex.Message, ToastType.Error);
 }
 }

 private void ExitApp()
 {
 var winwsService = Services.GetRequiredService<WinwsService>();
 winwsService.StatusChanged -= OnServiceStatusChanged;
 winwsService.Stop();
 _trayIcon?.Dispose();
 _hotkeyService?.Dispose();
 Application.Current.Exit();
 }
}
