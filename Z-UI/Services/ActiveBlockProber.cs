// ═══════════════════════════════════════════════════════════════
// Z-UI / Services / ActiveBlockProber.cs
// Активный анализатор блокировок (UI-side)
// DNS/TCP/HTTP/TLS пробы для определения типа блокировки
// Методология из dpi-detector (Runnin4ik) — нативная C# реализация
// ═══════════════════════════════════════════════════════════════

using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.Logging;

namespace ZUI.Services;

/// <summary>
/// Результат активной пробы домена.
/// </summary>
public sealed class ProbeResult
{
    /// <summary>Проверенный домен.</summary>
    public string Domain { get; init; } = string.Empty;

    /// <summary>Тип пробы.</summary>
    public ProbeType Type { get; init; }

    /// <summary>Успешна ли проба?</summary>
    public bool Success { get; init; }

    /// <summary>Задержка (ms).</summary>
    public long LatencyMs { get; init; }

    /// <summary>Локальный IP (DNS probe).</summary>
    public string? LocalIp { get; init; }

    /// <summary>DoH IP (DNS probe).</summary>
    public string? DohIp { get; init; }

    /// <summary>Есть ли mismatch DNS?</summary>
    public bool DnsMismatch => LocalIp != null && DohIp != null && !LocalIp.Equals(DohIp, StringComparison.OrdinalIgnoreCase);

    /// <summary>HTTP статус код (HTTP probe).</summary>
    public int? HttpStatusCode { get; init; }

    /// <summary>SNI из TLS (TLS probe).</summary>
    public string? ServerName { get; init; }

    /// <summary>Ошибка (если есть).</summary>
    public string? Error { get; init; }

    /// <summary>Описание результата.</summary>
    public string Description
    {
        get
        {
            if (!Success)
                return $"{Type} probe failed: {Error}";

            return Type switch
            {
                ProbeType.Dns => DnsMismatch
                    ? $"DNS mismatch: local={LocalIp}, DoH={DohIp}"
                    : $"DNS OK: {LocalIp}",
                ProbeType.Tcp => $"TCP OK: {LatencyMs}ms",
                ProbeType.Http => $"HTTP {(HttpStatusCode ?? 0)}: {LatencyMs}ms",
                ProbeType.Tls => $"TLS OK: SNI={ServerName}, {LatencyMs}ms",
                _ => "Unknown",
            };
        }
    }
}

/// <summary>
/// Тип пробы.
/// </summary>
public enum ProbeType
{
    /// <summary>DNS резолв (локальный vs DoH).</summary>
    Dns,

    /// <summary>TCP соединение (порт 80/443).</summary>
    Tcp,

    /// <summary>HTTP запрос (GET /).</summary>
    Http,

    /// <summary>TLS handshake (SNI check).</summary>
    Tls,
}

/// <summary>
/// Активный анализатор блокировок.
/// 
/// Выполняет пробы доменов для определения типа блокировки:
/// - DNS probe: сравнение локального и DoH резолва
/// - TCP probe: проверка TCP соединения (port 80/443)
/// - HTTP probe: проверка HTTP ответа
/// - TLS probe: проверка TLS handshake и SNI
/// 
/// Методология из dpi-detector (Runnin4ik) — нативная C# реализация.
/// </summary>
public sealed class ActiveBlockProber
{
    private const string DefaultDohServer = "1.1.1.1";
    private const int DohPort = 443;
    private const int TcpTimeoutMs = 5000;
    private const int HttpTimeoutMs = 10000;

    private readonly ILogger<ActiveBlockProber> _logger;
    private readonly HttpClient _httpClient;

