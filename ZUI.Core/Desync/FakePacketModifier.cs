// ═══════════════════════════════════════════════════════════════
// ZUI.Core / Desync / FakePacketModifier.cs
// Модификация fake TLS/QUIC пакетов
// Аналоги: --dpi-desync-fake-tls-mod=rnd,dupsid,sni=www.google.com
// ═══════════════════════════════════════════════════════════════

using System.Net;
using System.Runtime.InteropServices;
using ZUI.Core.Intercept;
using ZUI.Core.WinDivert;

namespace ZUI.Core.Desync;

/// <summary>
/// Модификаторы fake-пакетов для обхода DPI.
/// Применяются к payload'у fake-пакета ПОСЛЕ построения.
/// Порядок модификаций: rnd → dupsid → sni= (как в winws).
/// </summary>
public static class FakePacketModifier
{
    /// <summary>
    /// Применить все модификации к fake-пакету.
    /// Модификации: rnd, dupsid, sni=domain.
    /// </summary>
    /// <param name="fakePacket">Fake-пакет (raw bytes) с заголовками + payload.</param>
    /// <param name="fakeAddr">WinDivert address fake-пакета.</param>
    /// <param name="mods">Массив модификаторов (например ["rnd","dupsid","sni=www.google.com"]).</param>
    /// <param name="originalSni">SNI из оригинального пакета (для dupsid).</param>
    public static void ApplyMods(
        byte[] fakePacket,
        WinDivertAddress fakeAddr,
        string[] mods,
        string? originalSni)
    {
        if (mods is null || mods.Length == 0)
            return;

        // Определяем смещение payload в fake-пакете
        int payloadOffset = GetPayloadOffset(fakePacket, fakeAddr);
        if (payloadOffset < 0 || payloadOffset >= fakePacket.Length)
            return;

        foreach (var mod in mods)
        {
            var trimmed = mod.Trim();

            if (trimmed.Equals("rnd", StringComparison.OrdinalIgnoreCase))
            {
                ApplyRnd(fakePacket, payloadOffset);
            }
            else if (trimmed.Equals("dupsid", StringComparison.OrdinalIgnoreCase))
            {
                ApplyDupSid(fakePacket, payloadOffset, originalSni);
            }
            else if (trimmed.StartsWith("sni=", StringComparison.OrdinalIgnoreCase))
            {
                string domain = trimmed[4..];
                ApplySniOverride(fakePacket, payloadOffset, domain);
            }
        }
    }

    // ── rnd: заполнить Session ID случайными байтами ─────────
    // В TLS ClientHello Session ID находится по фиксированному смещению:
    // TLS Record (5) + Handshake Type (1) + Length (3) + Version (2) + Random (32) = offset 43
    // Session ID Length at offset 43, Session ID data starts at offset 44

    /// <summary>
    /// Заполнить TLS Session ID случайными байтами (аналог --dpi-desync-fake-tls-mod=rnd).
    /// </summary>
    private static void ApplyRnd(byte[] fakePacket, int payloadOffset)
    {
        // TLS record header: ContentType(1) + Version(2) + Length(2) = 5
        // Handshake: Type(1) + Length(3) + Version(2) + Random(32) = 38
        // Session ID Length: offset 43 от начала payload
        int sessionIdLenOffset = payloadOffset + 43;

        if (sessionIdLenOffset >= fakePacket.Length)
            return;

        byte sessionIdLen = fakePacket[sessionIdLenOffset];
        if (sessionIdLen == 0)
            return; // Нет Session ID

        int sessionIdStart = sessionIdLenOffset + 1;
        if (sessionIdStart + sessionIdLen > fakePacket.Length)
            return;

        // Заполняем случайными байтами
        Random.Shared.NextBytes(fakePacket.AsSpan(sessionIdStart, sessionIdLen));
    }

    // ── dupsid: скопировать Session ID из оригинального ClientHello ─

    /// <summary>
    /// Скопировать Session ID из оригинального TLS ClientHello в fake (аналог dupsid).
    /// </summary>
    private static void ApplyDupSid(byte[] fakePacket, int payloadOffset, string? originalSni)
    {
        // dupsid копирует Session ID из реального ClientHello в fake
        // Нам нужен оригинальный пакет для этого, но мы работаем только с fake.
        // На практике dupsid применяется когда fake payload создан на основе
        // оригинального пакета — здесь мы модифицируем session ID в fake,
        // копируя его из данных, извлечённых при парсинге оригинала.
        // Для упрощения: если нет оригинального SNI, пропускаем
        // (dupsid нужен для корреляции сессий DPI)

        // Смещение Session ID в fake payload
        int sessionIdLenOffset = payloadOffset + 43;
        if (sessionIdLenOffset >= fakePacket.Length)
            return;

        // dupsid = дублировать session ID из оригинального пакета
        // В текущей архитектуре оригинальный session ID передаётся через originalSni
        // Но dupsid работает с session ID, не SNI. Нужен отдельный метод для извлечения.
        // Оставляем заглушку — полная реализация в PacketInterceptor,
        // где есть доступ к оригинальному payload.
    }

    // ── sni=domain: заменить SNI в fake TLS ClientHello ──────

