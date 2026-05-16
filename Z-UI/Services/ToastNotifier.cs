// ToastNotifier.cs - Windows toast notification helper with AUMID registration
using System.Runtime.InteropServices;
using System.Text;
using Windows.Data.Xml.Dom;
using Windows.UI.Notifications;

namespace ZUI.Services;

/// <summary>
/// Toast notification types.
/// </summary>
public enum ToastType
{
    Success,
    Error,
    Informational,
    Warning
}

/// <summary>
/// Shows Windows toast notifications via WinRT ToastNotification API.
/// For WinUI 3 unpackaged apps, registers AUMID in registry + Start Menu shortcut.
/// </summary>
public static class ToastNotifier
{
    private const string AUMID = "Z-UI";

    private static IntPtr _hwnd;
    private static bool _initialized;
    private static Windows.UI.Notifications.ToastNotifier? _notifier;

    /// <summary>Whether the notifier has been initialized and can show toasts.</summary>
    public static bool IsEnabled => _initialized;

    /// <summary>
    /// Initialize the toast notifier with the main window handle.
    /// Registers AUMID in the registry and creates a Start Menu shortcut if needed.
    /// </summary>
    public static void Initialize(IntPtr hwnd)
    {
        _hwnd = hwnd;

        // Register AUMID for unpackaged app (best-effort)
        RegisterAumid();

        try
        {
            _notifier = ToastNotificationManager.CreateToastNotifier(AUMID);
            _initialized = true;
            System.Diagnostics.Debug.WriteLine($"[Z-UI] ToastNotifier: Initialized with AUMID '{AUMID}'");
        }
        catch (Exception)
        {
            // AUMID registration may have failed — try default
            try
            {
                _notifier = ToastNotificationManager.CreateToastNotifier();
                _initialized = true;
                System.Diagnostics.Debug.WriteLine("[Z-UI] ToastNotifier: Initialized with default AUMID");
            }
            catch (Exception ex2)
            {
                System.Diagnostics.Debug.WriteLine($"[Z-UI] ToastNotifier: Initialization failed - {ex2.Message}");
                _initialized = false;
            }
        }
    }

