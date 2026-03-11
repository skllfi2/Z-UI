using System;
using System.Runtime.InteropServices;

namespace ZUI
{
    public class TrayIcon : IDisposable
    {
        private const int WM_USER = 0x0400;
        private const int WM_TRAYICON = WM_USER + 1;
        private const int WM_COMMAND = 0x0111;
        private const int NIM_ADD = 0x00000000;
        private const int NIM_DELETE = 0x00000002;
        private const int NIM_MODIFY = 0x00000001;
        private const int NIF_MESSAGE = 0x00000001;
        private const int NIF_ICON = 0x00000002;
        private const int NIF_TIP = 0x00000004;
        private const int WM_LBUTTONDBLCLK = 0x0203;
        private const int WM_RBUTTONUP = 0x0205;
        private const int TPM_RETURNCMD = 0x0100;
        private const int TPM_RIGHTBUTTON = 0x0002;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct NOTIFYICONDATA
        {
            public int cbSize;
            public IntPtr hWnd;
            public int uID;
            public int uFlags;
            public int uCallbackMessage;
            public IntPtr hIcon;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string szTip;
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern bool Shell_NotifyIcon(int dwMessage, ref NOTIFYICONDATA lpData);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr LoadImage(IntPtr hInst, string name, uint type, int cx, int cy, uint fuLoad);

        [DllImport("user32.dll")]
        private static extern IntPtr CreatePopupMenu();

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern bool AppendMenu(IntPtr hMenu, uint uFlags, IntPtr uIDNewItem, string lpNewItem);

        [DllImport("user32.dll")]
        private static extern int TrackPopupMenu(IntPtr hMenu, uint uFlags, int x, int y, int nReserved, IntPtr hWnd, IntPtr prcRect);

        [DllImport("user32.dll")]
        private static extern bool DestroyMenu(IntPtr hMenu);

        [DllImport("user32.dll")]
        private static extern bool GetCursorPos(out POINT lpPoint);

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

        [DllImport("user32.dll")]
        private static extern IntPtr CallWindowProc(IntPtr lpPrevWndFunc, IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool DestroyIcon(IntPtr hIcon);

        private delegate IntPtr WndProcDelegate(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT { public int X; public int Y; }

        private const uint IMAGE_ICON = 1;
        private const uint LR_LOADFROMFILE = 0x10;
        private const uint MF_STRING = 0x0;
        private const uint MF_SEPARATOR = 0x800;

        private NOTIFYICONDATA _nid;
        private IntPtr _hIcon;
        private IntPtr _prevWndProc;
        private WndProcDelegate _wndProcDelegate;
        private readonly Action _onShow;
        private readonly Action _onExit;
        private readonly IntPtr _hwnd;

        public TrayIcon(IntPtr hwnd, string iconPath, string tooltip, Action onShow, Action onExit)
        {
            _hwnd = hwnd;
            _onShow = onShow;
            _onExit = onExit;

            _hIcon = LoadImage(IntPtr.Zero, iconPath, IMAGE_ICON, 16, 16, LR_LOADFROMFILE);

            _nid = new NOTIFYICONDATA
            {
                cbSize = Marshal.SizeOf<NOTIFYICONDATA>(),
                hWnd = hwnd,
                uID = 1,
                uFlags = NIF_MESSAGE | NIF_ICON | NIF_TIP,
                uCallbackMessage = WM_TRAYICON,
                hIcon = _hIcon,
                szTip = tooltip
            };
            Shell_NotifyIcon(NIM_ADD, ref _nid);

            // перехватываем WndProc
            _wndProcDelegate = WndProc;
            var ptr = Marshal.GetFunctionPointerForDelegate(_wndProcDelegate);
            _prevWndProc = SetWindowLongPtr(hwnd, -4, ptr);
        }

        private IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
        {
            if (msg == WM_TRAYICON)
            {
                int evt = lParam.ToInt32() & 0xFFFF;
                if (evt == WM_LBUTTONDBLCLK)
                {
                    _onShow();
                }
                else if (evt == WM_RBUTTONUP)
                {
                    ShowContextMenu();
                }
            }
            return CallWindowProc(_prevWndProc, hWnd, msg, wParam, lParam);
        }

        private void ShowContextMenu()
        {
            var hMenu = CreatePopupMenu();
            AppendMenu(hMenu, MF_STRING, new IntPtr(1), "Открыть");
            AppendMenu(hMenu, MF_SEPARATOR, IntPtr.Zero, "");
            AppendMenu(hMenu, MF_STRING, new IntPtr(2), "Выход");

            GetCursorPos(out POINT pt);
            SetForegroundWindow(_hwnd);
            int cmd = TrackPopupMenu(hMenu, TPM_RETURNCMD | TPM_RIGHTBUTTON, pt.X, pt.Y, 0, _hwnd, IntPtr.Zero);
            DestroyMenu(hMenu);

            if (cmd == 1) _onShow();
            else if (cmd == 2) _onExit();
        }

        public void UpdateStatus(bool isRunning)
        {
            _nid.szTip = isRunning ? "ZUI — Запущено ✓" : "ZUI — Остановлено";
            _nid.uFlags = NIF_TIP | NIF_ICON;
            Shell_NotifyIcon(NIM_MODIFY, ref _nid);
        }
        public void Dispose()

        {
            Shell_NotifyIcon(NIM_DELETE, ref _nid);
            if (_hIcon != IntPtr.Zero) DestroyIcon(_hIcon);
        }
    }
}