// ═══════════════════════════════════════════════════════════════
// ZUI.Telegram / MtProto / MtProtoTransport.cs
// MTProto obfuscation: XOR шифрование/дешифрование
// Поддержка: простой XOR-секрет и dd-secret (0xdd + AES)
// ═══════════════════════════════════════════════════════════════

using System.Security.Cryptography;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ZUI.Core;

namespace ZUI.Telegram.MtProto;

/// <summary>
/// MTProto obfuscation транспорт.
/// Шифрует/дешифрует пакеты по XOR-секрету.
/// 
/// Для dd-secret (0xdd):
/// - Первый байт пакета = 0xdd → используется AES-CTR шифрование
/// - Иначе → обычный XOR с секретом
///
/// Для simple-секрета:
/// - Все данные XOR-ятся циклически с 16-байтовым ключом
/// </summary>
public sealed class MtProtoTransport
{
    private readonly ILogger _logger;
    private readonly SecretConfig _secretConfig;

    /// <summary>
    /// Создать транспорт с указанным секретом.
    /// </summary>
    public MtProtoTransport(SecretConfig secretConfig, ILogger<MtProtoTransport>? logger = null)
    {
        _secretConfig = secretConfig ?? throw new ArgumentNullException(nameof(secretConfig));
        _logger = logger ?? NullLoggerFactory.Instance.CreateLogger<MtProtoTransport>();
    }

    /// <summary>Текущий секрет.</summary>
    public SecretConfig Secret => _secretConfig;

    // ── XOR шифрование/дешифрование ──────────────────────────

    /// <summary>
    /// XOR-шифрование/дешифрование данных (симметричная операция).
    /// Циклически применяет 16-байтовый ключ к данным.
    /// </summary>
    public static void XorWithKey(byte[] data, int offset, int count, byte[] key)
    {
        if (key.Length == 0) return;
        int keyLen = key.Length;

        for (int i = 0; i < count; i++)
        {
            data[offset + i] = (byte)(data[offset + i] ^ key[i % keyLen]);
        }
    }

    /// <summary>
    /// XOR-шифрование/дешифрование данных (симметричная операция).
    /// Создаёт новую копию данных (не модифицирует оригинал).
    /// </summary>
    public static byte[] XorWithKey(byte[] data, byte[] key)
    {
        var result = new byte[data.Length];
        Buffer.BlockCopy(data, 0, result, 0, data.Length);
        XorWithKey(result, 0, result.Length, key);
        return result;
    }

    /// <summary>
    /// Деобфусцировать MTProto заголовок и извлечь DC ID.
    /// </summary>
    /// <param name="header">64-байтовый obfuscated заголовок.</param>
    /// <returns>DC ID (1-5) или 0.</returns>
    public int DecryptHeader(byte[] header)
    {
        if (header.Length < MtProtoPacket.HeaderSize)
            return 0;

        // Проверяем тип обфускации по первому байту после random prefix
        // Для simple секрета — XOR с ключом
        if (_secretConfig.Type == SecretType.Simple)
        {
            return MtProtoPacket.ExtractDcId(header);
        }

        // Для dd-secret — первый байт может быть 0xdd
        // Но в MTProto обфускации заголовок уже содержит XOR-ключ в первых байтах
        // DC ID извлекается стандартным способом через XOR-ключ из первых 4 байт
        return MtProtoPacket.ExtractDcId(header);
    }

    // ── dd-secret обработка ──────────────────────────────────

    /// <summary>
    /// Проверить, использует ли пакет dd-secret обфускацию.
    /// Первый байт после random prefix = 0xdd.
    /// </summary>
    public static bool IsDdSecretPacket(byte[] packet)
    {
        if (packet.Length < MtProtoPacket.MinPrefixLength)
            return false;

        // В dd-secret формате, первый байт пакета после TLS record = 0xdd
        // Это индикатор, что используется dd-secret
        return packet[0] == 0xdd;
    }

