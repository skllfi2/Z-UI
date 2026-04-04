using System;
using System.Diagnostics;
using System.IO;
using Microsoft.Win32;

namespace ZUI.Services
{
    public static class AutoStartService
    {
        private const string TaskName = "Z-UI AutoStart";

        public static bool IsAutoStartEnabled()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", false);
                return key?.GetValue("Z-UI") != null;
            }
            catch
            {
                return false;
            }
        }

        public static bool IsAutoStartAdminEnabled()
        {
            try
            {
                var psi = new ProcessStartInfo("schtasks", $"/query /tn \"{TaskName}\"")
                {
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };
                using var proc = Process.Start(psi);
                proc?.WaitForExit();
                return proc?.ExitCode == 0;
            }
            catch
            {
                return false;
            }
        }

        public static bool SetAutoStart(bool enable)
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true);
                if (key == null) return false;

                if (enable)
                {
                    var exePath = Process.GetCurrentProcess().MainModule?.FileName;
                    if (string.IsNullOrEmpty(exePath)) return false;
                    key.SetValue("Z-UI", $"\"{exePath}\"");
                }
                else
                {
                    key.DeleteValue("Z-UI", false);
                }
                return true;
            }
            catch
            {
                return false;
            }
        }

        public static bool SetAutoStartAdmin(bool enable)
        {
            try
            {
                var exePath = Process.GetCurrentProcess().MainModule?.FileName;
                if (string.IsNullOrEmpty(exePath)) return false;

                if (enable)
                {
                    var psi = new ProcessStartInfo("schtasks",
                        $"/create /tn \"{TaskName}\" /tr \"\\\"{exePath}\\\"\" /sc onlogon /rl highest /f")
                    {
                        Verb = "runas",
                        UseShellExecute = true,
                        CreateNoWindow = true
                    };
                    using var proc = Process.Start(psi);
                    proc?.WaitForExit();
                    return proc?.ExitCode == 0;
                }
                else
                {
                    var psi = new ProcessStartInfo("schtasks", $"/delete /tn \"{TaskName}\" /f")
                    {
                        Verb = "runas",
                        UseShellExecute = true,
                        CreateNoWindow = true
                    };
                    using var proc = Process.Start(psi);
                    proc?.WaitForExit();
                    return true;
                }
            }
            catch
            {
                return false;
            }
        }
    }
}
