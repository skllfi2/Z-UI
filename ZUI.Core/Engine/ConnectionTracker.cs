// ═══════════════════════════════════════════════════════════════
// ZUI.Core / Engine / ConnectionTracker.cs
// Отслеживание пакетов по соединениям для DPI desync cutoff
// Применять десинхронизацию ТОЛЬКО к первым N пакетам соединения
// Thread-safe: ConcurrentDictionary
// ═══════════════════════════════════════════════════════════════

using System.Collections.Concurrent;
using System.Net;

namespace ZUI.Core.Engine;

/// <summary>
/// Ключ соединения: 5-tuple (srcIp, srcPort, dstIp, dstPort, protocol).
/// </summary>
public readonly record struct ConnectionKey(
    IPAddress SrcIp,
    ushort SrcPort,
    IPAddress DstIp,
    ushort DstPort,
    byte Protocol)
{
    /// <summary>Создать ключ из ParsedPacket-подобных данных.</summary>
    public ConnectionKey(IPAddress srcIp, ushort srcPort, IPAddress dstIp, ushort dstPort, bool isTcp)
        : this(srcIp, srcPort, dstIp, dstPort, isTcp ? (byte)6 : (byte)17) { }
}

/// <summary>
/// Отслеживание количества пакетов по TCP/UDP соединениям.
/// Для DPI desync cutoff: применять десинхронизацию только к первым N пакетам.
/// </summary>
public sealed class ConnectionTracker
{
    private readonly ConcurrentDictionary<ConnectionKey, ConnectionEntry> _connections = new();

    /// <summary>
    /// Запись в трекере: счётчик пакетов + время последней активности.
    /// </summary>
    private record struct ConnectionEntry(int Count, DateTime LastSeen);

    /// <summary>
    /// Проверить, нужно ли применять десинхронизацию к данному пакету.
    /// Увеличивает счётчик пакетов. Возвращает true если count &lt;= cutoff.
    /// </summary>
    public bool ShouldDesync(ConnectionKey key, int cutoff)
    {
        if (cutoff <= 0)
            return false;

        var now = DateTime.UtcNow;
        var entry = _connections.AddOrUpdate(
            key,
            _ => new ConnectionEntry(1, now),
            (_, existing) => new ConnectionEntry(existing.Count + 1, now));

        return entry.Count <= cutoff;
    }

    /// <summary>
    /// Увеличить счётчик пакетов для соединения.
    /// </summary>
    public void Increment(ConnectionKey key)
    {
        var now = DateTime.UtcNow;
        _connections.AddOrUpdate(key,
            _ => new ConnectionEntry(1, now),
            (_, existing) => new ConnectionEntry(existing.Count + 1, now));
    }

    /// <summary>
    /// Получить текущее количество пакетов для соединения (0 если не отслеживается).
    /// </summary>
    public int GetCount(ConnectionKey key)
    {
        return _connections.TryGetValue(key, out var entry) ? entry.Count : 0;
    }

    /// <summary>
    /// Удалить соединение из отслеживания.
    /// </summary>
    public void Remove(ConnectionKey key)
    {
        _connections.TryRemove(key, out _);
    }

    /// <summary>
    /// Удалить все соединения.
    /// </summary>
    public void Clear()
    {
        _connections.Clear();
    }

    /// <summary>
    /// Количество отслеживаемых соединений.
    /// </summary>
    public int Count => _connections.Count;

    /// <summary>
    /// Очистить устаревшие соединения (неактивные дольше maxAge).
    /// </summary>
    public void Cleanup(TimeSpan maxAge)
    {
        var cutoff = DateTime.UtcNow - maxAge;

        foreach (var kvp in _connections)
        {
            if (kvp.Value.LastSeen < cutoff)
                _connections.TryRemove(kvp.Key, out _);
        }
    }
}
