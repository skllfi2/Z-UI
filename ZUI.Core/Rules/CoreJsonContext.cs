// ═══════════════════════════════════════════════════════════════
// ZUI.Core / Rules / CoreJsonContext.cs
// AOT-compatible Source Generator для JSON сериализации ZUI.Core
// Стратегии, правила, конфигурации — всё через Source Generator
// ═══════════════════════════════════════════════════════════════

using System.Text.Json;
using System.Text.Json.Serialization;
using ZUI.Core.Rules;

namespace ZUI.Core;

/// <summary>
/// AOT-compatible JsonSerializerContext для ZUI.Core типов.
/// Используется StrategyConfigLoader для сериализации/десериализации стратегий.
/// </summary>
[JsonSerializable(typeof(StrategyConfig))]
[JsonSerializable(typeof(FilterRule))]
[JsonSerializable(typeof(PortRange))]
[JsonSerializable(typeof(FilterRule[]))]
[JsonSerializable(typeof(PortRange[]))]
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    WriteIndented = true)]
internal sealed partial class CoreJsonContext : JsonSerializerContext;
