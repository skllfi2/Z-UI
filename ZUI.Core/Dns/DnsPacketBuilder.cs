// ═══════════════════════════════════════════════════════════════
// ZUI.Core / Dns / DnsPacketBuilder.cs
// Формирование DNS пакетов (запросы и ответы) на уровне байтов
// RFC 1035 (DNS), RFC 3596 (AAAA), RFC 8484 (DoH)
// ═══════════════════════════════════════════════════════════════

using System.Net;
using System.Text;

namespace ZUI.Core.Dns;

/// <summary>
/// Тип DNS записи.
/// </summary>
public enum DnsRecordType : ushort
{
    A = 1,
    NS = 2,
    CNAME = 5,
    MX = 15,
    AAAA = 28,
    ANY = 255,
}

/// <summary>
/// Класс DNS записи.
/// </summary>
public enum DnsClass : ushort
{
    IN = 1,
}

/// <summary>
/// Код DNS ответа (RCODE).
/// </summary>
public enum DnsResponseCode : byte
{
    NoError = 0,
    FormErr = 1,
    ServFail = 2,
    NXDomain = 3,
    NotImp = 4,
    Refused = 5,
}

/// <summary>
/// Разобранный DNS пакет.
/// </summary>
public sealed class DnsPacket
{
    /// <summary>ID транзакции (2 байта).</summary>
    public ushort TransactionId { get; init; }

    /// <summary>Флаги DNS заголовка.</summary>
    public DnsHeaderFlags Flags { get; init; }

    /// <summary>Количество вопросов.</summary>
    public ushort QuestionCount { get; init; }

    /// <summary>Количество ответов.</summary>
    public ushort AnswerCount { get; init; }

    /// <summary>Количество авторитетных записей.</summary>
    public ushort AuthorityCount { get; init; }

    /// <summary>Количество дополнительных записей.</summary>
    public ushort AdditionalCount { get; init; }

    /// <summary>Вопросы (секция QD).</summary>
    public DnsQuestion[] Questions { get; init; } = [];

    /// <summary>Ответы (секция AN).</summary>
    public DnsAnswer[] Answers { get; init; } = [];
}

/// <summary>
/// Флаги DNS заголовка.
/// </summary>
public readonly struct DnsHeaderFlags
{
    public bool IsResponse { get; init; }
    public byte Opcode { get; init; }
    public bool Authoritative { get; init; }
    public bool Truncated { get; init; }
    public bool RecursionDesired { get; init; }
    public bool RecursionAvailable { get; init; }
    public DnsResponseCode ResponseCode { get; init; }
}

/// <summary>
/// DNS вопрос (секция QD).
/// </summary>
public sealed class DnsQuestion
{
    public string Name { get; init; } = string.Empty;
    public DnsRecordType Type { get; init; }
    public DnsClass Class { get; init; } = DnsClass.IN;
}

/// <summary>
/// DNS ответ (секция AN).
/// </summary>
public sealed class DnsAnswer
{
    public string Name { get; init; } = string.Empty;
    public DnsRecordType Type { get; init; }
    public DnsClass Class { get; init; } = DnsClass.IN;
    public uint Ttl { get; init; }
    public byte[] Rdata { get; init; } = [];

    /// <summary>IP адрес для A/AAAA записей.</summary>
    public IPAddress? Address { get; init; }
}

/// <summary>
/// Построитель и парсер DNS пакетов.
/// Статический класс — не требует состояния.
/// </summary>
public static class DnsPacketBuilder
{
    // ── Формирование DNS запроса ───────────────────────────

    /// <summary>
    /// Построить DNS запрос (A или AAAA) для домена.
    /// </summary>
    public static byte[] BuildQuery(string domain, DnsRecordType type = DnsRecordType.A, ushort transactionId = 0)
    {
        if (transactionId == 0)
            transactionId = (ushort)Random.Shared.Next(1, ushort.MaxValue);

        using var ms = new MemoryStream();
        var writer = new BinaryWriter(ms);

        // Header (12 байт)
        writer.WriteBe(transactionId);            // ID
        writer.WriteBe((ushort)0x0100);           // Flags: RD=1, стандартный запрос
        writer.WriteBe((ushort)1);                // QDCOUNT = 1
        writer.WriteBe((ushort)0);                // ANCOUNT = 0
        writer.WriteBe((ushort)0);                // NSCOUNT = 0
        writer.WriteBe((ushort)0);                // ARCOUNT = 0

        // Question section
        WriteDomainName(writer, domain);
        writer.WriteBe((ushort)type);             // QTYPE
        writer.WriteBe((ushort)DnsClass.IN);      // QCLASS

        return ms.ToArray();
    }

