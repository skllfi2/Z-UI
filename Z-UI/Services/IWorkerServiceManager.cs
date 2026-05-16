// IWorkerServiceManager.cs - Interface for managing the Z-UI Worker Windows Service
namespace ZUI.Services;

/// <summary>
/// Manages the Z-UI Worker Windows Service lifecycle from the UI app.
/// Uses native Win32 Service Control Manager API (advapi32.dll) via P/Invoke —
/// no sc.exe, PowerShell, or Process.Start dependency.
/// Install/Start/Stop/Uninstall require the app to run as Administrator.
/// </summary>
public interface IWorkerServiceManager
{
    /// <summary>
    /// Whether the Worker service is installed on this machine.
    /// </summary>
    bool IsInstalled { get; }

    /// <summary>
    /// Current service status (Stopped, Running, etc.).
    /// </summary>
    WorkerServiceStatus Status { get; }

    /// <summary>
    /// Install the Worker service via native Win32 SCM API.
    /// Requires Administrator privileges.
    /// </summary>
    Task<WorkerServiceResult> InstallAsync(CancellationToken ct = default);

    /// <summary>
    /// Start the Worker service via native Win32 SCM API.
    /// Requires Administrator privileges.
    /// </summary>
    Task<WorkerServiceResult> StartAsync(CancellationToken ct = default);

    /// <summary>
    /// Stop the Worker service via native Win32 SCM API.
    /// Requires Administrator privileges.
    /// </summary>
    Task<WorkerServiceResult> StopAsync(CancellationToken ct = default);

    /// <summary>
    /// Uninstall (stop + delete) the Worker service via native Win32 SCM API.
    /// Requires Administrator privileges.
    /// </summary>
    Task<WorkerServiceResult> UninstallAsync(CancellationToken ct = default);

    /// <summary>
    /// Reinstall the Worker service: uninstall then install fresh.
    /// Requires Administrator privileges.
    /// </summary>
    Task<WorkerServiceResult> ReinstallAsync(CancellationToken ct = default);

    /// <summary>
    /// Refresh service status from Windows Service Control Manager.
    /// </summary>
    Task RefreshStatusAsync(CancellationToken ct = default);

    /// <summary>
    /// Raised when service status changes.
    /// </summary>
    event Action<WorkerServiceStatus>? StatusChanged;
}

/// <summary>
/// Status values for the Z-UI Worker Windows Service.
/// </summary>
public enum WorkerServiceStatus
{
    /// <summary>Service is not installed on this machine.</summary>
    NotInstalled,

    /// <summary>Service is installed but not running.</summary>
    Stopped,

    /// <summary>Service is starting up.</summary>
    Starting,

    /// <summary>Service is running.</summary>
    Running,

    /// <summary>Service is shutting down.</summary>
    Stopping,

    /// <summary>Service is in an error state.</summary>
    Error
}

/// <summary>
/// Result of a Worker service operation.
/// </summary>
public readonly record struct WorkerServiceResult(bool IsSuccess, string? Error = null)
{
    public static WorkerServiceResult Ok() => new(true);
    public static WorkerServiceResult Failed(string error) => new(false, error);
}
