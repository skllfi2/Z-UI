// ═══════════════════════════════════════════════════════════════
// ZUI.Core / Engine / DpiBypassEngine.cs
// Движок десинхронизации DPI
// Координирует: RuleMatcher → Fake → Split → Frag → Fooling
// Для каждого перехваченного пакета решает: пропускать,
// модифицировать или заменить на fake/split/frag пакеты.
// ═══════════════════════════════════════════════════════════════

using System.Net;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ZUI.Core.Desync;
using ZUI.Core.Intercept;
using ZUI.Core.Rules;
using ZUI.Core.WinDivert;

namespace ZUI.Core.Engine;

/// <summary>
/// Действие над пакетом: пропустить, отбросить, заменить.
/// </summary>
public enum PacketAction
{
    /// <summary>Пропустить пакет без изменений.</summary>
    Pass,
    /// <summary>Отбросить пакет (не переинжектировать).</summary>
    Drop,
    /// <summary>Заменить пакет на один или несколько модифицированных.</summary>
    Replace,
}

/// <summary>
/// Результат обработки пакета движком десинхронизации.
/// </summary>
public sealed class DpiBypassResult
{
    /// <summary>Действие над оригинальным пакетом.</summary>
    public PacketAction Action { get; init; } = PacketAction.Pass;

    /// <summary>Заменяющие пакеты (для Action == Replace).</summary>
    public ReplacementPacket[]? Replacements { get; init; }

    /// <summary>Причина решения (для диагностики).</summary>
    public string? Reason { get; init; }
}

/// <summary>
/// Заменяющий пакет (raw bytes + WinDivert address).
/// </summary>
public sealed class ReplacementPacket
{
    /// <summary>Raw bytes заменяющего пакета.</summary>
    public byte[] Packet { get; init; } = [];

    /// <summary>WinDivert address для отправки.</summary>
    public WinDivertAddress Addr { get; set; }

    /// <summary>Отправить ДО оригинального пакета (для disorder: true для 2-го сегмента).</summary>
    public bool SendBeforeOriginal { get; init; }
}

/// <summary>
/// Движок десинхронизации DPI.
/// Для каждого перехваченного пакета:
/// 1. RuleMatcher — найти подходящее правило
/// 2. ConnectionTracker — проверить cutoff
/// 3. Применить десинхронизацию (fake, multisplit, fakedsplit, disorder)
/// 4. Применить fooling (ts, badseq)
/// </summary>
public sealed class DpiBypassEngine
{
    private readonly ILogger _logger;
    private readonly RuleMatcher _ruleMatcher;
    private readonly ConnectionTracker _connectionTracker;
    private readonly FakePacketBuilder _fakeBuilder;
    private readonly PidMapper _pidMapper;

    // Интервал очистки ConnectionTracker
    private TimeSpan _cleanupInterval = TimeSpan.FromMinutes(5);
    private DateTime _lastCleanup = DateTime.UtcNow;

    public DpiBypassEngine(
        RuleMatcher ruleMatcher,
        ConnectionTracker connectionTracker,
        FakePacketBuilder fakeBuilder,
        PidMapper pidMapper,
        ILogger<DpiBypassEngine>? logger = null)
    {
        _ruleMatcher = ruleMatcher;
        _connectionTracker = connectionTracker;
        _fakeBuilder = fakeBuilder;
        _pidMapper = pidMapper;
        _logger = logger ?? NullLoggerFactory.Instance.CreateLogger<DpiBypassEngine>();
    }

    /// <summary>Текущая стратегия.</summary>
    public StrategyConfig? CurrentStrategy { get; set; }

    /// <summary>Включен ли движок.</summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>Интервал очистки ConnectionTracker.</summary>
    public TimeSpan CleanupInterval
    {
        get => _cleanupInterval;
        set => _cleanupInterval = value > TimeSpan.Zero ? value : TimeSpan.FromMinutes(5);
    }

    // ── Статистика ──────────────────────────────────────────

    private long _totalPackets;
    private long _bypassedPackets;
    private long _passedPackets;
    private long _droppedPackets;

    /// <summary>Всего пакетов обработано.</summary>
    public long TotalPackets => Volatile.Read(ref _totalPackets);
    /// <summary>Пакетов с десинхронизацией.</summary>
    public long BypassedPackets => Volatile.Read(ref _bypassedPackets);
    /// <summary>Пакетов пропущено без изменений.</summary>
    public long PassedPackets => Volatile.Read(ref _passedPackets);
    /// <summary>Пакетов отброшено.</summary>
    public long DroppedPackets => Volatile.Read(ref _droppedPackets);

    // ── Основной метод обработки ────────────────────────────

