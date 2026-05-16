// ═══════════════════════════════════════════════════════════════
// ZUI.Core / Engine / PassiveBlockAnalyzer.cs
// Пассивный анализатор блокировок на основе WinDivert событий
// Определяет тип блокировки: RST, timeout, DPI drop, TTL anomaly
// ═══════════════════════════════════════════════════════════════

using System.Collections.Concurrent;
using System.Net;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace ZUI.Core.Engine;

/// <summary>
/// Тип обнаруженной блокировки.
/// </summary>
public enum BlockType
{
    /// <summary>TCP Reset (RST) — сервер или DPI сбросил соединение.</summary>
    TcpReset,

    /// <summary>Silent drop — нет SYN-ACK, таймаут (DPI/фаервол).</summary>
    SilentDrop,

    /// <summary>DPI drop — характерный паттерн (малые пакеты 16-20KB).</summary>
    DpiDrop,

    /// <summary>TTL anomaly — аномальный TTL, возможна манипуляция.</summary>
    TtlAnomaly,

    /// <summary>DNS mismatch — локальный и DoH резолв дают разные IP.</summary>
    DnsMismatch,
}

/// <summary>
/// Уровень уверенности в обнаружении блокировки.
/// </summary>
public enum BlockConfidence
{
    Low = 0,
    Medium = 1,
    High = 2,
    Certain = 3,
}

/// <summary>
/// Запись о блокировке.
/// </summary>
public sealed class BlockRecord
{
    /// <summary>Домен или IP цели.</summary>
    public string Target { get; init; } = string.Empty;

    /// <summary>Тип блокировки.</summary>
    public BlockType Type { get; init; }

    /// <summary>Уровень уверенности.</summary>
    public BlockConfidence Confidence { get; init; }

    /// <summary>Время обнаружения.</summary>
    public DateTime DetectedAt { get; init; } = DateTime.UtcNow;

    /// <summary>Количество повторений (для агрегации).</summary>
    public int Occurrences { get; set; } = 1;

    /// <summary>Описание (для UI).</summary>
    public string Description { get; init; } = string.Empty;
}

/// <summary>
/// Событие подключения для анализа.
/// </summary>
public sealed class ConnectionEvent
{
    /// <summary>Время SYN.</summary>
    public DateTime SynTime { get; set; } = DateTime.UtcNow;

    /// <summary>Время SYN-ACK (если получен).</summary>
    public DateTime? SynAckTime { get; set; }

    /// <summary>Время RST (если получен).</summary>
    public DateTime? RstTime { get; set; }

    /// <summary>Время FIN (если получен).</summary>
    public DateTime? FinTime { get; set; }

    /// <summary>Размер первого пакета (для DPI drop detection).</summary>
    public int? FirstPacketSize { get; set; }

    /// <summary>TTL первого пакета.</summary>
    public byte? Ttl { get; set; }

    /// <summary>Целевой IP.</summary>
    public IPAddress DestinationIp { get; set; } = IPAddress.None;

    /// <summary>Целевой порт.</summary>
    public int DestinationPort { get; set; }

    /// <summary>Домен (если известен из DnsReverseCache).</summary>
    public string? Domain { get; set; }

    /// <summary>Имя процесса.</summary>
    public string? ProcessName { get; set; }

    /// <summary>Соединение завершено?</summary>
    public bool IsCompleted => RstTime.HasValue || FinTime.HasValue;

    /// <summary>Время ожидания SYN-ACK.</summary>
    public double SynAckDelayMs => SynAckTime.HasValue
        ? (SynAckTime.Value - SynTime).TotalMilliseconds
        : (DateTime.UtcNow - SynTime).TotalMilliseconds;
}

/// <summary>
/// Пассивный анализатор блокировок.
/// 
/// Мониторит события подключений (SYN, SYN-ACK, RST, FIN) и определяет:
/// - TCP Reset (RST после SYN) — блокировка на уровне TCP
/// - Silent Drop (нет SYN-ACK в течение timeout) — DPI/фаервол
/// - DPI Drop (малые пакеты 16-20KB) — характерный паттерн DPI
/// - TTL Anomaly (аномальный TTL) — возможна манипуляция
/// 
/// Результаты доступны через GetRecentBlocks() и событие OnBlockDetected.
/// </summary>
public sealed class PassiveBlockAnalyzer
{
    private const int MaxEvents = 1000;
    private const int MaxBlocks = 100;
    private const int SynAckTimeoutMs = 5000; // 5 секунд
    private const int DpiDropMinSize = 16_000; // 16 KB
    private const int DpiDropMaxSize = 20_480; // 20 KB
    private const byte NormalTtlMin = 32;
    private const byte NormalTtlMax = 128;

    private readonly ConcurrentDictionary<string, ConnectionEvent> _events = new();
    private readonly ConcurrentQueue<BlockRecord> _blocks = new();
    private readonly ILogger _logger;

    private int _eventCount;
    private int _blockCount;

    /// <summary>Событие обнаружения блокировки.</summary>
    public event Action<BlockRecord>? OnBlockDetected;