    public ActiveBlockProber(ILogger<ActiveBlockProber> logger)
    {
        _logger = logger;

        var handler = new HttpClientHandler
        {
            AllowAutoRedirect = false,
            UseCookies = false,
        };

        _httpClient = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromMilliseconds(HttpTimeoutMs),
        };
    }

    // ── DNS Probe ──────────────────────────────────────────

    /// <summary>
    /// Сравнить локальный DNS резолв с DoH резолвом.
    /// </summary>
    public async Task<ProbeResult> ProbeDnsAsync(string domain, CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();

        try
        {
            // Локальный резолв
            string? localIp = null;
            try
            {
                var addresses = await Dns.GetHostAddressesAsync(domain, ct).ConfigureAwait(false);
                localIp = addresses.FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork)?.ToString();
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Local DNS resolution failed for {Domain}", domain);
            }

            // DoH резолв (Cloudflare)
            string? dohIp = null;
            try
            {
                dohIp = await ResolveViaDohAsync(domain, ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "DoH resolution failed for {Domain}", domain);
            }

            sw.Stop();

            var success = localIp != null || dohIp != null;

            return new ProbeResult
            {
                Domain = domain,
                Type = ProbeType.Dns,
                Success = success,
                LatencyMs = sw.ElapsedMilliseconds,
                LocalIp = localIp,
                DohIp = dohIp,
            };
        }
        catch (Exception ex)
        {
            return new ProbeResult
            {
                Domain = domain,
                Type = ProbeType.Dns,
                Success = false,
                LatencyMs = sw.ElapsedMilliseconds,
                Error = ex.Message,
            };
        }
    }

    // ── TCP Probe ──────────────────────────────────────────

    /// <summary>
    /// Проверить TCP соединение с доменом (порт 443).
    /// </summary>
    public async Task<ProbeResult> ProbeTcpAsync(string domain, int port = 443, CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();

        try
        {
            using var client = new TcpClient();
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TcpTimeoutMs);

            await client.ConnectAsync(domain, port, cts.Token).ConfigureAwait(false);

            sw.Stop();

            return new ProbeResult
            {
                Domain = domain,
                Type = ProbeType.Tcp,
                Success = true,
                LatencyMs = sw.ElapsedMilliseconds,
            };
        }
        catch (OperationCanceledException)
        {
            sw.Stop();
            return new ProbeResult
            {
                Domain = domain,
                Type = ProbeType.Tcp,
                Success = false,
                LatencyMs = sw.ElapsedMilliseconds,
                Error = "Connection timeout",
            };
        }
        catch (SocketException ex)
        {
            sw.Stop();
            return new ProbeResult
            {
                Domain = domain,
                Type = ProbeType.Tcp,
                Success = false,
                LatencyMs = sw.ElapsedMilliseconds,
                Error = $"{ex.SocketErrorCode}: {ex.Message}",
            };
        }
        catch (Exception ex)
        {
            sw.Stop();
            return new ProbeResult
            {
                Domain = domain,
                Type = ProbeType.Tcp,
                Success = false,
                LatencyMs = sw.ElapsedMilliseconds,
                Error = ex.Message,
            };
        }
    }

    // ── HTTP Probe ─────────────────────────────────────────

    /// <summary>
    /// Проверить HTTP ответ (GET /).
    /// </summary>
    public async Task<ProbeResult> ProbeHttpAsync(string domain, CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();

        try
        {
            var url = $"https://{domain}/";
            var response = await _httpClient.GetAsync(url, ct).ConfigureAwait(false);

            sw.Stop();

            return new ProbeResult
            {
                Domain = domain,
                Type = ProbeType.Http,
                Success = true,
                LatencyMs = sw.ElapsedMilliseconds,
                HttpStatusCode = (int)response.StatusCode,
            };
        }
        catch (OperationCanceledException)
        {
            sw.Stop();
            return new ProbeResult
            {
                Domain = domain,
                Type = ProbeType.Http,
                Success = false,
                LatencyMs = sw.ElapsedMilliseconds,
                Error = "Request timeout",
            };
        }
        catch (HttpRequestException ex)
        {
            sw.Stop();
            return new ProbeResult
            {
                Domain = domain,
                Type = ProbeType.Http,
                Success = false,
                LatencyMs = sw.ElapsedMilliseconds,
                Error = ex.Message,
            };
        }
        catch (Exception ex)
        {
            sw.Stop();
            return new ProbeResult
            {
                Domain = domain,
                Type = ProbeType.Http,
                Success = false,
                LatencyMs = sw.ElapsedMilliseconds,
                Error = ex.Message,
            };
        }
    }

    // ── TLS Probe ──────────────────────────────────────────

    /// <summary>
    /// Проверить TLS handshake и извлечь SNI.
    /// </summary>
    public async Task<ProbeResult> ProbeTlsAsync(string domain, int port = 443, CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();

        try
        {
            using var client = new TcpClient();
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TcpTimeoutMs);

            await client.ConnectAsync(domain, port, cts.Token).ConfigureAwait(false);

            string? serverName = null;
            X509Certificate2? cert = null;

            using var sslStream = new SslStream(client.GetStream(), false, ValidateServerCertificate);

            await sslStream.AuthenticateAsClientAsync(new SslClientAuthenticationOptions
            {
                TargetHost = domain,
            }, ct).ConfigureAwait(false);

            cert = new X509Certificate2(sslStream.RemoteCertificate!);
            serverName = cert.GetNameInfo(X509NameType.DnsName, false);

            sw.Stop();

            return new ProbeResult
            {
                Domain = domain,
                Type = ProbeType.Tls,
                Success = true,
                LatencyMs = sw.ElapsedMilliseconds,
                ServerName = serverName,
            };
        }
        catch (OperationCanceledException)
        {
            sw.Stop();
            return new ProbeResult
            {
                Domain = domain,
                Type = ProbeType.Tls,
                Success = false,
                LatencyMs = sw.ElapsedMilliseconds,
                Error = "TLS handshake timeout",
            };
        }
        catch (AuthenticationException ex)
        {
            sw.Stop();
            return new ProbeResult
            {
                Domain = domain,
                Type = ProbeType.Tls,
                Success = false,
                LatencyMs = sw.ElapsedMilliseconds,
                Error = $"TLS auth failed: {ex.Message}",
            };
        }
        catch (Exception ex)
        {
            sw.Stop();
            return new ProbeResult
            {
                Domain = domain,
                Type = ProbeType.Tls,
                Success = false,
                LatencyMs = sw.ElapsedMilliseconds,
                Error = ex.Message,
            };
        }
    }

    // ── Full Probe ─────────────────────────────────────────

    /// <summary>
    /// Выполнить все пробы для домена (DNS → TCP → HTTP → TLS).
    /// </summary>
    public async Task<ProbeResult[]> ProbeAllAsync(string domain, CancellationToken ct = default)
    {
        var results = new List<ProbeResult>();

        results.Add(await ProbeDnsAsync(domain, ct).ConfigureAwait(false));

        if (ct.IsCancellationRequested) return results.ToArray();

        results.Add(await ProbeTcpAsync(domain, ct: ct).ConfigureAwait(false));

        if (ct.IsCancellationRequested) return results.ToArray();

        results.Add(await ProbeHttpAsync(domain, ct).ConfigureAwait(false));

        if (ct.IsCancellationRequested) return results.ToArray();

        results.Add(await ProbeTlsAsync(domain, ct: ct).ConfigureAwait(false));

        return results.ToArray();
    }

    // ── Helpers ────────────────────────────────────────────

    private static async Task<string?> ResolveViaDohAsync(string domain, CancellationToken ct)
    {
        using var client = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(5),
        };

        // Cloudflare DoH: https://1.1.1.1/dns-query?name=domain&type=A
        var url = $"https://{DefaultDohServer}/dns-query?name={domain}&type=A";

        client.DefaultRequestHeaders.Accept.Add(
            new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/dns-json"));

        var response = await client.GetAsync(url, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

        // Простой парсинг JSON ответа DoH
        // Формат: {"Status":0,"Answer":[{"name":"domain","type":1,"TTL":300,"data":"1.2.3.4"}]}
        if (json.Contains("\"Answer\":") && json.Contains("\"data\":"))
        {
            var dataStart = json.IndexOf("\"data\":", StringComparison.Ordinal) + 7;
            var dataEnd = json.IndexOf('"', dataStart + 1);
            if (dataEnd > dataStart)
                return json.Substring(dataStart + 1, dataEnd - dataStart - 1);
        }

        return null;
    }

    private static bool ValidateServerCertificate(object sender, X509Certificate? certificate,
        X509Chain? chain, SslPolicyErrors sslPolicyErrors)
    {
        // Принимаем любые сертификаты для пробы
        return true;
    }

    public void Dispose()
    {
        _httpClient.Dispose();
    }
}
