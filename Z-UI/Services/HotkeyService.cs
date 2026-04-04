using System;
using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;

namespace ZUI.Services
{
    public class HotkeyService : IDisposable
    {
        private const int WM_HOTKEY = 0x0312;
        private const int HOTKEY_START = 1;
        private const int HOTKEY_STOP = 2;
        private const int HOTKEY_TOGGLE = 3;
        private const int HOTKEY_SHOW = 4;

        private const int MOD_CONTROL = 0x0002;
        private const int MOD_SHIFT = 0x0004;
        private const int MOD_ALT = 0x0001;

        [DllImport("user32.dll")]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, int fsModifiers, int vk);

        [DllImport("user32.dll")]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        [DllImport("user32.dll")]
        private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

        [DllImport("user32.dll")]
        private static extern IntPtr CallWindowProc(IntPtr lpPrevWndFunc, IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        private delegate IntPtr WndProcDelegate(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        private readonly IntPtr _hwnd;
        private readonly Action? _onStart;
        private readonly Action? _onStop;
        private readonly Action? _onToggle;
        private readonly Action? _onShow;
        private IntPtr _prevWndProc;
        private WndProcDelegate? _wndProcDelegate;
        private bool _disposed;

        public event Action? StartRequested;
        public event Action? StopRequested;
        public event Action? ToggleRequested;
        public event Action? ShowRequested;

        public HotkeyService(IntPtr hwnd)
        {
            _hwnd = hwnd;
        }

        public void RegisterHotkeys(
            bool enableToggle = true,
            bool enableShow = true,
            bool enableStartStop = false)
        {
            _wndProcDelegate = WndProc;
            var ptr = Marshal.GetFunctionPointerForDelegate(_wndProcDelegate);
            _prevWndProc = SetWindowLongPtr(_hwnd, -4, ptr);

            if (enableToggle)
            {
                RegisterHotKey(_hwnd, HOTKEY_TOGGLE, MOD_CONTROL | MOD_SHIFT, 0x54);
            }

            if (enableShow)
            {
                RegisterHotKey(_hwnd, HOTKEY_SHOW, MOD_CONTROL | MOD_ALT, 0x5A);
            }

            if (enableStartStop)
            {
                RegisterHotKey(_hwnd, HOTKEY_START, MOD_CONTROL | MOD_ALT, 0x53);
                RegisterHotKey(_hwnd, HOTKEY_STOP, MOD_CONTROL | MOD_ALT, 0x58);
            }
        }

        public void UnregisterHotkeys()
        {
            UnregisterHotKey(_hwnd, HOTKEY_TOGGLE);
            UnregisterHotKey(_hwnd, HOTKEY_SHOW);
            UnregisterHotKey(_hwnd, HOTKEY_START);
            UnregisterHotKey(_hwnd, HOTKEY_STOP);
        }

        private IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
        {
            if (msg == WM_HOTKEY)
            {
                var id = wParam.ToInt32();
                switch (id)
                {
                    case HOTKEY_START:
                        StartRequested?.Invoke();
                        break;
                    case HOTKEY_STOP:
                        StopRequested?.Invoke();
                        break;
                    case HOTKEY_TOGGLE:
                        ToggleRequested?.Invoke();
                        break;
                    case HOTKEY_SHOW:
                        ShowRequested?.Invoke();
                        break;
                }
            }
            return CallWindowProc(_prevWndProc, hWnd, msg, wParam, lParam);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            UnregisterHotkeys();
        }
    }
}
