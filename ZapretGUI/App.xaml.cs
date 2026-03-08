using Microsoft.UI.Xaml;
using System;
using System.Runtime.InteropServices;

namespace ZapretGUI
{
    public partial class App : Application
    {
        private Window? _window;
        private IntPtr _hwnd;
        private TrayIcon? _trayIcon;

        public static TrayIcon? TrayIcon { get; private set; }

        public App()
        {
            InitializeComponent();
        }

        protected override void OnLaunched(LaunchActivatedEventArgs args)
        {
            _window = new MainWindow();
            _window.Activate();
            _hwnd = WinRT.Interop.WindowNative.GetWindowHandle(_window);
            _window.Closed += OnWindowClosed;

            // Звук и DispatcherQueue
            Microsoft.UI.Xaml.ElementSoundPlayer.State = Microsoft.UI.Xaml.ElementSoundPlayerState.On;
            Microsoft.UI.Xaml.ElementSoundPlayer.SpatialAudioMode = Microsoft.UI.Xaml.ElementSpatialAudioMode.Off;
            AppState.WinwsService.SetDispatcherQueue(_window.DispatcherQueue);

            _trayIcon = new TrayIcon(_hwnd,
                System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "TLSProxy.ico"),
                "ZapretGUI — Остановлено",
                onShow: ShowMainWindow,
                onExit: ExitApp);

            TrayIcon = _trayIcon;

            AppState.WinwsService.StatusChanged += isRunning =>
            {
                _trayIcon?.UpdateStatus(isRunning);
            };
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

        private void ExitApp()
        {
            AppState.WinwsService.Stop();
            _trayIcon?.Dispose();
            Application.Current.Exit();
        }
    }
}