    public PassiveBlockAnalyzer(ILogger<PassiveBlockAnalyzer>? logger = null)
    {
        _logger = logger ?? NullLoggerFactory.Instance.CreateLogger<PassiveBlockAnalyzer>();
    }

    // ── Ввод событий ───────────────────────────────────────

    /// <summary>
    /// Зафиксировать SYN пакет (начало соединения).
    /// </summary>
    public void RecordSyn(string connectionKey, IPAddress dstIp, int dstPort, string? domain = null, string? processName = null)
    {
        var evt = new ConnectionEvent
        {
            DestinationIp = dstIp,
            DestinationPort = dstPort,
            Domain = domain,
            ProcessName = processName,
        };

        _events[connectionKey] = evt;
        Interlocked.Increment(ref _eventCount);
        PruneEvents();
    }

    /// <summary>
    /// Зафиксировать SYN-ACK (успешное рукопожатие).
    /// </summary>
    public void RecordSynAck(string connectionKey)
    {
        if (_events.TryGetValue(connectionKey, out var evt))
        {
            evt.SynAckTime = DateTime.UtcNow;
        }
    }

    /// <summary>
    /// Зафиксировать RST пакет (сброс соединения).
    /// </summary>
    public void RecordRst(string connectionKey)
    {
        if (_events.TryGetValue(connectionKey, out var evt))
        {
            evt.RstTime = DateTime.UtcNow;
            AnalyzeRst(evt);
            _events.TryRemove(connectionKey, out _);
        }
    }

    /// <summary>
    /// Зафиксировать FIN пакет (нормальное завершение).
    /// </summary>
    public void RecordFin(string connectionKey)
    {
        if (_events.TryGetValue(connectionKey, out var evt))
        {
            evt.FinTime = DateTime.UtcNow;
            _events.TryRemove(connectionKey, out _);
        }
    }

    /// <summary>
    /// Зафиксировать размер первого пакета (для DPI drop detection).
    /// </summary>
    public void RecordFirstPacketSize(string connectionKey, int size)
    {
        if (_events.TryGetValue(connectionKey, out var evt))
        {
            evt.FirstPacketSize = size;
            AnalyzeDpiDrop(evt);
        }
    }

    /// <summary>
    /// Зафиксировать TTL пакета (для anomaly detection).
    /// </summary>
    public void RecordTtl(string connectionKey, byte ttl)
    {
        if (_events.TryGetValue(connectionKey, out var evt))
        {
            evt.Ttl = ttl;
            AnalyzeTtlAnomaly(evt);
        }
    }

    // ── Анализ ─────────────────────────────────────────────

    /// <summary>
    /// Проверить таймауты SYN-ACK (вызывать периодически).
    /// </summary>
    public void CheckTimeouts()
    {
        var now = DateTime.UtcNow;
        var timedOut = new List<string>();

        foreach (var kvp in _events)
        {
            var evt = kvp.Value;
            if (!evt.SynAckTime.HasValue && !evt.RstTime.HasValue && !evt.FinTime.HasValue)
            {
                if ((now - evt.SynTime).TotalMilliseconds > SynAckTimeoutMs)
                {
                    timedOut.Add(kvp.Key);
                    EmitBlock(new BlockRecord
                    {
                        Target = evt.Domain ?? evt.DestinationIp.ToString(),
                        Type = BlockType.SilentDrop,
                        Confidence = BlockConfidence.Medium,
                        Description = $"SYN-ACK timeout ({SynAckTimeoutMs}ms) — possible DPI/firewall drop",
                    });
                }
            }
        }

        foreach (var key in timedOut)
        {
            _events.TryRemove(key, out _);
        }
    }

    // ── Результаты ─────────────────────────────────────────

    /// <summary>
    /// Получить последние обнаруженные блокировки.
    /// </summary>
    public BlockRecord[] GetRecentBlocks(int limit = 20)
    {
        return _blocks.Take(limit).ToArray();
    }

    /// <summary>
    /// Получить все блокировки по типу.
    /// </summary>
    public BlockRecord[] GetBlocksByType(BlockType type)
    {
        return _blocks.Where(b => b.Type == type).ToArray();
    }

    /// <summary>
    /// Получить агрегированную статистику блокировок.
    /// </summary>
    public BlockStats GetStats()
    {
        var all = _blocks.ToArray();
        return new BlockStats
        {
            TotalBlocks = all.Length,
            TcpResets = all.Count(b => b.Type == BlockType.TcpReset),
            SilentDrops = all.Count(b => b.Type == BlockType.SilentDrop),
            DpiDrops = all.Count(b => b.Type == BlockType.DpiDrop),
            TtlAnomalies = all.Count(b => b.Type == BlockType.TtlAnomaly),
            DnsMismatches = all.Count(b => b.Type == BlockType.DnsMismatch),
            ActiveConnections = _events.Count,
        };
    }

    /// <summary>
    /// Очистить все данные.
    /// </summary>
    public void Clear()
    {
        _events.Clear();
        while (_blocks.TryDequeue(out _)) { }
        Interlocked.Exchange(ref _eventCount, 0);
        Interlocked.Exchange(ref _blockCount, 0);
    }