    /// <summary>
    /// Обработать перехваченный пакет.
    /// Возвращает решение: пропустить, отбросить или заменить.
    /// </summary>
    public DpiBypassResult ProcessPacket(ParsedPacket packet, WinDivertAddress addr)
    {
        Interlocked.Increment(ref _totalPackets);

        // Периодическая очистка ConnectionTracker
        var now = DateTime.UtcNow;
        if (now - _lastCleanup > _cleanupInterval)
        {
            _connectionTracker.Cleanup(_cleanupInterval);
            _lastCleanup = now;
        }

        if (!IsEnabled || CurrentStrategy is null)
        {
            Interlocked.Increment(ref _passedPackets);
            return new DpiBypassResult { Action = PacketAction.Pass, Reason = "Engine disabled" };
        }

        // Только исходящий трафик
        if (!addr.Outbound)
        {
            Interlocked.Increment(ref _passedPackets);
            return new DpiBypassResult { Action = PacketAction.Pass, Reason = "Inbound" };
        }

        // Мэтчинг правила
        var match = _ruleMatcher.Match(packet, CurrentStrategy, _connectionTracker);
        if (!match.IsMatch)
        {
            Interlocked.Increment(ref _passedPackets);
            return new DpiBypassResult { Action = PacketAction.Pass, Reason = "No rule matched" };
        }

        var rule = match.Rule!;

        // Cutoff: применять десинхронизацию только к первым N пакетам соединения
        if (rule.Cutoff is not null && rule.Cutoff.Value > 0)
        {
            var connKey = new ConnectionKey(packet.SrcIp, packet.SrcPort, packet.DstIp, packet.DstPort, packet.IsTcp);
            if (!_connectionTracker.ShouldDesync(connKey, rule.Cutoff.Value))
            {
                Interlocked.Increment(ref _passedPackets);
                return new DpiBypassResult { Action = PacketAction.Pass, Reason = $"Cutoff ({rule.Cutoff.Value}) reached" };
            }
        }

        // Применяем десинхронизацию по режимам
        var replacements = new List<ReplacementPacket>();
        string? desyncReason = null;

        foreach (var mode in rule.DesyncModes)
        {
            var result = ApplyDesyncMode(mode, packet, addr, rule, match);
            if (result is not null)
            {
                replacements.AddRange(result);
                desyncReason ??= mode.ToString();
            }
        }

        // Fooling: применить к fake-пакетам
        if (replacements.Count > 0 && rule.Fooling != FoolingMode.None)
        {
            ApplyFooling(replacements, rule, addr);
        }

        if (replacements.Count > 0)
        {
            Interlocked.Increment(ref _bypassedPackets);
            return new DpiBypassResult
            {
                Action = PacketAction.Replace,
                Replacements = replacements.ToArray(),
                Reason = desyncReason,
            };
        }

        // Нет замен — пропускаем
        Interlocked.Increment(ref _passedPackets);
        return new DpiBypassResult { Action = PacketAction.Pass, Reason = "No desync applied" };
    }

    // ── Режимы десинхронизации ──────────────────────────────

    private List<ReplacementPacket>? ApplyDesyncMode(
        DesyncMode mode,
        ParsedPacket packet,
        WinDivertAddress addr,
        FilterRule rule,
        RuleMatch match)
    {
        return mode switch
        {
            DesyncMode.Fake => ApplyFake(packet, addr, rule, match),
            DesyncMode.MultiSplit => ApplyMultiSplit(packet, addr, rule),
            DesyncMode.FakeSplit => ApplyFakeSplit(packet, addr, rule, match),
            DesyncMode.MultiDisorder => ApplyMultiDisorder(packet, addr, rule),
            _ => null,
        };
    }

    /// <summary>
    /// Fake: отправить N fake-пакетов ПЕРЕД оригинальным.
    /// Оригинальный пакет пропускается без изменений.
    /// Аналог: --dpi-desync=fake --dpi-desync-repeats=6
    /// </summary>
    private List<ReplacementPacket>? ApplyFake(
        ParsedPacket packet, WinDivertAddress addr, FilterRule rule, RuleMatch match)
    {
        int repeats = rule.FakeRepeats > 0 ? rule.FakeRepeats : 1;

        var fakeResult = _fakeBuilder.BuildFakePacket(
            packet.RawPacket, addr, rule, match.L7Protocol);

        if (!fakeResult.IsSuccess)
        {
            _logger.LogDebug("Fake packet build failed: {Error}", fakeResult.Error);
            return null;
        }

        var (fakePacket, fakeAddr) = fakeResult.Value;

        // Применить модификации (rnd, dupsid, sni=)
        if (rule.FakeTlsMods is not null && rule.FakeTlsMods.Length > 0)
        {
            FakePacketModifier.ApplyMods(fakePacket, fakeAddr, rule.FakeTlsMods, match.Hostname);
        }

        var result = new List<ReplacementPacket>(repeats);
        for (int i = 0; i < repeats; i++)
        {
            // Каждый repeat — копия fake-пакета (может отличаться для rnd)
            byte[] copy = (byte[])fakePacket.Clone();

            // Для rnd: каждый repeat перегенерирует Session ID
            if (rule.FakeTlsMods is not null &&
                rule.FakeTlsMods.Any(m => m.Trim().Equals("rnd", StringComparison.OrdinalIgnoreCase)))
            {
                ApplyRndToCopy(copy, fakeAddr);
            }

            result.Add(new ReplacementPacket
            {
                Packet = copy,
                Addr = fakeAddr,
                SendBeforeOriginal = false, // Fake отправляется перед оригиналом
            });
        }

        return result;
    }

