// ═══════════════════════════════════════════════════════════════
// ZUI.Core / Desync / FakePacketBuilder.cs
// Построение fake-пакетов для DPI десинхронизации
// Создаёт IP+TCP/UDP + payload из .bin файлов zapret
// Fake-пакет = копия исходного заголовка + подмена payload
// ═══════════════════════════════════════════════════════════════

using System.Net;
using System.Runtime.InteropServices;
using ZUI.Core.Intercept;
using ZUI.Core.Rules;
using ZUI.Core.WinDivert;

namespace ZUI.Core.Desync;

/// <summary>
/// Построение fake-пакетов для десинхронизации DPI.
/// Fake-пакет — это IP+TCP/UDP заголовок исходного пакета, но с подменённым payload.
/// Payload загружается из .bin файлов (tls_clienthello_www_google_com.bin и т.д.).
/// </summary>
public sealed class FakePacketBuilder
{
    private readonly string _zapretDir;
    private readonly Dictionary<string, byte[]> _cache = new(StringComparer.OrdinalIgnoreCase);

    public FakePacketBuilder(string zapretDir)
    {
        _zapretDir = zapretDir;
    }

    /// <summary>
    /// Построить fake-пакет на основе исходного пакета и правила.
    /// Выбирает подходящий .bin файл по протоколу (TLS/QUIC/HTTP/UDP).
    /// </summary>
    /// <param name="originalPacket">Исходный перехваченный пакет (raw bytes).</param>
    /// <param name="originalAddr">WinDivert address исходного пакета.</param>
    /// <param name="rule">Правило с указанием fake-файлов.</param>
    /// <param name="detectedL7">Обнаруженный L7 протокол.</param>
    /// <returns>Fake-пакет (raw bytes) и модифицированный WinDivert address, или Failed.</returns>
    public Result<(byte[] Packet, WinDivertAddress Addr)> BuildFakePacket(
        byte[] originalPacket,
        WinDivertAddress originalAddr,
        FilterRule rule,
        L7Protocol detectedL7)
    {
        // Выбираем fake payload файл
        string? fakeFile = ChooseFakeFile(rule, detectedL7);
        if (fakeFile is null)
            return Result<(byte[], WinDivertAddress)>.Failed("No fake file configured for this protocol");

        // Загружаем fake payload
        var payloadResult = LoadFakePayload(fakeFile);
        if (!payloadResult.IsSuccess)
            return Result<(byte[], WinDivertAddress)>.Failed(payloadResult.Error!);

        byte[] fakePayload = payloadResult.Value;

        return BuildFakePacketWithPayload(originalPacket, originalAddr, fakePayload);
    }

    /// <summary>
    /// Построить fake-пакет с явно заданным payload.
    /// </summary>
    public Result<(byte[] Packet, WinDivertAddress Addr)> BuildFakePacketWithPayload(
        byte[] originalPacket,
        WinDivertAddress originalAddr,
        byte[] fakePayload)
    {
        unsafe
        {
            fixed (byte* pOriginal = originalPacket)
            {
                // Парсим IP заголовок исходного пакета
                byte version = (byte)((pOriginal[0] >> 4) & 0x0F);

                if (version == 4)
                {
                    return BuildFakeIPv4(pOriginal, originalPacket.Length, originalAddr, fakePayload);
                }
                else if (version == 6)
                {
                    return BuildFakeIPv6(pOriginal, originalPacket.Length, originalAddr, fakePayload);
                }
                else
                {
                    return Result<(byte[], WinDivertAddress)>.Failed($"Unknown IP version: {version}");
                }
            }
        }
    }

    // ── IPv4 fake packet ─────────────────────────────────────

    private unsafe Result<(byte[], WinDivertAddress)> BuildFakeIPv4(
        byte* pOriginal, int originalLen, WinDivertAddress originalAddr, byte[] fakePayload)
    {
        if (originalLen < 20)
            return Result<(byte[], WinDivertAddress)>.Failed("IPv4 packet too short");

        var ipHdr = (WinDivertIpHdr*)pOriginal;
        int ipHdrLen = ipHdr->HdrLength;
        int totalHdrLen = ipHdrLen;

        if (ipHdr->Protocol == 6) // TCP
        {
            if (originalLen < ipHdrLen + 20)
                return Result<(byte[], WinDivertAddress)>.Failed("TCP header too short");

            var tcpHdr = (WinDivertTcpHdr*)(pOriginal + ipHdrLen);
            totalHdrLen += tcpHdr->HdrLength;
        }
        else if (ipHdr->Protocol == 17) // UDP
        {
            totalHdrLen += 8; // UDP header = 8 bytes
        }
        else
        {
            return Result<(byte[], WinDivertAddress)>.Failed($"Unsupported protocol: {ipHdr->Protocol}");
        }

        // Копируем заголовки из оригинального пакета
        int fakePacketLen = totalHdrLen + fakePayload.Length;
        byte[] fakePacket = new byte[fakePacketLen];

        fixed (byte* pFake = fakePacket)
        {
            // Копируем все заголовки
            Buffer.MemoryCopy(pOriginal, pFake, totalHdrLen, totalHdrLen);

            // Копируем fake payload
            Marshal.Copy(fakePayload, 0, (IntPtr)(pFake + totalHdrLen), fakePayload.Length);

            // Обновляем IP Total Length
            var fakeIpHdr = (WinDivertIpHdr*)pFake;
            fakeIpHdr->Length = (ushort)IPAddress.HostToNetworkOrder((short)fakePacketLen);

            // Обновляем UDP Length если UDP
            if (ipHdr->Protocol == 17)
            {
                var fakeUdpHdr = (WinDivertUdpHdr*)(pFake + ipHdrLen);
                fakeUdpHdr->Length = (ushort)IPAddress.HostToNetworkOrder((short)(8 + fakePayload.Length));
            }
        }

        // Настраиваем WinDivert address: Impostor=true, пересчитать чексуммы
        var fakeAddr = originalAddr;
        fakeAddr.Impostor = true;
        fakeAddr.IPChecksum = false;
        fakeAddr.TCPChecksum = false;
        fakeAddr.UDPChecksum = false;

        return Result<(byte[], WinDivertAddress)>.Success((fakePacket, fakeAddr));
    }

