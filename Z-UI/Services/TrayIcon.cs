// TrayIcon.cs - System tray icon with context menu
using System.Runtime.InteropServices;

namespace ZUI.Services;

/// <summary>
/// System tray icon with show/exit context menu.
/// Uses Win32 Shell_NotifyIcon API for tray icon management.
/// </summary>
public sealed class TrayIcon : IDisposable
{
    private readonly IntPtr _hwnd;
    private readonly string _iconPath;
    private readonly string _tooltip;
    private readonly Action? _onShow;
    private readonly Action? _onExit;
    private bool _disposed;
    private bool _added;
    private IntPtr _hIcon;
    private IntPtr _hMenu;

    // Window message for tray icon callbacks
    private readonly int _wmTrayIcon;

    // Menu item IDs
    private const int IDM_SHOW = 1001;
    private const int IDM_EXIT = 1002;

    // NOTIFYICONDATA flags
    private const int NIM_ADD = 0x00000000;
    private const int NIM_MODIFY = 0x00000001;
    private const int NIM_DELETE = 0x00000002;

    private const int NIF_MESSAGE = 0x00000001;
    private const int NIF_ICON = 0x00000002;
    private const int NIF_TIP = 0x00000004;

    // Tray icon callback messages
    private const int WM_LBUTTONDBLCLK = 0x0203;
    private const int WM_RBUTTONUP = 0x0205;

    // Menu flags
    private const uint MF_STRING = 0x00000000;
    private const uint MF_SEPARATOR = 0x00000800;

    // LoadImage flags
    private const uint IMAGE_ICON = 1;
    private const uint LR_LOADFROMFILE = 0x00000010;
    private const uint LR_DEFAULTSIZE = 0x00000040;
    private static readonly IntPtr IDI_APPLICATION = new IntPtr(32512);