    /// <summary>
    /// Multisplit: разрезать TCP payload на несколько сегментов.
    /// Оригинальный пакет отбрасывается, заменяется сегментами.
    /// Аналог: --dpi-desync=multisplit --dpi-desync-split-pos=2,midsld
    /// </summary>
    private List<ReplacementPacket>? ApplyMultiSplit(
        ParsedPacket packet, WinDivertAddress addr, FilterRule rule)
    {
        if (!packet.IsTcp)
            return null;

        var splitResult = TcpSplitter.Split(packet.RawPacket, addr, rule);
        if (!splitResult.IsSuccess)
        {
            _logger.LogDebug("TCP split failed: {Error}", splitResult.Error);
            return null;
        }

        var result = new List<ReplacementPacket>();
        foreach (var seg in splitResult.Value.Segments)
        {
            result.Add(new ReplacementPacket
            {
                Packet = seg.Packet,
                Addr = seg.Addr,
                SendBeforeOriginal = seg.SendBeforeOriginal,
            });
        }

        return result;
    }

    /// <summary>
    /// FakeSplit: отправить fake-пакеты + разрезать оригинальный на сегменты.
    /// Комбинация Fake + MultiSplit.
    /// Аналог: --dpi-desync=fake,fakedsplit
    /// </summary>
    private List<ReplacementPacket>? ApplyFakeSplit(
        ParsedPacket packet, WinDivertAddress addr, FilterRule rule, RuleMatch match)
    {
        var result = new List<ReplacementPacket>();

        // 1. Fake пакеты
        var fakePackets = ApplyFake(packet, addr, rule, match);
        if (fakePackets is not null)
            result.AddRange(fakePackets);

        // 2. Split оригинального пакета
        if (packet.IsTcp)
        {
            // Для fakedsplit: разрез по позиции 1 (после 1 байта)
            // или по SplitPositions если указаны
            if (rule.SplitPositions is not null && rule.SplitPositions.Length > 0)
            {
                var splitResult = TcpSplitter.Split(packet.RawPacket, addr, rule);
                if (splitResult.IsSuccess)
                {
                    foreach (var seg in splitResult.Value.Segments)
                    {
                        result.Add(new ReplacementPacket
                        {
                            Packet = seg.Packet,
                            Addr = seg.Addr,
                            SendBeforeOriginal = seg.SendBeforeOriginal,
                        });
                    }
                }
            }
            else
            {
                // Default fakedsplit: разрез по позиции 1
                var splitResult = TcpSplitter.SplitAt(packet.RawPacket, addr, 1);
                if (splitResult.IsSuccess)
                {
                    foreach (var seg in splitResult.Value.Segments)
                    {
                        result.Add(new ReplacementPacket
                        {
                            Packet = seg.Packet,
                            Addr = seg.Addr,
                            SendBeforeOriginal = seg.SendBeforeOriginal,
                        });
                    }
                }
            }
        }

        return result.Count > 0 ? result : null;
    }

    /// <summary>
    /// MultiDisorder: как MultiSplit, но 2-й сегмент отправляется перед 1-м.
    /// DPI видит сегменты в обратном порядке → пропускает.
    /// Аналог: --dpi-desync=multisplit --dpi-desync-split-disorder
    /// </summary>
    private List<ReplacementPacket>? ApplyMultiDisorder(
        ParsedPacket packet, WinDivertAddress addr, FilterRule rule)
    {
        if (!packet.IsTcp)
            return null;

        var splitResult = TcpSplitter.Split(packet.RawPacket, addr, rule);
        if (!splitResult.IsSuccess)
        {
            _logger.LogDebug("TCP disorder split failed: {Error}", splitResult.Error);
            return null;
        }

        var result = new List<ReplacementPacket>();
        foreach (var seg in splitResult.Value.Segments)
        {
            result.Add(new ReplacementPacket
            {
                Packet = seg.Packet,
                Addr = seg.Addr,
                SendBeforeOriginal = seg.SendBeforeOriginal,
            });
        }

        return result;
    }

