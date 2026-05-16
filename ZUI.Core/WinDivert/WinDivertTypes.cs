// ═══════════════════════════════════════════════════════════════
// ZUI.Core / WinDivert / WinDivertTypes.cs
// Структуры и перечисления WinDivert 2.x для P/Invoke
// Layout: строго по windivert.h (commit 97101072)
// WINDIVERT_ADDRESS = 80 bytes (driver validates this)
// All structs are blittable (no managed types) for unsafe interop
// ═══════════════════════════════════════════════════════════════

using System.Net;
using System.Runtime.InteropServices;

namespace ZUI.Core.WinDivert;

// ── Перечисления ──────────────────────────────────────────────

public enum WinDivertLayer : int
{
    Network = 0,
    NetworkForward = 1,
    Flow = 2,
    Socket = 3,
    Reflect = 4,
}

public enum WinDivertEvent : int
{
    NetworkPacket = 0,
    FlowEstablished = 1,
    FlowDeleted = 2,
    SocketBind = 3,
    SocketConnect = 4,
    SocketListen = 5,
    SocketAccept = 6,
    SocketClose = 7,
    ReflectOpen = 8,
    ReflectClose = 9,
}

[Flags]
public enum WinDivertShutdown : uint
{
    Recv = 0x1,
    Send = 0x2,
    Both = 0x3,
}

public enum WinDivertParam : int
{
    QueueLength = 0,
    QueueTime = 1,
    QueueSize = 2,
    VersionMajor = 3,
    VersionMinor = 4,
}

[Flags]
public enum WinDivertFlags : ulong
{
    None = 0,
    Sniff = 1,
    Drop = 2,
    RecvOnly = 4,
    SendOnly = 8,
    NoInstall = 16,
    Fragments = 32,
}

// ── WINDIVERT_ADDRESS (80 bytes) ──────────────────────────────
// Offset 0-7:   INT64 Timestamp
// Offset 8-11:  UINT32 bitfield (Layer|Event|Flags|Reserved1)
// Offset 12-15: UINT32 Reserved2
// Offset 16-79: 64-byte union (Network/Flow/Socket/Reflect)
//
// Blittable struct using fixed byte buffer for unsafe interop.

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public unsafe struct WinDivertAddress
{
    // Offset 0-7
    public long Timestamp;

    // Offset 8-11: bitfield
    // Bits 0-7: Layer, Bits 8-15: Event, Bit 16: Sniffed, Bit 17: Outbound,
    // Bit 18: Loopback, Bit 19: Impostor, Bit 20: IPv6,
    // Bit 21: IPChecksum, Bit 22: TCPChecksum, Bit 23: UDPChecksum
    // Bits 24-31: Reserved1
    private uint _layerEventFlags;

    // Offset 12-15
    private uint _reserved2;

    // Offset 16-79: 64-byte union data
    public fixed byte Data[64];

    // ── Bitfield properties ────────────────────────────────────

    public WinDivertLayer Layer
    {
        readonly get => (WinDivertLayer)(_layerEventFlags & 0xFF);
        set => _layerEventFlags = (_layerEventFlags & ~0xFFu) | ((byte)value & 0xFFu);
    }

    public WinDivertEvent Event
    {
        readonly get => (WinDivertEvent)((_layerEventFlags >> 8) & 0xFF);
        set => _layerEventFlags = (_layerEventFlags & ~(0xFFu << 8)) | (((byte)value & 0xFFu) << 8);
    }

    public bool Sniffed
    {
        readonly get => (_layerEventFlags & (1u << 16)) != 0;
        set => _layerEventFlags = value ? _layerEventFlags | (1u << 16) : _layerEventFlags & ~(1u << 16);
    }

    public bool Outbound
    {
        readonly get => (_layerEventFlags & (1u << 17)) != 0;
        set => _layerEventFlags = value ? _layerEventFlags | (1u << 17) : _layerEventFlags & ~(1u << 17);
    }

    public bool Loopback
    {
        readonly get => (_layerEventFlags & (1u << 18)) != 0;
        set => _layerEventFlags = value ? _layerEventFlags | (1u << 18) : _layerEventFlags & ~(1u << 18);
    }

    public bool Impostor
    {
        readonly get => (_layerEventFlags & (1u << 19)) != 0;
        set => _layerEventFlags = value ? _layerEventFlags | (1u << 19) : _layerEventFlags & ~(1u << 19);
    }

    public bool IPv6
    {
        readonly get => (_layerEventFlags & (1u << 20)) != 0;
        set => _layerEventFlags = value ? _layerEventFlags | (1u << 20) : _layerEventFlags & ~(1u << 20);
    }

    public bool IPChecksum
    {
        readonly get => (_layerEventFlags & (1u << 21)) != 0;
        set => _layerEventFlags = value ? _layerEventFlags | (1u << 21) : _layerEventFlags & ~(1u << 21);
    }

    public bool TCPChecksum
    {
        readonly get => (_layerEventFlags & (1u << 22)) != 0;
        set => _layerEventFlags = value ? _layerEventFlags | (1u << 22) : _layerEventFlags & ~(1u << 22);
    }

    public bool UDPChecksum
    {
        readonly get => (_layerEventFlags & (1u << 23)) != 0;
        set => _layerEventFlags = value ? _layerEventFlags | (1u << 23) : _layerEventFlags & ~(1u << 23);
    }

    // ── Union accessors ────────────────────────────────────────

    public readonly WinDivertDataNetwork GetNetwork()
    {
        fixed (byte* p = Data)
            return *(WinDivertDataNetwork*)p;
    }

    public readonly WinDivertDataFlow GetFlow()
    {
        fixed (byte* p = Data)
            return *(WinDivertDataFlow*)p;
    }

    public readonly WinDivertDataSocket GetSocket()
    {
        fixed (byte* p = Data)
            return *(WinDivertDataSocket*)p;
    }
}

