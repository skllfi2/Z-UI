// ═══════════════════════════════════════════════════════════════
// ZUI.Proxy / Client / Socks5Client.cs
// SOCKS5 клиент: handshake + connect + auth (RFC 1928, RFC 1929)
// Поддержка: NoAuth, UserPass, IPv4, IPv6, Domain
// ═══════════════════════════════════════════════════════════════

using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ZUI.Core;
using ZUI.Proxy.Rules;

namespace ZUI.Proxy.Client;

/// <summary>
/// SOCKS5 клиент (RFC 1928).
/// Устанавливает TCP соединение через SOCKS5 прокси.
/// Поддержка аутентификации: NoAuth (0x00), UserPass (0x02).
/// </summary>
public sealed class Socks5Client
{
    private readonly ILogger _logger;

    public Socks5Client(ILogger<Socks5Client>? logger = null)
    {
        _logger = logger ?? NullLoggerFactory.Instance.CreateLogger<Socks5Client>();
    }

    /// <summary>
    /// Подключиться к целевому хосту через SOCKS5 прокси.
    /// Возвращает TcpClient с установленным соединением.
    /// </summary>
    public async Task<Result<TcpClient>> ConnectAsync(
        ProxyTarget proxy,
        string targetHost,
        int targetPort,
        CancellationToken ct = default)
    {
        var auth = proxy.RequiresAuth
            ? ProxyAuth.UserPass(proxy.Username!, proxy.Password!)
            : ProxyAuth.None;

            return await ConnectAsync(proxy.Host, proxy.Port, targetHost, targetPort, auth, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Подключиться к целевому хосту через SOCKS5 прокси.
    /// </summary>
    public async Task<Result<TcpClient>> ConnectAsync(
        string proxyHost,
        int proxyPort,
        string targetHost,
        int targetPort,
        ProxyAuth? auth = null,
        CancellationToken ct = default)
    {
        auth ??= ProxyAuth.None;

        try
        {
            // 1. TCP соединение к прокси
            var tcpClient = new TcpClient();
            await tcpClient.ConnectAsync(proxyHost, proxyPort, ct).ConfigureAwait(false);
            var stream = tcpClient.GetStream();

            // 2. SOCKS5 handshake
            var handshakeResult = await HandshakeAsync(stream, auth, ct).ConfigureAwait(false);
            if (!handshakeResult.IsSuccess)
            {
                tcpClient.Close();
                return Result<TcpClient>.Failed($"SOCKS5 handshake failed: {handshakeResult.Error}");
            }

            // 3. CONNECT запрос
            var connectResult = await SendConnectAsync(stream, targetHost, targetPort, ct).ConfigureAwait(false);
            if (!connectResult.IsSuccess)
            {
                tcpClient.Close();
                return Result<TcpClient>.Failed($"SOCKS5 connect failed: {connectResult.Error}");
            }

            _logger.LogDebug("SOCKS5 connected: {Proxy} → {Target}:{Port}", proxyHost, targetHost, targetPort);
            return Result<TcpClient>.Success(tcpClient);
        }
        catch (SocketException ex)
        {
            return Result<TcpClient>.Failed($"SOCKS5 connection error: {ex.Message}");
        }
    }

    // ── Handshake ─────────────────────────────────────────

    /// <summary>
    /// SOCKS5 handshake: выбор метода аутентификации.
    /// RFC 1928 Section 3.
    /// </summary>
    private async Task<Result> HandshakeAsync(
        NetworkStream stream,
        ProxyAuth auth,
        CancellationToken ct)
    {
        // Клиент предлагает методы: 0x00 (NoAuth) и/или 0x02 (UserPass)
        byte[] methods = auth.RequiresAuth
            ? [0x05, 0x02, 0x00, 0x02] // VER=5, NMETHODS=2, NoAuth, UserPass
            : [0x05, 0x01, 0x00];       // VER=5, NMETHODS=1, NoAuth

        await stream.WriteAsync(methods, ct).ConfigureAwait(false);
        await stream.FlushAsync(ct).ConfigureAwait(false);

        // Ответ сервера: VER, METHOD
        var response = new byte[2];
        await ReadExactAsync(stream, response, ct).ConfigureAwait(false);

        if (response[0] != 0x05)
            return Result.Failed($"Invalid SOCKS version: 0x{response[0]:X2} (expected 0x05)");

        byte selectedMethod = response[1];

        return selectedMethod switch
        {
            0x00 => Result.Success(), // NoAuth — handshake завершён
            0x02 => await AuthenticateUserPassAsync(stream, auth, ct).ConfigureAwait(false),
            0xFF => Result.Failed("SOCKS5 server rejected all authentication methods"),
            _ => Result.Failed($"Unsupported SOCKS5 auth method: 0x{selectedMethod:X2}"),
        };
    }

    /// <summary>
    /// SOCKS5 аутентификация UserPass (RFC 1929).
    /// </summary>
    private static async Task<Result> AuthenticateUserPassAsync(
        NetworkStream stream,
        ProxyAuth auth,
        CancellationToken ct)
    {
        if (!auth.RequiresAuth || string.IsNullOrEmpty(auth.Username) || string.IsNullOrEmpty(auth.Password))
            return Result.Failed("SOCKS5 server requires authentication but no credentials provided");

        // RFC 1929: VER(1) + ULEN(1) + UNAME(variable) + PLEN(1) + PASSWD(variable)
        var usernameBytes = System.Text.Encoding.ASCII.GetBytes(auth.Username);
        var passwordBytes = System.Text.Encoding.ASCII.GetBytes(auth.Password);

        if (usernameBytes.Length > 255 || passwordBytes.Length > 255)
            return Result.Failed("SOCKS5 username/password too long (max 255 bytes each)");

        var authRequest = new byte[3 + usernameBytes.Length + passwordBytes.Length];
        authRequest[0] = 0x01; // VER = 1 (subnegotiation version)
        authRequest[1] = (byte)usernameBytes.Length;
        usernameBytes.CopyTo(authRequest, 2);
        authRequest[2 + usernameBytes.Length] = (byte)passwordBytes.Length;
        passwordBytes.CopyTo(authRequest, 3 + usernameBytes.Length);

        await stream.WriteAsync(authRequest, ct).ConfigureAwait(false);
        await stream.FlushAsync(ct).ConfigureAwait(false);

        // Ответ: VER(1) + STATUS(1)
        var authResponse = new byte[2];
        await ReadExactAsync(stream, authResponse, ct).ConfigureAwait(false);

        if (authResponse[1] != 0x00)
            return Result.Failed($"SOCKS5 authentication failed (status: 0x{authResponse[1]:X2})");

        return Result.Success();
    }

    // ── CONNECT ───────────────────────────────────────────

    /// <summary>
    /// SOCKS5 CONNECT запрос (RFC 1928 Section 4).
    /// Поддержка: IPv4 (0x01), Domain (0x03), IPv6 (0x04).
    /// </summary>
    private static async Task<Result> SendConnectAsync(
        NetworkStream stream,
        string targetHost,
        int targetPort,
        CancellationToken ct)
    {
        using var ms = new MemoryStream();
        var writer = new BinaryWriter(ms);

        // Header
        writer.Write((byte)0x05); // VER = 5
        writer.Write((byte)0x01); // CMD = CONNECT
        writer.Write((byte)0x00); // RSV = 0

        // DST.ADDR — определяем тип адреса
        if (IPAddress.TryParse(targetHost, out var ip))
        {
            if (ip.AddressFamily == AddressFamily.InterNetwork)
            {
                writer.Write((byte)0x01); // ATYP = IPv4
                writer.Write(ip.GetAddressBytes());
            }
            else
            {
                writer.Write((byte)0x04); // ATYP = IPv6
                writer.Write(ip.GetAddressBytes());
            }
        }
        else
        {
            // Domain name
            var domainBytes = System.Text.Encoding.ASCII.GetBytes(targetHost);
            if (domainBytes.Length > 255)
                return Result.Failed("Domain name too long for SOCKS5 (max 255 bytes)");

            writer.Write((byte)0x03); // ATYP = Domain
            writer.Write((byte)domainBytes.Length);
            writer.Write(domainBytes);
        }

        // DST.PORT (big-endian)
        writer.Write((byte)((targetPort >> 8) & 0xFF));
        writer.Write((byte)(targetPort & 0xFF));

        await stream.WriteAsync(ms.ToArray(), ct).ConfigureAwait(false);
        await stream.FlushAsync(ct).ConfigureAwait(false);

        // Ответ сервера: VER(1) + REP(1) + RSV(1) + ATYP(1) + ADDR(variable) + PORT(2)
        var replyHeader = new byte[4];
        await ReadExactAsync(stream, replyHeader, ct).ConfigureAwait(false);

        if (replyHeader[0] != 0x05)
            return Result.Failed($"Invalid SOCKS5 reply version: 0x{replyHeader[0]:X2}");

        var replyCode = replyHeader[1];
        if (replyCode != 0x00)
        {
            var error = replyCode switch
            {
                0x01 => "General SOCKS server failure",
                0x02 => "Connection not allowed by ruleset",
                0x03 => "Network unreachable",
                0x04 => "Host unreachable",
                0x05 => "Connection refused",
                0x06 => "TTL expired",
                0x07 => "Command not supported",
                0x08 => "Address type not supported",
                _ => $"Unknown error (0x{replyCode:X2})",
            };
            return Result.Failed($"SOCKS5 connect failed: {error}");
        }

        // Прочитать адрес привязки (BND.ADDR + BND.PORT) — нужно потребить из потока
        var atyp = replyHeader[3];
        int addrLen = atyp switch
        {
            0x01 => 4,    // IPv4
            0x04 => 16,   // IPv6
            0x03 => 0,    // Domain — длина в первом байте
            _ => 4,       // Fallback
        };

        if (atyp == 0x03)
        {
            var lenBuf = new byte[1];
            await ReadExactAsync(stream, lenBuf, ct).ConfigureAwait(false);
            addrLen = lenBuf[0];
        }

        // Прочитать адрес + порт
        var remaining = new byte[addrLen + 2];
        await ReadExactAsync(stream, remaining, ct).ConfigureAwait(false);

        return Result.Success();
    }

    // ── Вспомогательные ───────────────────────────────────

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
                throw new IOException("SOCKS5 connection closed prematurely");
            offset += read;
        }
    }
}
