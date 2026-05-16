// ═══════════════════════════════════════════════════════════════
// ZUI.Proxy / Profile / ProxyJsonContext.cs
// AOT-compatible Source Generator для JSON сериализации ZUI.Proxy
// Профили проксификатора — сохранение/загрузка через Source Generator
// ═══════════════════════════════════════════════════════════════

using System.Text.Json;
using System.Text.Json.Serialization;
using ZUI.Core.Traffic;
using ZUI.Proxy.Chain;
using ZUI.Proxy.Profile;
using ZUI.Proxy.Rules;

namespace ZUI.Proxy;

/// <summary>
/// AOT-compatible JsonSerializerContext для ZUI.Proxy типов.
/// Используется ProxyProfileManager для сериализации/десериализации профилей.
/// </summary>
[JsonSerializable(typeof(ProxyProfile))]
[JsonSerializable(typeof(ProxyRule))]
[JsonSerializable(typeof(ProxyTarget))]
[JsonSerializable(typeof(ProxyChain))]
[JsonSerializable(typeof(FailoverPolicy))]
[JsonSerializable(typeof(ProxyAction))]
[JsonSerializable(typeof(ProxyType))]
[JsonSerializable(typeof(DnsPolicy))]
[JsonSerializable(typeof(ConnectionInfo))]
[JsonSerializable(typeof(ConnectionStatus))]
[JsonSerializable(typeof(List<ProxyRule>))]
[JsonSerializable(typeof(List<ProxyChain>))]
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    WriteIndented = true)]
public sealed partial class ProxyJsonContext : JsonSerializerContext;
