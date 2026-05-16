// ═══════════════════════════════════════════════════════════════
// ZUI.Proxy / Chain / ChainExecutor.cs
// Исполнитель цепочки: последовательно соединяет прокси
// proxy1 → proxy2 → ... → last_proxy → target
// ═══════════════════════════════════════════════════════════════

using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ZUI.Core;
using ZUI.Proxy.Client;
using ZUI.Proxy.Rules;

namespace ZUI.Proxy.Chain;

/// <summary>
/// Исполнитель прокси-цепочки.
/// Устанавливает последовательное соединение через N прокси:
/// 
/// 1. TCP connect к proxy1
/// 2. SOCKS5/4/HTTP CONNECT к proxy2 через proxy1
/// 3. SOCKS5/4/HTTP CONNECT к proxy3 через proxy2 (через proxy1)
/// 4. ... до последнего прокси
/// 5. Финальный CONNECT к targetHost:targetPort через последний прокси
/// 
/// Возвращает TcpClient (к proxy1) с установленным туннелем до target.
/// </summary>
public sealed class ChainExecutor
{
    private readonly Socks5Client _socks5;
    private readonly Socks4Client _socks4;
    private readonly HttpConnectClient _httpConnect;
    private readonly ILogger _logger;

    public ChainExecutor(
        Socks5Client socks5,
        Socks4Client socks4,
        HttpConnectClient httpConnect,
        ILogger<ChainExecutor>? logger = null)
    {
        _socks5 = socks5;
        _socks4 = socks4;
        _httpConnect = httpConnect;
        _logger = logger ?? NullLoggerFactory.Instance.CreateLogger<ChainExecutor>();
    }

    /// <summary>
    /// Выполнить соединение через цепочку прокси.
    /// Каждый узел цепочки устанавливает CONNECT к следующему узлу.
    /// Последний узел устанавливает CONNECT к целевому хосту.
    /// </summary>
    public async Task<Result<TcpClient>> ExecuteAsync(
        ProxyChain chain,
        string targetHost,
        int targetPort,
        CancellationToken ct = default)
    {
        if (chain.IsEmpty || chain.Nodes.Count == 0)
            return Result<TcpClient>.Failed("Proxy chain is empty");

        if (!chain.IsEnabled)
            return Result<TcpClient>.Failed($"Proxy chain '{chain.Name}' is disabled");

        _logger.LogDebug("Executing chain [{Chain}] with {Count} nodes → {Target}:{Port}",
            chain.Name, chain.Count, targetHost, targetPort);

        // Шаг 1: Соединение к первому прокси напрямую
        var firstNode = chain.Nodes[0];
        TcpClient? currentClient = null;

        try
        {
            currentClient = new TcpClient();
            await currentClient.ConnectAsync(firstNode.Host, firstNode.Port, ct).ConfigureAwait(false);
            _logger.LogDebug("Chain step 1: connected to {Proxy}", firstNode);
        }
        catch (SocketException ex)
        {
            currentClient?.Dispose();
            return Result<TcpClient>.Failed($"Chain step 1 failed (connect to {firstNode}): {ex.Message}");
        }

        // Шаг 2-N: Каждый промежуточный прокси устанавливает CONNECT к следующему
        for (int i = 1; i < chain.Nodes.Count; i++)
        {
            var nextNode = chain.Nodes[i];
                var connectResult = await ConnectThroughProxyAsync(currentClient, firstNode.Type, nextNode.Host, nextNode.Port, ct).ConfigureAwait(false);

            if (!connectResult.IsSuccess)
            {
                currentClient.Dispose();
                return Result<TcpClient>.Failed($"Chain step {i + 1} failed (CONNECT to {nextNode}): {connectResult.Error}");
            }

            _logger.LogDebug("Chain step {Step}: CONNECT to {Proxy} via {PrevProxy}",
                i + 1, nextNode, chain.Nodes[i - 1]);
        }

        // Шаг N+1: Финальный CONNECT к целевому хосту через последний прокси
        var lastNode = chain.Nodes[^1];
            var finalResult = await ConnectThroughProxyAsync(currentClient, lastNode.Type, targetHost, targetPort, ct).ConfigureAwait(false);

        if (!finalResult.IsSuccess)
        {
            currentClient.Dispose();
            return Result<TcpClient>.Failed(
                $"Chain final step failed (CONNECT to {targetHost}:{targetPort}): {finalResult.Error}");
        }

        _logger.LogInformation("Chain [{Chain}] established: {Count} hops → {Target}:{Port}",
            chain.Name, chain.Count, targetHost, targetPort);

        return Result<TcpClient>.Success(currentClient);
    }

