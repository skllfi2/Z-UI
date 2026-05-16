// ═══════════════════════════════════════════════════════════════
// ZUI.Ipc / IpcJsonContext.cs
// AOT-compatible Source Generator для JSON сериализации IPC сообщений
// Заменяет reflection-based IpcMessageTypeResolver
// Генерирует метаданные для полиморфной сериализации при компиляции
// ═══════════════════════════════════════════════════════════════

using System.Text.Json;
using System.Text.Json.Serialization;

namespace ZUI.Ipc;

/// <summary>
/// AOT-compatible JsonSerializerContext для всех IPC типов.
/// Source Generator генерирует метаданные сериализации при компиляции,
/// устраняя необходимость в reflection (DefaultJsonTypeInfoResolver).
/// 
/// Полиморфизм настроен через [JsonDerivedType] атрибуты на IpcMessage (все concrete типы).
/// Абстрактные IpcRequest/IpcResponse/IpcEvent не регистрируются — STJ не поддерживает их как derived types.
/// </summary>
[JsonSerializable(typeof(IpcMessage))]

// ── Concrete request types ─────────────────────────────────
[JsonSerializable(typeof(StartBypassRequest))]
[JsonSerializable(typeof(StopBypassRequest))]
[JsonSerializable(typeof(GetBypassStatusRequest))]
[JsonSerializable(typeof(GetAvailableStrategiesRequest))]
[JsonSerializable(typeof(SetGameFilterRequest))]
[JsonSerializable(typeof(StartProxifierRequest))]
[JsonSerializable(typeof(StopProxifierRequest))]
[JsonSerializable(typeof(GetProxifierStatusRequest))]
[JsonSerializable(typeof(GetProxifierConnectionsRequest))]
[JsonSerializable(typeof(AddProxyServerRequest))]
[JsonSerializable(typeof(RemoveProxyServerRequest))]
[JsonSerializable(typeof(UpdateProxyServerRequest))]
[JsonSerializable(typeof(GetProxyProfileRequest))]
[JsonSerializable(typeof(CheckProxyRequest))]
[JsonSerializable(typeof(AddProxyRuleRequest))]
[JsonSerializable(typeof(RemoveProxyRuleRequest))]
[JsonSerializable(typeof(StartTgWsProxyRequest))]
[JsonSerializable(typeof(StopTgWsProxyRequest))]
[JsonSerializable(typeof(StartMtProxyRequest))]
[JsonSerializable(typeof(StopMtProxyRequest))]
[JsonSerializable(typeof(GetTgProxyStatusRequest))]
[JsonSerializable(typeof(ConfigureDnsRequest))]
[JsonSerializable(typeof(GetDnsStatusRequest))]
[JsonSerializable(typeof(RunDiagnosticsRequest))]
[JsonSerializable(typeof(UpdateDomainListsRequest))]
[JsonSerializable(typeof(PingRequest))]

// ── Concrete response types ────────────────────────────────
[JsonSerializable(typeof(SuccessResponse))]
[JsonSerializable(typeof(ErrorResponse))]
[JsonSerializable(typeof(BypassStatusResponse))]
[JsonSerializable(typeof(ProxifierStatusResponse))]
[JsonSerializable(typeof(ProxifierConnectionsResponse))]
[JsonSerializable(typeof(ProxifierConnectionInfo))]
[JsonSerializable(typeof(TgProxyStatusResponse))]
[JsonSerializable(typeof(DnsStatusResponse))]
[JsonSerializable(typeof(DiagnosticResultsResponse))]
[JsonSerializable(typeof(DiagnosticResultItem))]
[JsonSerializable(typeof(PongResponse))]
[JsonSerializable(typeof(AvailableStrategiesResponse))]
[JsonSerializable(typeof(ProxyProfileResponse))]
[JsonSerializable(typeof(ProxyServerInfo))]
[JsonSerializable(typeof(ProxyRuleInfo))]
[JsonSerializable(typeof(ProxyChainInfo))]
[JsonSerializable(typeof(CheckProxyResponse))]

// ── Concrete event types ───────────────────────────────────
[JsonSerializable(typeof(PacketStatsEvent))]
[JsonSerializable(typeof(BypassStoppedEvent))]
[JsonSerializable(typeof(LogEntryEvent))]
[JsonSerializable(typeof(TgProxyClientConnectedEvent))]
internal sealed partial class IpcJsonContext : JsonSerializerContext;
