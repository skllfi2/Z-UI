// ═══════════════════════════════════════════════════════════════
// ZUI.Proxy / Client / Socks4Client.cs
// SOCKS4/SOCKS4a клиент (простой, без аутентификации)
// SOCKS4a: DNS resolution через прокси (если host = домен)
// ═══════════════════════════════════════════════════════════════

using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ZUI.Core;
using ZUI.Proxy.Rules;

namespace ZUI.Proxy.Client;

/// <summary>
/// SOCKS4/SOCKS4a клиент.
/// SOCKS4: только IPv4, без аутентификации.
/// SOCKS4a: поддержка доменных имён (DNS через прокси).
/// </summary>
public sealed class Socks4Client
{
    private readonly ILogger _logger;

    public Socks4Client(ILogger<Socks4Client>? logger = null)
    {
        _logger = logger ?? NullLoggerFactory.Instance.CreateLogger<Socks4Client>();
    }

    /// <summary>
    /// Подключиться через SOCKS4/SOCKS4a прокси.
    /// Если targetHost — домен, используется SOCKS4a (0.0.0.x + userid\0 + domain\0).
    /// Если targetHost — IPv4, используется классический SOCKS4.
    /// </summary>
    public async Task<Result<TcpClient>> ConnectAsync(
        ProxyTarget proxy,
        string targetHost,
        int targetPort,
        CancellationToken ct = default)
    {
            return await ConnectAsync(proxy.Host, proxy.Port, targetHost, targetPort, proxy.Type == ProxyType.Socks4a, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Подключиться через SOCKS4/SOCKS4a прокси.
    /// </summary>
    public async Task<Result<TcpClient>> ConnectAsync(
        string proxyHost,
        int proxyPort,
        string targetHost,
        int targetPort,
        bool useSocks4a = false,
        CancellationToken ct = default)
    {
        try
        {
            var tcpClient = new TcpClient();
            await tcpClient.ConnectAsync(proxyHost, proxyPort, ct).ConfigureAwait(false);
            var stream = tcpClient.GetStream();

            byte[] request;
            bool isDomain = !IPAddress.TryParse(targetHost, out var targetIp);

            if (isDomain || useSocks4a)
            {
                // SOCKS4a: IP = 0.0.0.x (x != 0), userid\0, domain\0
                request = BuildSocks4aRequest(targetHost, targetPort);
            }
            else
            {
                // SOCKS4: IP = реальный IPv4 адрес
                if (targetIp is null || targetIp.AddressFamily != AddressFamily.InterNetwork)
                    return Result<TcpClient>.Failed("SOCKS4 only supports IPv4 targets");

                request = BuildSocks4Request(targetIp, targetPort);
            }

            await stream.WriteAsync(request, ct).ConfigureAwait(false);
            await stream.FlushAsync(ct).ConfigureAwait(false);

            // Ответ: 8 байт (VN=0, CD, PORT, IP)
            var response = new byte[8];
            await ReadExactAsync(stream, response, ct).ConfigureAwait(false);

            // VN должен быть 0x00 (reply version)
            var replyCode = response[1];
            if (replyCode != 0x5A) // 0x5A = 90 = Request granted
            {
                var error = replyCode switch
                {
                    0x5B => "Request rejected or failed",
                    0x5C => "Request failed because SOCKS server cannot connect to identd",
                    0x5D => "Request failed because client and identd report different user-ids",
                    _ => $"Unknown SOCKS4 error (0x{replyCode:X2})",
                };
                tcpClient.Close();
                return Result<TcpClient>.Failed($"SOCKS4 connect failed: {error}");
            }

            _logger.LogDebug("SOCKS4 connected: {Proxy} → {Target}:{Port}", proxyHost, targetHost, targetPort);
            return Result<TcpClient>.Success(tcpClient);
        }
        catch (SocketException ex)
        {
            return Result<TcpClient>.Failed($"SOCKS4 connection error: {ex.Message}");
        }
    }

    // ── Формирование запросов ─────────────────────────────

    /// <summary>
    /// SOCKS4 запрос с IPv4 адресом.
    /// Format: VER(1) + CMD(1) + DSTPORT(2) + DSTIP(4) + USERID\0
    /// </summary>
    private static byte[] BuildSocks4Request(IPAddress targetIp, int targetPort)
    {
        var ipBytes = targetIp.GetAddressBytes();
        if (ipBytes.Length != 4)
            throw new ArgumentException("SOCKS4 requires IPv4 address", nameof(targetIp));

        var request = new byte[9]; // 1+1+2+4+1 (minimum, empty userid)
        request[0] = 0x04; // VER = 4
        request[1] = 0x01; // CMD = CONNECT
        request[2] = (byte)((targetPort >> 8) & 0xFF); // Port high
        request[3] = (byte)(targetPort & 0xFF);          // Port low
        ipBytes.CopyTo(request, 4);
        request[8] = 0x00; // USERID = empty (null-terminated)

        return request;
    }

    /// <summary>
    /// SOCKS4a запрос с доменным именем.
    /// Format: VER(1) + CMD(1) + DSTPORT(2) + DSTIP=0.0.0.x(4) + USERID\0 + DOMAIN\0
    /// </summary>
    private static byte[] BuildSocks4aRequest(string domain, int targetPort)
    {
        var domainBytes = System.Text.Encoding.ASCII.GetBytes(domain);
        // 1+1+2+4+1 + domain + 1 = 10 + domain.Length
        var request = new byte[10 + domainBytes.Length];
        request[0] = 0x04; // VER = 4
        request[1] = 0x01; // CMD = CONNECT
        request[2] = (byte)((targetPort >> 8) & 0xFF);
        request[3] = (byte)(targetPort & 0xFF);
        request[4] = 0x00; // IP: 0.0.0.x
        request[5] = 0x00;
        request[6] = 0x00;
        request[7] = 0x01; // x = 1 (non-zero = SOCKS4a signal)
        request[8] = 0x00; // USERID = empty
        domainBytes.CopyTo(request, 9);
        request[9 + domainBytes.Length] = 0x00; // Domain null-terminator

        return request;
    }

    private static async Task ReadExactAsync(
        NetworkStream stream,
        byte[] buffer,
        CancellationToken ct)
    {
        int offset = 0;
        while (offset < buffer.Length)
        {
            int read = await stream.ReadAsync(buffer.AsMemory(offset, buffer.Length - offset), ct).ConfigureAwait(false);
            if (read == 0)
                throw new IOException("SOCKS4 connection closed prematurely");
            offset += read;
        }
    }
}
