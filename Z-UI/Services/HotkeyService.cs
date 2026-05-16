// HotkeyService.cs - Global hotkey registration via Win32 RegisterHotKey API
using System.Runtime.InteropServices;

namespace ZUI.Services;

/// <summary>
/// Manages global hotkeys for Z-UI via Win32 RegisterHotKey API.
/// Registers Ctrl+Alt+T (toggle DPI bypass) and Ctrl+Alt+S (show window).
/// </summary>
public sealed class HotkeyService : IDisposable
{
    private readonly IntPtr _hwnd;
    private bool _disposed;

    // Hotkey IDs
    private const int HOTKEY_TOGGLE = 1;
    private const int HOTKEY_SHOW = 2;

    // Modifier flags
    private const uint MOD_ALT = 0x0001;
    private const uint MOD_CONTROL = 0x0002;

    // Virtual key codes
    private const uint VK_T = 0x54;
    private const uint VK_S = 0x53;

    // P/Invoke declarations
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    /// <summary>
    /// Fired when the toggle hotkey (Ctrl+Alt+T) is pressed.
    /// </summary>
    public event Action? ToggleRequested;

    /// <summary>
    /// Fired when the show hotkey (Ctrl+Alt+S) is pressed.
    /// </summary>
    public event Action? ShowRequested;

    /// <summary>
    /// Creates a new HotkeyService for the specified window handle.
    /// </summary>
    /// <param name="hwnd">Window handle to receive WM_HOTKEY messages.</param>
    public HotkeyService(IntPtr hwnd)
    {
        _hwnd = hwnd;
        System.Diagnostics.Debug.WriteLine("[Z-UI] HotkeyService: Created");
    }

    /// <summary>
    /// Registers global hotkeys: Ctrl+Alt+T (toggle) and Ctrl+Alt+S (show).
    /// Logs errors but does not throw on failure (e.g. hotkey already registered by another app).
    /// </summary>
    public void RegisterHotkeys()
    {
        uint modifiers = MOD_ALT | MOD_CONTROL;

        if (RegisterHotKey(_hwnd, HOTKEY_TOGGLE, modifiers, VK_T))
        {
            System.Diagnostics.Debug.WriteLine("[Z-UI] HotkeyService: Registered Ctrl+Alt+T (toggle)");
        }
        else
        {
            System.Diagnostics.Debug.WriteLine($"[Z-UI] HotkeyService: Failed to register Ctrl+Alt+T, error={Marshal.GetLastWin32Error()}");
        }

        if (RegisterHotKey(_hwnd, HOTKEY_SHOW, modifiers, VK_S))
        {
            System.Diagnostics.Debug.WriteLine("[Z-UI] HotkeyService: Registered Ctrl+Alt+S (show)");
        }
        else
        {
            System.Diagnostics.Debug.WriteLine($"[Z-UI] HotkeyService: Failed to register Ctrl+Alt+S, error={Marshal.GetLastWin32Error()}");
        }
    }

    /// <summary>
    /// Processes a WM_HOTKEY message. Call from WindowSubclass when WM_HOTKEY is received.
    /// </summary>
    /// <param name="wParam">The wParam from WM_HOTKEY (contains hotkey ID in low word).</param>
    /// <returns>True if the hotkey was recognized and handled; false otherwise.</returns>
    public bool ProcessHotkeyMessage(IntPtr wParam)
    {
        int hotkeyId = (int)wParam & 0xFFFF;

        switch (hotkeyId)
        {
            case HOTKEY_TOGGLE:
                ToggleRequested?.Invoke();
                return true;

            case HOTKEY_SHOW:
                ShowRequested?.Invoke();
                return true;
        }

        return false;
    }

    /// <summary>
    /// Unregisters all global hotkeys and releases resources.
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (!UnregisterHotKey(_hwnd, HOTKEY_TOGGLE))
        {
            System.Diagnostics.Debug.WriteLine($"[Z-UI] HotkeyService: Failed to unregister toggle hotkey, error={Marshal.GetLastWin32Error()}");
        }

        if (!UnregisterHotKey(_hwnd, HOTKEY_SHOW))
        {
            System.Diagnostics.Debug.WriteLine($"[Z-UI] HotkeyService: Failed to unregister show hotkey, error={Marshal.GetLastWin32Error()}");
        }

        System.Diagnostics.Debug.WriteLine("[Z-UI] HotkeyService: Disposed");
    }
}
