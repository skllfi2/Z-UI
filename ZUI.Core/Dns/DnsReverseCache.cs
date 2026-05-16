// ═══════════════════════════════════════════════════════════════
// ZUI.Core / Dns / DnsReverseCache.cs
// Обратный DNS кэш: IP → домен (для DNS-through-proxy)
// Потокобезопасный, с TTL и автоочисткой
// ═══════════════════════════════════════════════════════════════

using System.Collections.Concurrent;
using System.Net;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace ZUI.Core.Dns;

/// <summary>
/// Запись обратного DNS кэша: IP → домен.
/// </summary>
public sealed class DnsReverseEntry
{
    /// <summary>IP адрес.</summary>
    public IPAddress Ip { get; init; } = IPAddress.None;

    /// <summary>Доменное имя, резолвящееся в этот IP.</summary>
    public string Domain { get; init; } = string.Empty;

    /// <summary>DNS тип записи (A / AAAA).</summary>
    public string RecordType { get; init; } = "A";

    /// <summary>TTL в секундах (от DNS ответа).</summary>
    public uint Ttl { get; init; }

    /// <summary>Время добавления.</summary>
    public DateTime CachedAt { get; init; } = DateTime.UtcNow;

    /// <summary>Запись просрочена?</summary>
    public bool IsExpired => DateTime.UtcNow - CachedAt > TimeSpan.FromSeconds(Math.Max(Ttl, 60));

    /// <summary>Домен — более свежая запись (выше приоритет)?</summary>
    public int Priority { get; init; }
}

/// <summary>
/// Обратный DNS кэш: IP адрес → доменное имя.
/// 
/// Используется для DNS-through-proxy: когда WinDivert перехватывает
/// TCP SYN, в пакете уже IP (DNS резолвинг уже прошёл). Чтобы отправить
/// домен вместо IP в SOCKS5 CONNECT (ATYP_DOMAINNAME), нужен reverse lookup.
/// 
/// Источники данных:
/// 1. DNS-ответы, перехваченные через WinDivert (UDP 53)
/// 2. SNI из TLS ClientHello (если доступен)
/// 3. Ручное добавление из DnsCache (ZUI.Core.Dns)
/// 
/// Потокобезопасный (ConcurrentDictionary), с TTL и автоочисткой.
/// Один IP может иметь несколько доменов — выбирается с наивысшим приоритетом.
/// </summary>
public sealed class DnsReverseCache
{
    private readonly ILogger _logger;
    private readonly ConcurrentDictionary<string, DnsReverseEntry> _entries = new(StringComparer.OrdinalIgnoreCase);
    private DateTime _lastCleanup = DateTime.UtcNow;
    private readonly TimeSpan _cleanupInterval = TimeSpan.FromMinutes(5);

    public DnsReverseCache(ILogger<DnsReverseCache>? logger = null)
    {
        _logger = logger ?? NullLoggerFactory.Instance.CreateLogger<DnsReverseCache>();
    }

    /// <summary>Количество записей в кэше.</summary>
    public int Count => _entries.Count;

    // ── Добавление ─────────────────────────────────────────

    /// <summary>
    /// Добавить/обновить обратную DNS запись.
    /// Если для IP уже есть запись с более высоким приоритетом — новая игнорируется.
    /// </summary>
    /// <param name="ip">IP адрес.</param>
    /// <param name="domain">Доменное имя.</param>
    /// <param name="ttl">TTL в секундах (от DNS ответа).</param>
    /// <param name="recordType">Тип записи (A/AAAA).</param>
    /// <param name="priority">Приоритет (0=DNS response, 10=SNI, 20=manual). Ниже = лучше.</param>
    public void Add(IPAddress ip, string domain, uint ttl = 300, string recordType = "A", int priority = 0)
    {
        if (ip.Equals(IPAddress.None) || ip.Equals(IPAddress.Any) ||
            ip.Equals(IPAddress.IPv6None) || ip.Equals(IPAddress.IPv6Any))
            return;

        if (string.IsNullOrWhiteSpace(domain))
            return;

        domain = domain.TrimEnd('.').ToLowerInvariant();
        var key = ip.ToString();

        var entry = new DnsReverseEntry
        {
            Ip = ip,
            Domain = domain,
            RecordType = recordType,
            Ttl = ttl,
            Priority = priority,
        };

        _entries.AddOrUpdate(
            key,
            static (_, arg) => arg.entry,
            static (_, existing, arg) =>
                arg.entry.Priority <= existing.Priority || existing.IsExpired
                    ? arg.entry
                    : existing,
            (entry, ip, domain));

        _logger.LogDebug("DNS reverse cache: {Ip} → {Domain} (TTL={Ttl}s, P={Priority})", ip, domain, ttl, priority);

        MaybeCleanup();
    }

    // ── Получение ──────────────────────────────────────────

    /// <summary>
    /// Получить доменное имя по IP адресу.
    /// Возвращает null если записи нет или она просрочена.
    /// </summary>
    public string? TryGetDomain(IPAddress ip)
    {
        var key = ip.ToString();

        if (_entries.TryGetValue(key, out var entry) && !entry.IsExpired)
        {
            _logger.LogDebug("DNS reverse cache hit: {Ip} → {Domain}", ip, entry.Domain);
            return entry.Domain;
        }

        // Удаляем просроченную
        if (entry is not null)
        {
            _entries.TryRemove(key, out _);
            _logger.LogDebug("DNS reverse cache: expired entry for {Ip}", ip);
        }

        return null;
    }

    /// <summary>
    /// Попытаться получить домен по IP.
    /// </summary>
    public bool TryGetDomain(IPAddress ip, out string? domain)
    {
        domain = TryGetDomain(ip);
        return domain is not null;
    }

    // ── Удаление ───────────────────────────────────────────

    /// <summary>
    /// Удалить запись по IP.
    /// </summary>
    public bool Remove(IPAddress ip)
    {
        return _entries.TryRemove(ip.ToString(), out _);
    }

    /// <summary>
    /// Очистить весь кэш.
    /// </summary>
    public void Clear()
    {
        _entries.Clear();
        _logger.LogDebug("DNS reverse cache: cleared");
    }

    // ── Очистка просроченных ───────────────────────────────

    /// <summary>
    /// Удалить просроченные записи.
    /// </summary>
    public int Cleanup()
    {
        int removed = 0;
        foreach (var kvp in _entries)
        {
            if (kvp.Value.IsExpired)
            {
                _entries.TryRemove(kvp.Key, out _);
                removed++;
            }
        }

        if (removed > 0)
            _logger.LogDebug("DNS reverse cache: cleaned up {Count} expired entries", removed);

        return removed;
    }

    // ── Вспомогательные ────────────────────────────────────

    private void MaybeCleanup()
    {
        if (DateTime.UtcNow - _lastCleanup < _cleanupInterval)
            return;

        _lastCleanup = DateTime.UtcNow;
        _ = Cleanup();
    }
}
