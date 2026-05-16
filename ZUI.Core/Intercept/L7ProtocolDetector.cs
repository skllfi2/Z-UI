// ═══════════════════════════════════════════════════════════════
// ZUI.Core / Intercept / L7ProtocolDetector.cs
// Детекция протоколов прикладного уровня (L7) по payload + порту
// ═══════════════════════════════════════════════════════════════

namespace ZUI.Core.Intercept;

/// <summary>
/// Протокол прикладного уровня, определяемый по payload и порту.
/// </summary>
public enum L7Protocol
{
    None,
    Tls,
    Http,
    Discord,
    Stun,
}

/// <summary>
/// Детекция L7-протокола по содержимому пакета и порту назначения.
/// Статический класс — не требует состояния.
/// </summary>
public static class L7ProtocolDetector
{
    /// <summary>
    /// STUN magic cookie (RFC 5389).
    /// </summary>
    private const uint StunMagicCookie = 0x2112A442;

    /// <summary>
    /// Определить L7-протокол по payload и порту назначения.
    /// </summary>
    public static L7Protocol Detect(ReadOnlySpan<byte> payload, ushort dstPort)
    {
        if (payload.Length == 0)
            return L7Protocol.None;

        // 1. TLS — самый частый случай для DPI bypass
        if (IsTls(payload))
            return L7Protocol.Tls;

        // 2. HTTP — для Host-based фильтрации
        if (IsHttp(payload))
            return L7Protocol.Http;

        // 3. STUN — для WebRTC/Discord voice
        if (IsStun(payload))
            return L7Protocol.Stun;

        // 4. Discord — RTP/voice протокол на специфичных портах
        if (IsDiscordPort(dstPort) && IsRtpOrDiscordVoice(payload))
            return L7Protocol.Discord;

        // 5. Port-based hints (когда payload недостаточен для определения)
        if (IsDiscordPort(dstPort))
            return L7Protocol.Discord;

        if (IsStunPort(dstPort) && payload.Length >= 20)
            return L7Protocol.Stun;

        return L7Protocol.None;
    }

    /// <summary>
    /// TLS Record: ContentType=22 (Handshake), Version 0x0301-0x0303.
    /// Также учитывает ContentType=20 (ChangeCipherSpec), 21 (Alert), 23 (ApplicationData).
    /// </summary>
    private static bool IsTls(ReadOnlySpan<byte> payload)
    {
        if (payload.Length < 5)
            return false;

        byte contentType = payload[0];
        if (contentType is not (0x14 or 0x15 or 0x16 or 0x17))
            return false;

        // TLS version: 0x0301 (1.0) — 0x0303 (1.2), or 0x0300 (SSL 3.0)
        if (payload[1] != 0x03)
            return false;

        if (payload[2] is not (0x00 or 0x01 or 0x02 or 0x03))
            return false;

        // Проверяем что record length в разумных пределах
        int recordLen = (payload[3] << 8) | payload[4];
        return recordLen > 0 && recordLen <= 16384 + 2048; // TLS max + some margin
    }

    /// <summary>
    /// HTTP/1.x: начинается с метода или "HTTP/".
    /// </summary>
    private static bool IsHttp(ReadOnlySpan<byte> payload)
    {
        if (payload.Length < 4)
            return false;

        // HTTP methods
        if (payload.StartsWith("GET "u8)) return true;
        if (payload.StartsWith("POST"u8)) return true;
        if (payload.StartsWith("PUT "u8)) return true;
        if (payload.StartsWith("HEAD"u8)) return true;
        if (payload.StartsWith("DELE"u8)) return true;
        if (payload.StartsWith("OPTI"u8)) return true;
        if (payload.StartsWith("PATC"u8)) return true;
        if (payload.StartsWith("CONN"u8)) return true;

        // HTTP response
        if (payload.StartsWith("HTTP"u8)) return true;

        return false;
    }

    /// <summary>
    /// STUN (RFC 5389): первые 2 бита = 00, magic cookie = 0x2112A442.
    /// </summary>
    private static bool IsStun(ReadOnlySpan<byte> payload)
    {
        // STUN header = 20 bytes minimum
        if (payload.Length < 20)
            return false;

        // First 2 bits must be 00 (RFC 5389)
        if ((payload[0] & 0xC0) != 0)
            return false;

        // Message type: common values
        // 0x0001 = Binding Request
        // 0x0101 = Binding Success Response
        // 0x0111 = Binding Error Response
        ushort msgType = (ushort)((payload[0] << 8) | payload[1]);
        if (msgType is not (0x0001 or 0x0101 or 0x0111))
            return false;

        // Magic cookie at offset 4
        uint cookie = (uint)((payload[4] << 24) | (payload[5] << 16) | (payload[6] << 8) | payload[7]);
        return cookie == StunMagicCookie;
    }

    /// <summary>
    /// Discord voice/RTP: заголовок RTP (10xx xxxx) на Discord-портах.
    /// </summary>
    private static bool IsRtpOrDiscordVoice(ReadOnlySpan<byte> payload)
    {
        if (payload.Length < 12)
            return false;

        // RTP header: V=2 (0x80-0xBF), PT typically 0-127
        byte first = payload[0];
        int version = (first >> 6) & 0x03;

        if (version == 2) // RTP v2
            return true;

        // Discord-specific: type 0x46 (70) frame flag
        if (first == 0x46 && payload.Length >= 4)
            return true;

        return false;
    }

    /// <summary>
    /// Discord voice порты: 19294-19344, 50000-50100.
    /// </summary>
    private static bool IsDiscordPort(ushort port) =>
        port is >= 19294 and <= 19344 or >= 50000 and <= 50100;

    /// <summary>
    /// STUN порты: 3478 (classic), 5349 (TLS).
    /// </summary>
    private static bool IsStunPort(ushort port) =>
        port is 3478 or 5349;
}