// ── Union data structs ────────────────────────────────────────

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct WinDivertDataNetwork
{
    public uint IfIdx;
    public uint SubIfIdx;
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct WinDivertDataFlow
{
    public ulong EndpointId;
    public ulong ParentEndpointId;
    public uint ProcessId;
    public uint LocalAddr0;
    public uint LocalAddr1;
    public uint LocalAddr2;
    public uint LocalAddr3;
    public uint RemoteAddr0;
    public uint RemoteAddr1;
    public uint RemoteAddr2;
    public uint RemoteAddr3;
    public ushort LocalPort;
    public ushort RemotePort;
    public byte Protocol;
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct WinDivertDataSocket
{
    public ulong EndpointId;
    public ulong ParentEndpointId;
    public uint ProcessId;
    public uint LocalAddr0;
    public uint LocalAddr1;
    public uint LocalAddr2;
    public uint LocalAddr3;
    public uint RemoteAddr0;
    public uint RemoteAddr1;
    public uint RemoteAddr2;
    public uint RemoteAddr3;
    public ushort LocalPort;
    public ushort RemotePort;
    public byte Protocol;
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct WinDivertDataReflect
{
    public long Timestamp;
    public uint ProcessId;
    public WinDivertLayer Layer;
    public ulong Flags;
    public short Priority;
}

// ── IP/TCP/UDP/ICMP заголовки ─────────────────────────────────

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct WinDivertIpHdr
{
    public byte VersionHdrLength;
    public byte TOS;
    public ushort Length;
    public ushort Id;
    public ushort FragOff0;
    public byte TTL;
    public byte Protocol;
    public ushort Checksum;
    public uint SrcAddr;
    public uint DstAddr;

    public readonly int HdrLength => (VersionHdrLength & 0x0F) * 4;
    public readonly int Version => (VersionHdrLength >> 4) & 0x0F;
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct WinDivertIpv6Hdr
{
    public byte VersionTrafficClass0;
    public byte TrafficClass1FlowLabel0;
    public ushort FlowLabel1;
    public ushort Length;
    public byte NextHdr;
    public byte HopLimit;
    public uint SrcAddr0;
    public uint SrcAddr1;
    public uint SrcAddr2;
    public uint SrcAddr3;
    public uint DstAddr0;
    public uint DstAddr1;
    public uint DstAddr2;
    public uint DstAddr3;

    public readonly int Version => (VersionTrafficClass0 >> 4) & 0x0F;
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct WinDivertTcpHdr
{
    public ushort SrcPort;
    public ushort DstPort;
    public uint SeqNum;
    public uint AckNum;
    public ushort DataOffsetFlags;
    public ushort Window;
    public ushort Checksum;
    public ushort UrgPtr;

    public readonly int HdrLength => ((DataOffsetFlags >> 12) & 0xF) * 4;
    public readonly bool Fin => (DataOffsetFlags & 0x0001) != 0;
    public readonly bool Syn => (DataOffsetFlags & 0x0002) != 0;
    public readonly bool Rst => (DataOffsetFlags & 0x0004) != 0;
    public readonly bool Psh => (DataOffsetFlags & 0x0008) != 0;
    public readonly bool Ack => (DataOffsetFlags & 0x0010) != 0;
    public readonly bool Urg => (DataOffsetFlags & 0x0020) != 0;
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct WinDivertUdpHdr
{
    public ushort SrcPort;
    public ushort DstPort;
    public ushort Length;
    public ushort Checksum;
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct WinDivertIcmpHdr
{
    public byte Type;
    public byte Code;
    public ushort Checksum;
    public uint Body;
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct WinDivertIcmpv6Hdr
{
    public byte Type;
    public byte Code;
    public ushort Checksum;
    public uint Body;
}

// ── ParsedPacket ──────────────────────────────────────────────

public sealed class ParsedPacket
{
    public bool IsIPv6 { get; init; }
    public IPAddress SrcIp { get; init; } = IPAddress.None;
    public IPAddress DstIp { get; init; } = IPAddress.None;
    public ushort SrcPort { get; init; }
    public ushort DstPort { get; init; }
    public byte Protocol { get; init; } // 6=TCP, 17=UDP
    public bool IsTcp => Protocol == 6;
    public bool IsUdp => Protocol == 17;
    public bool Outbound { get; init; }
    public uint ProcessId { get; init; }
    public byte[] RawPacket { get; init; } = [];
    public int PayloadOffset { get; init; }

    /// <summary>Payload bytes (TLS ClientHello, HTTP request, etc.)</summary>
    public ReadOnlySpan<byte> Payload => RawPacket.AsSpan(PayloadOffset);
}
