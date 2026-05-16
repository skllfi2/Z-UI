// ═══════════════════════════════════════════════════════════════
// ZUI.Core / WinDivert / WinDivertInterceptor.cs
// Высокоуровневая async-обёртка над WinDivert P/Invoke
// IAsyncDisposable, IAsyncEnumerable, CancellationToken, Result
// ═══════════════════════════════════════════════════════════════

using System.Net;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace ZUI.Core.WinDivert;

/// <summary>
/// Основной API для перехвата пакетов через WinDivert.
/// Использование: Open → ReadPacketsAsync → SendPacket → DisposeAsync.
/// </summary>
public sealed class WinDivertInterceptor : IAsyncDisposable
{
    private SafeWinDivertHandle? _handle;
    private readonly ILogger _logger;
    private readonly Lock _lock = new();
    private int _isReading;

    public WinDivertInterceptor(ILogger<WinDivertInterceptor>? logger = null)
    {
        _logger = logger ?? NullLoggerFactory.Instance.CreateLogger<WinDivertInterceptor>();
    }

    /// <summary>Открыт ли WinDivert handle.</summary>
    public bool IsOpen
    {
        get
        {
            lock (_lock)
            {
                return _handle is { IsInvalid: false, IsClosed: false };
            }
        }
    }

    // ── Open ──────────────────────────────────────────────────

    /// <summary>
    /// Открыть WinDivert handle с фильтром. Требует прав администратора.
    /// </summary>
    public Result Open(string filter, WinDivertLayer layer = WinDivertLayer.Network,
        short priority = 0, ulong flags = 0)
    {
        lock (_lock)
        {
            if (_handle is { IsInvalid: false, IsClosed: false })
                return Result.Failed("WinDivert handle already open. Close it first.");

            var rawHandle = WinDivertNative.WinDivertOpen(filter, layer, priority, flags);

            if (rawHandle == WinDivertNative.InvalidHandleValue)
            {
                int error = Marshal.GetLastWin32Error();
                _logger.LogError("WinDivertOpen failed with error {Error}. Filter: {Filter}", error, filter);
                return Result.Failed($"WinDivertOpen failed (error {error}). Filter: {filter}");
            }

            _handle = new SafeWinDivertHandle(rawHandle);
            _logger.LogInformation("WinDivert handle opened. Filter: {Filter}", filter);
            return Result.Success();
        }
    }

    /// <summary>
    /// Проверить корректность фильтра без открытия handle.
    /// </summary>
    public static bool ValidateFilter(string filter, WinDivertLayer layer = WinDivertLayer.Network)
    {
        return WinDivertNative.WinDivertHelperCompileFilter(
            filter, layer, IntPtr.Zero, 0, out _, out _);
    }

    // ── Read ──────────────────────────────────────────────────

    /// <summary>
    /// Бесконечный async поток перехваченных пакетов.
    /// Выбрасывает InvalidOperationException если уже читается.
    /// </summary>
    public async IAsyncEnumerable<(ParsedPacket Packet, WinDivertAddress Address)> ReadPacketsAsync(
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        if (Interlocked.CompareExchange(ref _isReading, 1, 0) != 0)
            throw new InvalidOperationException("ReadPacketsAsync is already in progress. Only one reader is allowed.");

        var buffer = new byte[65535];

        try
        {
            while (!ct.IsCancellationRequested)
            {
                SafeWinDivertHandle? currentHandle;
                lock (_lock)
                {
                    currentHandle = _handle;
                }

                if (currentHandle is null or { IsInvalid: true } or { IsClosed: true })
                {
                    _logger.LogWarning("WinDivert handle is not open. Stopping read loop.");
                    yield break;
                }

                uint recvLen = 0;
                WinDivertAddress addr = default;

                bool ok;
                try
                {
                    ok = await Task.Run(() =>
                    {
                        var rawHandle = currentHandle.DangerousGetHandle();
                        return WinDivertNative.WinDivertRecv(rawHandle, buffer, (uint)buffer.Length, out recvLen, out addr);
                    }, ct).ConfigureAwait(false);
                }
        catch (OperationCanceledException)
        {
            yield break;
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            _logger.LogError(ex, "WinDivertRecv threw exception");
            continue;
        }
        catch (DllNotFoundException ex)
        {
            _logger.LogError(ex, "WinDivertRecv threw exception");
            continue;
        }

                if (!ok || recvLen == 0)
                {
                    int error = Marshal.GetLastWin32Error();
                    if (error != 0 && error != 995) // 995 = ERROR_OPERATION_ABORTED (cancelled)
                        _logger.LogWarning("WinDivertRecv failed with error {Error}", error);
                    continue;
                }

                var raw = buffer.AsSpan(0, (int)recvLen).ToArray();
                var parsed = ParsePacket(raw, addr);

                if (parsed is not null)
                    yield return (parsed, addr);
            }
        }
        finally
        {
            Interlocked.Exchange(ref _isReading, 0);
        }
    }

