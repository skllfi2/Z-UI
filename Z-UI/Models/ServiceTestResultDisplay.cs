// ServiceTestResultDisplay.cs - Display model for service test result
namespace ZUI.Models;

/// <summary>
/// Display model for service test result
/// </summary>
public record ServiceTestResultDisplay
{
    /// <summary>
    /// Service identifier
    /// </summary>
    public string ServiceId { get; init; } = string.Empty;

    /// <summary>
    /// Whether test passed
    /// </summary>
    public bool Passed { get; init; }

    /// <summary>
    /// Latency in milliseconds
    /// </summary>
    public int? LatencyMs { get; init; }

    /// <summary>
    /// Human-readable service name
    /// </summary>
    public string ServiceName => ServiceId switch
    {
        "youtube" => "YouTube",
        "discord" => "Discord",
        "telegram" => "Telegram",
        "whatsapp" => "WhatsApp",
        "instagram" => "Instagram",
        "twitter" => "Twitter/X",
        "facebook" => "Facebook",
        "tiktok" => "TikTok",
        "poe2" => "Path of Exile 2",
        "steam" => "Steam",
        "twitch" => "Twitch",
        _ when ServiceId.StartsWith("custom:") => ServiceId.Substring(7),
        _ => ServiceId
    };

    /// <summary>
    /// Human-readable latency text
    /// </summary>
    public string LatencyText => LatencyMs switch
    {
        null => Passed ? "OK" : "—",
        < 100 => $"{LatencyMs}ms",
        < 500 => $"{LatencyMs}ms",
        < 1000 => $"{LatencyMs}ms",
        _ => $"{LatencyMs / 1000f:F1}s"
    };

    public ServiceTestResultDisplay(string serviceId, bool passed, int? latencyMs)
    {
        ServiceId = serviceId;
        Passed = passed;
        LatencyMs = latencyMs;
    }
}
