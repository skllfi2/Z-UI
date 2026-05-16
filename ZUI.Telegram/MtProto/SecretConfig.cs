// ═══════════════════════════════════════════════════════════════
// ZUI.Telegram / MtProto / SecretConfig.cs
// Конфигурация секрета MTProxy: обычный XOR и dd-secret
// ═══════════════════════════════════════════════════════════════

namespace ZUI.Telegram.MtProto;

/// <summary>
/// Тип секрета MTProxy.
/// </summary>
public enum SecretType
{
    /// <summary>Обычный XOR-секрет (16 байт).</summary>
    Simple,

    /// <summary>dd-secret: первый байт 0xdd, за ним 16 байт AES-ключ.</summary>
    DdSecret,
}

/// <summary>
/// Конфигурация секрета MTProxy.
/// Поддерживает два формата:
/// - Simple: 16-байтовый XOR-ключ (hex: 32 символа)
/// - DdSecret: 0xdd + 16-байтовый AES-ключ (hex: dd + 32 символа)
/// </summary>
public sealed class SecretConfig
{
    /// <summary>Тип секрета.</summary>
    public SecretType Type { get; }

    /// <summary>16-байтовый ключ (XOR или AES в зависимости от типа).</summary>
    public byte[] Key { get; }

    /// <summary>Оригинальная hex-строка секрета.</summary>
    public string SecretHex { get; }

    private SecretConfig(SecretType type, byte[] key, string secretHex)
    {
        Type = type;
        Key = key;
        SecretHex = secretHex;
    }

    /// <summary>
    /// Разобрать секрет из hex-строки.
    /// Форматы:
    /// - "dd" + 32 hex символа → DdSecret
    /// - 32 hex символа → Simple
    /// </summary>
    public static SecretConfig Parse(string secretHex)
    {
        if (string.IsNullOrWhiteSpace(secretHex))
            throw new ArgumentException("Secret cannot be empty", nameof(secretHex));

        var hex = secretHex.Trim();

        // dd-secret: начинается с "dd" (в hex), всего 34 символа (0xdd + 16 байт)
        if (hex.Length == 34 && hex.StartsWith("dd", StringComparison.OrdinalIgnoreCase))
        {
            var keyHex = hex[2..];
            var key = Convert.FromHexString(keyHex);
            if (key.Length != 16)
                throw new ArgumentException("dd-secret key must be 16 bytes", nameof(secretHex));

            return new SecretConfig(SecretType.DdSecret, key, hex);
        }

        // Simple: 16 байт = 32 hex символа
        if (hex.Length == 32)
        {
            var key = Convert.FromHexString(hex);
            if (key.Length != 16)
                throw new ArgumentException("Simple secret must be 16 bytes", nameof(secretHex));

            return new SecretConfig(SecretType.Simple, key, hex);
        }

        throw new ArgumentException(
            $"Invalid secret format: expected 32 hex chars (simple) or 'dd'+32 hex chars (dd-secret), got {hex.Length} chars",
            nameof(secretHex));
    }

    /// <summary>
    /// Попробовать разобрать секрет. Возвращает null при ошибке.
    /// </summary>
    public static SecretConfig? TryParse(string secretHex)
    {
        try
        {
            return Parse(secretHex);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Определить, является ли первый байт MTProto-пакета индикатором dd-secret.
    /// Если первый байт после random prefix = 0xdd → используется dd-secret.
    /// </summary>
    public static bool IsDdSecretIndicator(byte firstByte)
    {
        return firstByte == 0xdd;
    }

    /// <summary>
    /// Сгенерировать случайный простой секрет (16 байт).
    /// </summary>
    public static SecretConfig GenerateRandom()
    {
        var key = new byte[16];
        System.Security.Cryptography.RandomNumberGenerator.Fill(key);
        return new SecretConfig(SecretType.Simple, key, Convert.ToHexString(key).ToLowerInvariant());
    }

    /// <summary>
    /// Сгенерировать MTProxy ссылку для Telegram.
    /// Формат: tg://proxy?server=HOST&amp;port=PORT&amp;secret=SECRET
    /// </summary>
    public string GenerateTgLink(string host, int port)
    {
        return $"tg://proxy?server={host}&port={port}&secret={SecretHex}";
    }

    public override string ToString() => $"Secret({Type}, {SecretHex[..8]}...)";
}
