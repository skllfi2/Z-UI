// ═══════════════════════════════════════════════════════════════
// ZUI.Core / Intercept / PidMapper.cs
// Маппинг: локальный порт → PID → имя процесса
// Через IP Helper API (iphlpapi.dll): GetExtendedTcpTable/UdpTable
// Thread-safe: ConcurrentDictionary + Lock для кэша
// ═══════════════════════════════════════════════════════════════

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace ZUI.Core.Intercept;

/// <summary>
/// Маппинг локального порта → PID → имя процесса через IP Helper API.
/// Thread-safe. Кэш имён процессов на 5 секунд.
/// </summary>
public sealed class PidMapper
{
    // ── P/Invoke ──────────────────────────────────────────────

    private const string IphlpApi = "iphlpapi.dll";
    private const uint AfInet = 2;     // IPv4
    private const uint AfInet6 = 23;   // IPv6
    private const uint TcpTableOwnerPidAll = 5;
    private const uint UdpTableOwnerPid = 1;

    [StructLayout(LayoutKind.Sequential)]
    private struct MibTcpRowOwnerPid
    {
        public uint State;
        public uint LocalAddr;
        public uint LocalPort;   // network byte order
        public uint RemoteAddr;
        public uint RemotePort;  // network byte order
        public uint OwningPid;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MibUdpRowOwnerPid
    {
        public uint LocalAddr;
        public uint LocalPort; // network byte order
        public uint OwningPid;
    }

    // IPv6 address: 16 bytes
    [StructLayout(LayoutKind.Sequential)]
    private struct In6Addr
    {
        // 16 bytes stored as 4 uint fields (not an array — StructLayout Sequential)
        public uint Addr0;
        public uint Addr1;
        public uint Addr2;
        public uint Addr3;
    }

    // MIB_TCP6ROW_OWNER_PID: 56 bytes (with alignment)
    [StructLayout(LayoutKind.Sequential)]
    private struct MibTcp6RowOwnerPid
    {
        public uint State;
        public In6Addr LocalAddr;
        public uint LocalScopeId;
        public uint LocalPort;    // network byte order
        public In6Addr RemoteAddr;
        public uint RemoteScopeId;
        public uint RemotePort;   // network byte order
        public uint OwningPid;
    }

    // MIB_UDP6ROW_OWNER_PID: 28 bytes
    [StructLayout(LayoutKind.Sequential)]
    private struct MibUdp6RowOwnerPid
    {
        public In6Addr LocalAddr;
        public uint LocalScopeId;
        public uint LocalPort;    // network byte order
        public uint OwningPid;
    }

    [DllImport(IphlpApi, SetLastError = true)]
    private static extern uint GetExtendedTcpTable(
        IntPtr pTcpTable, ref uint dwOutBufLen,
        bool bOrder, uint ulAf, uint tableClass, uint reserved);

    [DllImport(IphlpApi, SetLastError = true)]
    private static extern uint GetExtendedUdpTable(
        IntPtr pUdpTable, ref uint dwOutBufLen,
        bool bOrder, uint ulAf, uint tableClass, uint reserved);

    // ── Кэш имени процесса ────────────────────────────────────

    private readonly ConcurrentDictionary<uint, string> _pidCache = new();
    private readonly Lock _cacheLock = new();
    private DateTime _cacheExpiry = DateTime.MinValue;
    private static readonly TimeSpan CacheDuration = TimeSpan.FromSeconds(5);

    // ── Public API ────────────────────────────────────────────

    /// <summary>
    /// Найти PID по локальному порту (TCP).
    /// Возвращает 0 если соединение не найдено.
    /// </summary>
    public uint GetPidForConnection(ushort localPort, bool isIPv6 = false, bool isTcp = true)
    {
        try
        {
            if (isTcp)
                return isIPv6 ? GetTcpV6Pid(localPort) : GetTcpV4Pid(localPort);
            else
                return isIPv6 ? GetUdpV6Pid(localPort) : GetUdpV4Pid(localPort);
        }
        catch (InvalidOperationException)
        {
            // Не критично — если не удалось получить PID, продолжаем работу
            return 0;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // Не критично — если не удалось получить PID, продолжаем работу
            return 0;
        }
    }

    /// <summary>
    /// Имя процесса по PID (с кэшированием на 5 сек).
    /// Возвращает "System" для PID=0, "[pid]" если процесс не найден.
    /// </summary>
    public string GetProcessName(uint pid)
    {
        if (pid == 0)
            return "System";

        // Проверяем срок действия кэша
        lock (_cacheLock)
        {
            if (DateTime.UtcNow > _cacheExpiry)
            {
                _pidCache.Clear();
                _cacheExpiry = DateTime.UtcNow.Add(CacheDuration);
            }
        }

        return _pidCache.GetOrAdd(pid, LookupProcessName);
    }

    /// <summary>
    /// Очистить кэш имён процессов.
    /// </summary>
    public void ClearCache()
    {
        lock (_cacheLock)
        {
            _pidCache.Clear();
            _cacheExpiry = DateTime.MinValue;
        }
    }

