// ═══════════════════════════════════════════════════════════════
// ZUI.Proxy / Client / HttpConnectClient.cs
// HTTP CONNECT прокси-клиент
// Устанавливает TCP туннель через HTTP прокси (RFC 7231 Section 4.3.6)
// Поддержка: Basic authentication
// ═══════════════════════════════════════════════════════════════

using System.Net;
using System.Net.Sockets;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ZUI.Core;

namespace ZUI.Proxy.Client;

/// <summary>
/// HTTP CONNECT прокси-клиент.
/// Создаёт TCP туннель через HTTP/HTTPS прокси.
/// </summary>
public sealed class HttpConnectClient
{
    private readonly ILogger _logger;

    public HttpConnectClient(ILogger<HttpConnectClient>? logger = null)
    {
        _logger = logger ?? NullLoggerFactory.Instance.CreateLogger<HttpConnectClient>();
    }

    /// <summary>
    /// Подключиться к целевому хосту через HTTP CONNECT прокси.
    /// </summary>
    public async Task<Result<TcpClient>> ConnectAsync(
        Rules.ProxyTarget proxy,
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
    /// Подключиться к целевому хосту через HTTP CONNECT прокси.
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

            // 2. Отправить CONNECT запрос
            var connectRequest = BuildConnectRequest(targetHost, targetPort, auth);
            var requestBytes = Encoding.ASCII.GetBytes(connectRequest);
            await stream.WriteAsync(requestBytes, ct).ConfigureAwait(false);
            await stream.FlushAsync(ct).ConfigureAwait(false);

            // 3. Прочитать ответ
            var responseBuffer = new byte[4096];
            var sb = new StringBuilder();
            int totalRead = 0;
            var readBuffer = new byte[4096];

            // Читаем до \r\n\r\n (конец HTTP заголовков)
            while (totalRead < 65536) // Защита от бесконечного чтения
            {
                int bytesRead = await stream.ReadAsync(readBuffer, ct).ConfigureAwait(false);
                if (bytesRead == 0)
                    break;

                sb.Append(Encoding.ASCII.GetString(readBuffer, 0, bytesRead));
                totalRead += bytesRead;

                if (sb.ToString().Contains("\r\n\r\n"))
                    break;
            }

            var response = sb.ToString();

            // 4. Разобрать HTTP статус
            var firstLine = response.Split("\r\n", StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
            if (string.IsNullOrEmpty(firstLine))
            {
                tcpClient.Close();
                return Result<TcpClient>.Failed("HTTP CONNECT: empty response from proxy");
            }

            // Формат: HTTP/1.x STATUS_CODE REASON
            var parts = firstLine.Split(' ', 3);
            if (parts.Length < 2 || !int.TryParse(parts[1], out var statusCode))
            {
                tcpClient.Close();
                return Result<TcpClient>.Failed($"HTTP CONNECT: invalid response: {firstLine}");
            }

            if (statusCode != 200)
            {
                tcpClient.Close();
                return Result<TcpClient>.Failed($"HTTP CONNECT failed: {statusCode} {parts.ElementAtOrDefault(2)}");
            }

            // 5. Успех — соединение установлено, поток готов к релею
            _logger.LogDebug("HTTP CONNECT tunnel: {Proxy} → {Target}:{Port}", proxyHost, targetHost, targetPort);
            return Result<TcpClient>.Success(tcpClient);
        }
        catch (SocketException ex)
        {
            return Result<TcpClient>.Failed($"HTTP CONNECT connection error: {ex.Message}");
        }
    }

    // ── Формирование CONNECT запроса ──────────────────────

    private static string BuildConnectRequest(string targetHost, int targetPort, ProxyAuth auth)
    {
        var sb = new StringBuilder();
        sb.Append($"CONNECT {targetHost}:{targetPort} HTTP/1.1\r\n");
        sb.Append($"Host: {targetHost}:{targetPort}\r\n");

        // Proxy-Authorization (Basic)
        if (auth.RequiresAuth && !string.IsNullOrEmpty(auth.Username) && !string.IsNullOrEmpty(auth.Password))
        {
            var credentials = Convert.ToBase64String(
                Encoding.ASCII.GetBytes($"{auth.Username}:{auth.Password}"));
            sb.Append($"Proxy-Authorization: Basic {credentials}\r\n");
        }

        sb.Append("\r\n");
        return sb.ToString();
    }
}