    /// <summary>
    /// Дешифровать данные с dd-secret (AES-256-CTR).
    /// dd-secret: 0xdd + 16-байтовый ключ → полный 32-байтовый AES-ключ
    /// формируется из секрета + хеша.
    /// </summary>
    public byte[] DecryptDdSecret(byte[] data, byte[] initializationVector)
    {
        // Для dd-secret: используем AES-CTR с ключом, полученным из секрета
        // Формирование ключа: SHA256(secret + iv)[0..32]
        using var sha256 = SHA256.Create();
        var keyMaterial = new byte[_secretConfig.Key.Length + initializationVector.Length];
        Buffer.BlockCopy(_secretConfig.Key, 0, keyMaterial, 0, _secretConfig.Key.Length);
        Buffer.BlockCopy(initializationVector, 0, keyMaterial, _secretConfig.Key.Length, initializationVector.Length);

        var fullKey = sha256.ComputeHash(keyMaterial);
        var aesKey = new byte[32];
        Buffer.BlockCopy(fullKey, 0, aesKey, 0, 32);

        // AES-CTR дешифрование
        return AesCtrDecrypt(data, aesKey, initializationVector);
    }

    /// <summary>
    /// AES-CTR дешифрование (используется для dd-secret).
    /// </summary>
    private static byte[] AesCtrDecrypt(byte[] data, byte[] key, byte[] iv)
    {
        var result = new byte[data.Length];
        using var aes = Aes.Create();
        aes.Key = key;
        aes.Mode = CipherMode.ECB;
        aes.Padding = PaddingMode.None;

        var counter = new byte[16];
        Buffer.BlockCopy(iv, 0, counter, 0, Math.Min(iv.Length, 16));

        using var encryptor = aes.CreateEncryptor();
        var blockSize = 16;

        for (int offset = 0; offset < data.Length; offset += blockSize)
        {
            // Шифруем counter → keystream
            var keystream = new byte[blockSize];
            encryptor.TransformBlock(counter, 0, blockSize, keystream, 0);

            // XOR data с keystream
            int chunkSize = Math.Min(blockSize, data.Length - offset);
            for (int i = 0; i < chunkSize; i++)
            {
                result[offset + i] = (byte)(data[offset + i] ^ keystream[i]);
            }

            // Increment counter (big-endian)
            IncrementCounter(counter);
        }

        return result;
    }

    /// <summary>
    /// Инкремент 16-байтового counter (big-endian).
    /// </summary>
    private static void IncrementCounter(byte[] counter)
    {
        for (int i = 15; i >= 0; i--)
        {
            if (++counter[i] != 0)
                break;
        }
    }

    // ── Создание obfuscated заголовка ────────────────────────

    /// <summary>
    /// Создать MTProto obfuscated init-заголовок (64 байта).
    /// Используется при установке соединения к Telegram DC.
    /// </summary>
    /// <param name="dcId">DC ID (1-5).</param>
    /// <param name="secret">Секрет прокси (может быть null для прямого подключения).</param>
    /// <returns>64-байтовый obfuscated заголовок.</returns>
    public static byte[] CreateObfuscatedHeader(int dcId, SecretConfig? secret = null)
    {
        var header = new byte[MtProtoPacket.HeaderSize];
        RandomNumberGenerator.Fill(header);

        // Убедиться, что первые 4 байта не совпадают с TLS record types
        // (чтобы DPI не распознал как TLS)
        header[0] &= 0x7F; // Убрать старший бит

        // Вставить DC ID в байты 60-61 (little-endian)
        // Сначала закодировать, потом обфусцировать XOR-ключом из первых 4 байт
        short dcIdShort = (short)dcId;
        header[60] = (byte)(dcIdShort & 0xFF);
        header[61] = (byte)((dcIdShort >> 8) & 0xFF);

        // Обфусцировать байты 56-63 XOR-ключом из первых 4 байт (reversed)
        byte key0 = header[3], key1 = header[2], key2 = header[1], key3 = header[0];
        for (int i = 56; i < 64; i++)
        {
            int keyIndex = (i - 56) % 4;
            byte k = keyIndex switch { 0 => key0, 1 => key1, 2 => key2, _ => key3 };
            header[i] = (byte)(header[i] ^ k);
        }

        return header;
    }

    /// <summary>
    /// Создать полный init-пакет для подключения через прокси.
    /// Формат: random_prefix (8-60 байт) + 0xdd (если dd-secret) + encrypted_data.
    /// </summary>
    public byte[] CreateInitPacket(int dcId)
    {
        if (_secretConfig.Type == SecretType.DdSecret)
        {
            // dd-secret: первый байт = 0xdd, затем random padding, затем данные
            var packet = new byte[MtProtoPacket.HeaderSize];
            RandomNumberGenerator.Fill(packet);
            packet[0] = 0xdd; // Индикатор dd-secret
            return packet;
        }

        // Simple: обычный obfuscated заголовок
        return CreateObfuscatedHeader(dcId, _secretConfig);
    }
}
