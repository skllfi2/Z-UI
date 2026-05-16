// ═══════════════════════════════════════════════════════════════
// ZUI.Core / Desync / TcpSplitter.cs
// Разделение TCP-сегмента на несколько частей (multisplit)
// Аналоги: --dpi-desync-split-pos, --dpi-desync-split-seqovl
// --dpi-desync-split-seqovl-pattern
// Также: disorder mode (отправить 2-й сегмент первым)
// ═══════════════════════════════════════════════════════════════

using System.Net;
using System.Runtime.InteropServices;
using ZUI.Core.Rules;
using ZUI.Core.WinDivert;

namespace ZUI.Core.Desync;

/// <summary>
/// Результат разделения TCP-сегмента.
/// Содержит 2+ сегментов с корректными SEQ/ACK номерами.
/// </summary>
public sealed class TcpSplitResult
{
    /// <summary>Сегменты в порядке отправки (для disorder — 2-й первый).</summary>
    public SplitSegment[] Segments { get; init; } = [];

    /// <summary>Оригинальный пакет нужно отбросить (true = отправить сегменты вместо него).</summary>
    public bool ReplaceOriginal { get; init; } = true;
}

/// <summary>
/// Один сегмент после разделения.
/// </summary>
public sealed class SplitSegment
{
    /// <summary>Raw bytes сегмента (IP + TCP + payload фрагмента).</summary>
    public byte[] Packet { get; init; } = [];

    /// <summary>WinDivert address для сегмента.</summary>
    public WinDivertAddress Addr { get; init; }

    /// <summary>Отправить до оригинального пакета (для disorder: true для 2-го сегмента).</summary>
    public bool SendBeforeOriginal { get; init; }
}

/// <summary>
/// Разделение TCP-сегмента для обхода DPI.
/// Multisplit: разрезать payload на несколько позициях.
/// SeqOvl: наложить seq overlap для обмана DPI.
/// Disorder: отправить 2-й сегмент перед 1-м.
/// </summary>
public static class TcpSplitter
{
    /// <summary>
    /// Разделить TCP-пакет по указанным позициям.
    /// Аналог: --dpi-desync=fake,multisplit --dpi-desync-split-pos=2,midsld
    /// </summary>
    /// <param name="originalPacket">Исходный raw packet bytes.</param>
    /// <param name="originalAddr">WinDivert address исходного пакета.</param>
    /// <param name="rule">Правило с параметрами split.</param>
    /// <returns>Результат разделения или Failed.</returns>
    public static Result<TcpSplitResult> Split(
        byte[] originalPacket,
        WinDivertAddress originalAddr,
        FilterRule rule)
    {
        if (rule.SplitPositions is null || rule.SplitPositions.Length == 0)
            return Result<TcpSplitResult>.Failed("No split positions defined");

        // Вычисляем абсолютные позиции разреза в payload
        var payloadPositions = CalculateSplitPositions(originalPacket, originalAddr, rule.SplitPositions);
        if (payloadPositions.Count == 0)
            return Result<TcpSplitResult>.Failed("Could not calculate split positions");

        return SplitAtPositions(originalPacket, originalAddr, payloadPositions, rule);
    }

    /// <summary>
    /// Разделить на 2 сегмента (простой split для fake,multisplit).
    /// </summary>
    public static Result<TcpSplitResult> SplitAt(
        byte[] originalPacket,
        WinDivertAddress originalAddr,
        int payloadOffset)
    {
        return SplitAtPositions(originalPacket, originalAddr, [payloadOffset],
            new FilterRule { DesyncModes = [DesyncMode.MultiSplit] });
    }

    // ── Внутренняя логика разделения ─────────────────────────

    private unsafe static Result<TcpSplitResult> SplitAtPositions(
        byte[] originalPacket,
        WinDivertAddress originalAddr,
        List<int> payloadPositions,
        FilterRule rule)
    {
        fixed (byte* pOriginal = originalPacket)
        {
            if (originalAddr.IPv6)
                return SplitIPv6(pOriginal, originalPacket.Length, originalAddr, payloadPositions, rule);
            else
                return SplitIPv4(pOriginal, originalPacket.Length, originalAddr, payloadPositions, rule);
        }
    }

