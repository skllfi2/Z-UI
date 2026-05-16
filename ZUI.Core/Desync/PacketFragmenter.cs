// ═══════════════════════════════════════════════════════════════
// ZUI.Core / Desync / PacketFragmenter.cs
// IP-фрагментация пакетов для обхода DPI
// Аналог: --dpi-desync-split (IP-level fragmentation)
// Фрагментация на IP уровне: разделяет payload на 2 IP фрагмента
// ═══════════════════════════════════════════════════════════════

using System.Net;
using System.Runtime.InteropServices;
using ZUI.Core.WinDivert;

namespace ZUI.Core.Desync;

/// <summary>
/// Результат IP-фрагментации.
/// </summary>
public sealed class FragmentResult
{
    /// <summary>Фрагменты в порядке отправки.</summary>
    public IpFragment[] Fragments { get; init; } = [];

    /// <summary>Заменить оригинальный пакет фрагментами.</summary>
    public bool ReplaceOriginal { get; init; } = true;
}

/// <summary>
/// Один IP-фрагмент.
/// </summary>
public sealed class IpFragment
{
    /// <summary>Raw bytes фрагмента (IP header + payload фрагмента).</summary>
    public byte[] Packet { get; init; } = [];

    /// <summary>WinDivert address для фрагмента.</summary>
    public WinDivertAddress Addr { get; init; }
}

/// <summary>
/// IP-фрагментация пакетов для обхода DPI.
/// Разделяет один IP-пакет на 2 фрагмента по указанному смещению.
/// Фрагмент 1: IP header (MF=1, offset=0) + первые N байт payload
/// Фрагмент 2: IP header (MF=0, offset=N) + остальные байты payload
///
/// Работает ТОЛЬКО с IPv4 (IPv6 использует фрагментацию через extension headers).
/// </summary>
public static class PacketFragmenter
{
    // IP More Fragments flag
    private const ushort IpMfFlag = 0x2000;
    // Fragment offset mask (in 8-byte units, in the high 13 bits of FragOff0)
    // WinDivert stores FragOff0 as-is from the wire (network byte order bits)

    /// <summary>
    /// Разделить IPv4-пакет на 2 фрагмента.
    /// </summary>
    /// <param name="originalPacket">Исходный raw packet bytes (IPv4).</param>
    /// <param name="originalAddr">WinDivert address исходного пакета.</param>
    /// <param name="fragmentOffset">Смещение в payload, по которому резать (в байтах, должно быть кратно 8).</param>
    /// <returns>Два фрагмента или Failed.</returns>
    public static Result<FragmentResult> Fragment(
        byte[] originalPacket,
        WinDivertAddress originalAddr,
        int fragmentOffset)
    {
        if (originalAddr.IPv6)
            return Result<FragmentResult>.Failed("IPv6 fragmentation not supported");

        if (fragmentOffset <= 0)
            return Result<FragmentResult>.Failed("Fragment offset must be positive");

        // Выравниваем offset по 8 байт (IP фрагментация требует)
        fragmentOffset = (fragmentOffset + 7) & ~7;

        int ipHdrLen;
        int payloadLen;

        unsafe
        {
            fixed (byte* pOriginal = originalPacket)
            {
                if (originalPacket.Length < 20)
                    return Result<FragmentResult>.Failed("Packet too short for IPv4");

                var ipHdr = (WinDivertIpHdr*)pOriginal;
                ipHdrLen = ipHdr->HdrLength;
                payloadLen = originalPacket.Length - ipHdrLen;
            }
        }

        if (fragmentOffset >= payloadLen)
            return Result<FragmentResult>.Failed("Fragment offset exceeds payload length");

        // Фрагмент 1: IP header + payload[0..fragmentOffset]
        int frag1Len = ipHdrLen + fragmentOffset;
        byte[] frag1 = new byte[frag1Len];

        // Фрагмент 2: IP header + payload[fragmentOffset..]
        int frag2PayloadLen = payloadLen - fragmentOffset;
        int frag2Len = ipHdrLen + frag2PayloadLen;
        byte[] frag2 = new byte[frag2Len];

        unsafe
        {
            fixed (byte* pOriginal = originalPacket)
            fixed (byte* pFrag1 = frag1)
            fixed (byte* pFrag2 = frag2)
            {
                // ── Фрагмент 1 ─────────────────────────────────
                // Копируем IP заголовок
                Buffer.MemoryCopy(pOriginal, pFrag1, ipHdrLen, ipHdrLen);
                // Копируем первую часть payload
                Buffer.MemoryCopy(pOriginal + ipHdrLen, pFrag1 + ipHdrLen,
                    fragmentOffset, fragmentOffset);

                // Настраиваем IP заголовок фрагмента 1
                var frag1IpHdr = (WinDivertIpHdr*)pFrag1;
                frag1IpHdr->Length = (ushort)IPAddress.HostToNetworkOrder((short)frag1Len);
                // MF=1 (More Fragments), offset=0
                frag1IpHdr->FragOff0 = IpMfFlag;

                // ── Фрагмент 2 ─────────────────────────────────
                // Копируем IP заголовок
                Buffer.MemoryCopy(pOriginal, pFrag2, ipHdrLen, ipHdrLen);
                // Копируем вторую часть payload
                Buffer.MemoryCopy(pOriginal + ipHdrLen + fragmentOffset,
                    pFrag2 + ipHdrLen, frag2PayloadLen, frag2PayloadLen);

                // Настраиваем IP заголовок фрагмента 2
                var frag2IpHdr = (WinDivertIpHdr*)pFrag2;
                frag2IpHdr->Length = (ushort)IPAddress.HostToNetworkOrder((short)frag2Len);
                // MF=0 (last fragment), offset=fragmentOffset/8
                ushort frag2Off = (ushort)((fragmentOffset / 8) & 0x1FFF);
                frag2IpHdr->FragOff0 = frag2Off;
            }
        }

        var addr1 = originalAddr;
        addr1.IPChecksum = false;

        var addr2 = originalAddr;
        addr2.IPChecksum = false;

        return Result<FragmentResult>.Success(new FragmentResult
        {
            Fragments =
            [
                new IpFragment { Packet = frag1, Addr = addr1 },
                new IpFragment { Packet = frag2, Addr = addr2 },
            ],
            ReplaceOriginal = true,
        });
    }

