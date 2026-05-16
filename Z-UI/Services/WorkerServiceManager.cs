// WorkerServiceManager.cs - Full P/Invoke implementation for Z-UI Worker Windows Service management
// Uses advapi32.dll Service Control Manager API — no sc.exe, PowerShell, or Process.Start.
// Install/Start/Stop/Uninstall require Administrator privileges.
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using Microsoft.Extensions.Logging;

namespace ZUI.Services;

/// <summary>
/// Manages the Z-UI Worker Windows Service lifecycle via native Win32 SCM API (advapi32.dll).
/// Used in two contexts:
/// 1. DI-injected in DashboardViewModel — normal app usage (async)
/// 2. Headless elevated in Program.cs — UAC self-elevation (sync via .GetAwaiter().GetResult())
/// </summary>
public class WorkerServiceManager : IWorkerServiceManager
{
    private readonly ILogger<WorkerServiceManager> _logger;
    private WorkerServiceStatus _status = WorkerServiceStatus.NotInstalled;
    private bool _isInstalled;

    private const string ServiceName = "Z-UI Worker";
    private const string ServiceDisplayName = "Z-UI Worker Service";

    #region Properties

    public bool IsInstalled
    {
        get => _isInstalled;
        private set
        {
            if (_isInstalled != value)
            {
                _isInstalled = value;
                _logger.LogDebug("Worker service IsInstalled changed to {Value}", value);
            }
        }
    }

    public WorkerServiceStatus Status
    {
        get => _status;
        private set
        {
            if (_status != value)
            {
                var previous = _status;
                _status = value;
                _logger.LogDebug("Worker service Status changed from {Previous} to {Current}", previous, value);
                StatusChanged?.Invoke(value);
            }
        }
    }

    public event Action<WorkerServiceStatus>? StatusChanged;

    #endregion

    #region Constructor

