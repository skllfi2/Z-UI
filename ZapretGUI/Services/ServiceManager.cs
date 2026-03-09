using System;
using System.Diagnostics;
using System.IO;
using System.ServiceProcess;
using System.Threading.Tasks;

namespace ZapretGUI.Services
{
    public static class ServiceManager
    {
        private const string ServiceName = "zapret";

        public static async Task<string> GetStatusAsync()
        {
            return await Task.Run(() =>
            {
                try
                {
                    var sc = new ServiceController(ServiceName);
                    var state = sc.Status switch
                    {
                        ServiceControllerStatus.Running => "Запущена ✓",
                        ServiceControllerStatus.Stopped => "Остановлена",
                        ServiceControllerStatus.StartPending => "Запускается...",
                        ServiceControllerStatus.StopPending => "Останавливается...",
                        _ => sc.Status.ToString()
                    };

                    var strategy = GetInstalledStrategy();
                    if (!string.IsNullOrEmpty(strategy))
                        return $"{state} (стратегия: {strategy})";
                    return state;
                }
                catch
                {
                    return "Не установлена";
                }
            });
        }

        public static bool IsInstalled()
        {
            try
            {
                var sc = new ServiceController(ServiceName);
                _ = sc.Status;
                return true;
            }
            catch { return false; }
        }

        public static string GetInstalledStrategy()
        {
            try
            {
                using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
                    @"System\CurrentControlSet\Services\zapret");
                return key?.GetValue("zapret-discord-youtube")?.ToString() ?? "";
            }
            catch { return ""; }
        }

        public static async Task InstallAsync(string strategyName, string arguments, Action<string> log)
        {
            await Task.Run(() =>
            {
                try
                {
                    var binPath = Path.Combine(AppContext.BaseDirectory, "winws", "winws.exe");
                    log("Останавливаю службу...");
                    RunSc($"stop {ServiceName}");
                    log("Удаляю старую службу...");
                    RunSc($"delete {ServiceName}");
                    log("Создаю службу...");
                    RunSc($"create {ServiceName} binPath= \"\\\"{binPath}\\\" {arguments}\" DisplayName= \"zapret\" start= auto");
                    RunSc($"description {ServiceName} \"Zapret DPI bypass software\"");
                    log("Запускаю службу...");
                    RunSc($"start {ServiceName}");

                    using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
                        @"System\CurrentControlSet\Services\zapret", true);
                    key?.SetValue("zapret-discord-youtube", strategyName,
                        Microsoft.Win32.RegistryValueKind.String);

                    log("✓ Служба успешно установлена и запущена");
                }
                catch (Exception ex)
                {
                    log($"✗ Ошибка: {ex.Message}");
                }
            });
        }

        public static async Task RemoveAsync(Action<string> log)
        {
            await Task.Run(() =>
            {
                try
                {
                    log("Останавливаю zapret...");
                    RunSc($"stop {ServiceName}");
                    log("Удаляю zapret...");
                    RunSc($"delete {ServiceName}");

                    foreach (var p in Process.GetProcessesByName("winws"))
                    {
                        p.Kill();
                        log("winws.exe завершён");
                    }

                    log("Удаляю WinDivert...");
                    RunSc("stop WinDivert");
                    RunSc("delete WinDivert");
                    RunSc("stop WinDivert14");
                    RunSc("delete WinDivert14");

                    log("✓ Службы успешно удалены");
                }
                catch (Exception ex)
                {
                    log($"✗ Ошибка: {ex.Message}");
                }
            });
        }

        private static string RunSc(string args)
        {
            try
            {
                var psi = new ProcessStartInfo("sc", args)
                {
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };
                using var p = Process.Start(psi)!;
                p.WaitForExit(5000);
                return p.StandardOutput.ReadToEnd();
            }
            catch { return ""; }
        }
    }
}