    // P/Invoke declarations
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern bool Shell_NotifyIcon(int dwMessage, ref NOTIFYICONDATA pnid);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr LoadImage(IntPtr hInst, string lpszName, uint uType,
        int cxDesired, int cyDesired, uint fuLoad);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr LoadIcon(IntPtr hInstance, IntPtr lpIconName);

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr hIcon);

    [DllImport("user32.dll")]
    private static extern IntPtr CreatePopupMenu();

    [DllImport("user32.dll")]
    private static extern bool InsertMenu(IntPtr hMenu, uint uPosition, uint uFlags, uint uIDNewItem, string lpNewItem);

    [DllImport("user32.dll")]
    private static extern int TrackPopupMenu(IntPtr hMenu, uint uFlags, int x, int y,
        int nReserved, IntPtr hWnd, IntPtr prcRect);

    [DllImport("user32.dll")]
    private static extern bool DestroyMenu(IntPtr hMenu);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int RegisterWindowMessage(string lpString);

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT lpPoint);

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NOTIFYICONDATA
    {
        public int cbSize;
        public IntPtr hWnd;
        public uint uID;
        public uint uFlags;
        public uint uCallbackMessage;
        public IntPtr hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string szTip;
    }

    public TrayIcon(IntPtr hwnd, string iconPath, string tooltip, Action? onShow = null, Action? onExit = null)
    {
        _hwnd = hwnd;
        _iconPath = iconPath;
        _tooltip = tooltip;
        _onShow = onShow;
        _onExit = onExit;
        _wmTrayIcon = RegisterWindowMessage("ZUI_TrayIcon_Callback");

        // Load icon
        _hIcon = LoadIconFromFile(iconPath);

        // Create context menu
        _hMenu = CreatePopupMenu();
        InsertMenu(_hMenu, 0, MF_STRING, IDM_SHOW, "Show");
        InsertMenu(_hMenu, 1, MF_SEPARATOR, 0, "");
        InsertMenu(_hMenu, 2, MF_STRING, IDM_EXIT, "Exit");

        // Add tray icon
        AddTrayIcon();

        System.Diagnostics.Debug.WriteLine($"[Z-UI] TrayIcon: Created with tooltip='{tooltip}'");
    }

    /// <summary>The registered window message ID for tray icon callbacks.</summary>
    public int CallbackMessageId => _wmTrayIcon;

    /// <summary>
    /// Update the tray icon tooltip to reflect the current protection status.
    /// </summary>
    public void UpdateStatus(bool isRunning)
    {
        if (_disposed || !_added) return;

        var status = isRunning ? "Active" : "Stopped";
        var tip = $"Z-UI — {status}";

        var nid = new NOTIFYICONDATA
        {
            cbSize = Marshal.SizeOf<NOTIFYICONDATA>(),
            hWnd = _hwnd,
            uID = 1,
            uFlags = NIF_TIP,
            szTip = tip
        };

        Shell_NotifyIcon(NIM_MODIFY, ref nid);
        System.Diagnostics.Debug.WriteLine($"[Z-UI] TrayIcon: Status changed to {status}");
    }

    /// <summary>
    /// Process tray icon callback messages. Call from host window's WndProc.
    /// </summary>
    /// <param name="msg">The message ID.</param>
    /// <param name="wParam">wParam.</param>
    /// <param name="lParam">lParam.</param>
    /// <returns>True if the message was handled.</returns>
    public bool ProcessMessage(uint msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg != _wmTrayIcon)
            return false;

        var notification = lParam.ToInt32() & 0xFFFF;

        switch (notification)
        {
            case WM_LBUTTONDBLCLK:
                _onShow?.Invoke();
                return true;

            case WM_RBUTTONUP:
                ShowContextMenu();
                return true;
        }

        return false;
    }

    /// <summary>
    /// Process WM_COMMAND messages from the context menu.
    /// </summary>
    /// <param name="wParam">The wParam from WM_COMMAND (menu item ID).</param>
    /// <returns>True if the command was handled.</returns>
    public bool ProcessCommand(IntPtr wParam)
    {
        var menuId = (int)wParam & 0xFFFF;

        switch (menuId)
        {
            case IDM_SHOW:
                _onShow?.Invoke();
                return true;
            case IDM_EXIT:
                _onExit?.Invoke();
                return true;
        }

        return false;
    }

    private void AddTrayIcon()
    {
        var nid = new NOTIFYICONDATA
        {
            cbSize = Marshal.SizeOf<NOTIFYICONDATA>(),
            hWnd = _hwnd,
            uID = 1,
            uFlags = NIF_MESSAGE | NIF_ICON | NIF_TIP,
            uCallbackMessage = (uint)_wmTrayIcon,
            hIcon = _hIcon,
            szTip = _tooltip
        };

        _added = Shell_NotifyIcon(NIM_ADD, ref nid);
        if (!_added)
            System.Diagnostics.Debug.WriteLine($"[Z-UI] TrayIcon: Failed to add tray icon, error={Marshal.GetLastWin32Error()}");
    }

    private void ShowContextMenu()
    {
        // Get cursor position for context menu placement
        GetCursorPos(out var pt);
        TrackPopupMenu(_hMenu, 0x0100 /*TPM_BOTTOMALIGN*/,
            pt.X, pt.Y,
            0, _hwnd, IntPtr.Zero);
    }

    private IntPtr LoadIconFromFile(string path)
    {
        if (!string.IsNullOrEmpty(path) && File.Exists(path))
        {
            var hIcon = LoadImage(IntPtr.Zero, path, IMAGE_ICON, 16, 16, LR_LOADFROMFILE | LR_DEFAULTSIZE);
            if (hIcon != IntPtr.Zero)
                return hIcon;
        }

        // Fallback to system default application icon
        return LoadIcon(IntPtr.Zero, IDI_APPLICATION);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        // Remove tray icon
        if (_added)
        {
            var nid = new NOTIFYICONDATA
            {
                cbSize = Marshal.SizeOf<NOTIFYICONDATA>(),
                hWnd = _hwnd,
                uID = 1
            };
            Shell_NotifyIcon(NIM_DELETE, ref nid);
            _added = false;
        }

        // Cleanup menu
        if (_hMenu != IntPtr.Zero)
        {
            DestroyMenu(_hMenu);
            _hMenu = IntPtr.Zero;
        }

        // Cleanup icon (only if we loaded from file, not system icon)
        if (_hIcon != IntPtr.Zero)
        {
            DestroyIcon(_hIcon);
            _hIcon = IntPtr.Zero;
        }

        System.Diagnostics.Debug.WriteLine("[Z-UI] TrayIcon: Disposed");
    }
}
