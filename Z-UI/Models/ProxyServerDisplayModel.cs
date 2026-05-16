// ProxyServerDisplayModel.cs - UI display model for proxy server
namespace ZUI.Models;

/// <summary>
/// UI-модель для отображения и редактирования прокси-сервера.
/// Не содержит Password в открытом виде в UI.
/// </summary>
public sealed class ProxyServerDisplayModel
{
    public string Name { get; set; } = string.Empty;
    public string Host { get; set; } = string.Empty;
    public int Port { get; set; } = 1080;
    public string ProxyType { get; set; } = "Socks5"; // Socks4, Socks4a, Socks5, HttpConnect
    public bool AuthenticationEnabled { get; set; }
    public string? Username { get; set; }
    public string? Password { get; set; }
    public string DnsPolicy { get; set; } = "Local"; // Local, ThroughProxy
}