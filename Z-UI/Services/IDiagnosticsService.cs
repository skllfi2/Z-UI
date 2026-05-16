// IDiagnosticsService.cs - Interface for system diagnostics (Level 3 native)
// Updated: CheckWinwsProcessAsync → CheckWorkerProcessAsync (IPC-based)
using ZUI.Models;

namespace ZUI.Services;

/// <summary>
/// Service for running system diagnostics
/// </summary>
public interface IDiagnosticsService
{
    /// <summary>
    /// Run all diagnostic checks
    /// </summary>
    Task<List<DiagnosticResult>> RunAllChecksAsync();

    /// <summary>
    /// Check if WinDivert driver is loaded
    /// </summary>
    Task<DiagnosticResult> CheckWinDivertAsync();

    /// <summary>
    /// Check if Worker process is reachable via IPC
    /// </summary>
    Task<DiagnosticResult> CheckWorkerProcessAsync();

    /// <summary>
    /// Check if domain lists exist
    /// </summary>
    Task<DiagnosticResult> CheckDomainListsAsync();

    /// <summary>
    /// Check if binary files exist (.bin)
    /// </summary>
    Task<DiagnosticResult> CheckBinaryFilesAsync();

    /// <summary>
    /// Check if administrator rights are present
    /// </summary>
    Task<DiagnosticResult> CheckAdminRightsAsync();

    /// <summary>
    /// Test connectivity to a URL
    /// </summary>
    Task<DiagnosticResult> TestConnectivityAsync(string url, string name);

    /// <summary>
    /// Quick health check (returns overall status)
    /// </summary>
    Task<DiagnosticHealthStatus> QuickHealthCheckAsync();
}

/// <summary>
/// Result of a diagnostic check
/// </summary>
public class DiagnosticResult
{
    /// <summary>
    /// Name of the check
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Whether the check passed
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// Status message
    /// </summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>
    /// How to fix if failed
    /// </summary>
    public string? FixAction { get; set; }

    /// <summary>
    /// Error details if failed
    /// </summary>
    public string? ErrorDetails { get; set; }

    /// <summary>
    /// Severity of the issue
    /// </summary>
    public DiagnosticSeverity Severity { get; set; } = DiagnosticSeverity.Info;

    /// <summary>
    /// Category of the check
    /// </summary>
    public DiagnosticCategory Category { get; set; } = DiagnosticCategory.System;
}

/// <summary>
/// Severity level for diagnostic results
/// </summary>
public enum DiagnosticSeverity
{
    Info,
    Warning,
    Error,
    Critical
}

/// <summary>
/// Category of diagnostic check
/// </summary>
public enum DiagnosticCategory
{
    System,
    Network,
    Files,
    Process,
    Permissions
}

/// <summary>
/// Overall health status
/// </summary>
public class DiagnosticHealthStatus
{
    public bool IsHealthy { get; set; }
    public int PassedChecks { get; set; }
    public int TotalChecks { get; set; }
    public string Summary { get; set; } = string.Empty;
    public List<string> Issues { get; set; } = new();
}