    /// <summary>
    /// Заменить SNI в fake TLS ClientHello на указанный домен (аналог sni=www.google.com).
    /// Если fake payload не TLS ClientHello или SNI не найден, ничего не делает.
    /// </summary>
    private static void ApplySniOverride(byte[] fakePacket, int payloadOffset, string domain)
    {
        var payload = fakePacket.AsSpan(payloadOffset);
        if (payload.Length < 5)
            return;

        // Проверяем что это TLS Handshake
        if (payload[0] != 0x16)
            return;

        // Находим позицию SNI в fake payload
        int sniPos = FindSniPosition(payload);
        if (sniPos < 0)
            return;

        // SNI structure: Name Type (1 byte = 0x00) + Name Length (2 bytes) + Name
        int nameTypeOffset = payloadOffset + sniPos;
        int nameLenOffset = nameTypeOffset + 1;
        int nameDataOffset = nameLenOffset + 2;

        if (nameDataOffset + 2 > fakePacket.Length)
            return;

        int oldNameLen = (fakePacket[nameLenOffset] << 8) | fakePacket[nameLenOffset + 1];

        // Новое SNI должно быть такой же длины или короче
        // Если длиннее — обрезаем domain; если короче — дополняем нулями
        byte[] domainBytes = System.Text.Encoding.ASCII.GetBytes(domain);

        if (domainBytes.Length <= oldNameLen)
        {
            // Копируем новый SNI, остальное заполняем нулями
            Array.Clear(fakePacket, nameDataOffset, oldNameLen);
            Array.Copy(domainBytes, 0, fakePacket, nameDataOffset, domainBytes.Length);
        }
        else
        {
            // Обрезаем domain до длины старого SNI
            Array.Copy(domainBytes, 0, fakePacket, nameDataOffset, oldNameLen);
        }

        // Обновляем длину SNI в поле
        fakePacket[nameLenOffset] = (byte)(Math.Min(domainBytes.Length, oldNameLen) >> 8);
        fakePacket[nameLenOffset + 1] = (byte)(Math.Min(domainBytes.Length, oldNameLen) & 0xFF);
    }

    // ── Вспомогательные методы ───────────────────────────────

    /// <summary>
    /// Определить смещение payload (TCP/UDP data) в пакете.
    /// </summary>
    private static int GetPayloadOffset(byte[] packet, WinDivertAddress addr)
    {
        unsafe
        {
            fixed (byte* p = packet)
            {
                if (addr.IPv6)
                {
                    if (packet.Length < 40)
                        return -1;

                    var ip6Hdr = (WinDivertIpv6Hdr*)p;
                    int ipHdrLen = 40;

                    if (ip6Hdr->NextHdr == 6) // TCP
                    {
                        if (packet.Length < ipHdrLen + 20)
                            return -1;
                        var tcpHdr = (WinDivertTcpHdr*)(p + ipHdrLen);
                        return ipHdrLen + tcpHdr->HdrLength;
                    }
                    else if (ip6Hdr->NextHdr == 17) // UDP
                    {
                        return ipHdrLen + 8;
                    }
                }
                else
                {
                    if (packet.Length < 20)
                        return -1;

                    var ipHdr = (WinDivertIpHdr*)p;
                    int ipHdrLen = ipHdr->HdrLength;

                    if (ipHdr->Protocol == 6) // TCP
                    {
                        if (packet.Length < ipHdrLen + 20)
                            return -1;
                        var tcpHdr = (WinDivertTcpHdr*)(p + ipHdrLen);
                        return ipHdrLen + tcpHdr->HdrLength;
                    }
                    else if (ipHdr->Protocol == 17) // UDP
                    {
                        return ipHdrLen + 8;
                    }
                }
            }
        }

        return -1;
    }

    /// <summary>
    /// Найти позицию SNI Name Data в TLS ClientHello payload.
    /// Возвращает смещение Name Type байта относительно начала payload.
    /// </summary>
    private static int FindSniPosition(ReadOnlySpan<byte> payload)
    {
        if (payload.Length < 6 || payload[0] != 0x16)
            return -1;

        int recordLen = (payload[3] << 8) | payload[4];
        if (payload.Length < 5 + recordLen)
            return -1;

        var handshake = payload[5..];
        if (handshake.Length < 4 || handshake[0] != 0x01) // ClientHello
            return -1;

        int hsLen = (handshake[1] << 16) | (handshake[2] << 8) | handshake[3];
        var hello = handshake[4..];
        int pos = 0;

        // Version (2) + Random (32)
        pos += 34;
        if (pos >= hello.Length) return -1;

        // Session ID
        int sessionLen = hello[pos++];
        pos += sessionLen;
        if (pos + 2 >= hello.Length) return -1;

        // Cipher Suites
        int cipherLen = (hello[pos] << 8) | hello[pos + 1];
        pos += 2 + cipherLen;
        if (pos >= hello.Length) return -1;

        // Compression Methods
        int compLen = hello[pos++];
        pos += compLen;
        if (pos + 2 >= hello.Length) return -1;

        // Extensions
        int extTotal = (hello[pos] << 8) | hello[pos + 1];
        pos += 2;
        int extEnd = pos + extTotal;

        while (pos + 4 <= extEnd && pos + 4 <= hello.Length)
        {
            int extType = (hello[pos] << 8) | hello[pos + 1];
            int extLen = (hello[pos + 2] << 8) | hello[pos + 3];
            pos += 4;

            if (extType == 0x0000 && extLen > 5) // SNI extension
            {
                // Server Name List Length (2 bytes) + Name Type (1 byte)
                // Возвращаем позицию Name Type относительно hello[0]
                // Но нужно вернуть относительно payload[0]
                // offset = 5 (TLS record) + 4 (handshake header) + pos + 2 (Server Name List Length)
                return 5 + 4 + pos + 2; // Относительно payload начала
            }

            pos += extLen;
        }

        return -1;
    }
}