    // ── Формирование DNS ответа ────────────────────────────

    /// <summary>
    /// Построить DNS ответ с A-записью (IPv4).
    /// </summary>
    public static byte[] BuildAResponse(ushort transactionId, string domain, IPAddress ip, uint ttl = 300)
    {
        var ipBytes = ip.GetAddressBytes();
        if (ipBytes.Length != 4)
            throw new ArgumentException("Expected IPv4 address for A record", nameof(ip));

        return BuildResponse(transactionId, domain, DnsRecordType.A, ipBytes, ttl);
    }

    /// <summary>
    /// Построить DNS ответ с AAAA-записью (IPv6).
    /// </summary>
    public static byte[] BuildAaaaResponse(ushort transactionId, string domain, IPAddress ip, uint ttl = 300)
    {
        var ipBytes = ip.GetAddressBytes();
        if (ipBytes.Length != 16)
            throw new ArgumentException("Expected IPv6 address for AAAA record", nameof(ip));

        return BuildResponse(transactionId, domain, DnsRecordType.AAAA, ipBytes, ttl);
    }

    /// <summary>
    /// Построить DNS ответ с NXDOMAIN (домен не существует).
    /// </summary>
    public static byte[] BuildNxDomainResponse(ushort transactionId, string domain, DnsRecordType type)
    {
        using var ms = new MemoryStream();
        var writer = new BinaryWriter(ms);

        // Header
        ushort flags = 0x8403; // QR=1, AA=1, RCODE=NXDomain
        writer.WriteBe(transactionId);
        writer.WriteBe(flags);
        writer.WriteBe((ushort)1);    // QDCOUNT
        writer.WriteBe((ushort)0);    // ANCOUNT
        writer.WriteBe((ushort)0);    // NSCOUNT
        writer.WriteBe((ushort)0);    // ARCOUNT

        // Question (повторяем вопрос)
        WriteDomainName(writer, domain);
        writer.WriteBe((ushort)type);
        writer.WriteBe((ushort)DnsClass.IN);

        return ms.ToArray();
    }

    // ── Парсинг DNS пакета ─────────────────────────────────