    // ── Внутренний анализ ──────────────────────────────────

    private void AnalyzeRst(ConnectionEvent evt)
    {
        var delay = (evt.RstTime!.Value - evt.SynTime).TotalMilliseconds;

        // RST сразу после SYN (< 100ms) — вероятная блокировка
        if (delay < 100)
        {
            EmitBlock(new BlockRecord
            {
                Target = evt.Domain ?? evt.DestinationIp.ToString(),
                Type = BlockType.TcpReset,
                Confidence = BlockConfidence.High,
                Description = $"TCP RST after {delay:F0}ms — likely DPI/ISP reset",
            });
        }
        // RST после некоторой задержки — может быть нормальным
        else if (delay < 1000)
        {
            EmitBlock(new BlockRecord
            {
                Target = evt.Domain ?? evt.DestinationIp.ToString(),
                Type = BlockType.TcpReset,
                Confidence = BlockConfidence.Medium,
                Description = $"TCP RST after {delay:F0}ms — possible block",
            });
        }
    }

    private void AnalyzeDpiDrop(ConnectionEvent evt)
    {
        if (evt.FirstPacketSize.HasValue)
        {
            var size = evt.FirstPacketSize.Value;
            if (size >= DpiDropMinSize && size <= DpiDropMaxSize)
            {
                EmitBlock(new BlockRecord
                {
                    Target = evt.Domain ?? evt.DestinationIp.ToString(),
                    Type = BlockType.DpiDrop,
                    Confidence = BlockConfidence.High,
                    Description = $"DPI drop pattern: {size} bytes (16-20KB signature)",
                });
            }
        }
    }

    private void AnalyzeTtlAnomaly(ConnectionEvent evt)
    {
        if (evt.Ttl.HasValue)
        {
            var ttl = evt.Ttl.Value;
            if (ttl < NormalTtlMin || ttl > NormalTtlMax)
            {
                EmitBlock(new BlockRecord
                {
                    Target = evt.Domain ?? evt.DestinationIp.ToString(),
                    Type = BlockType.TtlAnomaly,
                    Confidence = BlockConfidence.Low,
                    Description = $"TTL anomaly: {ttl} (normal: {NormalTtlMin}-{NormalTtlMax})",
                });
            }
        }
    }

    /// <summary>
    /// Записать DNS mismatch (вызывается извне при сравнении резолвов).
    /// </summary>
    public void RecordDnsMismatch(string domain, string localIp, string dohIp)
    {
        EmitBlock(new BlockRecord
        {
            Target = domain,
            Type = BlockType.DnsMismatch,
            Confidence = BlockConfidence.High,
            Description = $"DNS mismatch: local={localIp}, DoH={dohIp}",
        });
    }

    private void EmitBlock(BlockRecord block)
    {
        // Проверить дубликаты (агрегация)
        foreach (var existing in _blocks)
        {
            if (existing.Target == block.Target && existing.Type == block.Type &&
                (DateTime.UtcNow - existing.DetectedAt).TotalMinutes < 5)
            {
                existing.Occurrences++;
                return;
            }
        }

        _blocks.Enqueue(block);
        Interlocked.Increment(ref _blockCount);
        PruneBlocks();

        _logger.LogDebug("Block detected: {Type} on {Target} ({Confidence})",
            block.Type, block.Target, block.Confidence);

        OnBlockDetected?.Invoke(block);
    }

    // ── Очистка ────────────────────────────────────────────

    private void PruneEvents()
    {
        if (Volatile.Read(ref _eventCount) <= MaxEvents) return;

        // Удалить старые события
        var oldest = _events.OrderBy(kvp => kvp.Value.SynTime)
            .Take(_events.Count - MaxEvents / 2)
            .Select(kvp => kvp.Key)
            .ToList();

        foreach (var key in oldest)
            _events.TryRemove(key, out _);

        Interlocked.Exchange(ref _eventCount, _events.Count);
    }

    private void PruneBlocks()
    {
        if (Volatile.Read(ref _blockCount) <= MaxBlocks) return;

        // Удалить половину старых блоков
        var toRemove = _blockCount - MaxBlocks / 2;
        for (int i = 0; i < toRemove; i++)
        {
            if (_blocks.TryDequeue(out _))
                Interlocked.Decrement(ref _blockCount);
        }
    }
}

/// <summary>
/// Агрегированная статистика блокировок.
/// </summary>
public sealed class BlockStats
{
    public int TotalBlocks { get; init; }
    public int TcpResets { get; init; }
    public int SilentDrops { get; init; }
    public int DpiDrops { get; init; }
    public int TtlAnomalies { get; init; }
    public int DnsMismatches { get; init; }
    public int ActiveConnections { get; init; }

    public override string ToString() =>
        $"Blocks: {TotalBlocks} (RST={TcpResets}, Drop={SilentDrops}, DPI={DpiDrops}, TTL={TtlAnomalies}, DNS={DnsMismatches})";
}