    // ── IPv6 fake packet ─────────────────────────────────────

    private unsafe Result<(byte[], WinDivertAddress)> BuildFakeIPv6(
        byte* pOriginal, int originalLen, WinDivertAddress originalAddr, byte[] fakePayload)
    {
        if (originalLen < 40)
            return Result<(byte[], WinDivertAddress)>.Failed("IPv6 packet too short");

        var ip6Hdr = (WinDivertIpv6Hdr*)pOriginal;
        int ipHdrLen = 40; // Fixed IPv6 header
        int totalHdrLen = ipHdrLen;

        if (ip6Hdr->NextHdr == 6) // TCP
        {
            if (originalLen < ipHdrLen + 20)
                return Result<(byte[], WinDivertAddress)>.Failed("TCP header too short");

            var tcpHdr = (WinDivertTcpHdr*)(pOriginal + ipHdrLen);
            totalHdrLen += tcpHdr->HdrLength;
        }
        else if (ip6Hdr->NextHdr == 17) // UDP
        {
            totalHdrLen += 8;
        }
        else
        {
            return Result<(byte[], WinDivertAddress)>.Failed($"Unsupported IPv6 next header: {ip6Hdr->NextHdr}");
        }

        int fakePacketLen = totalHdrLen + fakePayload.Length;
        byte[] fakePacket = new byte[fakePacketLen];

        fixed (byte* pFake = fakePacket)
        {
            Buffer.MemoryCopy(pOriginal, pFake, totalHdrLen, totalHdrLen);
            Marshal.Copy(fakePayload, 0, (IntPtr)(pFake + totalHdrLen), fakePayload.Length);

            // Обновляем IPv6 Payload Length
            var fakeIp6Hdr = (WinDivertIpv6Hdr*)pFake;
            fakeIp6Hdr->Length = (ushort)IPAddress.HostToNetworkOrder(
                (short)(fakePacketLen - ipHdrLen));

            // Обновляем UDP Length если UDP
            if (ip6Hdr->NextHdr == 17)
            {
                var fakeUdpHdr = (WinDivertUdpHdr*)(pFake + ipHdrLen);
                fakeUdpHdr->Length = (ushort)IPAddress.HostToNetworkOrder(
                    (short)(8 + fakePayload.Length));
            }
        }

        var fakeAddr = originalAddr;
        fakeAddr.Impostor = true;
        fakeAddr.IPChecksum = false;
        fakeAddr.TCPChecksum = false;
        fakeAddr.UDPChecksum = false;

        return Result<(byte[], WinDivertAddress)>.Success((fakePacket, fakeAddr));
    }

    // ── Выбор fake файла ─────────────────────────────────────

    private static string? ChooseFakeFile(FilterRule rule, L7Protocol l7)
    {
        return l7 switch
        {
            L7Protocol.Tls => rule.FakeTlsFiles is { Length: > 0 } ? rule.FakeTlsFiles[0] : null,
            L7Protocol.Http => rule.FakeHttpFile,
            L7Protocol.Stun => rule.FakeQuicFile, // STUN обычно по UDP → QUIC fake
            L7Protocol.None when rule.FakeUnknownUdpFile is not null => rule.FakeUnknownUdpFile,
            _ => null,
        };
    }

    // ── Загрузка .bin файлов ─────────────────────────────────

    /// <summary>
    /// Загрузить fake payload из .bin файла (с кэшированием).
    /// </summary>
    public Result<byte[]> LoadFakePayload(string relativeOrAbsolutePath)
    {
        if (_cache.TryGetValue(relativeOrAbsolutePath, out var cached))
            return Result<byte[]>.Success(cached);

        string path = Path.IsPathRooted(relativeOrAbsolutePath)
            ? relativeOrAbsolutePath
            : Path.Combine(_zapretDir, relativeOrAbsolutePath);

        if (!File.Exists(path))
            return Result<byte[]>.Failed($"Fake payload file not found: {path}");

        try
        {
            var data = File.ReadAllBytes(path);
            _cache[relativeOrAbsolutePath] = data;
            return Result<byte[]>.Success(data);
        }
        catch (IOException ex)
        {
            return Result<byte[]>.Failed($"Failed to load fake payload: {ex.Message}");
        }
        catch (UnauthorizedAccessException ex)
        {
            return Result<byte[]>.Failed($"Failed to load fake payload: {ex.Message}");
        }
    }

    /// <summary>
    /// Очистить кэш загруженных payload файлов.
    /// </summary>
    public void ClearCache() => _cache.Clear();
}
