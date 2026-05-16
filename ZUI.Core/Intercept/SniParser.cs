// ═══════════════════════════════════════════════════════════════
// ZUI.Core / Intercept / SniParser.cs
// Парсинг TLS ClientHello → SNI (RFC 6066) + HTTP Host header
// ═══════════════════════════════════════════════════════════════

using System.Text;

namespace ZUI.Core.Intercept;

/// <summary>
/// Извлечение SNI из TLS ClientHello и Host из HTTP/1.x.
/// Статический класс — не требует состояния.
/// </summary>
public static class SniParser
{
    /// <summary>
    /// Извлечь SNI из TLS ClientHello (RFC 6066).
    /// Возвращает null если это не TLS, не ClientHello, или SNI отсутствует.
    /// </summary>
    public static string? ExtractSni(ReadOnlySpan<byte> payload)
    {
        // TLS Record Layer: ContentType=22 (Handshake), Version=0x0301..0x0303
        if (payload.Length < 5)
            return null;
        if (payload[0] != 0x16) // не Handshake
            return null;
        if (payload[1] != 0x03) // не TLS
            return null;

        int recordLen = (payload[2] << 8) | payload[3];
        if (payload.Length < 5 + recordLen)
            return null;

        var handshake = payload[5..];

        // Handshake Type = 1 (ClientHello)
        if (handshake.Length < 4 || handshake[0] != 0x01)
            return null;

        int hsLen = (handshake[1] << 16) | (handshake[2] << 8) | handshake[3];
        if (handshake.Length < 4 + hsLen)
            return null;

        var hello = handshake[4..];
        int pos = 0;

        // Version (2) + Random (32)
        pos += 34;
        if (pos >= hello.Length)
            return null;

        // Session ID (1 byte length + data)
        int sessionLen = hello[pos++];
        pos += sessionLen;
        if (pos + 2 >= hello.Length)
            return null;

        // Cipher Suites (2 byte length + data)
        int cipherLen = (hello[pos] << 8) | hello[pos + 1];
        pos += 2 + cipherLen;
        if (pos >= hello.Length)
            return null;

        // Compression Methods (1 byte length + data)
        int compLen = hello[pos++];
        pos += compLen;
        if (pos + 2 >= hello.Length)
            return null;

        // Extensions total length
        int extTotal = (hello[pos] << 8) | hello[pos + 1];
        pos += 2;
        int extEnd = pos + extTotal;

        // Iterate extensions
        while (pos + 4 <= extEnd && pos + 4 <= hello.Length)
        {
            int extType = (hello[pos] << 8) | hello[pos + 1];
            int extLen = (hello[pos + 2] << 8) | hello[pos + 3];
            pos += 4;

            if (extType == 0x0000 && extLen > 5) // SNI extension (type 0)
            {
                // Server Name List Length (2) + Name Type (1) + Name Length (2) + Name
                int nameLen = (hello[pos + 3] << 8) | hello[pos + 4];
                if (pos + 5 + nameLen <= hello.Length)
                    return Encoding.ASCII.GetString(hello.Slice(pos + 5, nameLen));
            }

            pos += extLen;
        }

        return null;
    }

    /// <summary>
    /// Проверить совпадение SNI с паттерном (поддержка wildcard *).
    /// </summary>
    public static bool MatchSni(string sni, string pattern)
    {
        if (pattern == "*")
            return true;
        if (!pattern.StartsWith("*."))
            return sni.Equals(pattern, StringComparison.OrdinalIgnoreCase);

        // *.example.com → совпадает с sub.example.com, но НЕ с example.com
        var suffix = pattern[1..]; // ".example.com"
        return sni.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)
            && sni.Length > suffix.Length;
    }

    /// <summary>
    /// Извлечь Host из HTTP/1.x запроса или ответа.
    /// Ищет заголовок "Host:" (case-insensitive).
    /// </summary>
    public static string? ExtractHostFromHttp(ReadOnlySpan<byte> payload)
    {
        // Минимум: "GET / HTTP/1.1\r\n" = 16 bytes
        if (payload.Length < 16)
            return null;

        // Быстрая проверка: это HTTP?
        var firstLine = payload[..Math.Min(payload.Length, 8)];
        if (!IsHttpMethod(firstLine) && !IsHttpResponse(firstLine))
            return null;

        // Ищем \r\nHost: или \nHost:
        for (int i = 0; i < payload.Length - 6; i++)
        {
            // Ищем "Host:" после \r\n или \n
            if (i > 0 && payload[i - 1] != '\n')
                continue;

            if (payload[i] == 'H' || payload[i] == 'h')
            {
                var candidate = payload[i..];
                if (candidate.Length < 6)
                    continue;

                // "Host:" case-insensitive
                if ((candidate[0] == 'H' || candidate[0] == 'h')
                    && (candidate[1] == 'o' || candidate[1] == 'O')
                    && (candidate[2] == 's' || candidate[2] == 'S')
                    && (candidate[3] == 't' || candidate[3] == 'T')
                    && candidate[4] == ':')
                {
                    // Пропускаем ':' и возможные пробелы
                    int hostStart = i + 5;
                    while (hostStart < payload.Length && payload[hostStart] == ' ')
                        hostStart++;

                    int hostEnd = hostStart;
                    while (hostEnd < payload.Length && payload[hostEnd] != '\r' && payload[hostEnd] != '\n')
                        hostEnd++;

                    if (hostEnd > hostStart)
                        return Encoding.ASCII.GetString(payload[hostStart..hostEnd]);
                }
            }
        }

        return null;
    }

    private static bool IsHttpMethod(ReadOnlySpan<byte> data)
    {
        return data.StartsWith("GET "u8)
            || data.StartsWith("POST "u8)
            || data.StartsWith("PUT "u8)
            || data.StartsWith("HEAD "u8)
            || data.StartsWith("DELETE "u8)
            || data.StartsWith("OPTIONS "u8)
            || data.StartsWith("PATCH "u8)
            || data.StartsWith("CONNECT "u8);
    }

    private static bool IsHttpResponse(ReadOnlySpan<byte> data)
    {
        return data.StartsWith("HTTP/"u8);
    }
}