    private unsafe static Result<TcpSplitResult> SplitIPv4(
        byte* pOriginal, int originalLen,
        WinDivertAddress originalAddr,
        List<int> payloadPositions,
        FilterRule rule)
    {
        if (originalLen < 20)
            return Result<TcpSplitResult>.Failed("Packet too short for IPv4");

        var ipHdr = (WinDivertIpHdr*)pOriginal;
        int ipHdrLen = ipHdr->HdrLength;

        if (ipHdr->Protocol != 6) // Только TCP
            return Result<TcpSplitResult>.Failed("Not a TCP packet");

        if (originalLen < ipHdrLen + 20)
            return Result<TcpSplitResult>.Failed("TCP header too short");

        var tcpHdr = (WinDivertTcpHdr*)(pOriginal + ipHdrLen);
        int tcpHdrLen = tcpHdr->HdrLength;
        int headerLen = ipHdrLen + tcpHdrLen;
        int payloadLen = originalLen - headerLen;

        if (payloadLen <= 0)
            return Result<TcpSplitResult>.Failed("No TCP payload to split");

        // Сортируем позиции разреза + добавляем начало и конец
        var cutPoints = new List<int> { 0 };
        foreach (int pos in payloadPositions)
        {
            int clampedPos = Math.Clamp(pos, 1, payloadLen - 1);
            if (clampedPos > 0 && clampedPos < payloadLen)
                cutPoints.Add(clampedPos);
        }
        cutPoints.Add(payloadLen);
        cutPoints.Sort();
        cutPoints = cutPoints.Distinct().ToList();

        // Если нет реальных точек разреза (только 0 и payloadLen), не делим
        if (cutPoints.Count <= 2)
            return Result<TcpSplitResult>.Failed("No valid split points after calculation");

        // Строим сегменты
        uint origSeq = (uint)IPAddress.NetworkToHostOrder((int)tcpHdr->SeqNum);
        int seqOvl = rule.SplitSeqOvl ?? 0;
        bool isDisorder = rule.DesyncModes.Contains(DesyncMode.MultiDisorder);

        var segments = new List<SplitSegment>();
        uint currentSeq = origSeq;

        for (int i = 0; i < cutPoints.Count - 1; i++)
        {
            int start = cutPoints[i];
            int end = cutPoints[i + 1];
            int segPayloadLen = end - start;

            if (segPayloadLen <= 0)
                continue;

            // Применяем seq overlap: каждый сегмент после первого
            // начинается на seqOvl байт раньше (перекрытие)
            uint segSeq = currentSeq;
            if (i > 0 && seqOvl > 0)
                segSeq = currentSeq - (uint)seqOvl;

            // Строим сегмент
            int segTotalLen = headerLen + segPayloadLen;
            byte[] segPacket = new byte[segTotalLen];

            fixed (byte* pSeg = segPacket)
            {
                // Копируем IP + TCP заголовки
                Buffer.MemoryCopy(pOriginal, pSeg, headerLen, headerLen);

                // Копируем payload фрагмент
                byte* pSrcPayload = pOriginal + headerLen + start;
                byte* pDstPayload = pSeg + headerLen;
                Buffer.MemoryCopy(pSrcPayload, pDstPayload, segPayloadLen, segPayloadLen);

                // Обновляем TCP SEQ
                var segTcpHdr = (WinDivertTcpHdr*)(pSeg + ipHdrLen);
                segTcpHdr->SeqNum = (uint)IPAddress.HostToNetworkOrder((int)segSeq);

                // Обновляем IP Total Length
                var segIpHdr = (WinDivertIpHdr*)pSeg;
                segIpHdr->Length = (ushort)IPAddress.HostToNetworkOrder((short)segTotalLen);

                // Если есть seqovl pattern — заполнить overlap область
                if (i > 0 && seqOvl > 0 && rule.SplitSeqOvlPattern is not null)
                {
                    // seqovl pattern = заменить первые seqOvl байт payload паттерном
                    // Паттерн загружается извне, в этой версии — заполняем нулями
                    // (полная реализация с загрузкой .bin в PacketInterceptor)
                    if (seqOvl <= segPayloadLen)
                    {
                        Array.Clear(segPacket, headerLen, seqOvl);
                    }
                }
            }

            // WinDivert address: пересчитать чексуммы
            var segAddr = originalAddr;
            segAddr.IPChecksum = false;
            segAddr.TCPChecksum = false;

            bool sendFirst = isDisorder && i == 1; // Disorder: 2-й сегмент первым

            segments.Add(new SplitSegment
            {
                Packet = segPacket,
                Addr = segAddr,
                SendBeforeOriginal = sendFirst,
            });

            currentSeq = origSeq + (uint)end;
        }

        // Disorder: переставить сегменты — 2-й перед 1-м
        if (isDisorder && segments.Count >= 2)
        {
            // Помечаем 2-й сегмент как "отправить первым"
            // PacketInterceptor разберётся с порядком
        }

        return Result<TcpSplitResult>.Success(new TcpSplitResult
        {
            Segments = segments.ToArray(),
            ReplaceOriginal = true,
        });
    }

