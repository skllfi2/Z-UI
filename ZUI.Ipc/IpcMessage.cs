// ═══════════════════════════════════════════════════════════════
// ZUI.Ipc / IpcMessage.cs
// Базовые типы IPC протокола между UI (user) и Worker (SYSTEM)
// Named Pipes + JSON сериализация
// ═══════════════════════════════════════════════════════════════

using System.Text.Json.Serialization;

namespace ZUI.Ipc;

/// <summary>
/// Базовый тип IPC сообщения. Все запросы/ответы/события наследуются от него.
/// </summary>
[JsonPolymorphic(UnknownDerivedTypeHandling = JsonUnknownDerivedTypeHandling.FallBackToNearestAncestor)]
// Concrete request types (IpcRequest is abstract — handled via FallBackToNearestAncestor)
[JsonDerivedType(typeof(StartBypassRequest), "startBypass")]
[JsonDerivedType(typeof(StopBypassRequest), "stopBypass")]
[JsonDerivedType(typeof(GetBypassStatusRequest), "getBypassStatus")]
[JsonDerivedType(typeof(GetAvailableStrategiesRequest), "getAvailableStrategies")]
[JsonDerivedType(typeof(SetGameFilterRequest), "setGameFilter")]
[JsonDerivedType(typeof(StartProxifierRequest), "startProxifier")]
[JsonDerivedType(typeof(StopProxifierRequest), "stopProxifier")]
[JsonDerivedType(typeof(GetProxifierStatusRequest), "getProxifierStatus")]
[JsonDerivedType(typeof(GetProxifierConnectionsRequest), "getProxifierConnections")]
[JsonDerivedType(typeof(AddProxyServerRequest), "addProxyServer")]
[JsonDerivedType(typeof(RemoveProxyServerRequest), "removeProxyServer")]
[JsonDerivedType(typeof(UpdateProxyServerRequest), "updateProxyServer")]
[JsonDerivedType(typeof(GetProxyProfileRequest), "getProxyProfile")]
[JsonDerivedType(typeof(CheckProxyRequest), "checkProxy")]
[JsonDerivedType(typeof(AddProxyRuleRequest), "addProxyRule")]
[JsonDerivedType(typeof(RemoveProxyRuleRequest), "removeProxyRule")]
[JsonDerivedType(typeof(ExportProxyRulesRequest), "exportProxyRules")]
[JsonDerivedType(typeof(ImportProxyRulesRequest), "importProxyRules")]
[JsonDerivedType(typeof(StartTgWsProxyRequest), "startTgWsProxy")]
[JsonDerivedType(typeof(StopTgWsProxyRequest), "stopTgWsProxy")]
[JsonDerivedType(typeof(StartMtProxyRequest), "startMtProxy")]
[JsonDerivedType(typeof(StopMtProxyRequest), "stopMtProxy")]
[JsonDerivedType(typeof(GetTgProxyStatusRequest), "getTgProxyStatus")]
[JsonDerivedType(typeof(ConfigureDnsRequest), "configureDns")]
[JsonDerivedType(typeof(GetDnsStatusRequest), "getDnsStatus")]
[JsonDerivedType(typeof(RunDiagnosticsRequest), "runDiagnostics")]
[JsonDerivedType(typeof(UpdateDomainListsRequest), "updateDomainLists")]
[JsonDerivedType(typeof(PingRequest), "ping")]
[JsonDerivedType(typeof(GetTrafficStatsRequest), "getTrafficStats")]
[JsonDerivedType(typeof(GetBlockStatusRequest), "getBlockStatus")]
[JsonDerivedType(typeof(ClearBlocksRequest), "clearBlocks")]
// Concrete response types
[JsonDerivedType(typeof(SuccessResponse), "success")]
[JsonDerivedType(typeof(ErrorResponse), "error")]
[JsonDerivedType(typeof(BypassStatusResponse), "bypassStatus")]
[JsonDerivedType(typeof(ProxifierStatusResponse), "proxifierStatus")]
[JsonDerivedType(typeof(ProxifierConnectionsResponse), "proxifierConnections")]
[JsonDerivedType(typeof(ProxifierConnectionInfo), "proxifierConnectionInfo")]
[JsonDerivedType(typeof(TgProxyStatusResponse), "tgProxyStatus")]
[JsonDerivedType(typeof(DnsStatusResponse), "dnsStatus")]
[JsonDerivedType(typeof(DiagnosticResultsResponse), "diagnosticResults")]
[JsonDerivedType(typeof(AvailableStrategiesResponse), "availableStrategies")]
[JsonDerivedType(typeof(PongResponse), "pong")]
[JsonDerivedType(typeof(ProxyProfileResponse), "proxyProfile")]
[JsonDerivedType(typeof(CheckProxyResponse), "checkProxyRes")]
[JsonDerivedType(typeof(TrafficStatsResponse), "trafficStats")]
[JsonDerivedType(typeof(BlockStatusResponse), "blockStatus")]
[JsonDerivedType(typeof(ProbeResultResponse), "probeResult")]
// Concrete event types
[JsonDerivedType(typeof(PacketStatsEvent), "packetStats")]
[JsonDerivedType(typeof(BypassStoppedEvent), "bypassStopped")]
[JsonDerivedType(typeof(LogEntryEvent), "logEntry")]
[JsonDerivedType(typeof(TgProxyClientConnectedEvent), "tgProxyClientConnected")]
[JsonDerivedType(typeof(BlockDetectedEvent), "blockDetected")]
public abstract record IpcMessage
{
    /// <summary>Уникальный ID сообщения (для корреляции запрос-ответ).</summary>
    public Guid MessageId { get; init; } = Guid.NewGuid();

    /// <summary>Временная метка сообщения.</summary>
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
}
