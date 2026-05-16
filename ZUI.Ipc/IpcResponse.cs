// ═══════════════════════════════════════════════════════════════
// ZUI.Ipc / IpcResponse.cs
// Ответы Worker → UI (Named Pipe)
// ═══════════════════════════════════════════════════════════════

namespace ZUI.Ipc;

/// <summary>
/// Базовый тип ответа Worker → UI.
/// </summary>
public abstract record IpcResponse : IpcMessage
{
    /// <summary>ID запроса, на который этот ответ (для корреляции).</summary>
    public Guid RequestId { get; init; }
}

// ── Общие ───────────────────────────────────────────────────

/// <summary>Успешное выполнение запроса (без данных).</summary>
public sealed record SuccessResponse : IpcResponse;

/// <summary>Ошибка при выполнении запроса.</summary>
public sealed record ErrorResponse(string Message, string? Details = null) : IpcResponse;

/// <summary>Pong ответ на PingRequest.</summary>
public sealed record PongResponse : IpcResponse;

// ── DPI Bypass Status ───────────────────────────────────────

/// <summary>Статус обхода DPI.</summary>
public sealed record BypassStatusResponse(
    bool IsRunning,
    string? StrategyId,
    int GameFilterMode,
    long PacketsProcessed,
    long PacketsBypassed,
    double UptimeSeconds) : IpcResponse;

// ── Proxifier Status ────────────────────────────────────────

/// <summary>Статус проксификатора.</summary>
public sealed record ProxifierStatusResponse(
    bool IsRunning,
    int ActiveRules,
    int ActiveConnections,
    long TotalBytesSent,
    long TotalBytesReceived) : IpcResponse;

/// <summary>Информация о соединении проксификатора (для IPC).</summary>
public sealed record ProxifierConnectionInfo(
    string ConnectionId,
    int Pid,
    string ProcessName,
    string TargetHost,
    int TargetPort,
    string TargetIp,
    string Action,
    string? ProxyName,
    string DnsPolicy,
    DateTime StartedAt,
    DateTime? EndedAt,
    long BytesSent,
    long BytesReceived,
    string Status) : IpcResponse;

/// <summary>Список соединений проксификатора.</summary>
public sealed record ProxifierConnectionsResponse(
    ProxifierConnectionInfo[] Connections) : IpcResponse;

// ── Telegram Proxy Status ───────────────────────────────────

/// <summary>Статус Telegram proxy.</summary>
public sealed record TgProxyStatusResponse(
    bool Socks5Running,
    int Socks5Port,
    bool MtProxyRunning,
    int MtProxyPort,
    int ActiveConnections) : IpcResponse;

// ── DNS Status ──────────────────────────────────────────────

/// <summary>Статус DNS компонентов.</summary>
public sealed record DnsStatusResponse(
    bool DohEnabled,
    bool FakeDnsEnabled,
    int CachedEntries,
    int FakeDnsOverrides,
    bool SnifferRunning,
    long SnifferPackets,
    long SnifferRecords) : IpcResponse;

// ── Diagnostics ─────────────────────────────────────────────

/// <summary>Результат диагностики.</summary>
public sealed record DiagnosticResultItem(
    string Name,
    bool Passed,
    string? Message,
    string? Remediation);

/// <summary>Результаты диагностики.</summary>
public sealed record DiagnosticResultsResponse(
    DiagnosticResultItem[] Results) : IpcResponse;

// ── Available Strategies ────────────────────────────────────

/// <summary>Список доступных стратегий.</summary>
public sealed record AvailableStrategiesResponse(
    string[] StrategyIds) : IpcResponse;

// ── Proxy Profile ───────────────────────────────────────────

/// <summary>Десериализованная модель прокси-сервера (для ответа).</summary>
public sealed record ProxyServerInfo(
    string Id,
    string Name,
    string Host,
    int Port,
    string ProxyType,
    bool AuthenticationEnabled,
    string? Username,
    string DnsPolicy);

/// <summary>Десериализованная модель правила маршрутизации.</summary>
public sealed record ProxyRuleInfo(
    string Id,
    string Name,
    bool IsEnabled,
    int Priority,
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
    string DnsPolicy);

/// <summary>Десериализованная модель цепочки прокси.</summary>
public sealed record ProxyChainInfo(
    string Id,
    string Name,
    string[] ServerIds,
    string FailoverPolicy);

/// <summary>Полный профиль проксификатора.</summary>
public sealed record ProxyProfileResponse(
    ProxyServerInfo[] Servers,
    ProxyRuleInfo[] Rules,
    ProxyChainInfo[] Chains) : IpcResponse;

/// <summary>Результат проверки прокси-сервера.</summary>
public sealed record CheckProxyResponse(
    bool Success,
    string? Error,
    long LatencyMs) : IpcResponse;

// ── Traffic Stats ───────────────────────────────────────────

/// <summary>Статистика одного соединения (per-app).</summary>
public sealed record ConnectionStatsInfo(
    string ConnectionId,
    string ProcessName,
    int Pid,
    string TargetHost,
    int TargetPort,
    long BytesSent,
    long BytesReceived,
    DateTime StartedAt,
    string Status);

/// <summary>Ответ со статистикой трафика.</summary>
public sealed record TrafficStatsResponse(
    long TotalBytesSent,
    long TotalBytesReceived,
    long TotalConnections,
    long ActiveConnections,
    double BytesPerSecond,
    ConnectionStatsInfo[] Connections) : IpcResponse;

// ── Block Detection ─────────────────────────────────────────

/// <summary>Информация о блокировке (для ответа).</summary>
public sealed record BlockInfo(
    string Target,
    string Type,       // "TcpReset" | "SilentDrop" | "DpiDrop" | "TtlAnomaly" | "DnsMismatch"
    string Confidence, // "Low" | "Medium" | "High" | "Certain"
    string Description,
    DateTime DetectedAt,
    int Occurrences);

/// <summary>Статистика блокировок.</summary>
public sealed record BlockStatsInfo(
    int TotalBlocks,
    int TcpResets,
    int SilentDrops,
    int DpiDrops,
    int TtlAnomalies,
    int DnsMismatches,
    int ActiveConnections);

/// <summary>Ответ со статусом блокировок.</summary>
public sealed record BlockStatusResponse(
    BlockStatsInfo Stats,
    BlockInfo[] RecentBlocks) : IpcResponse;

// ── Active Probe ────────────────────────────────────────────

/// <summary>Результат одной пробы.</summary>
public sealed record ProbeResultInfo(
    string Domain,
    string Type,
    bool Success,
    long LatencyMs,
    string? LocalIp,
    string? DohIp,
    bool DnsMismatch,
    int? HttpStatusCode,
    string? ServerName,
    string? Error,
    string Description);

/// <summary>Ответ с результатами проб домена.</summary>
public sealed record ProbeResultResponse(
    string Domain,
    ProbeResultInfo[] Results) : IpcResponse;