    /// <summary>
    /// Show a toast notification.
    /// </summary>
    public static void Show(string title, string message, ToastType type)
    {
        if (!_initialized) return;

        System.Diagnostics.Debug.WriteLine($"[Z-UI] Toast: [{type}] {title} — {message}");

        try
        {
            var scenario = type switch
            {
                ToastType.Error => "urgent",
                ToastType.Warning => "important",
                _ => "default"
            };

            var toastXml = $"""
                <toast scenario="{scenario}" launch="action=default">
                    <visual>
                        <binding template="ToastGeneric">
                            <text>{EscapeXml(title)}</text>
                            <text>{EscapeXml(message)}</text>
                        </binding>
                    </visual>
                </toast>
                """;

            var xmlDoc = new XmlDocument();
            xmlDoc.LoadXml(toastXml);

            var toast = new ToastNotification(xmlDoc);
            _notifier?.Show(toast);
        }
        catch (COMException ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Z-UI] ToastNotifier: COM error (notifications may be disabled) - {ex.Message}");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Z-UI] ToastNotifier: Failed to show toast - {ex.Message}");
        }
    }

    // ── AUMID Registration ──────────────────────────────────────────────

    /// <summary>
    /// Registers the AUMID in HKCU registry and creates a Start Menu shortcut
    /// with the AUMID property set. Required for toast notifications in unpackaged apps.
    /// </summary>
    private static void RegisterAumid()
    {
        try
        {
            RegisterAumidRegistry();
            RegisterAumidShortcut();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Z-UI] ToastNotifier: AUMID registration failed - {ex.Message}");
        }
    }

    private static void RegisterAumidRegistry()
    {
        // Write HKCU\Software\Classes\AppUserModelId\Z-UI
        using var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(
            $@"Software\Classes\AppUserModelId\{AUMID}");

        key.SetValue("DisplayName", "Z-UI", Microsoft.Win32.RegistryValueKind.String);

        var exePath = Environment.ProcessPath;
        if (exePath is not null)
            key.SetValue("IconUri", exePath, Microsoft.Win32.RegistryValueKind.String);

        System.Diagnostics.Debug.WriteLine($"[Z-UI] ToastNotifier: AUMID '{AUMID}' registered in HKCU");
    }

    private static void RegisterAumidShortcut()
    {
        var exePath = Environment.ProcessPath;
        if (exePath is null) return;

        var startMenuDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Microsoft", "Windows", "Start Menu", "Programs");

        var shortcutPath = Path.Combine(startMenuDir, "Z-UI.lnk");

        // If shortcut already exists, skip
        if (File.Exists(shortcutPath))
            return;

        if (!Directory.Exists(startMenuDir))
            Directory.CreateDirectory(startMenuDir);

        IShellLinkW? shellLink = null;
        try
        {
            shellLink = (IShellLinkW)new ShellLinkCoClass();
            shellLink.SetPath(exePath);
            shellLink.SetWorkingDirectory(Path.GetDirectoryName(exePath)!);
            shellLink.SetDescription("Z-UI — DPI Bypass Shell");

            // Set AUMID on the shortcut's property store
            var propertyStore = (IPropertyStore)shellLink;
            var aumidVar = new PropVariant(AUMID);
            try
            {
                var hr = propertyStore.SetValue(PKEY_AppUserModel_Id, aumidVar.GetNative());
                if (hr != 0) // S_OK = 0
                {
                    Marshal.ThrowExceptionForHR(hr);
                }
                hr = propertyStore.Commit();
                if (hr != 0)
                {
                    Marshal.ThrowExceptionForHR(hr);
                }
            }
            finally
            {
                aumidVar.Dispose();
            }

            ((IPersistFile)shellLink).Save(shortcutPath, false);

    System.Diagnostics.Debug.WriteLine($"[Z-UI] ToastNotifier: Start Menu shortcut created at '{shortcutPath}'");
}
catch (InvalidCastException)
{
    // ShellLinkCoClass may not implement IShellLinkW on some Windows versions
    System.Diagnostics.Debug.WriteLine($"[Z-UI] ToastNotifier: Shortcut creation skipped (InvalidCast) — toasts will use registry AUMID");
}
catch (COMException ex)
        {
            // E_NOINTERFACE or other COM errors — shortcut creation is best-effort
            // Toasts still work with just the registry AUMID in most cases
            System.Diagnostics.Debug.WriteLine($"[Z-UI] ToastNotifier: Shortcut creation skipped (COM 0x{ex.ErrorCode:X8}) — toasts will use registry AUMID");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Z-UI] ToastNotifier: Shortcut creation failed — {ex.Message}");
        }
    }

    private static string EscapeXml(string text)
    {
        return text
            .Replace("&", "&amp;")
            .Replace("<", "&lt;")
            .Replace(">", "&gt;")
            .Replace("\"", "&quot;")
            .Replace("'", "&apos;");
    }

    // ── COM Interop for Shortcut Creation ───────────────────────────────

    // PKEY_AppUserModel_Id = {9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3}, 5
    private static readonly PROPERTYKEY PKEY_AppUserModel_Id = new()
    {
        fmtid = new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3"),
        pid = 5
    };

    [StructLayout(LayoutKind.Sequential)]
    private struct PROPERTYKEY
    {
        public Guid fmtid;
        public int pid;
    }

    [ComImport]
    [Guid("00021401-0000-0000-C000-000000000046")]
    [ClassInterface(ClassInterfaceType.None)]
    private class ShellLinkCoClass { }

    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("000214F9-0000-0000-C000-000000000046")]
    private interface IShellLinkW
    {
        void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszFile, int cch, IntPtr pfd, uint fFlags);
        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string pszFile);
        void GetIDList(out IntPtr ppidl);
        void SetIDList(IntPtr pidl);
        void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszName, int cch);
        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string pszName);
        void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszDir, int cch);
        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string pszDir);
        void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszArgs, int cch);
        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string pszArgs);
        void GetHotkey(out ushort pwHotkey);
        void SetHotkey(ushort wHotkey);
        void GetShowCmd(out int piShowCmd);
        void SetShowCmd(int iShowCmd);
        void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszIconPath, int cch, out int piIcon);
        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string pszIconPath, int iIcon);
        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string pszPathRel, uint dwReserved);
        void Resolve(IntPtr hwnd, uint fFlags);
    }

    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBCFB99")]
    private interface IPropertyStore
    {
        int GetCount(out uint cProps);
        int GetAt(uint iProp, out PROPERTYKEY pkey);
        int GetValue(in PROPERTYKEY key, out PropVariant.PROPVARIANT pv);
        int SetValue(in PROPERTYKEY key, in PropVariant.PROPVARIANT pv);
        int Commit();
    }

    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("0000010b-0000-0000-C000-000000000046")]
    private interface IPersistFile
    {
        void GetClassID(out Guid pClassID);
        void GetCurFile([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder ppszFileName, int cch);
        int IsDirty();
        void Load([MarshalAs(UnmanagedType.LPWStr)] string pszFileName, uint dwMode);
        void Save([MarshalAs(UnmanagedType.LPWStr)] string? pszFileName, [MarshalAs(UnmanagedType.Bool)] bool fRemember);
        void SaveCompleted([MarshalAs(UnmanagedType.LPWStr)] string? pszFileName);
    }

    /// <summary>
    /// Minimal PROPVARIANT wrapper for VT_LPWSTR (string) values.
    /// </summary>
    private sealed class PropVariant : IDisposable
    {
        private PROPVARIANT _native;

        public PropVariant(string value)
        {
            _native.vt = (ushort)VarEnum.VT_LPWSTR;
            _native.value = Marshal.StringToCoTaskMemUni(value);
        }

        public ref PROPVARIANT GetNative() => ref _native;

        public void Dispose()
        {
            if (_native.value != IntPtr.Zero)
            {
                Marshal.FreeCoTaskMem(_native.value);
                _native.value = IntPtr.Zero;
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct PROPVARIANT
        {
            public ushort vt;
            public ushort wReserved1;
            public ushort wReserved2;
            public ushort wReserved3;
            public IntPtr value;
        }
    }

}
