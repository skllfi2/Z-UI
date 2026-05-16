// ═══════════════════════════════════════════════════════════════
// ZUI.Core / Rules / FilterRule.cs
// Одно правило фильтрации (аналог --filter-tcp/udp + --dpi-desync)
// ═══════════════════════════════════════════════════════════════

namespace ZUI.Core.Rules;

/// <summary>
/// Режим десинхронизации DPI (аналог --dpi-desync).
/// </summary>
public enum DesyncMode
{
    None,
    Fake,
    MultiSplit,
    FakeSplit,
    MultiDisorder,
}

/// <summary>
/// Режим обмана DPI (аналог --dpi-desync-fooling).
/// </summary>
public enum FoolingMode
{
    None,
    Ts,     // TCP Timestamps manipulation
    BadSeq, // Bad sequence number in fake packets
}

/// <summary>
/// Протокол фильтрации.
/// </summary>
public enum FilterProtocol
{
    Tcp,
    Udp,
}

/// <summary>
/// Диапазон портов (один порт или диапазон: 19294-19344).
/// </summary>
public readonly record struct PortRange(ushort Start, ushort End)
{
    public PortRange(ushort singlePort) : this(singlePort, singlePort) { }

    public bool Contains(ushort port) => port >= Start && port <= End;

    public override string ToString() => Start == End ? $"{Start}" : $"{Start}-{End}";
}

/// <summary>
/// Одно правило фильтрации пакетов.
/// Аналог одного блока параметров в BAT стратегии (между --new разделителями).
/// </summary>
public sealed record class FilterRule
{
    // ── Фильтр: какие пакеты перехватывать ──────────────────

    /// <summary>Протокол: TCP или UDP.</summary>
    public FilterProtocol Protocol { get; init; } = FilterProtocol.Tcp;

    /// <summary>Порты назначения (аналог --filter-tcp=443 или --filter-udp=443).</summary>
    public PortRange[] Ports { get; init; } = [];

    /// <summary>L7 протокол для фильтрации (аналог --filter-l7=discord,stun). null = любой.</summary>
    public string[]? L7Protocols { get; init; }

    // ── Сопоставление: кому применять правило ───────────────

    /// <summary>Списки доменов whitelist (аналог --hostlist=list-general.txt).</summary>
    public string[]? HostLists { get; init; }

    /// <summary>Конкретные домены (аналог --hostlist-domains=discord.media).</summary>
    public string[]? HostDomains { get; init; }

    /// <summary>Списки исключений доменов (аналог --hostlist-exclude=list-exclude.txt).</summary>
    public string[]? HostExcludeLists { get; init; }

    /// <summary>Списки IP-адресов (аналог --ipset=ipset-all.txt).</summary>
    public string[]? IpsetLists { get; init; }

    /// <summary>Списки исключений IP (аналог --ipset-exclude=ipset-exclude.txt).</summary>
    public string[]? IpsetExcludeLists { get; init; }

    /// <summary>Установить IP ID = 0 (аналог --ip-id=zero).</summary>
    public bool IpIdZero { get; init; }

    // ── Действие: что делать с пакетом ──────────────────────

    /// <summary>Режим десинхронизации (аналог --dpi-desync=fake,multisplit).</summary>
    public DesyncMode[] DesyncModes { get; init; } = [];

    /// <summary>Количество fake пакетов (аналог --dpi-desync-repeats=6).</summary>
    public int FakeRepeats { get; init; }

    /// <summary>Режим обмана DPI (аналог --dpi-desync-fooling=ts,badseq).</summary>
    public FoolingMode Fooling { get; init; } = FoolingMode.None;

    // ── Fake параметры ──────────────────────────────────────

	/// <summary>Fake TLS payload файлы (аналог --dpi-desync-fake-tls=tls.bin, может быть несколько).</summary>
	public string[]? FakeTlsFiles { get; init; }

    /// <summary>Модификации fake TLS (аналог --dpi-desync-fake-tls-mod=rnd,dupsid,sni=www.google.com).</summary>
    public string[]? FakeTlsMods { get; init; }

    /// <summary>Fake QUIC payload файл (аналог --dpi-desync-fake-quic=quic_initial_www_google_com.bin).</summary>
    public string? FakeQuicFile { get; init; }

    /// <summary>Fake HTTP payload файл (аналог --dpi-desync-fake-http).</summary>
    public string? FakeHttpFile { get; init; }

    /// <summary>Fake unknown UDP payload файл (аналог --dpi-desync-fake-unknown-udp).</summary>
    public string? FakeUnknownUdpFile { get; init; }

    /// <summary>Паттерн fakedsplit (аналог --dpi-desync-fakedsplit-pattern=0x00).</summary>
    public string? FakeSplitPattern { get; init; }

    // ── Split параметры ─────────────────────────────────────

    /// <summary>Позиции разреза (аналог --dpi-desync-split-pos=1,midsld). int = byte offset, string = named position.</summary>
    public object[]? SplitPositions { get; init; } // int или string ("midsld")

    /// <summary>Seq overlap при разрезе (аналог --dpi-desync-split-seqovl=681).</summary>
    public int? SplitSeqOvl { get; init; }

    /// <summary>Паттерн для seq overlap (аналог --dpi-desync-split-seqovl-pattern=tls_clienthello_4pda_to.bin).</summary>
    public string? SplitSeqOvlPattern { get; init; }

    // ── Прочее ──────────────────────────────────────────────

    /// <summary>Любой протокол, не только HTTP/TLS (аналог --dpi-desync-any-protocol=1).</summary>
    public bool AnyProtocol { get; init; }

    /// <summary>Обрезать после N пакетов (аналог --dpi-desync-cutoff=n3). null = без cutoff.</summary>
    public int? Cutoff { get; init; }

    /// <summary>Порядок правила (для сортировки при матче).</summary>
    public int Order { get; init; }
}