    public WorkerServiceManager(ILogger<WorkerServiceManager> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    #endregion

    #region Public Methods

    public Task<WorkerServiceResult> InstallAsync(CancellationToken ct = default)
    {
        return Task.Run(async () =>
        {
            ct.ThrowIfCancellationRequested();

            var binaryPath = GetWorkerBinaryPath();
            _logger.LogInformation("Installing worker service from {Path}", binaryPath);

            // Pre-check: verify Worker binary exists before calling SCM
            if (!File.Exists(binaryPath))
            {
                _logger.LogError("Worker binary not found at {Path}", binaryPath);
                return WorkerServiceResult.Failed(
                    $"Worker executable not found: {binaryPath}. Ensure the ZUI.Worker project is built and deployed alongside Z-UI.");
            }

            SafeSCHandle? scManager = null;
            try
            {
                scManager = NativeMethods.OpenSCManager(null, null, NativeMethods.SC_MANAGER_CREATE_SERVICE);
                if (scManager.IsInvalid)
                {
                    var error = Marshal.GetLastWin32Error();
                    return WorkerServiceResult.Failed(MapOpenScManagerError(error));
                }

                // Quote the binary path for CreateService
                var quotedPath = $"\"{binaryPath}\"";

                var service = NativeMethods.CreateServiceW(
                    scManager,
                    ServiceName,
                    ServiceDisplayName,
                    NativeMethods.SERVICE_ALL_ACCESS,
                    NativeMethods.SERVICE_WIN32_OWN_PROCESS,
                    NativeMethods.SERVICE_AUTO_START,
                    NativeMethods.SERVICE_ERROR_NORMAL,
                    quotedPath,
                    null,
                    IntPtr.Zero,
                    null,
                    "LocalSystem",
                    null);

                if (service.IsInvalid)
                {
                    var error = Marshal.GetLastWin32Error();
                    return WorkerServiceResult.Failed(MapCreateServiceError(error));
                }

                // Auto-start: immediately start the service after creation
                var started = NativeMethods.StartServiceW(service, 0, IntPtr.Zero);
                if (!started)
                {
                    var startError = Marshal.GetLastWin32Error();
                    // ERROR_SERVICE_ALREADY_RUNNING is fine — service is up
                    if (startError != NativeMethods.ERROR_SERVICE_ALREADY_RUNNING)
                    {
                        _logger.LogWarning("Worker service installed but auto-start failed: {Error} ({Code})",
                            GetWin32ErrorMessage(startError), startError);
                        // Don't fail the whole install — service is created, just not started
                    }
                }

                if (started || Marshal.GetLastWin32Error() == NativeMethods.ERROR_SERVICE_ALREADY_RUNNING)
                {
                    // Wait for the service to reach RUNNING state (up to 15 seconds)
                    WaitForServiceState(service, NativeMethods.SERVICE_RUNNING, timeoutMilliseconds: 15000, ct);
                    _logger.LogInformation("Worker service installed and started successfully");
                }

                service.Dispose();
                await RefreshStatusAsync(ct).ConfigureAwait(false);
                return WorkerServiceResult.Ok();
            }
            finally
            {
                scManager?.Dispose();
            }
        }, ct);
    }

    public Task<WorkerServiceResult> StartAsync(CancellationToken ct = default)
    {
        return Task.Run(async () =>
        {
            ct.ThrowIfCancellationRequested();

            _logger.LogInformation("Starting worker service");

            SafeSCHandle? scManager = null;
            try
            {
                scManager = NativeMethods.OpenSCManager(null, null, NativeMethods.SC_MANAGER_CONNECT);
                if (scManager.IsInvalid)
                {
                    var error = Marshal.GetLastWin32Error();
                    return WorkerServiceResult.Failed(MapOpenScManagerError(error));
                }

                var service = NativeMethods.OpenService(scManager, ServiceName,
                    NativeMethods.SERVICE_START | NativeMethods.SERVICE_QUERY_STATUS);
                if (service.IsInvalid)
                {
                    var error = Marshal.GetLastWin32Error();
                    return WorkerServiceResult.Failed(MapOpenServiceError(error));
                }

                try
                {
                    var started = NativeMethods.StartServiceW(service, 0, IntPtr.Zero);
                    if (!started)
                    {
                        var error = Marshal.GetLastWin32Error();
                        return WorkerServiceResult.Failed(MapStartServiceError(error));
                    }

                    _logger.LogInformation("Worker service start command sent successfully");
                }
                finally
                {
                    service.Dispose();
                }

                await RefreshStatusAsync(ct).ConfigureAwait(false);
                return WorkerServiceResult.Ok();
            }
            finally
            {
                scManager?.Dispose();
            }
        }, ct);
    }

    public Task<WorkerServiceResult> StopAsync(CancellationToken ct = default)
    {
        return Task.Run(async () =>
        {
            ct.ThrowIfCancellationRequested();

            _logger.LogInformation("Stopping worker service");

            SafeSCHandle? scManager = null;
            try
            {
                scManager = NativeMethods.OpenSCManager(null, null, NativeMethods.SC_MANAGER_CONNECT);
                if (scManager.IsInvalid)
                {
                    var error = Marshal.GetLastWin32Error();
                    return WorkerServiceResult.Failed(MapOpenScManagerError(error));
                }

                var service = NativeMethods.OpenService(scManager, ServiceName,
                    NativeMethods.SERVICE_STOP | NativeMethods.SERVICE_QUERY_STATUS);
                if (service.IsInvalid)
                {
                    var error = Marshal.GetLastWin32Error();
                    return WorkerServiceResult.Failed(MapOpenServiceError(error));
                }

                try
                {
                    var status = new NativeMethods.SERVICE_STATUS();
                    var stopped = NativeMethods.ControlService(service, NativeMethods.SERVICE_CONTROL_STOP, ref status);
                    if (!stopped)
                    {
                        var error = Marshal.GetLastWin32Error();
                        return WorkerServiceResult.Failed(MapControlServiceError(error));
                    }

                    _logger.LogInformation("Worker service stop command sent successfully");
                }
                finally
                {
                    service.Dispose();
                }

                await RefreshStatusAsync(ct).ConfigureAwait(false);
                return WorkerServiceResult.Ok();
            }
            finally
            {
                scManager?.Dispose();
            }
        }, ct);
    }

    public Task<WorkerServiceResult> UninstallAsync(CancellationToken ct = default)
    {
        return Task.Run(async () =>
        {
            ct.ThrowIfCancellationRequested();

            _logger.LogInformation("Uninstalling worker service");

            SafeSCHandle? scManager = null;
            try
            {
                scManager = NativeMethods.OpenSCManager(null, null, NativeMethods.SC_MANAGER_CREATE_SERVICE);
                if (scManager.IsInvalid)
                {
                    var error = Marshal.GetLastWin32Error();
                    return WorkerServiceResult.Failed(MapOpenScManagerError(error));
                }

                var service = NativeMethods.OpenService(scManager, ServiceName,
                    NativeMethods.SERVICE_STOP | NativeMethods.SERVICE_DELETE | NativeMethods.SERVICE_QUERY_STATUS);
                if (service.IsInvalid)
                {
                    var error = Marshal.GetLastWin32Error();
                    return WorkerServiceResult.Failed(MapOpenServiceError(error));
                }

                try
                {
                    // Try to stop the service first if it's running
                    var status = new NativeMethods.SERVICE_STATUS();
                    var currentState = QueryServiceCurrentState(service);
                    if (currentState == NativeMethods.SERVICE_RUNNING ||
                        currentState == NativeMethods.SERVICE_START_PENDING)
                    {
                        _logger.LogDebug("Service is running, stopping before uninstall");
                        NativeMethods.ControlService(service, NativeMethods.SERVICE_CONTROL_STOP, ref status);

                        // Wait briefly for the service to stop
                        WaitForServiceState(service, NativeMethods.SERVICE_STOPPED,
                            timeoutMilliseconds: 10000, ct);
                    }

                    var deleted = NativeMethods.DeleteService(service);
                    if (!deleted)
                    {
                        var error = Marshal.GetLastWin32Error();
                        return WorkerServiceResult.Failed(MapDeleteServiceError(error));
                    }

                    _logger.LogInformation("Worker service uninstalled successfully");
                }
                finally
                {
                    service.Dispose();
                }

                await RefreshStatusAsync(ct).ConfigureAwait(false);
                return WorkerServiceResult.Ok();
            }
            finally
            {
                scManager?.Dispose();
            }
        }, ct);
    }

    public Task<WorkerServiceResult> ReinstallAsync(CancellationToken ct = default)
    {
        return Task.Run(async () =>
        {
            _logger.LogInformation("Reinstalling worker service");

            // If installed, stop and uninstall first
            if (IsInstalled)
            {
                var uninstallResult = await UninstallAsync(ct).ConfigureAwait(false);
                if (!uninstallResult.IsSuccess)
                {
                    // If uninstall failed because service doesn't exist, that's fine — proceed to install
                    if (!(uninstallResult.Error?.Contains("not installed", StringComparison.OrdinalIgnoreCase) == true ||
                          uninstallResult.Error?.Contains("not exist", StringComparison.OrdinalIgnoreCase) == true))
                    {
                        return WorkerServiceResult.Failed(
                            $"Reinstall failed during uninstall: {uninstallResult.Error}");
                    }
                }
            }

            var installResult = await InstallAsync(ct).ConfigureAwait(false);
            if (!installResult.IsSuccess)
            {
                return WorkerServiceResult.Failed(
                    $"Reinstall failed during install: {installResult.Error}");
            }

            _logger.LogInformation("Worker service reinstalled successfully");
            return WorkerServiceResult.Ok();
        }, ct);
    }

    public Task RefreshStatusAsync(CancellationToken ct = default)
    {
        return Task.Run(() =>
        {
            ct.ThrowIfCancellationRequested();

            SafeSCHandle? scManager = null;
            try
            {
                scManager = NativeMethods.OpenSCManager(null, null, NativeMethods.SC_MANAGER_CONNECT);
                if (scManager.IsInvalid)
                {
                    var error = Marshal.GetLastWin32Error();
                    _logger.LogWarning("Failed to open SCM for status refresh: {Error} ({Code})",
                        GetWin32ErrorMessage(error), error);

                    // If we can't connect to SCM, assume not installed
                    IsInstalled = false;
                    Status = WorkerServiceStatus.NotInstalled;
                    return;
                }

                var service = NativeMethods.OpenService(scManager, ServiceName,
                    NativeMethods.SERVICE_QUERY_STATUS);
                if (service.IsInvalid)
                {
                    var error = Marshal.GetLastWin32Error();
                    if (error == NativeMethods.ERROR_SERVICE_DOES_NOT_EXIST)
                    {
                        _logger.LogDebug("Worker service is not installed");
                        IsInstalled = false;
                        Status = WorkerServiceStatus.NotInstalled;
                    }
                    else
                    {
                        _logger.LogWarning("Failed to open service for status query: {Error} ({Code})",
                            GetWin32ErrorMessage(error), error);
                        IsInstalled = false;
                        Status = WorkerServiceStatus.Error;
                    }

                    return;
                }

                try
                {
                    var state = QueryServiceCurrentState(service);
                    IsInstalled = true;
                    Status = MapServiceState(state);
                    _logger.LogDebug("Worker service status refreshed: {Status} (raw state: 0x{State:X})",
                        Status, state);
                }
                finally
                {
                    service.Dispose();
                }
            }
            finally
            {
                scManager?.Dispose();
            }
        }, ct);
    }

    #endregion

    #region Private Helpers

    /// <summary>
    /// Gets the full path to the ZUI.Worker.exe binary.
    /// </summary>
    private static string GetWorkerBinaryPath()
    {
        return Path.Combine(AppContext.BaseDirectory, "ZUI.Worker", "ZUI.Worker.exe");
    }

    /// <summary>
    /// Queries the current state of a service using QueryServiceStatus.
    /// </summary>
    private static uint QueryServiceCurrentState(SafeSCHandle service)
    {
        var status = new NativeMethods.SERVICE_STATUS();
        if (!NativeMethods.QueryServiceStatus(service, ref status))
        {
            var error = Marshal.GetLastWin32Error();
            throw new InvalidOperationException(
                $"QueryServiceStatus failed with error {error}: {GetWin32ErrorMessage(error)}");
        }

        return status.dwCurrentState;
    }

    /// <summary>
    /// Polls the service state until it reaches the desired state or times out.
    /// </summary>
    private static void WaitForServiceState(SafeSCHandle service, uint desiredState,
        int timeoutMilliseconds, CancellationToken ct)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < timeoutMilliseconds)
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                var currentState = QueryServiceCurrentState(service);
                if (currentState == desiredState)
                    return;
            }
            catch (InvalidOperationException)
            {
                // Service handle may become invalid after delete — exit gracefully
                return;
            }

            Thread.Sleep(500);
        }
    }

    /// <summary>
    /// Maps a Win32 SERVICE_STATUS.dwCurrentState value to the WorkerServiceStatus enum.
    /// </summary>
    private static WorkerServiceStatus MapServiceState(uint state) => state switch
    {
        NativeMethods.SERVICE_STOPPED => WorkerServiceStatus.Stopped,
        NativeMethods.SERVICE_START_PENDING => WorkerServiceStatus.Starting,
        NativeMethods.SERVICE_STOP_PENDING => WorkerServiceStatus.Stopping,
        NativeMethods.SERVICE_RUNNING => WorkerServiceStatus.Running,
        NativeMethods.SERVICE_CONTINUE_PENDING => WorkerServiceStatus.Starting,
        NativeMethods.SERVICE_PAUSE_PENDING => WorkerServiceStatus.Stopping,
        NativeMethods.SERVICE_PAUSED => WorkerServiceStatus.Stopped,
        _ => WorkerServiceStatus.Error
    };

    /// <summary>
    /// Converts a Win32 error code to a human-readable message using FormatMessage.
    /// </summary>
    private static string GetWin32ErrorMessage(int errorCode)
    {
        if (errorCode == 0)
            return "Success";

        var buffer = IntPtr.Zero;
        var formatFlags = NativeMethods.FORMAT_MESSAGE_ALLOCATE_BUFFER |
                          NativeMethods.FORMAT_MESSAGE_FROM_SYSTEM |
                          NativeMethods.FORMAT_MESSAGE_IGNORE_INSERTS;

        var messageLength = NativeMethods.FormatMessageW(
            formatFlags,
            IntPtr.Zero,
            (uint)errorCode,
            0, // default language
            ref buffer,
            0,
            IntPtr.Zero);

        if (messageLength == 0 || buffer == IntPtr.Zero)
            return $"Win32 error {errorCode}";

        try
        {
            var message = Marshal.PtrToStringUni(buffer)?.Trim() ?? $"Win32 error {errorCode}";
            return message;
        }
        finally
        {
            NativeMethods.LocalFree(buffer);
        }
    }

    #endregion

    #region Error Mapping

    /// <summary>
    /// Maps OpenSCManager errors to user-friendly messages with required substrings for Program.cs.
    /// </summary>
    private static string MapOpenScManagerError(int error) => error switch
    {
        NativeMethods.ERROR_ACCESS_DENIED =>
            $"Access denied. Administrator privileges required to manage services. (0x{error:X})",
        NativeMethods.ERROR_RPC_S_SERVER_UNAVAILABLE =>
            $"SCM is not available: {GetWin32ErrorMessage(error)} (0x{error:X})",
        _ => $"Failed to open Service Control Manager: {GetWin32ErrorMessage(error)} (0x{error:X})"
    };

    /// <summary>
    /// Maps CreateService errors to user-friendly messages with required substrings for Program.cs.
    /// </summary>
    private static string MapCreateServiceError(int error) => error switch
    {
        NativeMethods.ERROR_SERVICE_ALREADY_EXISTS =>
            $"Service is already installed. (0x{error:X})",
        NativeMethods.ERROR_ACCESS_DENIED =>
            $"Access denied. Administrator privileges required to install the service. (0x{error:X})",
        NativeMethods.ERROR_DUPLICATE_SERVICE_NAME =>
            $"A service with this name already exists. (0x{error:X})",
        NativeMethods.ERROR_INVALID_PARAMETER =>
            $"Invalid service configuration: {GetWin32ErrorMessage(error)} (0x{error:X})",
        NativeMethods.ERROR_SERVICE_MARKED_FOR_DELETE =>
            $"Service is marked for deletion. Wait a moment and try again. (0x{error:X})",
        _ => $"Failed to install service: {GetWin32ErrorMessage(error)} (0x{error:X})"
    };

    /// <summary>
    /// Maps OpenService errors to user-friendly messages with required substrings for Program.cs.
    /// </summary>
    private static string MapOpenServiceError(int error) => error switch
    {
        NativeMethods.ERROR_SERVICE_DOES_NOT_EXIST =>
            $"Worker service is not installed. (0x{error:X})",
        NativeMethods.ERROR_ACCESS_DENIED =>
            $"Access denied. Administrator privileges required. (0x{error:X})",
        NativeMethods.ERROR_INVALID_HANDLE =>
            $"Invalid SCM handle: {GetWin32ErrorMessage(error)} (0x{error:X})",
        _ => $"Failed to open service: {GetWin32ErrorMessage(error)} (0x{error:X})"
    };

    /// <summary>
    /// Maps StartService errors to user-friendly messages with required substrings for Program.cs.
    /// </summary>
    private static string MapStartServiceError(int error) => error switch
    {
        NativeMethods.ERROR_SERVICE_ALREADY_RUNNING =>
            $"Service is already running. (0x{error:X})",
        NativeMethods.ERROR_SERVICE_DOES_NOT_EXIST =>
            $"Worker service is not installed. (0x{error:X})",
        NativeMethods.ERROR_ACCESS_DENIED =>
            $"Access denied. Administrator privileges required to start the service. (0x{error:X})",
        NativeMethods.ERROR_SERVICE_MARKED_FOR_DELETE =>
            $"Service is marked for deletion and cannot be started. (0x{error:X})",
        NativeMethods.ERROR_PATH_NOT_FOUND =>
            $"Service binary not found. The worker executable may be missing. (0x{error:X})",
        NativeMethods.ERROR_SERVICE_REQUEST_TIMEOUT =>
            $"Service start timed out. (0x{error:X})",
        NativeMethods.ERROR_SERVICE_DEPENDENCY_FAIL =>
            $"A service dependency failed. (0x{error:X})",
        _ => $"Failed to start service: {GetWin32ErrorMessage(error)} (0x{error:X})"
    };

    /// <summary>
    /// Maps ControlService (stop) errors to user-friendly messages with required substrings for Program.cs.
    /// </summary>
    private static string MapControlServiceError(int error) => error switch
    {
        NativeMethods.ERROR_SERVICE_NOT_ACTIVE =>
            $"Service is not running. (0x{error:X})",
        NativeMethods.ERROR_SERVICE_DOES_NOT_EXIST =>
            $"Worker service is not installed. (0x{error:X})",
        NativeMethods.ERROR_ACCESS_DENIED =>
            $"Access denied. Administrator privileges required to stop the service. (0x{error:X})",
        NativeMethods.ERROR_DEPENDENT_SERVICES_RUNNING =>
            $"Dependent services are still running. (0x{error:X})",
        NativeMethods.ERROR_INVALID_SERVICE_CONTROL =>
            $"Invalid service control request: {GetWin32ErrorMessage(error)} (0x{error:X})",
        _ => $"Failed to stop service: {GetWin32ErrorMessage(error)} (0x{error:X})"
    };

    /// <summary>
    /// Maps DeleteService errors to user-friendly messages with required substrings for Program.cs.
    /// </summary>
    private static string MapDeleteServiceError(int error) => error switch
    {
        NativeMethods.ERROR_ACCESS_DENIED =>
            $"Access denied. Administrator privileges required to delete the service. (0x{error:X})",
        NativeMethods.ERROR_SERVICE_MARKED_FOR_DELETE =>
            $"Service is already marked for deletion. (0x{error:X})",
        NativeMethods.ERROR_SERVICE_DOES_NOT_EXIST =>
            $"Worker service does not exist. (0x{error:X})",
        _ => $"Failed to delete service: {GetWin32ErrorMessage(error)} (0x{error:X})"
    };

    #endregion

    #region SafeHandle

    /// <summary>
    /// SafeHandle wrapper for Win32 service/SCM handles.
    /// Calls CloseServiceHandle on release to prevent handle leaks.
    /// </summary>
    private sealed class SafeSCHandle : SafeHandleZeroOrMinusOneIsInvalid
    {
        public SafeSCHandle() : base(ownsHandle: true) { }

        protected override bool ReleaseHandle()
        {
            return NativeMethods.CloseServiceHandle(handle);
        }
    }

    #endregion

    #region Native Methods (P/Invoke)

    private static class NativeMethods
    {
        // ── Access Rights ──────────────────────────────────────────────

        internal const uint SC_MANAGER_CONNECT = 0x0001;
        internal const uint SC_MANAGER_CREATE_SERVICE = 0x0002;
        internal const uint SC_MANAGER_ALL_ACCESS = 0x000F003F;

        internal const uint SERVICE_ALL_ACCESS = 0x000F01FF;
        internal const uint SERVICE_QUERY_STATUS = 0x0004;
        internal const uint SERVICE_START = 0x0010;
        internal const uint SERVICE_STOP = 0x0020;
        internal const uint SERVICE_DELETE = 0x10000;
        internal const uint SERVICE_INTERROGATE = 0x0080;

        // ── Service Types ──────────────────────────────────────────────

        internal const uint SERVICE_WIN32_OWN_PROCESS = 0x00000010;
        internal const uint SERVICE_AUTO_START = 0x00000002;
        internal const uint SERVICE_DEMAND_START = 0x00000003;
        internal const uint SERVICE_ERROR_NORMAL = 0x00000001;

        // ── Service States ─────────────────────────────────────────────

        internal const uint SERVICE_STOPPED = 0x00000001;
        internal const uint SERVICE_START_PENDING = 0x00000002;
        internal const uint SERVICE_STOP_PENDING = 0x00000003;
        internal const uint SERVICE_RUNNING = 0x00000004;
        internal const uint SERVICE_CONTINUE_PENDING = 0x00000005;
        internal const uint SERVICE_PAUSE_PENDING = 0x00000006;
        internal const uint SERVICE_PAUSED = 0x00000007;

        // ── Service Controls ───────────────────────────────────────────

        internal const uint SERVICE_CONTROL_STOP = 0x00000001;
        internal const uint SERVICE_CONTROL_INTERROGATE = 0x00000004;

        // ── Win32 Error Codes ──────────────────────────────────────────

        internal const int ERROR_ACCESS_DENIED = 5;
        internal const int ERROR_SERVICE_ALREADY_EXISTS = 1073;
        internal const int ERROR_SERVICE_DOES_NOT_EXIST = 1060;
        internal const int ERROR_SERVICE_ALREADY_RUNNING = 1056;
        internal const int ERROR_SERVICE_NOT_ACTIVE = 1062;
        internal const int ERROR_SERVICE_MARKED_FOR_DELETE = 1072;
        internal const int ERROR_DUPLICATE_SERVICE_NAME = 1078;
        internal const int ERROR_INVALID_PARAMETER = 87;
        internal const int ERROR_INVALID_HANDLE = 6;
        internal const int ERROR_PATH_NOT_FOUND = 3;
        internal const int ERROR_SERVICE_REQUEST_TIMEOUT = 1053;
        internal const int ERROR_SERVICE_DEPENDENCY_FAIL = 1068;
        internal const int ERROR_DEPENDENT_SERVICES_RUNNING = 1051;
        internal const int ERROR_INVALID_SERVICE_CONTROL = 1052;
        internal const int ERROR_RPC_S_SERVER_UNAVAILABLE = 1722;

        // ── FormatMessage Flags ────────────────────────────────────────

        internal const uint FORMAT_MESSAGE_ALLOCATE_BUFFER = 0x00000100;
        internal const uint FORMAT_MESSAGE_FROM_SYSTEM = 0x00001000;
        internal const uint FORMAT_MESSAGE_IGNORE_INSERTS = 0x00000200;

        // ── SERVICE_STATUS Struct ──────────────────────────────────────

        [StructLayout(LayoutKind.Sequential)]
        internal struct SERVICE_STATUS
        {
            public uint dwServiceType;
            public uint dwCurrentState;
            public uint dwControlsAccepted;
            public uint dwWin32ExitCode;
            public uint dwServiceSpecificExitCode;
            public uint dwCheckPoint;
            public uint dwWaitHint;
        }

        // ── advapi32.dll P/Invoke ──────────────────────────────────────

        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        internal static extern SafeSCHandle OpenSCManager(
            string? lpMachineName,
            string? lpDatabaseName,
            uint dwAccess);

        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        internal static extern SafeSCHandle OpenService(
            SafeSCHandle hSCManager,
            string lpServiceName,
            uint dwDesiredAccess);

        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        internal static extern SafeSCHandle CreateServiceW(
            SafeSCHandle hSCManager,
            string lpServiceName,
            string lpDisplayName,
            uint dwDesiredAccess,
            uint dwServiceType,
            uint dwStartType,
            uint dwErrorControl,
            string lpBinaryPathName,
            string? lpLoadOrderGroup,
            IntPtr lpdwTagId,
            string? lpDependencies,
            string? lpServiceStartName,
            string? lpPassword);

        [DllImport("advapi32.dll", SetLastError = true)]
        internal static extern bool DeleteService(SafeSCHandle hService);

        [DllImport("advapi32.dll", SetLastError = true)]
        internal static extern bool StartServiceW(
            SafeSCHandle hService,
            uint dwNumServiceArgs,
            IntPtr lpServiceArgVectors);

        [DllImport("advapi32.dll", SetLastError = true)]
        internal static extern bool ControlService(
            SafeSCHandle hService,
            uint dwControl,
            ref SERVICE_STATUS lpServiceStatus);

        [DllImport("advapi32.dll", SetLastError = true)]
        internal static extern bool QueryServiceStatus(
            SafeSCHandle hService,
            ref SERVICE_STATUS lpServiceStatus);

        [DllImport("advapi32.dll", SetLastError = true)]
        internal static extern bool CloseServiceHandle(IntPtr hSCObject);

        // ── kernel32.dll P/Invoke (for FormatMessage + LocalFree) ─────

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        internal static extern uint FormatMessageW(
            uint dwFlags,
            IntPtr lpSource,
            uint dwMessageId,
            uint dwLanguageId,
            ref IntPtr lpBuffer,
            uint nSize,
            IntPtr va_list);

        [DllImport("kernel32.dll", SetLastError = true)]
        internal static extern IntPtr LocalFree(IntPtr hMem);
    }

    #endregion
}
