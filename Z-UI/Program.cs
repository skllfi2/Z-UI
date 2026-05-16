// Program.cs — Custom entry point for Z-UI.
// Intercepts --elevated-worker-action CLI arg for headless UAC self-elevation,
// otherwise starts the normal WinUI 3 application.
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using ZUI.Services;

namespace ZUI;

/// <summary>
/// Exit codes for the elevated helper process.
/// Communicated back to the parent via Process.ExitCode.
/// </summary>
internal static class ElevatedExitCode
{
    internal const int Success = 0;
    internal const int GenericFailure = 1;
    internal const int AccessDenied = 2;
    internal const int ServiceNotFound = 3;
    internal const int ServiceAlreadyExists = 4;
    internal const int InvalidAction = 5;
}

public static class Program
{
    [STAThread]
    static int Main(string[] args)
    {
        // Intercept elevated worker action before WinUI 3 starts
        if (args.Length >= 2 && args[0] == "--elevated-worker-action")
        {
            return ExecuteElevatedAction(args[1]);
        }

        // Normal WinUI 3 app launch
        Application.Start(_ => new App());
        return 0;
    }

    /// <summary>
    /// Execute a service operation headlessly under elevated (admin) context.
    /// Creates a WorkerServiceManager with a NullLogger and performs the requested action.
    /// Returns an exit code that the parent process maps back to a WorkerServiceResult.
    /// </summary>
    private static int ExecuteElevatedAction(string action)
    {
        try
        {
            // Create a minimal logger that writes to stderr (stdout must stay clean)
            using var loggerFactory = LoggerFactory.Create(builder =>
            {
                builder.SetMinimumLevel(LogLevel.Information);
                // No console provider — elevated process has no console window.
                // Debug output goes to OutputDebugString via AddDebug().
                builder.AddDebug();
            });
            var logger = loggerFactory.CreateLogger<WorkerServiceManager>();

            var manager = new WorkerServiceManager(logger);
            var ct = CancellationToken.None;

            WorkerServiceResult result = action.ToLowerInvariant() switch
            {
                "install" => manager.InstallAsync(ct).GetAwaiter().GetResult(),
                "start" => manager.StartAsync(ct).GetAwaiter().GetResult(),
                "stop" => manager.StopAsync(ct).GetAwaiter().GetResult(),
                "uninstall" => manager.UninstallAsync(ct).GetAwaiter().GetResult(),
                "reinstall" => manager.ReinstallAsync(ct).GetAwaiter().GetResult(),
                _ => WorkerServiceResult.Failed($"Unknown action: {action}")
            };

            return MapResultToExitCode(result, action);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Z-UI Elevated] Fatal error: {ex}");
            return ElevatedExitCode.GenericFailure;
        }
    }

    /// <summary>
    /// Map a WorkerServiceResult to an exit code for the parent process to interpret.
    /// </summary>
    private static int MapResultToExitCode(WorkerServiceResult result, string action)
    {
        if (result.IsSuccess)
            return ElevatedExitCode.Success;

        var error = result.Error ?? "Unknown error";

        if (error.Contains("already installed", StringComparison.OrdinalIgnoreCase) ||
            error.Contains("already running", StringComparison.OrdinalIgnoreCase))
            return ElevatedExitCode.ServiceAlreadyExists;

        if (error.Contains("not installed", StringComparison.OrdinalIgnoreCase) ||
            error.Contains("not exist", StringComparison.OrdinalIgnoreCase))
            return ElevatedExitCode.ServiceNotFound;

        if (error.Contains("access denied", StringComparison.OrdinalIgnoreCase) ||
            error.Contains("Administrator", StringComparison.OrdinalIgnoreCase))
            return ElevatedExitCode.AccessDenied;

        return ElevatedExitCode.GenericFailure;
    }
}