    /// <summary>
    /// Разобрать DNS пакет из байтов.
    /// </summary>
    public static Result<DnsPacket> Parse(ReadOnlySpan<byte> data)
    {
        if (data.Length < 12)
            return Result<DnsPacket>.Failed("DNS packet too short (min 12 bytes for header)");

        var reader = new DnsReader(data);

        try
        {
            // Header
            var transactionId = reader.ReadBeUInt16();
            var flags = reader.ReadBeUInt16();
            var qdCount = reader.ReadBeUInt16();
            var anCount = reader.ReadBeUInt16();
            var nsCount = reader.ReadBeUInt16();
            var arCount = reader.ReadBeUInt16();

            var headerFlags = new DnsHeaderFlags
            {
                IsResponse = (flags & 0x8000) != 0,
                Opcode = (byte)((flags >> 11) & 0xF),
                Authoritative = (flags & 0x0400) != 0,
                Truncated = (flags & 0x0200) != 0,
                RecursionDesired = (flags & 0x0100) != 0,
                RecursionAvailable = (flags & 0x0080) != 0,
                ResponseCode = (DnsResponseCode)(flags & 0x000F),
            };

            // Questions
            var questions = new DnsQuestion[qdCount];
            for (int i = 0; i < qdCount; i++)
            {
                var name = reader.ReadDomainName(data);
                var qType = (DnsRecordType)reader.ReadBeUInt16();
                var qClass = (DnsClass)reader.ReadBeUInt16();
                questions[i] = new DnsQuestion { Name = name, Type = qType, Class = qClass };
            }

            // Answers
            var answers = new DnsAnswer[anCount];
            for (int i = 0; i < anCount; i++)
            {
                var name = reader.ReadDomainName(data);
                var rType = (DnsRecordType)reader.ReadBeUInt16();
                var rClass = (DnsClass)reader.ReadBeUInt16();
                var ttl = reader.ReadBeUInt32();
                var rdLength = reader.ReadBeUInt16();
                var rdata = reader.ReadBytes(rdLength);

                IPAddress? address = null;
                if (rType == DnsRecordType.A && rdata.Length == 4)
                    address = new IPAddress(rdata);
                else if (rType == DnsRecordType.AAAA && rdata.Length == 16)
                    address = new IPAddress(rdata);

                answers[i] = new DnsAnswer
                {
                    Name = name,
                    Type = rType,
                    Class = rClass,
                    Ttl = ttl,
                    Rdata = rdata,
                    Address = address,
                };
            }

            return Result<DnsPacket>.Success(new DnsPacket
            {
                TransactionId = transactionId,
                Flags = headerFlags,
                QuestionCount = qdCount,
                AnswerCount = anCount,
                AuthorityCount = nsCount,
                AdditionalCount = arCount,
                Questions = questions,
                Answers = answers,
            });
        }
        catch (FormatException ex)
        {
            return Result<DnsPacket>.Failed($"Failed to parse DNS packet: {ex.Message}");
        }
    }

    /// <summary>
    /// Извлечь домен из DNS запроса (без полного парсинга).
    /// </summary>
    public static Result<string> ExtractDomainFromQuery(ReadOnlySpan<byte> data)
    {
        if (data.Length < 14)
            return Result<string>.Failed("DNS query too short");

        var reader = new DnsReader(data);
        reader.Position = 12; // Пропуск заголовка

        try
        {
            var name = reader.ReadDomainName(data);
            return Result<string>.Success(name);
        }
        catch (FormatException ex)
        {
            return Result<string>.Failed($"Failed to extract domain: {ex.Message}");
        }
        catch (ArgumentOutOfRangeException ex)
        {
            return Result<string>.Failed($"Failed to extract domain: {ex.Message}");
        }
    }

    /// <summary>
    /// Извлечь TransactionId из DNS пакета.
    /// </summary>
    public static ushort GetTransactionId(ReadOnlySpan<byte> data)
    {
        if (data.Length < 2) return 0;
        return (ushort)((data[0] << 8) | data[1]);
    }

    /// <summary>
    /// Проверить, что данные выглядят как DNS запрос.
    /// </summary>
    public static bool LooksLikeDnsQuery(ReadOnlySpan<byte> data)
    {
        if (data.Length < 12) return false;

        // QR bit = 0 (запрос), Opcode = 0 (стандартный запрос)
        var flags = (ushort)((data[2] << 8) | data[3]);
        bool isQuery = (flags & 0x8000) == 0;
        byte opcode = (byte)((flags >> 11) & 0xF);
        ushort qdCount = (ushort)((data[4] << 8) | data[5]);

        return isQuery && opcode == 0 && qdCount >= 1;
    }

    // ── Вспомогательные методы ─────────────────────────────

    private static byte[] BuildResponse(ushort transactionId, string domain, DnsRecordType type, byte[] rdata, uint ttl)
    {
        using var ms = new MemoryStream();
        var writer = new BinaryWriter(ms);

        // Header
        ushort flags = 0x8400; // QR=1, AA=1, RCODE=NoError
        writer.WriteBe(transactionId);
        writer.WriteBe(flags);
        writer.WriteBe((ushort)1);    // QDCOUNT
        writer.WriteBe((ushort)1);    // ANCOUNT
        writer.WriteBe((ushort)0);    // NSCOUNT
        writer.WriteBe((ushort)0);    // ARCOUNT

        // Question (повторяем вопрос)
        WriteDomainName(writer, domain);
        writer.WriteBe((ushort)type);
        writer.WriteBe((ushort)DnsClass.IN);

        // Answer
        WriteDomainName(writer, domain);
        writer.WriteBe((ushort)type);
        writer.WriteBe((ushort)DnsClass.IN);
        writer.WriteBe(ttl);
        writer.WriteBe((ushort)rdata.Length);
        writer.Write(rdata);

        return ms.ToArray();
    }

