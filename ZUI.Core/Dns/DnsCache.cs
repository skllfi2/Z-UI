// ═══════════════════════════════════════════════════════════════
// ZUI.Core / Dns / DnsCache.cs
// Потокобезопасный кэш DNS записей с TTL
// ConcurrentDictionary, автоматическая очистка просроченных
// ═══════════════════════════════════════════════════════════════

using System.Collections.Concurrent;
using System.Net;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace ZUI.Core.Dns;

/// <summary>
/// Кэшированная DNS запись.
/// </summary>
public sealed class DnsCacheEntry
{
    /// <summary>IP адрес.</summary>
    public IPAddress Address { get; init; } = IPAddress.None;

    /// <summary>Тип записи (A/AAAA).</summary>
    public DnsRecordType Type { get; init; }

    /// <summary>TTL в секундах (оригинальный от DNS сервера).</summary>
    public uint Ttl { get; init; }

    /// <summary>Время добавления в кэш.</summary>
    public DateTime CachedAt { get; init; } = DateTime.UtcNow;

    /// <summary>Запись просрочена?</summary>
    public bool IsExpired => DateTime.UtcNow - CachedAt > TimeSpan.FromSeconds(Ttl);

    /// <summary>Оставшееся время жизни.</summary>
    public TimeSpan RemainingTtl
    {
        get
        {
            var elapsed = DateTime.UtcNow - CachedAt;
            var remaining = TimeSpan.FromSeconds(Ttl) - elapsed;
            return remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
        }
    }
}

/// <summary>
/// Потокобезопасный кэш DNS записей.
/// Ключ: (домен, тип записи). Значение: IP + TTL + время кэширования.
/// Автоматическая очистка просроченных записей.
/// </summary>
public sealed class DnsCache
{
    private readonly ILogger _logger;
    private readonly ConcurrentDictionary<string, DnsCacheEntry> _entries = new(StringComparer.OrdinalIgnoreCase);
    private DateTime _lastCleanup = DateTime.UtcNow;
    private TimeSpan _cleanupInterval = TimeSpan.FromMinutes(5);

    public DnsCache(ILogger<DnsCache>? logger = null)
    {
        _logger = logger ?? NullLoggerFactory.Instance.CreateLogger<DnsCache>();
    }

    /// <summary>Количество записей в кэше.</summary>
    public int Count => _entries.Count;

    // ── Добавление ─────────────────────────────────────────

    /// <summary>
    /// Добавить или обновить DNS запись в кэше.
    /// Ключ: "domain:type" (например, "google.com:A").
    /// </summary>
    public void Add(string domain, DnsRecordType type, IPAddress address, uint ttl)
    {
        var key = BuildKey(domain, type);
        var entry = new DnsCacheEntry
        {
            Address = address,
            Type = type,
            Ttl = ttl,
            CachedAt = DateTime.UtcNow,
        };

        _entries[key] = entry;
        _logger.LogDebug("DNS cache: cached {Domain} {Type} → {Address} (TTL={Ttl}s)", domain, type, address, ttl);

        // Периодическая очистка
        MaybeCleanup();
    }

    // ── Получение ──────────────────────────────────────────

    /// <summary>
    /// Получить закэшированный IP адрес для домена.
    /// Возвращает null если записи нет или она просрочена.
    /// </summary>
    public IPAddress? Get(string domain, DnsRecordType type = DnsRecordType.A)
    {
        var key = BuildKey(domain, type);

        if (_entries.TryGetValue(key, out var entry) && !entry.IsExpired)
        {
            _logger.LogDebug("DNS cache: hit {Domain} {Type} → {Address}", domain, type, entry.Address);
            return entry.Address;
        }

        // Удаляем просроченную
        if (entry is not null)
        {
            _entries.TryRemove(key, out _);
            _logger.LogDebug("DNS cache: expired {Domain} {Type}", domain, type);
        }

        return null;
    }

    /// <summary>
    /// Попытаться получить закэшированную запись.
    /// </summary>
    public bool TryGet(string domain, DnsRecordType type, out IPAddress? address)
    {
        address = Get(domain, type);
        return address is not null;
    }

    // ── Проверка наличия ───────────────────────────────────

    /// <summary>
    /// Проверить, есть ли закэшированная непросроченная запись.
    /// </summary>
    public bool Contains(string domain, DnsRecordType type = DnsRecordType.A)
    {
        var key = BuildKey(domain, type);
        return _entries.TryGetValue(key, out var entry) && !entry.IsExpired;
    }

    // ── Удаление ───────────────────────────────────────────

    /// <summary>
    /// Удалить запись из кэша.
    /// </summary>
    public bool Remove(string domain, DnsRecordType type = DnsRecordType.A)
    {
        var key = BuildKey(domain, type);
        return _entries.TryRemove(key, out _);
    }

    /// <summary>
    /// Очистить весь кэш.
    /// </summary>
    public void Clear()
    {
        _entries.Clear();
        _logger.LogDebug("DNS cache: cleared");
    }

    // ── Очистка просроченных ───────────────────────────────

    /// <summary>
    /// Очистить просроченные записи. Вызывается автоматически.
    /// </summary>
    public int Cleanup()
    {
        int removed = 0;
        foreach (var kvp in _entries)
        {
            if (kvp.Value.IsExpired)
            {
                if (_entries.TryRemove(kvp.Key, out _))
                    removed++;
            }
        }

        if (removed > 0)
            _logger.LogDebug("DNS cache: cleaned up {Count} expired entries", removed);

        return removed;
    }

    /// <summary>
    /// Получить все непросроченные записи (для диагностики/UI).
    /// </summary>
    public IReadOnlyList<(string Domain, DnsRecordType Type, DnsCacheEntry Entry)> GetAllEntries()
    {
        var result = new List<(string, DnsRecordType, DnsCacheEntry)>();
        foreach (var kvp in _entries)
        {
            if (!kvp.Value.IsExpired)
            {
                // Парсим ключ обратно
                var parts = kvp.Key.Split(':');
                if (parts.Length == 2 && Enum.TryParse<DnsRecordType>(parts[1], out var type))
                {
                    result.Add((parts[0], type, kvp.Value));
                }
            }
        }
        return result;
    }

    // ── Внутренние методы ──────────────────────────────────

    private static string BuildKey(string domain, DnsRecordType type)
    {
        return $"{domain}:{type}";
    }

    private void MaybeCleanup()
    {
        var now = DateTime.UtcNow;
        if (now - _lastCleanup > _cleanupInterval)
        {
            _lastCleanup = now;
            _ = Cleanup();
        }
    }
}
