// DiagnosticsService.cs - System diagnostics implementation (Level 3 native)
// DiagnosticsService.cs - System health checks via IIpcClientService
using Microsoft.Extensions.Logging;
using System.ComponentModel;
using System.Diagnostics;
using System.Net.Http;
using System.Security;

namespace ZUI.Services;

/// <summary>
/// Runs system diagnostics for Z-UI.
/// UI-side checks (admin, files, connectivity) + Worker status via IPC.
/// </summary>
public class DiagnosticsService : IDiagnosticsService, IDisposable
{
    private readonly ILogger<DiagnosticsService> _logger;
    private readonly IIpcClientService _ipc;
    private readonly string _zapretDir;
    private readonly HttpClient _httpClient;
    private bool _disposed;

    public DiagnosticsService(ILogger<DiagnosticsService> logger, IIpcClientService ipc)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _ipc = ipc ?? throw new ArgumentNullException(nameof(ipc));

        // Find zapret directory
        _zapretDir = FindZapretDirectory();

        // HTTP client for connectivity tests
        _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(10)
        };
    }

    private string FindZapretDirectory()
    {
        var baseDir = AppContext.BaseDirectory;
        var candidates = new[]
        {
            Path.Combine(baseDir, "zapret"),
            Path.Combine(baseDir, "..", "zapret"),
        };

        foreach (var dir in candidates)
        {
            var fullPath = Path.GetFullPath(dir);
            if (Directory.Exists(fullPath) && File.Exists(Path.Combine(fullPath, "winws.exe")))
                return fullPath;
        }

        return Path.Combine(baseDir, "zapret");
    }

    /// <inheritdoc/>
    public async Task<List<DiagnosticResult>> RunAllChecksAsync()
    {
        _logger.LogInformation("Running all diagnostics");

        var results = new List<DiagnosticResult>();

        // System checks
		results.Add(await CheckAdminRightsAsync().ConfigureAwait(false));
		results.Add(await CheckWinDivertAsync().ConfigureAwait(false));

        // IPC/Worker check (replaces old winws.exe check)
        results.Add(await CheckWorkerProcessAsync().ConfigureAwait(false));

		// File checks
		results.Add(await CheckDomainListsAsync().ConfigureAwait(false));
		results.Add(await CheckBinaryFilesAsync().ConfigureAwait(false));

		// Connectivity tests
		results.Add(await TestConnectivityAsync("https://www.youtube.com", "YouTube").ConfigureAwait(false));
		results.Add(await TestConnectivityAsync("https://discord.com", "Discord").ConfigureAwait(false));

        _logger.LogInformation("Diagnostics complete: {Passed}/{Total} checks passed",
            results.Count(r => r.Success), results.Count);

        return results;
    }

    /// <inheritdoc/>
    public async Task<DiagnosticResult> CheckWinDivertAsync()
    {
        _logger.LogDebug("Checking WinDivert driver");

        try
        {
            // Check if driver files exist
            var sysPath = Path.Combine(_zapretDir, "WinDivert64.sys");
            var dllPath = Path.Combine(_zapretDir, "WinDivert.dll");

            if (!File.Exists(sysPath))
            {
                return new DiagnosticResult
                {
                    Name = "WinDivert Driver",
                    Success = false,
                    Message = "Driver file not found",
                    FixAction = "Reinstall zapret or download WinDivert",
                    Severity = DiagnosticSeverity.Critical,
                    Category = DiagnosticCategory.Files
                };
            }

            if (!File.Exists(dllPath))
            {
                return new DiagnosticResult
                {
                    Name = "WinDivert DLL",
                    Success = false,
                    Message = "DLL file not found",
                    FixAction = "Reinstall zapret or download WinDivert",
                    Severity = DiagnosticSeverity.Critical,
                    Category = DiagnosticCategory.Files
                };
            }

            // Check if driver is loaded (via sc query)
		var output = await RunCommandAsync("sc", "query WinDivert").ConfigureAwait(false);

            if (output.Contains("RUNNING", StringComparison.OrdinalIgnoreCase))
            {
                return new DiagnosticResult
                {
                    Name = "WinDivert Driver",
                    Success = true,
                    Message = "Driver is running",
                    Category = DiagnosticCategory.System
                };
            }

            // Driver files exist but not loaded - this is OK, Worker will load it
            return new DiagnosticResult
            {
                Name = "WinDivert Driver",
                Success = true,
                Message = "Driver files present (Worker загрузит при запуске)",
                Category = DiagnosticCategory.System
            };
        }
        catch (Win32Exception ex)
        {
            _logger.LogError(ex, "Error checking WinDivert");
            return new DiagnosticResult
            {
                Name = "WinDivert Driver",
                Success = false,
                Message = "Error checking driver",
                ErrorDetails = ex.Message,
                FixAction = "Run as Administrator",
                Severity = DiagnosticSeverity.Warning,
                Category = DiagnosticCategory.System
            };
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogError(ex, "Error checking WinDivert");
            return new DiagnosticResult
            {
                Name = "WinDivert Driver",
                Success = false,
                Message = "Error checking driver",
                ErrorDetails = ex.Message,
                FixAction = "Run as Administrator",
                Severity = DiagnosticSeverity.Warning,
                Category = DiagnosticCategory.System
            };
        }
    }

    /// <inheritdoc/>
    public async Task<DiagnosticResult> CheckWorkerProcessAsync()
    {
        _logger.LogDebug("Checking Worker process via IPC");

        try
        {
            if (!_ipc.IsConnected)
            {
                return new DiagnosticResult
                {
                    Name = "Worker Service",
                    Success = false,
                    Message = "Worker не подключён (IPC)",
                    FixAction = "Убедитесь, что Z-UI Worker запущен (служба SYSTEM)",
                    Severity = DiagnosticSeverity.Warning,
                    Category = DiagnosticCategory.Process
                };
            }

            // Try to get bypass status as a health check
            var statusResult = await _ipc.GetBypassStatusAsync().ConfigureAwait(false);

            if (statusResult.IsSuccess && statusResult.Value != null)
            {
                var status = statusResult.Value;
                return new DiagnosticResult
                {
                    Name = "Worker Service",
                    Success = true,
                    Message = status.IsRunning
                        ? $"Worker работает, DPI bypass активен (стратегия: {status.StrategyId ?? "auto"})"
                        : "Worker работает, DPI bypass неактивен",
                    Category = DiagnosticCategory.Process
                };
            }

            // Connected but couldn't get status
            return new DiagnosticResult
            {
                Name = "Worker Service",
                Success = true,
                Message = "Worker подключён, но не удалось получить статус",
                Severity = DiagnosticSeverity.Info,
                Category = DiagnosticCategory.Process
            };
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogError(ex, "Error checking Worker process");
            return new DiagnosticResult
            {
                Name = "Worker Service",
                Success = false,
                Message = "Ошибка проверки Worker",
                ErrorDetails = ex.Message,
                Severity = DiagnosticSeverity.Error,
                Category = DiagnosticCategory.Process
            };
        }
        catch (TimeoutException ex)
        {
            _logger.LogError(ex, "Error checking Worker process");
            return new DiagnosticResult
            {
                Name = "Worker Service",
                Success = false,
                Message = "Ошибка проверки Worker",
                ErrorDetails = ex.Message,
                Severity = DiagnosticSeverity.Error,
                Category = DiagnosticCategory.Process
            };
        }
    }

    /// <inheritdoc/>
    public async Task<DiagnosticResult> CheckDomainListsAsync()
    {
        _logger.LogDebug("Checking domain lists");

        try
        {
            var listsPath = FindListsPath();
            var requiredFiles = new[]
            {
                "list-general.txt",
                "list-google.txt",
                "list-exclude.txt"
            };

            var missingFiles = new List<string>();
            foreach (var file in requiredFiles)
            {
                var path = Path.Combine(listsPath, file);
                if (!File.Exists(path))
                    missingFiles.Add(file);
            }

            if (missingFiles.Count == 0)
            {
                return new DiagnosticResult
                {
                    Name = "Domain Lists",
                    Success = true,
                    Message = "All required lists present",
                    Category = DiagnosticCategory.Files
                };
            }

            return new DiagnosticResult
            {
                Name = "Domain Lists",
                Success = false,
                Message = $"Missing: {string.Join(", ", missingFiles)}",
                FixAction = "Download zapret from GitHub",
                Severity = DiagnosticSeverity.Warning,
                Category = DiagnosticCategory.Files
            };
        }
        catch (IOException ex)
        {
            _logger.LogError(ex, "Error checking domain lists");
            return new DiagnosticResult
            {
                Name = "Domain Lists",
                Success = false,
                Message = "Error checking lists",
                ErrorDetails = ex.Message,
                Severity = DiagnosticSeverity.Error,
                Category = DiagnosticCategory.Files
            };
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogError(ex, "Error checking domain lists");
            return new DiagnosticResult
            {
                Name = "Domain Lists",
                Success = false,
                Message = "Error checking lists",
                ErrorDetails = ex.Message,
                Severity = DiagnosticSeverity.Error,
                Category = DiagnosticCategory.Files
            };
        }
    }

    /// <inheritdoc/>
    public async Task<DiagnosticResult> CheckBinaryFilesAsync()
    {
        _logger.LogDebug("Checking binary files");

        try
        {
            var binPath = FindBinPath();
            var requiredFiles = new[]
            {
                "quic_initial_www_google_com.bin",
                "tls_clienthello_www_google_com.bin"
            };

            var missingFiles = new List<string>();
            foreach (var file in requiredFiles)
            {
                var path = Path.Combine(binPath, file);
                if (!File.Exists(path))
                    missingFiles.Add(file);
            }

            if (missingFiles.Count == 0)
            {
                return new DiagnosticResult
                {
                    Name = "Binary Files",
                    Success = true,
                    Message = "All binary files present",
                    Category = DiagnosticCategory.Files
                };
            }

            return new DiagnosticResult
            {
                Name = "Binary Files",
                Success = false,
                Message = $"Missing: {string.Join(", ", missingFiles)}",
                FixAction = "Download zapret from GitHub",
                Severity = DiagnosticSeverity.Warning,
                Category = DiagnosticCategory.Files
            };
        }
        catch (IOException ex)
        {
            _logger.LogError(ex, "Error checking binary files");
            return new DiagnosticResult
            {
                Name = "Binary Files",
                Success = false,
                Message = "Error checking binaries",
                ErrorDetails = ex.Message,
                Severity = DiagnosticSeverity.Error,
                Category = DiagnosticCategory.Files
            };
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogError(ex, "Error checking binary files");
            return new DiagnosticResult
            {
                Name = "Binary Files",
                Success = false,
                Message = "Error checking binaries",
                ErrorDetails = ex.Message,
                Severity = DiagnosticSeverity.Error,
                Category = DiagnosticCategory.Files
            };
        }
    }

    /// <inheritdoc/>
    public async Task<DiagnosticResult> CheckAdminRightsAsync()
    {
        _logger.LogDebug("Checking admin rights");

        try
        {
            using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
            var principal = new System.Security.Principal.WindowsPrincipal(identity);
            var isAdmin = principal.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);

            return new DiagnosticResult
            {
                Name = "Administrator Rights",
                Success = isAdmin,
                Message = isAdmin ? "Running as Administrator" : "Not running as Administrator",
                FixAction = isAdmin ? null : "Right-click and 'Run as Administrator'",
                Severity = isAdmin ? DiagnosticSeverity.Info : DiagnosticSeverity.Warning,
                Category = DiagnosticCategory.Permissions
            };
        }
        catch (SecurityException ex)
        {
            _logger.LogError(ex, "Error checking admin rights");
            return new DiagnosticResult
            {
                Name = "Administrator Rights",
                Success = false,
                Message = "Error checking permissions",
                ErrorDetails = ex.Message,
                Severity = DiagnosticSeverity.Error,
                Category = DiagnosticCategory.Permissions
            };
        }
    }

    /// <inheritdoc/>
    public async Task<DiagnosticResult> TestConnectivityAsync(string url, string name)
    {
        _logger.LogDebug("Testing connectivity to: {Url}", url);

        try
        {
		var response = await _httpClient.GetAsync(new Uri(url)).ConfigureAwait(false);

            if (response.IsSuccessStatusCode)
            {
                return new DiagnosticResult
                {
                    Name = $"{name} Connectivity",
                    Success = true,
                    Message = $"HTTP {(int)response.StatusCode} OK",
                    Category = DiagnosticCategory.Network
                };
            }

            return new DiagnosticResult
            {
                Name = $"{name} Connectivity",
                Success = false,
                Message = $"HTTP {(int)response.StatusCode}",
                FixAction = "Check network connection or enable protection",
                Severity = DiagnosticSeverity.Warning,
                Category = DiagnosticCategory.Network
            };
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Connectivity test failed for: {Url}", url);
            return new DiagnosticResult
            {
                Name = $"{name} Connectivity",
                Success = false,
                Message = "Connection failed",
                ErrorDetails = ex.Message,
                FixAction = "Enable protection or check DNS settings",
                Severity = DiagnosticSeverity.Warning,
                Category = DiagnosticCategory.Network
            };
        }
        catch (TaskCanceledException)
        {
            return new DiagnosticResult
            {
                Name = $"{name} Connectivity",
                Success = false,
                Message = "Connection timeout",
                FixAction = "Enable protection or check DNS settings",
                Severity = DiagnosticSeverity.Warning,
                Category = DiagnosticCategory.Network
            };
        }
    }

    /// <inheritdoc/>
    public async Task<DiagnosticHealthStatus> QuickHealthCheckAsync()
    {
        _logger.LogDebug("Running quick health check");

		var results = await RunAllChecksAsync().ConfigureAwait(false);
        var passed = results.Count(r => r.Success);
        var total = results.Count;

        var issues = results
            .Where(r => !r.Success)
            .Select(r => $"{r.Name}: {r.Message}")
            .ToList();

        return new DiagnosticHealthStatus
        {
            IsHealthy = passed == total,
            PassedChecks = passed,
            TotalChecks = total,
            Summary = passed == total ? "All systems operational" : $"{passed}/{total} checks passed",
            Issues = issues
        };
    }

    private string FindListsPath()
    {
        var candidates = new[]
        {
            Path.Combine(_zapretDir, "lists"),
            Path.Combine(_zapretDir, "strategies", "lists"),
        };

        foreach (var dir in candidates)
        {
            if (Directory.Exists(dir) && File.Exists(Path.Combine(dir, "list-google.txt")))
                return dir;
        }

        return Path.Combine(_zapretDir, "lists");
    }

    private string FindBinPath()
    {
        var candidates = new[]
        {
            _zapretDir,
            Path.Combine(_zapretDir, "bin"),
            Path.Combine(_zapretDir, "strategies", "bin"),
        };

        foreach (var dir in candidates)
        {
            if (File.Exists(Path.Combine(dir, "quic_initial_www_google_com.bin")))
                return dir;
        }

        return _zapretDir;
    }

    private async Task<string> RunCommandAsync(string fileName, string arguments)
    {
        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = fileName,
                    Arguments = arguments,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                }
            };

		process.Start();
		var output = await process.StandardOutput.ReadToEndAsync().ConfigureAwait(false);
		await process.WaitForExitAsync().ConfigureAwait(false);

            return output;
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            _logger.LogError(ex, "Command failed: {FileName} {Arguments}", fileName, arguments);
            return string.Empty;
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogError(ex, "Command failed: {FileName} {Arguments}", fileName, arguments);
            return string.Empty;
        }
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (_disposed) return;

        if (disposing)
        {
            _httpClient?.Dispose();
        }

        _disposed = true;
    }
}
