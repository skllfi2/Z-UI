// ═══════════════════════════════════════════════════════════════
// ZUI.Proxy / Traffic / TrafficMonitor.cs
/// Агрегатор трафика: per-connection + глобальная статистика
/// + события для UI (уведомления об изменениях)
// ═══════════════════════════════════════════════════════════════

using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace ZUI.Core.Traffic;

/// <summary>
/// Монитор трафика: агрегирует статистику по всем соединениям.
/// Отслеживает: общие байты, количество соединений, скорость.
/// </summary>
public sealed class TrafficMonitor
{
    private readonly ILogger _logger;
    private readonly ConcurrentDictionary<string, TrafficStats> _connections = new();

    // Глобальные счётчики (Interlocked)
    private long _totalBytesSent;
    private long _totalBytesReceived;
    private long _totalConnections;
    private long _activeConnections;

    // Для расчёта скорости (скользящее окно)
    private long _lastTotalBytes;
    private DateTime _lastSpeedCheck = DateTime.UtcNow;
    private double _currentBytesPerSecond;
    private readonly Lock _speedLock = new();

    public TrafficMonitor(ILogger<TrafficMonitor>? logger = null)
    {
        _logger = logger ?? NullLoggerFactory.Instance.CreateLogger<TrafficMonitor>();
    }

    // ── Агрегированная статистика ─────────────────────────

    /// <summary>Всего байт отправлено (все соединения).</summary>
    public long TotalBytesSent => Volatile.Read(ref _totalBytesSent);

    /// <summary>Всего байт получено (все соединения).</summary>
    public long TotalBytesReceived => Volatile.Read(ref _totalBytesReceived);

    /// <summary>Общий трафик (sent + received).</summary>
    public long TotalBytes => TotalBytesSent + TotalBytesReceived;

    /// <summary>Всего соединений за всё время.</summary>
    public long TotalConnections => Volatile.Read(ref _totalConnections);

    /// <summary>Активных соединений сейчас.</summary>
    public long ActiveConnections => Volatile.Read(ref _activeConnections);

    /// <summary>Текущая скорость (байт/сек) — обновляется при GetSpeed().</summary>
    public double CurrentBytesPerSecond => Volatile.Read(ref _currentBytesPerSecond);

    // ── Управление соединениями ───────────────────────────

    /// <summary>
    /// Зарегистрировать новое соединение.
    /// </summary>
    public void AddConnection(string connectionId)
    {
        _connections[connectionId] = new TrafficStats();
        Interlocked.Increment(ref _totalConnections);
        Interlocked.Increment(ref _activeConnections);
    }

    /// <summary>
    /// Удалить соединение (при закрытии).
    /// </summary>
    public void RemoveConnection(string connectionId)
    {
        _connections.TryRemove(connectionId, out _);
        Interlocked.Decrement(ref _activeConnections);
    }

    // ── Запись трафика ────────────────────────────────────

    /// <summary>
    /// Зафиксировать передачу байт для соединения.
    /// </summary>
    /// <param name="connectionId">ID соединения.</param>
    /// <param name="bytes">Количество байт.</param>
    /// <param name="isUpstream">true = отправлено (app→proxy), false = получено (proxy→app).</param>
    public void RecordBytes(string connectionId, long bytes, bool isUpstream)
    {
        if (bytes <= 0) return;

        // Per-connection
        if (_connections.TryGetValue(connectionId, out var stats))
        {
            if (isUpstream)
                stats.AddSent(bytes);
            else
                stats.AddReceived(bytes);
        }

        // Глобальная статистика
        if (isUpstream)
            Interlocked.Add(ref _totalBytesSent, bytes);
        else
            Interlocked.Add(ref _totalBytesReceived, bytes);
    }

    // ── Скорость ──────────────────────────────────────────

    /// <summary>
    /// Рассчитать и вернуть текущую скорость (байт/сек).
    /// Использует скользящее окно с момента последнего вызова.
    /// </summary>
    public double UpdateSpeed()
    {
        lock (_speedLock)
        {
            var now = DateTime.UtcNow;
            var currentTotal = Volatile.Read(ref _totalBytesSent) + Volatile.Read(ref _totalBytesReceived);
            var lastTotal = Volatile.Read(ref _lastTotalBytes);

            var elapsed = (now - _lastSpeedCheck).TotalSeconds;
            if (elapsed > 0)
            {
                var bytesDelta = currentTotal - lastTotal;
                var speed = bytesDelta / elapsed;
                Volatile.Write(ref _currentBytesPerSecond, speed);
            }

            Volatile.Write(ref _lastTotalBytes, currentTotal);
            _lastSpeedCheck = now;

            return Volatile.Read(ref _currentBytesPerSecond);
        }
    }

    // ── Получение статистики ──────────────────────────────

    /// <summary>
    /// Получить статистику для конкретного соединения.
    /// </summary>
    public TrafficStats? GetConnectionStats(string connectionId)
    {
        return _connections.TryGetValue(connectionId, out var stats) ? stats : null;
    }

    /// <summary>
    /// Получить агрегированный снимок всей статистики.
    /// </summary>
    public TrafficSnapshot GetSnapshot()
    {
        return new TrafficSnapshot
        {
            TotalBytesSent = TotalBytesSent,
            TotalBytesReceived = TotalBytesReceived,
            TotalConnections = TotalConnections,
            ActiveConnections = ActiveConnections,
            CurrentBytesPerSecond = CurrentBytesPerSecond,
        };
    }

    /// <summary>
    /// Сбросить всю статистику.
    /// </summary>
    public void Reset()
    {
        _connections.Clear();
        Volatile.Write(ref _totalBytesSent, 0L);
        Volatile.Write(ref _totalBytesReceived, 0L);
        Volatile.Write(ref _totalConnections, 0L);
        Volatile.Write(ref _activeConnections, 0L);
        Volatile.Write(ref _lastTotalBytes, 0L);
        Volatile.Write(ref _currentBytesPerSecond, 0.0);
        lock (_speedLock)
        {
            _lastSpeedCheck = DateTime.UtcNow;
        }
    }
}

/// <summary>
/// Неизменяемый снимок статистики трафика.
/// </summary>
public sealed class TrafficSnapshot
{
    public long TotalBytesSent { get; init; }
    public long TotalBytesReceived { get; init; }
    public long TotalConnections { get; init; }
    public long ActiveConnections { get; init; }
    public double CurrentBytesPerSecond { get; init; }

    public override string ToString() =>
        $"↑{TotalBytesSent} ↓{TotalBytesReceived} " +
        $"conn={ActiveConnections}/{TotalConnections} " +
        $"speed={CurrentBytesPerSecond:F0} B/s";
}
