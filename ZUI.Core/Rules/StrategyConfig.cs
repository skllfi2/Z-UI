// ═══════════════════════════════════════════════════════════════
// ZUI.Core / Rules / StrategyConfig.cs
// Конфигурация стратегии (= один BAT файл или JSON конфиг)
// ═══════════════════════════════════════════════════════════════

namespace ZUI.Core.Rules;

/// <summary>
/// Режим Game Filter для стратегии.
/// </summary>
public enum GameFilterMode
{
    None,
    PoE2,
    General,
}

/// <summary>
/// Конфигурация стратегии обхода DPI.
/// Аналог одного BAT файла из zapret/strategies/.
/// Содержит набор правил FilterRule и WinDivert фильтр.
/// </summary>
public sealed class StrategyConfig
{
    /// <summary>Уникальный идентификатор стратегии.</summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>Отображаемое имя стратегии.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>Описание стратегии.</summary>
    public string? Description { get; init; }

    /// <summary>WinDivert фильтр-строка (комбинируется из правил).
    /// Например: "outbound and (tcp.DstPort == 80 or tcp.DstPort == 443 or udp.DstPort == 443)"</summary>
    public string WinDivertFilter { get; init; } = string.Empty;

    /// <summary>Упорядоченный список правил фильтрации.</summary>
    public FilterRule[] Rules { get; init; } = [];

    /// <summary>Режим Game Filter.</summary>
    public GameFilterMode GameFilter { get; init; } = GameFilterMode.None;

    /// <summary>Исходный BAT файл (если стратегия загружена из BAT). null = загружена из JSON.</summary>
    public string? SourceBatFile { get; init; }

    /// <summary>Рейтинг стратегии (user feedback).</summary>
    public int Rating { get; init; }

    /// <summary>Включена ли стратегия.</summary>
    public bool IsEnabled { get; init; } = true;
}
