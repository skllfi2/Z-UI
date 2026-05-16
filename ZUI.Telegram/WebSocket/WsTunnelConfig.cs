// ═══════════════════════════════════════════════════════════════
// ZUI.Telegram / WebSocket / WsTunnelConfig.cs
// Конфигурация WebSocket туннеля к Telegram DC
// ═══════════════════════════════════════════════════════════════

namespace ZUI.Telegram.WebSocket;

/// <summary>
/// Конфигурация WebSocket туннеля (SOCKS5 → WSS → Telegram DC).
/// </summary>
public sealed class WsTunnelConfig
{
    /// <summary>WebSocket URL (например, wss://kws1.web.telegram.org/apiws).</summary>
    public string WsUrl { get; set; } = string.Empty;

    /// <summary>Секрет для WebSocket подключения.</summary>
    public string Secret { get; set; } = string.Empty;

    /// <summary>Включён ли WebSocket туннель.</summary>
    public bool IsEnabled => !string.IsNullOrWhiteSpace(WsUrl);

    /// <summary>Origin заголовок для WebSocket handshake.</summary>
    public string Origin { get; set; } = string.Empty;

    /// <summary>User-Agent для WebSocket handshake.</summary>
    public string UserAgent { get; set; } = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36";

    /// <summary>Таймаут подключения к WebSocket (мс).</summary>
    public int ConnectTimeoutMs { get; set; } = 10000;

    /// <summary>
    /// Создать конфигурацию из параметров IPC запроса.
    /// </summary>
    public static WsTunnelConfig FromIpcParams(string wsUrl, string secret)
    {
        // Определить Origin из WsUrl
        string origin = string.Empty;
        if (!string.IsNullOrWhiteSpace(wsUrl))
        {
            try
            {
                var uri = new Uri(wsUrl);
                origin = $"https://{uri.Host}";
            }
            catch
            {
                // Игнорировать невалидный URL
            }
        }

        return new WsTunnelConfig
        {
            WsUrl = wsUrl ?? string.Empty,
            Secret = secret ?? string.Empty,
            Origin = origin,
        };
    }

    /// <summary>
    /// Создать конфигурацию по умолчанию (отключено).
    /// </summary>
    public static WsTunnelConfig Disabled => new();
}
