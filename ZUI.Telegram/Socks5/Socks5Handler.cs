// ═══════════════════════════════════════════════════════════════
// ZUI.Telegram / Socks5 / Socks5Handler.cs
// Обработка одного SOCKS5 подключения: handshake + CONNECT
// Поддерживает NoAuth и UserPass аутентификацию
// ═══════════════════════════════════════════════════════════════

using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ZUI.Core;

namespace ZUI.Telegram.Socks5;

/// <summary>
/// Результат SOCKS5 handshake.
/// </summary>
public sealed class Socks5HandshakeResult
{
    /// <summary>Адрес назначения (IP или домен).</summary>
    public required string TargetHost { get; init; }

    /// <summary>Порт назначения.</summary>
    public required int TargetPort { get; init; }

    /// <summary>Адрес назначения как IPAddress (если IP, не домен).</summary>
    public IPAddress? TargetIpAddress { get; init; }

    /// <summary>Имя пользователя (если аутентификация пройдена).</summary>
    public string? Username { get; init; }
}

/// <summary>
/// Обработчик SOCKS5 подключения.
/// Реализует RFC 1928 (SOCKS5) + RFC 1929 (Username/Password auth).
/// 
/// Поддерживаемые методы аутентификации:
/// - 0x00: No Auth (по умолчанию)
/// - 0x02: Username/Password (если настроен)
/// 
/// Поддерживаемые команды:
/// - 0x01: CONNECT (TCP)
/// </summary>
public sealed class Socks5Handler
{
    private readonly ILogger _logger;
    private readonly string? _authUsername;
    private readonly string? _authPassword;

    /// <summary>Требуется ли аутентификация.</summary>
    public bool RequiresAuth => _authUsername is not null;

    public Socks5Handler(ILogger? logger = null)
    {
        _logger = logger ?? NullLoggerFactory.Instance.CreateLogger<Socks5Handler>();
    }

    public Socks5Handler(string? username, string? password, ILogger? logger = null)
    {
        _authUsername = username;
        _authPassword = password;
        _logger = logger ?? NullLoggerFactory.Instance.CreateLogger<Socks5Handler>();
    }

