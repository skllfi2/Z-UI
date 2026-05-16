// ProtectionResult.cs - Result type for protection/bypass operations
namespace ZUI.Services;

/// <summary>
/// Result of a protection operation
/// </summary>
public record ProtectionResult(bool Success, string? Message = null, string? Strategy = null)
{
    public static ProtectionResult Succeeded(string strategy) => new(true, null, strategy);
    public static ProtectionResult Failed(string error) => new(false, error);
}
