// ═══════════════════════════════════════════════════════════════
// ZUI.Telegram / MtProto / MtProtoPacket.cs
// Формат MTProto obfuscated пакетов
// Random prefix (8-60) + encrypted payload + padding
// ═══════════════════════════════════════════════════════════════

namespace ZUI.Telegram.MtProto;

/// <summary>
/// MTProto obfuscated заголовок (64 байта).
/// Формат: random_prefix (8-60 байт) + DC ID (2 байта, little-endian) + padding
/// Первые 4 байта — XOR-ключ для деобфускации DC ID.
/// </summary>
public sealed class MtProtoPacket
{
    /// <summary>Размер MTProto init-заголовка (фиксированный).</summary>
    public const int HeaderSize = 64;

    /// <summary>Минимальная длина random prefix.</summary>
    public const int MinPrefixLength = 8;

    /// <summary>Максимальная длина random prefix.</summary>
    public const int MaxPrefixLength = 60;

    /// <summary>Смещение DC ID в деобфусцированном заголовке (от начала).</summary>
    public const int DcIdOffset = 60;

    // ── Telegram DC endpoints ────────────────────────────────

    /// <summary>
    /// Telegram DC ID → (Host, Port) для прямого TCP подключения.
    /// </summary>
    public static readonly Dictionary<int, (string Host, int Port)> DcEndpoints = new()
    {
        [1] = ("149.154.175.55", 443),
        [2] = ("149.154.167.51", 443),
        [3] = ("149.154.175.100", 443),
        [4] = ("149.154.167.91", 443),
        [5] = ("91.108.56.130", 443),
    };

    /// <summary>
    /// Telegram DC ID → WSS хост для WebSocket-туннеля.
    /// </summary>
    public static readonly Dictionary<int, string> DcWsHosts = new()
    {
        [1] = "kws1.web.telegram.org",
        [2] = "kws2.web.telegram.org",
        [3] = "kws3.web.telegram.org",
        [4] = "kws4.web.telegram.org",
        [5] = "kws5.web.telegram.org",
    };

    /// <summary>
    /// Известные IP-адреса Telegram DC.
    /// </summary>
    public static readonly HashSet<string> TelegramDcIps =
    [
        "149.154.167.220", "149.154.175.205", "149.154.167.51",
        "91.108.56.180", "91.108.4.1", "91.108.56.100",
        "149.154.167.92", "149.154.167.15", "91.108.56.14",
        "149.154.171.5", "149.154.175.100",
    ];

    /// <summary>
    /// Извлечь DC ID из MTProto obfuscated заголовка.
    /// XOR-ключ = первые 4 байта заголовка, записанные в обратном порядке.
    /// DC ID находится в байтах 60-61 (после деобфускации).
    /// </summary>
    /// <param name="header">64-байтовый заголовок.</param>
    /// <returns>DC ID (1-5) или 0 если не удалось определить.</returns>
    public static int ExtractDcId(byte[] header)
    {
        if (header.Length < HeaderSize)
            return 0;

        // XOR-ключ: первые 4 байта, reversed
        byte key0 = header[3], key1 = header[2], key2 = header[1], key3 = header[0];

        // Деобфусцировать байты 56-63 (8 байт)
        Span<byte> decoded = stackalloc byte[8];
        for (int i = 0; i < 8; i++)
        {
            byte k = i switch
            {
                0 => key0, 1 => key1, 2 => key2, 3 => key3,
                4 => key0, 5 => key1, 6 => key2, 7 => key3,
                _ => 0,
            };
            decoded[i] = (byte)(header[56 + i] ^ k);
        }

        // DC ID в байтах 60-61 заголовка = смещение 4 в decoded (60-56=4)
        // Little-endian: младший байт первый
        short dcId = (short)(decoded[4] | (decoded[5] << 8));
        int absDcId = Math.Abs(dcId);

        return absDcId is >= 1 and <= 5 ? absDcId : 0;
    }

    /// <summary>
    /// Проверить, является ли IP-адрес Telegram DC.
    /// </summary>
    public static bool IsTelegramDcIp(string ip)
    {
        return TelegramDcIps.Contains(ip);
    }

    /// <summary>
    /// Получить WSS хост для DC ID.
    /// </summary>
    public static string? GetWsHost(int dcId)
    {
        return DcWsHosts.TryGetValue(dcId, out var host) ? host : null;
    }

    /// <summary>
    /// Получить TCP endpoint для DC ID.
    /// </summary>
    public static (string Host, int Port)? GetDcEndpoint(int dcId)
    {
        return DcEndpoints.TryGetValue(dcId, out var ep) ? ep : null;
    }
}