    // ── TCP IPv4 ──────────────────────────────────────────────

    private uint GetTcpV4Pid(ushort localPort)
    {
        uint bufLen = 0;
        GetExtendedTcpTable(IntPtr.Zero, ref bufLen, false, AfInet, TcpTableOwnerPidAll, 0);

        IntPtr buf = Marshal.AllocHGlobal((int)bufLen);
        try
        {
            if (GetExtendedTcpTable(buf, ref bufLen, false, AfInet, TcpTableOwnerPidAll, 0) != 0)
                return 0;

            int rowSize = Marshal.SizeOf<MibTcpRowOwnerPid>();
            uint numRows = (uint)Marshal.ReadInt32(buf);
            IntPtr rowPtr = buf + 4;

            for (uint i = 0; i < numRows; i++, rowPtr += rowSize)
            {
                var entry = Marshal.PtrToStructure<MibTcpRowOwnerPid>(rowPtr);
                if (NetworkToHostOrder(entry.LocalPort) == localPort)
                    return entry.OwningPid;
            }
        }
        finally
        {
            Marshal.FreeHGlobal(buf);
        }

        return 0;
    }

    // ── TCP IPv6 ──────────────────────────────────────────────

    private uint GetTcpV6Pid(ushort localPort)
    {
        uint bufLen = 0;
        GetExtendedTcpTable(IntPtr.Zero, ref bufLen, false, AfInet6, TcpTableOwnerPidAll, 0);

        IntPtr buf = Marshal.AllocHGlobal((int)bufLen);
        try
        {
            if (GetExtendedTcpTable(buf, ref bufLen, false, AfInet6, TcpTableOwnerPidAll, 0) != 0)
                return 0;

            int rowSize = Marshal.SizeOf<MibTcp6RowOwnerPid>();
            uint numRows = (uint)Marshal.ReadInt32(buf);
            IntPtr rowPtr = buf + 4;

            for (uint i = 0; i < numRows; i++, rowPtr += rowSize)
            {
                var entry = Marshal.PtrToStructure<MibTcp6RowOwnerPid>(rowPtr);
                if (NetworkToHostOrder(entry.LocalPort) == localPort)
                    return entry.OwningPid;
            }
        }
        finally
        {
            Marshal.FreeHGlobal(buf);
        }

        return 0;
    }

    // ── UDP IPv4 ──────────────────────────────────────────────

    private uint GetUdpV4Pid(ushort localPort)
    {
        uint bufLen = 0;
        GetExtendedUdpTable(IntPtr.Zero, ref bufLen, false, AfInet, UdpTableOwnerPid, 0);

        IntPtr buf = Marshal.AllocHGlobal((int)bufLen);
        try
        {
            if (GetExtendedUdpTable(buf, ref bufLen, false, AfInet, UdpTableOwnerPid, 0) != 0)
                return 0;

            int rowSize = Marshal.SizeOf<MibUdpRowOwnerPid>();
            uint numRows = (uint)Marshal.ReadInt32(buf);
            IntPtr rowPtr = buf + 4;

            for (uint i = 0; i < numRows; i++, rowPtr += rowSize)
            {
                var entry = Marshal.PtrToStructure<MibUdpRowOwnerPid>(rowPtr);
                if (NetworkToHostOrder(entry.LocalPort) == localPort)
                    return entry.OwningPid;
            }
        }
        finally
        {
            Marshal.FreeHGlobal(buf);
        }

        return 0;
    }

    // ── UDP IPv6 ──────────────────────────────────────────────

    private uint GetUdpV6Pid(ushort localPort)
    {
        uint bufLen = 0;
        GetExtendedUdpTable(IntPtr.Zero, ref bufLen, false, AfInet6, UdpTableOwnerPid, 0);

        IntPtr buf = Marshal.AllocHGlobal((int)bufLen);
        try
        {
            if (GetExtendedUdpTable(buf, ref bufLen, false, AfInet6, UdpTableOwnerPid, 0) != 0)
                return 0;

            int rowSize = Marshal.SizeOf<MibUdp6RowOwnerPid>();
            uint numRows = (uint)Marshal.ReadInt32(buf);
            IntPtr rowPtr = buf + 4;

            for (uint i = 0; i < numRows; i++, rowPtr += rowSize)
            {
                var entry = Marshal.PtrToStructure<MibUdp6RowOwnerPid>(rowPtr);
                if (NetworkToHostOrder(entry.LocalPort) == localPort)
                    return entry.OwningPid;
            }
        }
        finally
        {
            Marshal.FreeHGlobal(buf);
        }

        return 0;
    }

    // ── Helpers ───────────────────────────────────────────────

    private static ushort NetworkToHostOrder(uint netPort)
        => (ushort)(((netPort & 0xFF) << 8) | ((netPort >> 8) & 0xFF));

    private static string LookupProcessName(uint pid)
    {
        try
        {
            var proc = Process.GetProcessById((int)pid);
            return proc.ProcessName + ".exe";
        }
        catch (ArgumentException)
        {
            return $"[{pid}]";
        }
        catch (InvalidOperationException)
        {
            return $"[{pid}]";
        }
    }
}