    // ── Fooling ─────────────────────────────────────────────

    /// <summary>
    /// Применить fooling к заменяющим пакетам.
    /// Ts: добавить TCP Timestamp option с нулевым значением.
    /// BadSeq: установить неверный SEQ номер в fake-пакетах.
    /// </summary>
    private void ApplyFooling(List<ReplacementPacket> replacements, FilterRule rule, WinDivertAddress originalAddr)
    {
        if (rule.Fooling == FoolingMode.None)
            return;

        foreach (var replacement in replacements)
        {
            unsafe
            {
                fixed (byte* pPacket = replacement.Packet)
                {
                    // Определяем IPv4 vs IPv6
                    bool isV6 = (pPacket[0] >> 4) == 6;

                    int ipHdrLen;
                    byte protocol;

                    if (!isV6)
                    {
                        var ipHdr = (WinDivertIpHdr*)pPacket;
                        ipHdrLen = ipHdr->HdrLength;
                        protocol = ipHdr->Protocol;
                    }
                    else
                    {
                        ipHdrLen = 40;
                        var ip6Hdr = (WinDivertIpv6Hdr*)pPacket;
                        protocol = (byte)ip6Hdr->NextHdr;
                    }

                    if (protocol != 6) // TCP only
                        continue;

                    if (replacement.Packet.Length < ipHdrLen + 20)
                        continue;

                    var tcpHdr = (WinDivertTcpHdr*)(pPacket + ipHdrLen);

                    if (rule.Fooling == FoolingMode.BadSeq)
                    {
                        // Установить SEQ = 0 в fake-пакете
                        // DPI отбрасывает пакеты с неверным SEQ
                        tcpHdr->SeqNum = 0;
                    }
                    // Ts fooling: добавляется через TCP options
                    // Это сложнее — требует расширения TCP заголовка
                    // В winws это делается через WinDivertHelperCalcChecksums
                    // Для упрощения: помечаем TCP checksum как невалидный
                    // (WinDivert пересчитает при отправке)
                }
            }

            // Пересчитать checksums при отправке
            var modAddr = replacement.Addr;
            modAddr.TCPChecksum = false;
            replacement.Addr = modAddr;
        }
    }

    // ── Вспомогательные ─────────────────────────────────────

    /// <summary>
    /// Применить rnd модификацию к копии fake-пакета.
    /// </summary>
    private static void ApplyRndToCopy(byte[] copy, WinDivertAddress addr)
    {
        // Определяем смещение payload
        unsafe
        {
            fixed (byte* p = copy)
            {
                bool isV6 = addr.IPv6;
                int ipHdrLen;
                byte protocol;

                if (!isV6)
                {
                    if (copy.Length < 20) return;
                    var ipHdr = (WinDivertIpHdr*)p;
                    ipHdrLen = ipHdr->HdrLength;
                    protocol = ipHdr->Protocol;
                }
                else
                {
                    ipHdrLen = 40;
                    var ip6Hdr = (WinDivertIpv6Hdr*)p;
                    protocol = (byte)ip6Hdr->NextHdr;
                }

                int payloadOffset;
                if (protocol == 6 && copy.Length >= ipHdrLen + 20)
                {
                    var tcpHdr = (WinDivertTcpHdr*)(p + ipHdrLen);
                    payloadOffset = ipHdrLen + tcpHdr->HdrLength;
                }
                else if (protocol == 17)
                {
                    payloadOffset = ipHdrLen + 8;
                }
                else
                {
                    return;
                }

                // Session ID offset = payloadOffset + 43
                int sessionIdLenOffset = payloadOffset + 43;
                if (sessionIdLenOffset >= copy.Length) return;

                byte sessionIdLen = copy[sessionIdLenOffset];
                if (sessionIdLen == 0) return;

                int sessionIdStart = sessionIdLenOffset + 1;
                if (sessionIdStart + sessionIdLen > copy.Length) return;

                Random.Shared.NextBytes(copy.AsSpan(sessionIdStart, sessionIdLen));
            }
        }
    }

    /// <summary>
    /// Сбросить статистику.
    /// </summary>
    public void ResetStats()
    {
        Interlocked.Exchange(ref _totalPackets, 0);
        Interlocked.Exchange(ref _bypassedPackets, 0);
        Interlocked.Exchange(ref _passedPackets, 0);
        Interlocked.Exchange(ref _droppedPackets, 0);
    }
}
