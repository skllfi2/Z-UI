// JsonElementHelper.cs - Utility for unwrapping System.Text.Json.JsonElement values
using System.Text.Json;

namespace ZUI.Models;

/// <summary>
/// Utility for unwrapping System.Text.Json.JsonElement values to native CLR types.
/// Needed because System.Text.Json deserializes 'object' properties as JsonElement,
/// which does not implement IConvertible and causes Convert.ToInt32() to crash.
/// </summary>
public static class JsonElementHelper
{
    /// <summary>
    /// Unwraps a System.Text.Json.JsonElement to its native CLR int.
    /// </summary>
    public static int? UnwrapToInt(object? value)
    {
        if (value is null) return null;
        if (value is int i) return i;
        if (value is JsonElement je && je.ValueKind == JsonValueKind.Number) return je.GetInt32();
        if (value is string s && int.TryParse(s, out var parsed)) return parsed;
        return Convert.ToInt32(value.ToString());
    }

    /// <summary>
    /// Unwraps a JsonElement value to string, stripping JSON quotes.
    /// </summary>
    public static string? UnwrapToString(object? value)
    {
        if (value is null) return null;
        if (value is string s) return s;
        if (value is JsonElement je) return je.ValueKind == JsonValueKind.String ? je.GetString() : je.ToString();
        return value.ToString();
    }
}
