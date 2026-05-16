// ═══════════════════════════════════════════════════════════════
// ZUI.Proxy / Rules / ProxyRule.cs
// Модели правил проксификатора:
// ProxyAction, ProxyType, ProxyTarget, ProxyRule, DnsPolicy
// Per-app routing: процесс → действие (Direct/Proxy/Chain/Block)
// ═══════════════════════════════════════════════════════════════

using System.Net;
using System.Text.Json.Serialization;

namespace ZUI.Proxy.Rules;

/// <summary>
/// Действие при совпадении правила.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<ProxyAction>))]
public enum ProxyAction
{
    /// <summary>Пропустить напрямую (без прокси).</summary>
    Direct,
    /// <summary>Маршрутизировать через прокси.</summary>
    Proxy,
    /// <summary>Маршрутизировать через цепочку прокси.</summary>
    Chain,
    /// <summary>Заблокировать соединение.</summary>
    Block,
}

/// <summary>
/// Тип прокси-сервера.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<ProxyType>))]
public enum ProxyType
{
    /// <summary>SOCKS4 прокси (без аутентификации, только TCP).</summary>
    Socks4,
    /// <summary>SOCKS4a прокси (с DNS resolution через прокси).</summary>
    Socks4a,
    /// <summary>SOCKS5 прокси (TCP + UDP, аутентификация).</summary>
    Socks5,
    /// <summary>HTTP CONNECT прокси.</summary>
    HttpConnect,
}

/// <summary>
/// Политика DNS для правила.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<DnsPolicy>))]
public enum DnsPolicy
{
    /// <summary>DNS резолвится локально (по умолчанию).</summary>
    Local,
    /// <summary>DNS резолвится через прокси-сервер.</summary>
    ThroughProxy,
}

/// <summary>
/// Целевой прокси-сервер.
/// </summary>
public sealed class ProxyTarget
{
    /// <summary>Название (для UI).</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Тип прокси.</summary>
    public ProxyType Type { get; set; } = ProxyType.Socks5;

    /// <summary>Адрес прокси-сервера.</summary>
    public string Host { get; set; } = "127.0.0.1";

    /// <summary>Порт прокси-сервера.</summary>
    public int Port { get; set; } = 1080;

    /// <summary>Имя пользователя (если нужна аутентификация).</summary>
    public string? Username { get; set; }

    /// <summary>Пароль (если нужна аутентификация).</summary>
    public string? Password { get; set; }

    /// <summary>Нужна ли аутентификация?</summary>
    [JsonIgnore]
    public bool RequiresAuth => !string.IsNullOrEmpty(Username);

    public override string ToString() => $"{Name} ({Type}://{Host}:{Port})";
}

/// <summary>
/// Правило маршрутизации по приложениям.
/// Первое совпавшее правило применяется; fallback = DefaultRule.
/// </summary>
public sealed class ProxyRule
{
    /// <summary>Уникальный идентификатор правила.</summary>
    public string Id { get; init; } = Guid.NewGuid().ToString("N")[..8];

    /// <summary>Название правила (для UI).</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Включено ли правило.</summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>Приоритет (меньше = выше приоритет).</summary>
    public int Priority { get; set; }

    // ── Условия сопоставления ─────────────────────────────

    /// <summary>Имя процесса (без .exe). Null = любое.</summary>
    public string? ProcessName { get; set; }

    /// <summary>Шаблон имени процесса (wildcard: *telegram*, *chrome*).</summary>
    public string? ProcessNamePattern { get; set; }

    /// <summary>PID процесса (для точного совпадения, временные правила).</summary>
    public int? ProcessId { get; set; }

    /// <summary>Целевой IP или диапазон (CIDR: 192.168.0.0/16, single: 10.0.0.1).</summary>
    public string? DestinationIp { get; set; }

    /// <summary>Целевой порт или диапазон (80, 443, 8080-8090).</summary>
    public string? DestinationPort { get; set; }

    /// <summary>Целевой домен (точное совпадение: "discord.com").</summary>
    public string? DestinationDomain { get; set; }

    /// <summary>Шаблон домена (wildcard: "*.google.com", "*.discord.com").</summary>
    public string? DestinationDomainPattern { get; set; }

    // ── Действие ──────────────────────────────────────────

    /// <summary>Действие при совпадении.</summary>
    public ProxyAction Action { get; set; } = ProxyAction.Direct;

    /// <summary>Целевой прокси (если Action = Proxy).</summary>
    public ProxyTarget? Proxy { get; set; }

    /// <summary>Имя цепочки прокси (если Action = Chain).</summary>
    public string? ChainName { get; set; }

    /// <summary>Политика DNS для этого правила.</summary>
    public DnsPolicy DnsPolicy { get; set; } = DnsPolicy.Local;

    // ── Вспомогательные ───────────────────────────────────

    /// <summary>Является ли это правилом по умолчанию?</summary>
    [JsonIgnore]
    public bool IsDefault => string.IsNullOrEmpty(ProcessName)
        && string.IsNullOrEmpty(ProcessNamePattern)
        && !ProcessId.HasValue
        && string.IsNullOrEmpty(DestinationIp)
        && string.IsNullOrEmpty(DestinationPort)
        && string.IsNullOrEmpty(DestinationDomain)
        && string.IsNullOrEmpty(DestinationDomainPattern);

    public override string ToString() =>
        $"[{Priority}] {Name}: {ProcessName ?? "*"}" +
        (!string.IsNullOrEmpty(DestinationDomain) ? $" → {DestinationDomain}" : "") +
        (!string.IsNullOrEmpty(DestinationDomainPattern) ? $" → {DestinationDomainPattern}" : "") +
        $" → {Action}" +
        (Action == ProxyAction.Proxy ? $" via {Proxy}" : "") +
        (Action == ProxyAction.Chain ? $" chain={ChainName}" : "");
}
