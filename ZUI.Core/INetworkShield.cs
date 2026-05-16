// ═══════════════════════════════════════════════════════════════
// ZUI.Core / INetworkShield.cs
// Фасад ядра системы: BlockDetector + BypassEngine + TrafficWatch
// Три столпа: понимание блокировок, обход, контроль трафика
// ═══════════════════════════════════════════════════════════════

using ZUI.Core.Engine;
using ZUI.Core.Traffic;

namespace ZUI.Core;

// ── IBlockDetector ──────────────────────────────────────────

/// <summary>
/// Детектор блокировок: пассивный анализ + активные пробы.
/// Понимает что заблокировано и как — не работает вслепую.
/// </summary>
public interface IBlockDetector
{
    /// <summary>Всего обнаруженных блокировок.</summary>
    int TotalBlocks { get; }

    /// <summary>Последние обнаруженные блокировки.</summary>
    BlockRecord[] GetRecentBlocks(int limit = 20);

    /// <summary>Агрегированная статистика блокировок.</summary>
    BlockStats GetStats();

    /// <summary>Очистить историю блокировок.</summary>
    void ClearBlocks();

    /// <summary>Событие обнаружения новой блокировки.</summary>
    event Action<BlockRecord> OnBlockDetected;
}

// ── IBypassEngine ───────────────────────────────────────────

/// <summary>
/// Движок обхода блокировок: DPI + DNS + proxy routing.
/// </summary>
public interface IBypassEngine
{
    /// <summary>Движок запущен?</summary>
    bool IsRunning { get; }

    /// <summary>Текущая стратегия обхода.</summary>
    string? ActiveStrategy { get; }

    /// <summary>Запустить обход с указанной стратегией.</summary>
    Task<Result> StartAsync(string strategyId, CancellationToken ct = default);

    /// <summary>Остановить обход.</summary>
    Task<Result> StopAsync(CancellationToken ct = default);

    /// <summary>Получить статистику обхода.</summary>
    BypassStats GetStats();
}

/// <summary>
/// Статистика обхода.
/// </summary>
public sealed class BypassStats
{
    public long PacketsProcessed { get; init; }
    public long PacketsBypassed { get; init; }
    public double UptimeSeconds { get; init; }
    public int ActiveConnections { get; init; }
}

// ── ITrafficWatch ───────────────────────────────────────────

/// <summary>
/// Наблюдатель трафика: полная статистика per-app, per-domain, global.
/// </summary>
public interface ITrafficWatch
{
    /// <summary>Глобальная статистика трафика.</summary>
    TrafficSnapshot GetGlobalStats();

    /// <summary>Статистика по приложениям.</summary>
    AppTrafficInfo[] GetAppStats();

    /// <summary>Статистика по доменам.</summary>
    DomainTrafficInfo[] GetDomainStats();

    /// <summary>Сбросить статистику.</summary>
    void Reset();
}

/// <summary>
/// Статистика приложения.
/// </summary>
public sealed class AppTrafficInfo
{
    public string ProcessName { get; init; } = string.Empty;
    public int Pid { get; init; }
    public long BytesSent { get; init; }
    public long BytesReceived { get; init; }
    public int ConnectionCount { get; init; }
    public DateTime StartedAt { get; init; }
}

/// <summary>
/// Статистика домена.
/// </summary>
public sealed class DomainTrafficInfo
{
    public string Domain { get; init; } = string.Empty;
    public long BytesSent { get; init; }
    public long BytesReceived { get; init; }
    public int ConnectionCount { get; init; }
}

// ── INetworkShield ──────────────────────────────────────────

/// <summary>
/// Фасад ядра Z-UI.
/// Объединяет три столпа:
/// 1. BlockDetector — понимает что заблокировано
/// 2. BypassEngine — обходит блокировки (DPI + DNS + proxy)
/// 3. TrafficWatch — контролирует трафик
/// </summary>
public interface INetworkShield
{
    /// <summary>Детектор блокировок.</summary>
    IBlockDetector BlockDetector { get; }

    /// <summary>Движок обхода.</summary>
    IBypassEngine BypassEngine { get; }

    /// <summary>Наблюдатель трафика.</summary>
    ITrafficWatch TrafficWatch { get; }

    /// <summary>Система запущена?</summary>
    bool IsRunning { get; }

    /// <summary>Запустить все модули.</summary>
    Task<Result> StartAsync(string strategyId, CancellationToken ct = default);

    /// <summary>Остановить все модули.</summary>
    Task<Result> StopAsync(CancellationToken ct = default);
}