    // ── Send ──────────────────────────────────────────────────

    /// <summary>
    /// Отправить (переинжектировать) пакет через WinDivert.
    /// Автоматически пересчитывает контрольные суммы.
    /// </summary>
    public Result SendPacket(byte[] packet, ref WinDivertAddress addr)
    {
        if (packet is null || packet.Length == 0)
            return Result.Failed("Packet is empty.");

        SafeWinDivertHandle? currentHandle;
        lock (_lock)
        {
            currentHandle = _handle;
        }

        if (currentHandle is null or { IsInvalid: true } or { IsClosed: true })
            return Result.Failed("WinDivert handle is not open.");

        // Recalculate checksums
        unsafe
        {
            fixed (byte* pPacket = packet)
            fixed (WinDivertAddress* pAddr = &addr)
            {
                WinDivertNative.WinDivertHelperCalcChecksums(pPacket, (uint)packet.Length, pAddr, 0);
            }
        }

        var rawHandle = currentHandle.DangerousGetHandle();
        bool ok = WinDivertNative.WinDivertSend(rawHandle, packet, (uint)packet.Length, out _, ref addr);

        if (!ok)
        {
            int error = Marshal.GetLastWin32Error();
            _logger.LogWarning("WinDivertSend failed with error {Error}", error);
            return Result.Failed($"WinDivertSend failed (error {error})");
        }

        return Result.Success();
    }

    // ── Shutdown ──────────────────────────────────────────────

    /// <summary>
    /// Shutdown WinDivert handle (stops pending Recv/Send).
    /// </summary>
    public void Shutdown(WinDivertShutdown how = WinDivertShutdown.Both)
    {
        SafeWinDivertHandle? currentHandle;
        lock (_lock)
        {
            currentHandle = _handle;
        }

        if (currentHandle is null or { IsInvalid: true } or { IsClosed: true })
            return;

        try
        {
            var rawHandle = currentHandle.DangerousGetHandle();
            WinDivertNative.WinDivertShutdown(rawHandle, how);
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            _logger.LogWarning(ex, "WinDivertShutdown failed");
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "WinDivertShutdown failed");
        }
    }

    // ── Dispose ───────────────────────────────────────────────

    public async ValueTask DisposeAsync()
    {
        Shutdown(WinDivertShutdown.Both);

        lock (_lock)
        {
            if (_handle is not null)
            {
                _handle.Dispose();
                _handle = null;
            }
        }

        await Task.CompletedTask;
    }

    // ── Packet parsing ────────────────────────────────────────

    /// <summary>
    /// Разобрать сырой пакет в ParsedPacket с извлечением IP/портов/payload.
    /// </summary>
    internal static ParsedPacket? ParsePacket(byte[] raw, WinDivertAddress addr)
    {
        if (raw.Length < 20)
            return null;

        bool isV6 = (raw[0] >> 4) == 6;
        IPAddress srcIp, dstIp;
        byte proto;
        int headerLen;

        if (!isV6)
        {
            // IPv4
            headerLen = (raw[0] & 0x0F) * 4;
            if (headerLen < 20 || raw.Length < headerLen)
                return null;

            proto = raw[9];
            srcIp = new IPAddress(raw.AsSpan(12, 4));
            dstIp = new IPAddress(raw.AsSpan(16, 4));
        }
        else
        {
            // IPv6
            headerLen = 40;
            if (raw.Length < headerLen)
                return null;

            proto = raw[6];
            srcIp = new IPAddress(raw.AsSpan(8, 16));
            dstIp = new IPAddress(raw.AsSpan(24, 16));
        }

        if (raw.Length < headerLen + 4)
            return null;

        ushort srcPort = 0, dstPort = 0;
        int payloadOffset = headerLen;

        if (proto == 6) // TCP
        {
            srcPort = (ushort)((raw[headerLen] << 8) | raw[headerLen + 1]);
            dstPort = (ushort)((raw[headerLen + 2] << 8) | raw[headerLen + 3]);

            if (raw.Length > headerLen + 12)
            {
                int tcpHdrLen = ((raw[headerLen + 12] >> 4) & 0xF) * 4;
                payloadOffset = headerLen + tcpHdrLen;
            }
        }
        else if (proto == 17) // UDP
        {
            srcPort = (ushort)((raw[headerLen] << 8) | raw[headerLen + 1]);
            dstPort = (ushort)((raw[headerLen + 2] << 8) | raw[headerLen + 3]);
            payloadOffset = headerLen + 8;
        }

        return new ParsedPacket
        {
            IsIPv6 = isV6,
            SrcIp = srcIp,
            DstIp = dstIp,
            SrcPort = srcPort,
            DstPort = dstPort,
            Protocol = proto,
            Outbound = addr.Outbound,
            ProcessId = 0, // Заполняется PidMapper
            RawPacket = raw,
            PayloadOffset = payloadOffset,
        };
    }
}