    /// <summary>
    /// Выполнить SOCKS5 handshake: greeting + request → target address.
    /// </summary>
    /// <param name="stream">Поток подключения клиента.</param>
    /// <param name="ct">Токен отмены.</param>
    /// <returns>Результат handshake с адресом назначения, или ошибка.</returns>
    public async Task<Result<Socks5HandshakeResult>> HandleHandshakeAsync(
        NetworkStream stream, CancellationToken ct = default)
    {
        try
        {
            // Шаг 1: Greeting (VER + NMETHODS + METHODS)
            var greeting = new byte[2];
            await ReadExactAsync(stream, greeting, ct).ConfigureAwait(false);

            if (greeting[0] != 0x05)
                return Result<Socks5HandshakeResult>.Failed($"Invalid SOCKS version: 0x{greeting[0]:X2}");

            int nMethods = greeting[1];
            if (nMethods == 0)
                return Result<Socks5HandshakeResult>.Failed("No authentication methods offered");

            var methods = new byte[nMethods];
            await ReadExactAsync(stream, methods, ct).ConfigureAwait(false);

            // Выбрать метод аутентификации
            bool clientSupportsNoAuth = methods.Contains((byte)0x00);
            bool clientSupportsUserPass = methods.Contains((byte)0x02);

            string? authenticatedUser = null;

            if (RequiresAuth)
            {
                // Сервер требует аутентификацию
                if (!clientSupportsUserPass)
                {
                    // Отклонить: нет поддерживаемого метода
                    await stream.WriteAsync(new byte[] { 0x05, 0xFF }, ct).ConfigureAwait(false);
                    return Result<Socks5HandshakeResult>.Failed("Client does not support required authentication");
                }

                // Выбрать Username/Password
                await stream.WriteAsync(new byte[] { 0x05, 0x02 }, ct).ConfigureAwait(false);

                var authResult = await HandleUserPassAuthAsync(stream, ct).ConfigureAwait(false);
                if (!authResult.IsSuccess)
                    return Result<Socks5HandshakeResult>.Failed(authResult.Error ?? "Authentication failed");

                authenticatedUser = authResult.Value;
            }
            else
            {
                // Без аутентификации
                if (clientSupportsNoAuth)
                {
                    await stream.WriteAsync(new byte[] { 0x05, 0x00 }, ct).ConfigureAwait(false);
                }
                else if (clientSupportsUserPass)
                {
                    // Клиент хочет UserPass — согласиться, но не проверять
                    await stream.WriteAsync(new byte[] { 0x05, 0x02 }, ct).ConfigureAwait(false);
                    var authResult = await HandleUserPassAuthAsync(stream, ct).ConfigureAwait(false);
                    if (!authResult.IsSuccess)
                        return Result<Socks5HandshakeResult>.Failed(authResult.Error ?? "Authentication failed");
                }
                else
                {
                    await stream.WriteAsync(new byte[] { 0x05, 0xFF }, ct).ConfigureAwait(false);
                    return Result<Socks5HandshakeResult>.Failed("No acceptable authentication method");
                }
            }

            // Шаг 2: Request (VER + CMD + RSV + ATYP + DST.ADDR + DST.PORT)
            var requestHeader = new byte[4];
            await ReadExactAsync(stream, requestHeader, ct).ConfigureAwait(false);

            if (requestHeader[0] != 0x05)
                return Result<Socks5HandshakeResult>.Failed($"Invalid SOCKS version in request: 0x{requestHeader[0]:X2}");

            if (requestHeader[1] != 0x01) // Только CONNECT
            {
                await SendConnectReplyAsync(stream, Socks5Reply.CommandNotSupported, ct).ConfigureAwait(false);
                return Result<Socks5HandshakeResult>.Failed($"Unsupported SOCKS5 command: 0x{requestHeader[1]:X2}");
            }

            byte atyp = requestHeader[3];
            string targetHost;
            IPAddress? targetIp = null;

            switch (atyp)
            {
                case 0x01: // IPv4
                    {
                        var ipBytes = new byte[4];
                        await ReadExactAsync(stream, ipBytes, ct).ConfigureAwait(false);
                        targetIp = new IPAddress(ipBytes);
                        targetHost = targetIp.ToString();
                        break;
                    }
                case 0x03: // Domain
                    {
                        var lenBuf = new byte[1];
                        await ReadExactAsync(stream, lenBuf, ct).ConfigureAwait(false);
                        var domainBytes = new byte[lenBuf[0]];
                        await ReadExactAsync(stream, domainBytes, ct).ConfigureAwait(false);
                        targetHost = System.Text.Encoding.ASCII.GetString(domainBytes);
                        break;
                    }
                case 0x04: // IPv6
                    {
                        var ip6Bytes = new byte[16];
                        await ReadExactAsync(stream, ip6Bytes, ct).ConfigureAwait(false);
                        targetIp = new IPAddress(ip6Bytes);
                        targetHost = targetIp.ToString();
                        break;
                    }
                default:
                    await SendConnectReplyAsync(stream, Socks5Reply.AddressTypeNotSupported, ct).ConfigureAwait(false);
                    return Result<Socks5HandshakeResult>.Failed($"Unsupported ATYP: 0x{atyp:X2}");
            }

            // Порт (2 байта, big-endian)
            var portBytes = new byte[2];
            await ReadExactAsync(stream, portBytes, ct).ConfigureAwait(false);
            int targetPort = (portBytes[0] << 8) | portBytes[1];

            // Отправить успешный CONNECT reply (привязка к 0.0.0.0:0)
            await SendConnectReplyAsync(stream, Socks5Reply.Succeeded, ct).ConfigureAwait(false);

            _logger.LogDebug("SOCKS5 handshake completed: {Host}:{Port}", targetHost, targetPort);

            return Result<Socks5HandshakeResult>.Success(new Socks5HandshakeResult
            {
                TargetHost = targetHost,
                TargetPort = targetPort,
                TargetIpAddress = targetIp,
                Username = authenticatedUser,
            });
        }
        catch (IOException ex)
        {
            return Result<Socks5HandshakeResult>.Failed($"SOCKS5 handshake IO error: {ex.Message}");
        }
        catch (SocketException ex)
        {
            return Result<Socks5HandshakeResult>.Failed($"SOCKS5 handshake socket error: {ex.Message}");
        }
    }