    /// <summary>
    /// Установить CONNECT через уже подключённый TcpClient.
    /// Использует тип прокси для выбора протокола (SOCKS5/SOCKS4/HTTP).
    /// </summary>
    private async Task<Result> ConnectThroughProxyAsync(
        TcpClient client,
        ProxyType proxyType,
        string nextHost,
        int nextPort,
        CancellationToken ct)
    {
        try
        {
            var stream = client.GetStream();

            switch (proxyType)
            {
                case ProxyType.Socks5:
                    return await Socks5ConnectThroughStreamAsync(stream, nextHost, nextPort, ct).ConfigureAwait(false);

                case ProxyType.Socks4:
                case ProxyType.Socks4a:
                return await Socks4ConnectThroughStreamAsync(stream, nextHost, nextPort, proxyType == ProxyType.Socks4a, ct).ConfigureAwait(false);

                case ProxyType.HttpConnect:
                    return await HttpConnectThroughStreamAsync(stream, nextHost, nextPort, ct).ConfigureAwait(false);

                default:
                    return Result.Failed($"Unsupported proxy type in chain: {proxyType}");
            }
        }
        catch (SocketException ex)
        {
            return Result.Failed($"Connection through proxy failed: {ex.Message}");
        }
        catch (IOException ex)
        {
            return Result.Failed($"Connection through proxy failed: {ex.Message}");
        }
    }

    // ── SOCKS5 через существующий поток ──────────────────

