// ═══════════════════════════════════════════════════════════════
// ZUI.Proxy / Traffic / TrafficStats.cs
/// Статистика трафика одного соединения:
/// байты sent/recv, скорость, время соединения
// ═══════════════════════════════════════════════════════════════

namespace ZUI.Core.Traffic;

/// <summary>
/// Статистика трафика для одного соединения.
/// Потокобезопасная (Interlocked операции).
/// </summary>
public sealed class TrafficStats
{
    private long _bytesSent;
    private long _bytesReceived;
    private readonly DateTime _connectedAt;

    public TrafficStats()
    {
        _connectedAt = DateTime.UtcNow;
    }

    /// <summary>Время установления соединения.</summary>
    public DateTime ConnectedAt => _connectedAt;

    /// <summary>Байт отправлено (upstream: app→proxy).</summary>
    public long BytesSent => Volatile.Read(ref _bytesSent);

    /// <summary>Байт получено (downstream: proxy→app).</summary>
    public long BytesReceived => Volatile.Read(ref _bytesReceived);

    /// <summary>Общий объём трафика.</summary>
    public long TotalBytes => BytesSent + BytesReceived;

    /// <summary>Длительность соединения.</summary>
    public TimeSpan Duration => DateTime.UtcNow - _connectedAt;

    /// <summary>Средняя скорость (байт/сек), общая.</summary>
    public double AverageBytesPerSecond
    {
        get
        {
            var duration = Duration.TotalSeconds;
            return duration > 0 ? TotalBytes / duration : 0;
        }
    }

    /// <summary>
    /// Зафиксировать отправленные байты (upstream).
    /// </summary>
    public void AddSent(long bytes)
    {
        if (bytes > 0)
            Interlocked.Add(ref _bytesSent, bytes);
    }

    /// <summary>
    /// Зафиксировать полученные байты (downstream).
    /// </summary>
    public void AddReceived(long bytes)
    {
        if (bytes > 0)
            Interlocked.Add(ref _bytesReceived, bytes);
    }

    public override string ToString()
    {
        var sent = FormatBytes(BytesSent);
        var recv = FormatBytes(BytesReceived);
        return $"↑{sent} ↓{recv} ({FormatBytes(TotalBytes)} total, {AverageBytesPerSecond:F0} B/s)";
    }

    private static string FormatBytes(long bytes)
    {
        return bytes switch
        {
            < 1024 => $"{bytes} B",
            < 1024 * 1024 => $"{bytes / 1024.0:F1} KB",
            < 1024 * 1024 * 1024 => $"{bytes / (1024.0 * 1024):F1} MB",
            _ => $"{bytes / (1024.0 * 1024 * 1024):F1} GB",
        };
    }
}
