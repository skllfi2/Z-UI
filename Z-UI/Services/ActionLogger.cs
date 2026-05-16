// ActionLogger.cs - Simple action logging for audit trail
namespace ZUI.Services;

/// <summary>
/// Logs user actions (start, stop, errors) to Debug output.
/// </summary>
public static class ActionLogger
{
    public static void LogStart(string strategy)
    {
        System.Diagnostics.Debug.WriteLine($"[Z-UI] Action: Start strategy={strategy}");
    }

    public static void LogStop()
    {
        System.Diagnostics.Debug.WriteLine("[Z-UI] Action: Stop");
    }

    public static void LogError(string context, string message)
    {
        System.Diagnostics.Debug.WriteLine($"[Z-UI] Action: Error in {context}: {message}");
    }
}