    private static async Task<Result> Socks5ConnectThroughStreamAsync(
        NetworkStream stream, string host, int port, CancellationToken ct)
    {
        // Handshake: NoAuth (в цепочке промежуточные прокси обычно без auth)
        var handshake = new byte[] { 0x05, 0x01, 0x00 };
        await stream.WriteAsync(handshake, ct).ConfigureAwait(false);
        await stream.FlushAsync(ct).ConfigureAwait(false);

        var handshakeResp = new byte[2];
        await ReadExactAsync(stream, handshakeResp, ct).ConfigureAwait(false);

        if (handshakeResp[0] != 0x05)
            return Result.Failed($"Invalid SOCKS5 version in chain: 0x{handshakeResp[0]:X2}");

        if (handshakeResp[1] == 0xFF)
            return Result.Failed("SOCKS5 proxy in chain rejected NoAuth");

        // CONNECT
        using var ms = new MemoryStream();
        var writer = new BinaryWriter(ms);
        writer.Write((byte)0x05); // VER
        writer.Write((byte)0x01); // CMD = CONNECT
        writer.Write((byte)0x00); // RSV

        if (System.Net.IPAddress.TryParse(host, out var ip))
        {
            if (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
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
            var domainBytes = System.Text.Encoding.ASCII.GetBytes(host);
            writer.Write((byte)0x03); // ATYP = Domain
            writer.Write((byte)domainBytes.Length);
            writer.Write(domainBytes);
        }

        writer.Write((byte)((port >> 8) & 0xFF));
        writer.Write((byte)(port & 0xFF));

        await stream.WriteAsync(ms.ToArray(), ct).ConfigureAwait(false);
        await stream.FlushAsync(ct).ConfigureAwait(false);

        // Читать ответ
        var replyHeader = new byte[4];
        await ReadExactAsync(stream, replyHeader, ct).ConfigureAwait(false);

        if (replyHeader[0] != 0x05 || replyHeader[1] != 0x00)
            return Result.Failed($"SOCKS5 CONNECT in chain failed: reply=0x{replyHeader[1]:X2}");

        // Потребить BND.ADDR + BND.PORT
        var atyp = replyHeader[3];
        int addrLen = atyp switch
        {
            0x01 => 4,   // IPv4
            0x04 => 16,  // IPv6
            0x03 => 0,   // Domain
            _ => 4,
        };

        if (atyp == 0x03)
        {
            var lenBuf = new byte[1];
            await ReadExactAsync(stream, lenBuf, ct).ConfigureAwait(false);
            addrLen = lenBuf[0];
        }

        var remaining = new byte[addrLen + 2];
        await ReadExactAsync(stream, remaining, ct).ConfigureAwait(false);

        return Result.Success();
    }

    // ── SOCKS4/4a через существующий поток ───────────────

    private static async Task<Result> Socks4ConnectThroughStreamAsync(
        NetworkStream stream, string host, int port, bool useSocks4a, CancellationToken ct)
    {
        byte[] request;

        if (useSocks4a || !System.Net.IPAddress.TryParse(host, out var targetIp))
        {
            // SOCKS4a
            var domainBytes = System.Text.Encoding.ASCII.GetBytes(host);
            request = new byte[10 + domainBytes.Length];
            request[0] = 0x04; // VER
            request[1] = 0x01; // CMD = CONNECT
            request[2] = (byte)((port >> 8) & 0xFF);
            request[3] = (byte)(port & 0xFF);
            request[4] = 0x00; request[5] = 0x00; request[6] = 0x00; request[7] = 0x01; // 0.0.0.1
            request[8] = 0x00; // USERID = empty
            domainBytes.CopyTo(request, 9);
            request[9 + domainBytes.Length] = 0x00; // null terminator
        }
        else
        {
            if (targetIp.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork)
                return Result.Failed("SOCKS4 in chain only supports IPv4");

            var ipBytes = targetIp.GetAddressBytes();
            request = new byte[9];
            request[0] = 0x04;
            request[1] = 0x01;
            request[2] = (byte)((port >> 8) & 0xFF);
            request[3] = (byte)(port & 0xFF);
            ipBytes.CopyTo(request, 4);
            request[8] = 0x00;
        }

        await stream.WriteAsync(request, ct).ConfigureAwait(false);
        await stream.FlushAsync(ct).ConfigureAwait(false);

        var response = new byte[8];
        await ReadExactAsync(stream, response, ct).ConfigureAwait(false);

        if (response[1] != 0x5A)
            return Result.Failed($"SOCKS4 CONNECT in chain failed: 0x{response[1]:X2}");

        return Result.Success();
    }

    // ── HTTP CONNECT через существующий поток ─────────────

    private static async Task<Result> HttpConnectThroughStreamAsync(
        NetworkStream stream, string host, int port, CancellationToken ct)
    {
        var requestStr = $"CONNECT {host}:{port} HTTP/1.1\r\nHost: {host}:{port}\r\n\r\n";
        var requestBytes = System.Text.Encoding.ASCII.GetBytes(requestStr);

        await stream.WriteAsync(requestBytes, ct).ConfigureAwait(false);
        await stream.FlushAsync(ct).ConfigureAwait(false);

        // Читать до \r\n\r\n
        var sb = new System.Text.StringBuilder();
        var readBuffer = new byte[4096];
        int totalRead = 0;

        while (totalRead < 65536)
        {
            int bytesRead = await stream.ReadAsync(readBuffer, ct).ConfigureAwait(false);
            if (bytesRead == 0)
                break;

            sb.Append(System.Text.Encoding.ASCII.GetString(readBuffer, 0, bytesRead));
            totalRead += bytesRead;

            if (sb.ToString().Contains("\r\n\r\n"))
                break;
        }

        var response = sb.ToString();
        var firstLine = response.Split("\r\n", StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();

        if (string.IsNullOrEmpty(firstLine))
            return Result.Failed("HTTP CONNECT in chain: empty response");

        var parts = firstLine.Split(' ', 3);
        if (parts.Length < 2 || !int.TryParse(parts[1], out var statusCode))
            return Result.Failed($"HTTP CONNECT in chain: invalid response: {firstLine}");

        if (statusCode != 200)
            return Result.Failed($"HTTP CONNECT in chain failed: {statusCode}");

        return Result.Success();
    }

    // ── Вспомогательные ──────────────────────────────────

    private static async Task ReadExactAsync(
        NetworkStream stream, byte[] buffer, CancellationToken ct)
    {
        int offset = 0;
        while (offset < buffer.Length)
        {
            int read = await stream.ReadAsync(buffer.AsMemory(offset, buffer.Length - offset), ct).ConfigureAwait(false);
            if (read == 0)
                throw new IOException("Connection closed prematurely in chain");
            offset += read;
        }
    }
}