    private unsafe static Result<TcpSplitResult> SplitIPv6(
        byte* pOriginal, int originalLen,
        WinDivertAddress originalAddr,
        List<int> payloadPositions,
        FilterRule rule)
    {
        if (originalLen < 40)
            return Result<TcpSplitResult>.Failed("Packet too short for IPv6");

        var ip6Hdr = (WinDivertIpv6Hdr*)pOriginal;
        int ipHdrLen = 40;

        if (ip6Hdr->NextHdr != 6)
            return Result<TcpSplitResult>.Failed("Not a TCP packet");

        if (originalLen < ipHdrLen + 20)
            return Result<TcpSplitResult>.Failed("TCP header too short");

        var tcpHdr = (WinDivertTcpHdr*)(pOriginal + ipHdrLen);
        int tcpHdrLen = tcpHdr->HdrLength;
        int headerLen = ipHdrLen + tcpHdrLen;
        int payloadLen = originalLen - headerLen;

        if (payloadLen <= 0)
            return Result<TcpSplitResult>.Failed("No TCP payload to split");

        var cutPoints = new List<int> { 0 };
        foreach (int pos in payloadPositions)
        {
            int clampedPos = Math.Clamp(pos, 1, payloadLen - 1);
            if (clampedPos > 0 && clampedPos < payloadLen)
                cutPoints.Add(clampedPos);
        }
        cutPoints.Add(payloadLen);
        cutPoints.Sort();
        cutPoints = cutPoints.Distinct().ToList();

        if (cutPoints.Count <= 2)
            return Result<TcpSplitResult>.Failed("No valid split points");

        uint origSeq = (uint)IPAddress.NetworkToHostOrder((int)tcpHdr->SeqNum);
        int seqOvl = rule.SplitSeqOvl ?? 0;
        bool isDisorder = rule.DesyncModes.Contains(DesyncMode.MultiDisorder);

        var segments = new List<SplitSegment>();
        uint currentSeq = origSeq;

        for (int i = 0; i < cutPoints.Count - 1; i++)
        {
            int start = cutPoints[i];
            int end = cutPoints[i + 1];
            int segPayloadLen = end - start;

            if (segPayloadLen <= 0) continue;

            uint segSeq = currentSeq;
            if (i > 0 && seqOvl > 0)
                segSeq = currentSeq - (uint)seqOvl;

            int segTotalLen = headerLen + segPayloadLen;
            byte[] segPacket = new byte[segTotalLen];

            fixed (byte* pSeg = segPacket)
            {
                Buffer.MemoryCopy(pOriginal, pSeg, headerLen, headerLen);

                byte* pSrcPayload = pOriginal + headerLen + start;
                byte* pDstPayload = pSeg + headerLen;
                Buffer.MemoryCopy(pSrcPayload, pDstPayload, segPayloadLen, segPayloadLen);

                var segTcpHdr = (WinDivertTcpHdr*)(pSeg + ipHdrLen);
                segTcpHdr->SeqNum = (uint)IPAddress.HostToNetworkOrder((int)segSeq);

                var segIp6Hdr = (WinDivertIpv6Hdr*)pSeg;
                segIp6Hdr->Length = (ushort)IPAddress.HostToNetworkOrder(
                    (short)(segTotalLen - ipHdrLen));
            }

            var segAddr = originalAddr;
            segAddr.IPChecksum = false;
            segAddr.TCPChecksum = false;

            segments.Add(new SplitSegment
            {
                Packet = segPacket,
                Addr = segAddr,
                SendBeforeOriginal = isDisorder && i == 1,
            });

            currentSeq = origSeq + (uint)end;
        }

        return Result<TcpSplitResult>.Success(new TcpSplitResult
        {
            Segments = segments.ToArray(),
            ReplaceOriginal = true,
        });
    }