    /// <summary>
    /// Обработать Username/Password аутентификацию (RFC 1929).
    /// </summary>
    private async Task<Result<string>> HandleUserPassAuthAsync(NetworkStream stream, CancellationToken ct)
    {
        // VER (1) + ULEN (1)
        var authHeader = new byte[2];
        await ReadExactAsync(stream, authHeader, ct).ConfigureAwait(false);

        if (authHeader[0] != 0x01)
            return Result<string>.Failed($"Invalid auth version: 0x{authHeader[0]:X2}");

        int uLen = authHeader[1];
        if (uLen > 255)
            return Result<string>.Failed("Username too long");

        var usernameBytes = new byte[uLen];
        await ReadExactAsync(stream, usernameBytes, ct).ConfigureAwait(false);
        var username = System.Text.Encoding.ASCII.GetString(usernameBytes);

        // PLEN (1)
        var pLenBuf = new byte[1];
        await ReadExactAsync(stream, pLenBuf, ct).ConfigureAwait(false);
        int pLen = pLenBuf[0];

        var passwordBytes = new byte[pLen];
        await ReadExactAsync(stream, passwordBytes, ct).ConfigureAwait(false);
        var password = System.Text.Encoding.ASCII.GetString(passwordBytes);

        // Проверить credentials
        bool authOk = !RequiresAuth ||
                      (username == _authUsername && password == _authPassword);

        // Ответ: VER + STATUS (0x00 = success, 0x01 = failure)
        await stream.WriteAsync(new byte[] { 0x01, authOk ? (byte)0x00 : (byte)0x01 }, ct).ConfigureAwait(false);

        if (!authOk)
            return Result<string>.Failed("Authentication failed: invalid credentials");

        return Result<string>.Success(username);
    }

    /// <summary>
    /// Отправить SOCKS5 CONNECT reply.
    /// </summary>
    private static async Task SendConnectReplyAsync(NetworkStream stream, Socks5Reply reply, CancellationToken ct)
    {
        // VER + REP + RSV + ATYP + BND.ADDR (4 для IPv4) + BND.PORT (2)
        var response = new byte[10];
        response[0] = 0x05; // VER
        response[1] = (byte)reply; // REP
        response[2] = 0x00; // RSV
        response[3] = 0x01; // ATYP = IPv4
        // BND.ADDR = 0.0.0.0 (4 нуля)
        // BND.PORT = 0 (2 нуля)
        await stream.WriteAsync(response, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Отправить SOCKS5 CONNECT reply с конкретным bound address.
    /// </summary>
    public static async Task SendConnectReplyWithEndpointAsync(
        NetworkStream stream, IPEndPoint boundEndpoint, CancellationToken ct = default)
    {
        var ipBytes = boundEndpoint.Address.GetAddressBytes();
        var portBytes = new byte[] { (byte)((boundEndpoint.Port >> 8) & 0xFF), (byte)(boundEndpoint.Port & 0xFF) };

        using var ms = new MemoryStream();
        ms.WriteByte(0x05); // VER
        ms.WriteByte((byte)Socks5Reply.Succeeded); // REP
        ms.WriteByte(0x00); // RSV

        if (boundEndpoint.Address.AddressFamily == AddressFamily.InterNetwork)
        {
            ms.WriteByte(0x01); // ATYP = IPv4
            ms.Write(ipBytes, 0, 4);
        }
        else
        {
            ms.WriteByte(0x04); // ATYP = IPv6
            ms.Write(ipBytes, 0, 16);
        }

        ms.Write(portBytes, 0, 2);
        await stream.WriteAsync(ms.ToArray(), ct).ConfigureAwait(false);
    }

    // ── Вспомогательные ──────────────────────────────────

    private static async Task ReadExactAsync(NetworkStream stream, byte[] buffer, CancellationToken ct)
    {
        int offset = 0;
        while (offset < buffer.Length)
        {
            int read = await stream.ReadAsync(buffer.AsMemory(offset, buffer.Length - offset), ct).ConfigureAwait(false);
            if (read == 0)
                throw new IOException("Connection closed during SOCKS5 handshake");
            offset += read;
        }
    }
}

/// <summary>
/// Коды ответов SOCKS5 CONNECT.
/// </summary>
public enum Socks5Reply : byte
{
    Succeeded = 0x00,
    GeneralFailure = 0x01,
    ConnectionNotAllowed = 0x02,
    NetworkUnreachable = 0x03,
    HostUnreachable = 0x04,
    ConnectionRefused = 0x05,
    TtlExpired = 0x06,
    CommandNotSupported = 0x07,
    AddressTypeNotSupported = 0x08,
}