    /// <summary>
    /// Записать доменное имя в DNS формате (label sequence).
    /// "www.google.com" → [3]"www"[6]"google"[3]"com"[0]
    /// </summary>
    private static void WriteDomainName(BinaryWriter writer, string domain)
    {
        var labels = domain.Split('.', StringSplitOptions.RemoveEmptyEntries);
        foreach (var label in labels)
        {
            var bytes = Encoding.ASCII.GetBytes(label);
            writer.Write((byte)bytes.Length);
            writer.Write(bytes);
        }
        writer.Write((byte)0); // Корневой label
    }

    // ── BinaryWriter расширения (Big-Endian) ───────────────

    private static void WriteBe(this BinaryWriter writer, ushort value)
    {
        writer.Write((byte)(value >> 8));
        writer.Write((byte)(value & 0xFF));
    }

    private static void WriteBe(this BinaryWriter writer, uint value)
    {
        writer.Write((byte)((value >> 24) & 0xFF));
        writer.Write((byte)((value >> 16) & 0xFF));
        writer.Write((byte)((value >> 8) & 0xFF));
        writer.Write((byte)(value & 0xFF));
    }
}

// ── DnsReader — чтение DNS данных с компрессией ─────────────

file ref struct DnsReader
{
    private readonly ReadOnlySpan<byte> _data;

    public int Position { get; set; }

    public DnsReader(ReadOnlySpan<byte> data)
    {
        _data = data;
        Position = 0;
    }

    public ushort ReadBeUInt16()
    {
        if (Position + 2 > _data.Length)
            throw new ArgumentOutOfRangeException(nameof(Position), "Read beyond DNS packet");
        var value = (ushort)((_data[Position] << 8) | _data[Position + 1]);
        Position += 2;
        return value;
    }

    public uint ReadBeUInt32()
    {
        if (Position + 4 > _data.Length)
            throw new ArgumentOutOfRangeException(nameof(Position), "Read beyond DNS packet");
        var value = (uint)((_data[Position] << 24) | (_data[Position + 1] << 16) |
                           (_data[Position + 2] << 8) | _data[Position + 3]);
        Position += 4;
        return value;
    }

    public byte[] ReadBytes(int count)
    {
        if (Position + count > _data.Length)
            throw new ArgumentOutOfRangeException(nameof(Position), "Read beyond DNS packet");
        var result = _data.Slice(Position, count).ToArray();
        Position += count;
        return result;
    }

    /// <summary>
    /// Прочитать доменное имя с поддержкой DNS компрессии (pointer).
    /// Указатель: 2 байта, старшие 2 бита = 11, остальные 14 = смещение.
    /// </summary>
    public string ReadDomainName(ReadOnlySpan<byte> fullPacket)
    {
        var labels = new List<string>();
        int pos = Position;
        bool jumped = false;
        int maxJumps = 10; // Защита от циклов

        while (pos < fullPacket.Length)
        {
            byte len = fullPacket[pos];

            if (len == 0)
            {
                // Конец имени
                if (!jumped)
                    Position = pos + 1;
                break;
            }

            // Проверка указателя (compression pointer)
            if ((len & 0xC0) == 0xC0)
            {
                if (pos + 1 >= fullPacket.Length)
                    break;

                if (!jumped)
                    Position = pos + 2; // После указателя — конец имени

                var offset = ((len & 0x3F) << 8) | fullPacket[pos + 1];
                pos = offset;
                jumped = true;

                if (--maxJumps <= 0)
                    break; // Защита от бесконечного цикла

                continue;
            }

            // Обычный label
            pos++;
            if (pos + len > fullPacket.Length)
                break;

            var label = Encoding.ASCII.GetString(fullPacket.Slice(pos, len));
            labels.Add(label);
            pos += len;
        }

        return string.Join('.', labels);
    }
}