    // ── Расчёт позиций разреза ───────────────────────────────

    /// <summary>
    /// Вычислить абсолютные позиции разреза в TCP payload.
    /// Поддержка: int (байт), "midsld", "midsld2", "sid", "sni", "host".
    /// </summary>
    private static List<int> CalculateSplitPositions(
        byte[] originalPacket,
        WinDivertAddress originalAddr,
        object[] splitPositions)
    {
        var result = new List<int>();

        // Определяем payload
        int payloadOffset = 0;
        int payloadLen = 0;

        unsafe
        {
            fixed (byte* p = originalPacket)
            {
                if (originalAddr.IPv6)
                {
                    if (originalPacket.Length < 40) return result;
                    var ip6 = (WinDivertIpv6Hdr*)p;
                    if (ip6->NextHdr != 6) return result;
                    if (originalPacket.Length < 60) return result;
                    var tcp = (WinDivertTcpHdr*)(p + 40);
                    payloadOffset = 40 + tcp->HdrLength;
                    payloadLen = originalPacket.Length - payloadOffset;
                }
                else
                {
                    if (originalPacket.Length < 20) return result;
                    var ip = (WinDivertIpHdr*)p;
                    if (ip->Protocol != 6) return result;
                    int ipHdrLen = ip->HdrLength;
                    if (originalPacket.Length < ipHdrLen + 20) return result;
                    var tcp = (WinDivertTcpHdr*)(p + ipHdrLen);
                    payloadOffset = ipHdrLen + tcp->HdrLength;
                    payloadLen = originalPacket.Length - payloadOffset;
                }
            }
        }

        if (payloadLen <= 0)
            return result;

        foreach (var pos in splitPositions)
        {
            if (pos is int absPos)
            {
                result.Add(absPos);
            }
            else if (pos is string namedPos)
            {
                int calculated = CalculateNamedPosition(
                    namedPos, originalPacket, payloadOffset, payloadLen);
                if (calculated > 0)
                    result.Add(calculated);
            }
        }

        return result;
    }

    /// <summary>
    /// Вычислить именованную позицию разреза.
    /// midsld = середина SLD (Second Level Domain) в SNI
    /// midsld2 = середина SLD, смещение 2
    /// sid = после Session ID в TLS ClientHello
    /// sni = начало SNI extension
    /// host = после Host: заголовка в HTTP
    /// </summary>
    private static int CalculateNamedPosition(
        string name, byte[] packet, int payloadOffset, int payloadLen)
    {
        var payload = packet.AsSpan(payloadOffset, payloadLen);

        switch (name.ToLowerInvariant())
        {
            case "midsld":
                return FindMidSldPosition(payload, offset: 0);

            case "midsld2":
                return FindMidSldPosition(payload, offset: 2);

            case "sid":
                return FindSessionIdEndPosition(payload);

            case "sni":
                return FindSniStartPosition(payload);

            case "host":
                return FindHostEndPosition(payload);

            default:
                // Попытка распарсить как число
                if (int.TryParse(name, out var num))
                    return num;
                return -1;
        }
    }

