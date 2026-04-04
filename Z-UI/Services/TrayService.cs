using System;
using System.Diagnostics;
using Windows.System;

namespace ZUI.Services
{
    public class TrayService : IDisposable
    {
        private readonly Action? _showWindow;
        private readonly Action? _exitApp;
        private bool _disposed;

        public event Action? ShowWindowRequested;
        public event Action? ExitRequested;

        public TrayService()
        {
        }

        public void Initialize()
        {
            // Трей инициализируется в App.xaml.cs через WinUI 3
            // Здесь только логика показа/скрытия
        }

        public void OnTrayIconClicked()
        {
            ShowWindowRequested?.Invoke();
        }

        public void OnTrayIconDoubleClicked()
        {
            ShowWindowRequested?.Invoke();
        }

        public void OnShowClicked()
        {
            ShowWindowRequested?.Invoke();
        }

        public void OnExitClicked()
        {
            ExitRequested?.Invoke();
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _disposed = true;
            }
        }
    }
}
