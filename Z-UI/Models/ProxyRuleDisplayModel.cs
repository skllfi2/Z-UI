// ProxyRuleDisplayModel.cs - UI display model for proxy routing rule
namespace ZUI.Models;

/// <summary>
/// UI-модель для отображения и редактирования правила маршрутизации.
/// </summary>
public sealed class ProxyRuleDisplayModel
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public bool IsEnabled { get; set; } = true;
    public int Priority { get; set; }
    public string? ProcessName { get; set; }
    public string? ProcessNamePattern { get; set; }
    public int? ProcessId { get; set; }
    public string? DestinationIp { get; set; }
    public string? DestinationPort { get; set; }
    public string? DestinationDomain { get; set; }
    public string? DestinationDomainPattern { get; set; }
    public string Action { get; set; } = "Direct"; // Direct, Proxy, Chain, Block
    public string? ProxyServerId { get; set; }
    public string? ChainName { get; set; }
    public string DnsPolicy { get; set; } = "Local";
}