    /// <summary>
    /// Найти середину SLD (Second Level Domain) в SNI.
    /// Пример: www.google.com → SLD = google → mid = позиция после "goo"
    /// </summary>
    private static int FindMidSldPosition(ReadOnlySpan<byte> payload, int offset)
    {
        var sni = ZUI.Core.Intercept.SniParser.ExtractSni(payload);
        if (sni is null)
            return 2; // Default: разрез после 2 байт (как в winws)

        // Находим SLD: "www.google.com" → SLD = "google"
        var parts = sni.Split('.');
        string? sld = null;
        for (int i = 0; i < parts.Length; i++)
        {
            if (i == parts.Length - 1) break;
            // SLD = предпоследняя часть если есть subdomain
            if (i == parts.Length - 2)
            {
                sld = parts[i];
                break;
            }
        }

        if (sld is null && parts.Length > 0)
            sld = parts[0]; // Fallback

        if (sld is null)
            return 2;

        // Считаем позицию: TLS record header (5) + handshake type (1) + length (3) +
        // version (2) + random (32) + session id (1+len) + cipher suites (2+len) +
        // compression (1+len) + extensions length (2) = базовый заголовок
        // Ищем позицию SNI в raw payload
        int sniPos = FindSniBytePosition(payload);
        if (sniPos < 0)
            return 2;

        // SNI data offset: sni_pos + 5 (Name Type + Name Length)
        // SLD начинается после первой точки в имени
        // Для упрощения: разрез в середине SNI name
        int midPos = sniPos + 5 + sni.Length / 2 + offset;
        return Math.Max(1, midPos);
    }

    /// <summary>
    /// Найти позицию конца Session ID в TLS ClientHello.
    /// </summary>
    private static int FindSessionIdEndPosition(ReadOnlySpan<byte> payload)
    {
        // TLS record (5) + Handshake type (1) + Length (3) + Version (2) + Random (32) = 43
        // Session ID Length at offset 43
        if (payload.Length < 44)
            return 2;

        if (payload[0] != 0x16)
            return 2;

        byte sessionIdLen = payload[43];
        return 44 + sessionIdLen; // После Session ID
    }

    /// <summary>
    /// Найти начало SNI extension в raw payload.
    /// </summary>
    private static int FindSniBytePosition(ReadOnlySpan<byte> payload)
    {
        if (payload.Length < 6 || payload[0] != 0x16)
            return -1;

        int recordLen = (payload[3] << 8) | payload[4];
        if (payload.Length < 5 + recordLen)
            return -1;

        var handshake = payload[5..];
        if (handshake.Length < 4 || handshake[0] != 0x01)
            return -1;

        var hello = handshake[4..];
        int pos = 0;

        pos += 34; // Version + Random
        if (pos >= hello.Length) return -1;

        int sessionLen = hello[pos++];
        pos += sessionLen;
        if (pos + 2 >= hello.Length) return -1;

        int cipherLen = (hello[pos] << 8) | hello[pos + 1];
        pos += 2 + cipherLen;
        if (pos >= hello.Length) return -1;

        int compLen = hello[pos++];
        pos += compLen;
        if (pos + 2 >= hello.Length) return -1;

        int extTotal = (hello[pos] << 8) | hello[pos + 1];
        pos += 2;
        int extEnd = pos + extTotal;

        while (pos + 4 <= extEnd && pos + 4 <= hello.Length)
        {
            int extType = (hello[pos] << 8) | hello[pos + 1];
            int extLen = (hello[pos + 2] << 8) | hello[pos + 3];
            pos += 4;

            if (extType == 0x0000) // SNI
            {
                // Возвращаем позицию относительно payload[0]
                return 5 + 4 + pos; // 5 (TLS record) + 4 (handshake header) + pos
            }

            pos += extLen;
        }

        return -1;
    }

    private static int FindSniStartPosition(ReadOnlySpan<byte> payload)
    {
        int pos = FindSniBytePosition(payload);
        return pos >= 0 ? pos + 2 : 2; // +2 = Server Name List Length
    }

    private static int FindHostEndPosition(ReadOnlySpan<byte> payload)
    {
        // Для HTTP: разрез после "Host: domain\r\n"
        var host = ZUI.Core.Intercept.SniParser.ExtractHostFromHttp(payload);
        if (host is null)
            return 2;

        // Ищем \r\n после Host header
        for (int i = 0; i < payload.Length - 1; i++)
        {
            if (payload[i] == '\r' && payload[i + 1] == '\n')
            {
                // Проверяем что это конец строки с Host
                if (i > 4) // Минимум "Host:" длины
                {
                    // Ищем следующий \r\n — конец заголовка Host
                    for (int j = i; j < payload.Length - 1; j++)
                    {
                        if (payload[j] == '\r' && payload[j + 1] == '\n')
                            return j + 2;
                    }
                }
            }
        }

        return 2;
    }
}
