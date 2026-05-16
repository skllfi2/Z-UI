// ═══════════════════════════════════════════════════════════════
// ZUI.Core / WinDivert / WinDivertNative.cs
// P/Invoke декларации для WinDivert 2.x
// DLL: WinDivert.dll, Calling convention: Cdecl
// ═══════════════════════════════════════════════════════════════

using System.Runtime.InteropServices;

namespace ZUI.Core.WinDivert;

internal static class WinDivertNative
{
    private const string DllName = "WinDivert.dll";

    internal static readonly IntPtr InvalidHandleValue = new(-1);

    // ── Handle management ─────────────────────────────────────

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, SetLastError = true)]
    internal static extern IntPtr WinDivertOpen(
        [MarshalAs(UnmanagedType.LPStr)] string filter,
        WinDivertLayer layer,
        short priority,
        ulong flags);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, SetLastError = true)]
    internal static extern bool WinDivertClose(IntPtr handle);

    // ── Packet I/O ────────────────────────────────────────────

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, SetLastError = true)]
    internal static extern bool WinDivertRecv(
        IntPtr handle,
        byte[] packet,
        uint packetLen,
        out uint recvLen,
        out WinDivertAddress addr);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, SetLastError = true)]
    internal static extern bool WinDivertSend(
        IntPtr handle,
        byte[] packet,
        uint packetLen,
        out uint sendLen,
        ref WinDivertAddress addr);

    // ── Async I/O (OVERLAPPED) ────────────────────────────────

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, SetLastError = true)]
    internal static extern bool WinDivertRecvEx(
        IntPtr handle,
        byte[] packet,
        uint packetLen,
        out uint recvLen,
        ulong flags,
        out WinDivertAddress addr,
        IntPtr addrLen,
        IntPtr overlapped);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, SetLastError = true)]
    internal static extern bool WinDivertSendEx(
        IntPtr handle,
        byte[] packet,
        uint packetLen,
        out uint sendLen,
        ulong flags,
        ref WinDivertAddress addr,
        IntPtr addrLen,
        IntPtr overlapped);

    // ── Shutdown & params ─────────────────────────────────────

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, SetLastError = true)]
    internal static extern bool WinDivertShutdown(
        IntPtr handle,
        WinDivertShutdown how);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, SetLastError = true)]
    internal static extern bool WinDivertSetParam(
        IntPtr handle,
        WinDivertParam param,
        ulong value);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, SetLastError = true)]
    internal static extern bool WinDivertGetParam(
        IntPtr handle,
        WinDivertParam param,
        out ulong value);

    // ── Helper functions ──────────────────────────────────────

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, SetLastError = true)]
    internal static unsafe extern bool WinDivertHelperParsePacket(
        byte* packet,
        uint packetLen,
        WinDivertIpHdr** ppIpHdr,
        WinDivertIpv6Hdr** ppIpv6Hdr,
        byte* pProtocol,
        WinDivertIcmpHdr** ppIcmpHdr,
        WinDivertIcmpv6Hdr** ppIcmpv6Hdr,
        WinDivertTcpHdr** ppTcpHdr,
        WinDivertUdpHdr** ppUdpHdr,
        byte** ppData,
        uint* pDataLen,
        byte** ppNext,
        uint* pNextLen);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    internal static unsafe extern bool WinDivertHelperCalcChecksums(
        byte* packet,
        uint packetLen,
        WinDivertAddress* addr,
        ulong flags);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, SetLastError = true)]
    internal static extern bool WinDivertHelperCompileFilter(
        [MarshalAs(UnmanagedType.LPStr)] string filter,
        WinDivertLayer layer,
        IntPtr obj,
        uint objLen,
        out IntPtr errStr,
        out uint errPos);
}
