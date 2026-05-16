// WindowSubclass.cs - Win32 window subclass for intercepting tray/hotkey messages
using System.Runtime.InteropServices;

namespace ZUI.Services;

/// <summary>
/// Installs a Win32 window subclass on the main window to intercept
/// WM_HOTKEY, WM_COMMAND, and tray icon callback messages.
/// Routes them to <see cref="HotkeyService"/> and <see cref="TrayIcon"/>.
/// </summary>
public sealed class WindowSubclass : IDisposable
{
    private IntPtr _hwnd;
    private readonly TrayIcon _trayIcon;
    private readonly HotkeyService _hotkeyService;
    private bool _disposed;

    // Keep delegate alive to prevent GC collection of the callback
    private readonly SUBCLASSPROC _subclassProc;

    // Window messages
    private const int WM_HOTKEY = 0x0312;
    private const int WM_COMMAND = 0x0111;

    // P/Invoke: comctl32 SetWindowSubclass family
    [DllImport("comctl32.dll", SetLastError = true)]
    private static extern bool SetWindowSubclass(IntPtr hWnd, SUBCLASSPROC pfnSubclass, uint uIdSubclass, IntPtr dwRefData);

    [DllImport("comctl32.dll", SetLastError = true)]
    private static extern bool RemoveWindowSubclass(IntPtr hWnd, SUBCLASSPROC pfnSubclass, uint uIdSubclass);

    [DllImport("comctl32.dll")]
    private static extern IntPtr DefSubclassProc(IntPtr hWnd, int uMsg, IntPtr wParam, IntPtr lParam);

    // SUBCLASSPROC delegate type
    private delegate IntPtr SUBCLASSPROC(IntPtr hWnd, int uMsg, IntPtr wParam, IntPtr lParam, IntPtr uIdSubclass, IntPtr dwRefData);

    public WindowSubclass(IntPtr hwnd, TrayIcon trayIcon, HotkeyService hotkeyService)
    {
        _hwnd = hwnd;
        _trayIcon = trayIcon;
        _hotkeyService = hotkeyService;
        _subclassProc = SubclassProc; // store to prevent GC

        if (!SetWindowSubclass(_hwnd, _subclassProc, 0, IntPtr.Zero))
        {
            var err = Marshal.GetLastWin32Error();
            System.Diagnostics.Debug.WriteLine($"[Z-UI] WindowSubclass: SetWindowSubclass failed, error={err}");
        }
        else
        {
            System.Diagnostics.Debug.WriteLine("[Z-UI] WindowSubclass: Installed");
        }
    }

    private IntPtr SubclassProc(IntPtr hWnd, int uMsg, IntPtr wParam, IntPtr lParam, IntPtr uIdSubclass, IntPtr dwRefData)
    {
        switch (uMsg)
        {
            case WM_HOTKEY:
                if (_hotkeyService.ProcessHotkeyMessage(wParam))
                    return IntPtr.Zero;
                break;

            case WM_COMMAND:
                if (_trayIcon.ProcessCommand(wParam))
                    return IntPtr.Zero;
                break;

            default:
                // Check if this is the tray icon callback message
                if (uMsg == _trayIcon.CallbackMessageId)
                {
                    if (_trayIcon.ProcessMessage((uint)uMsg, wParam, lParam))
                        return IntPtr.Zero;
                }
                break;
        }

        return DefSubclassProc(hWnd, uMsg, wParam, lParam);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (_hwnd != IntPtr.Zero)
        {
            RemoveWindowSubclass(_hwnd, _subclassProc, 0);
            _hwnd = IntPtr.Zero;
            System.Diagnostics.Debug.WriteLine("[Z-UI] WindowSubclass: Removed");
        }
    }
}
