// ═══════════════════════════════════════════════════════════════
// ZUI.Ipc / IpcRequest.cs
// Запросы UI → Worker (Named Pipe)
// Все запросы — record types для иммутабельности и value equality
// ═══════════════════════════════════════════════════════════════

namespace ZUI.Ipc;

/// <summary>
/// Базовый тип запроса UI → Worker.
/// </summary>
public abstract record IpcRequest : IpcMessage;

// ── DPI Bypass ──────────────────────────────────────────────

/// <summary>Запустить обход DPI с указанной стратегией.</summary>
public sealed record StartBypassRequest(string StrategyId, int GameFilterMode) : IpcRequest;

/// <summary>Остановить обход DPI.</summary>
public sealed record StopBypassRequest : IpcRequest;

/// <summary>Получить статус обхода DPI.</summary>
public sealed record GetBypassStatusRequest : IpcRequest;

/// <summary>Получить список доступных стратегий.</summary>
public sealed record GetAvailableStrategiesRequest : IpcRequest;

/// <summary>Установить режим Game Filter.</summary>
public sealed record SetGameFilterRequest(int GameFilterMode) : IpcRequest;

// ── Proxifier ───────────────────────────────────────────────

/// <summary>Запустить проксификатор.</summary>
public sealed record StartProxifierRequest : IpcRequest;

/// <summary>Остановить проксификатор.</summary>
public sealed record StopProxifierRequest : IpcRequest;

/// <summary>Получить статус проксификатора.</summary>
public sealed record GetProxifierStatusRequest : IpcRequest;

/// <summary>Получить список активных соединений проксификатора.</summary>
public sealed record GetProxifierConnectionsRequest : IpcRequest;

// ── Telegram Proxy ──────────────────────────────────────────

/// <summary>Запустить Telegram WebSocket proxy.</summary>
public sealed record StartTgWsProxyRequest(int Socks5Port, string WsUrl, string Secret) : IpcRequest;

/// <summary>Остановить Telegram WebSocket proxy.</summary>
public sealed record StopTgWsProxyRequest : IpcRequest;

/// <summary>Запустить MTProxy server.</summary>
public sealed record StartMtProxyRequest(int Port, string Secret) : IpcRequest;

/// <summary>Остановить MTProxy server.</summary>
public sealed record StopMtProxyRequest : IpcRequest;

/// <summary>Получить статус Telegram proxy.</summary>
public sealed record GetTgProxyStatusRequest : IpcRequest;

// ── DNS ─────────────────────────────────────────────────────

/// <summary>Настроить DNS (DoH, Fake DNS).</summary>
public sealed record ConfigureDnsRequest(bool EnableDoh, bool EnableFakeDns) : IpcRequest;

/// <summary>Получить статус DNS компонентов.</summary>
public sealed record GetDnsStatusRequest : IpcRequest;

// ── Diagnostics / Maintenance ───────────────────────────────

/// <summary>Запустить диагностику.</summary>
public sealed record RunDiagnosticsRequest : IpcRequest;

/// <summary>Обновить списки доменов.</summary>
public sealed record UpdateDomainListsRequest : IpcRequest;

// ── Proxy Server CRUD ───────────────────────────────────────

/// <summary>Добавить новый прокси-сервер в профиль.</summary>
public sealed record AddProxyServerRequest(
    string Name,
    string Host,
    int Port,
    string ProxyType,      // "Socks4" | "Socks4a" | "Socks5" | "HttpConnect"
    string? Username,
    string? Password,
    string DnsPolicy        // "Local" | "ThroughProxy"
) : IpcRequest;

/// <summary>Удалить прокси-сервер по ID.</summary>
public sealed record RemoveProxyServerRequest(string ServerId) : IpcRequest;

/// <summary>Обновить существующий прокси-сервер (все поля опциональны).</summary>
public sealed record UpdateProxyServerRequest(
    string ServerId,
    string? Name,
    string? Host,
    int? Port,
    string? ProxyType,
    string? Username,
    string? Password,
    string? DnsPolicy
) : IpcRequest;

/// <summary>Получить полный профиль проксификатора (серверы + правила + цепочки).</summary>
public sealed record GetProxyProfileRequest(
    bool IncludeRules = true,
    bool IncludeChains = true
) : IpcRequest;

/// <summary>Проверить работоспособность прокси-сервера.</summary>
public sealed record CheckProxyRequest(
    string Host,
    int Port,
    string ProxyType,
    string? Username,
    string? Password,
    string? TestUrl = null
) : IpcRequest;

// ── Proxy Rule CRUD ─────────────────────────────────────────

/// <summary>Добавить правило маршрутизации.</summary>
public sealed record AddProxyRuleRequest(
    string? ProcessName,
    string? ProcessNamePattern,
    int? ProcessId,
    string? DestinationIp,
    string? DestinationPort,
    string? DestinationDomain,
    string? DestinationDomainPattern,
    string Action,
    string? ProxyServerId,
    string? ChainName,
    string DnsPolicy
) : IpcRequest;

/// <summary>Удалить правило маршрутизации.</summary>
public sealed record RemoveProxyRuleRequest(string RuleId) : IpcRequest;

/// <summary>Экспортировать правила маршрутизации в JSON.</summary>
public sealed record ExportProxyRulesRequest : IpcRequest;

/// <summary>Импортировать правила маршрутизации из JSON.</summary>
public sealed record ImportProxyRulesRequest(string RulesJson) : IpcRequest;

// ── Health Check ────────────────────────────────────────────

/// <summary>Ping для проверки связи (Worker отвечает PongResponse).</summary>
public sealed record PingRequest : IpcRequest;

// ── Traffic Stats ───────────────────────────────────────────

/// <summary>Получить статистику трафика (per-app + global).</summary>
public sealed record GetTrafficStatsRequest : IpcRequest;

// ── Block Detection ─────────────────────────────────────────

/// <summary>Получить статус обнаруженных блокировок.</summary>
public sealed record GetBlockStatusRequest : IpcRequest;

/// <summary>Очистить историю блокировок.</summary>
public sealed record ClearBlocksRequest : IpcRequest;