    /// <summary>
    /// Разделить IPv4-пакет на 2 фрагмента, разрезая по TCP payload offset.
    /// Удобная обёртка: указываете позицию внутри TCP payload,
    /// а метод сам вычисляет IP-уровневый offset.
    /// </summary>
    public static Result<FragmentResult> FragmentAtTcpPayloadOffset(
        byte[] originalPacket,
        WinDivertAddress originalAddr,
        int tcpPayloadOffset)
    {
        if (originalAddr.IPv6)
            return Result<FragmentResult>.Failed("IPv6 fragmentation not supported");

        unsafe
        {
            fixed (byte* pOriginal = originalPacket)
            {
                if (originalPacket.Length < 20)
                    return Result<FragmentResult>.Failed("Packet too short for IPv4");

                var ipHdr = (WinDivertIpHdr*)pOriginal;
                int ipHdrLen = ipHdr->HdrLength;

                if (ipHdr->Protocol != 6)
                    return Result<FragmentResult>.Failed("Not a TCP packet");

                if (originalPacket.Length < ipHdrLen + 20)
                    return Result<FragmentResult>.Failed("TCP header too short");

                var tcpHdr = (WinDivertTcpHdr*)(pOriginal + ipHdrLen);
                int tcpHdrLen = tcpHdr->HdrLength;

                // IP offset = IP header + TCP header + TCP payload offset
                int ipPayloadOffset = ipHdrLen + tcpHdrLen + tcpPayloadOffset;

                return Fragment(originalPacket, originalAddr, ipPayloadOffset);
            }
        }
    }

    /// <summary>
    /// Быстрая фрагментация для UDP: разрез на границе UDP header.
    /// Первый фрагмент: IP + UDP header (8 байт).
    /// Второй фрагмент: IP + UDP payload.
    /// </summary>
    public static Result<FragmentResult> FragmentUdp(
        byte[] originalPacket,
        WinDivertAddress originalAddr)
    {
        if (originalAddr.IPv6)
            return Result<FragmentResult>.Failed("IPv6 fragmentation not supported");

        unsafe
        {
            fixed (byte* pOriginal = originalPacket)
            {
                if (originalPacket.Length < 28) // 20 IP + 8 UDP
                    return Result<FragmentResult>.Failed("UDP packet too short");

                var ipHdr = (WinDivertIpHdr*)pOriginal;
                if (ipHdr->Protocol != 17)
                    return Result<FragmentResult>.Failed("Not a UDP packet");

                int ipHdrLen = ipHdr->HdrLength;
                // Разрез после UDP header (8 байт от начала транспортного уровня)
                return Fragment(originalPacket, originalAddr, ipHdrLen + 8);
            }
        }
    }
}
