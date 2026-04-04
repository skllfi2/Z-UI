using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Security;
using System.Runtime.InteropServices;
using System.Security.Authentication;
using System.Text.RegularExpressions;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace ZUI.Services
{
    // ── Models ────────────────────────────────────────────────────────────────

    public enum TestMode { Standard, Dpi }

    public enum HttpStatus { Ok, Error, Unsupported, SslError }

    public enum DpiStatus { Ok, Fail, Unsupported, LikelyBlocked }

    public record TestTarget(string Name, string? Url, string? PingTarget)
    {
        public bool IsPingOnly => Url is null;
    }

    public record HttpResult(string Label, HttpStatus Status);

    public record PingResult(bool Success, double AvgMs);

    public record TargetResult(TestTarget Target, IReadOnlyList<HttpResult> Http, PingResult Ping);

    public record DpiLineResult(string Label, string Code, long SizeBytes, double SizeKb, DpiStatus Status);

    public record DpiTargetResult(string Id, string Provider, IReadOnlyList<DpiLineResult> Lines)
    {
        public bool HasBlocked => Lines.Any(l => l.Status == DpiStatus.LikelyBlocked);
    }

    public record ConfigResult(
        string ConfigName,
        IReadOnlyList<TargetResult> Standard,
        IReadOnlyList<DpiTargetResult> Dpi)
    {
        public int HttpOk     => Standard.SelectMany(t => t.Http).Count(h => h.Status == HttpStatus.Ok);
        public int HttpErr    => Standard.SelectMany(t => t.Http).Count(h => h.Status is HttpStatus.Error or HttpStatus.SslError);
        public int HttpUnsup  => Standard.SelectMany(t => t.Http).Count(h => h.Status == HttpStatus.Unsupported);
        public int PingOk     => Standard.Count(t => t.Ping.Success);
        public int DpiOk      => Dpi.SelectMany(d => d.Lines).Count(l => l.Status == DpiStatus.Ok);
        public int DpiBlocked => Dpi.SelectMany(d => d.Lines).Count(l => l.Status == DpiStatus.LikelyBlocked);
        public double Score => CalculateScore(TestMode.Standard);

        private double CalculateScore(TestMode mode)
        {
            if (mode == TestMode.Standard)
            {
                var httpOk = HttpOk;
                var httpErr = HttpErr;
                var httpUnsup = HttpUnsup;
                var pingOk = PingOk;
                var totalHttp = httpOk + httpErr + httpUnsup;
                var totalTests = totalHttp + (Standard.Count * 2); // включая пинги

                if (totalTests == 0) return 0;

                var httpScore = (httpOk + httpUnsup * 0.5) / totalHttp * 100;
                var pingScore = pingOk / Standard.Count * 100;
                return (httpScore + pingScore) / 2;
            }
            else
            {
                var dpiOk = DpiOk;
                var dpiBlocked = DpiBlocked;
                var totalDpi = dpiOk + dpiBlocked;

                if (totalDpi == 0) return 0;

                return dpiOk / totalDpi * 100;
            }
        }
    }

    public record TestProgress(int ConfigIndex, int TotalConfigs, string ConfigName, string Phase);

    // ── Preflight result ──────────────────────────────────────────────────────

    public record PreflightResult(bool Ok, bool IsAdmin, IReadOnlyList<string> Errors, IReadOnlyList<string> Warnings);

    // ── Ipset Manager ─────────────────────────────────────────────────────────

    public class IpsetManager(string listsDir)
    {
        private readonly string _listFile   = Path.Combine(listsDir, "ipset-all.txt");
        private readonly string _backupFile = Path.Combine(listsDir, "ipset-all.test-backup.txt");
        private readonly string _flagFile   = Path.Combine(Path.GetDirectoryName(listsDir)!, "ipset_switched.flag");

        public enum IpsetStatus { None, Any, Loaded }

        public IpsetStatus GetStatus()
        {
            if (!File.Exists(_listFile)) return IpsetStatus.None;
            var lines = File.ReadAllLines(_listFile)
                            .Where(l => !string.IsNullOrWhiteSpace(l)).ToArray();
            if (lines.Length == 0) return IpsetStatus.Any;
            return lines.Any(l => l.Contains("203.0.113.113/32"))
                ? IpsetStatus.None : IpsetStatus.Loaded;
        }

        public void SwitchToAny()
        {
            if (File.Exists(_listFile))
                File.Copy(_listFile, _backupFile, overwrite: true);
            else
                File.WriteAllText(_backupFile, "");

            File.WriteAllText(_listFile, "");
            File.WriteAllText(_flagFile, "");
        }

        public void Restore()
        {
            if (File.Exists(_backupFile))
                File.Move(_backupFile, _listFile, overwrite: true);
            TryDeleteFlag();
        }

        public bool RecoverIfNeeded()
        {
            if (!File.Exists(_flagFile)) return false;
            Restore();
            TryDeleteFlag();
            return true;
        }

        private void TryDeleteFlag() { try { File.Delete(_flagFile); } catch { } }
    }

    // ── Winws Process Controller ──────────────────────────────────────────────
    // Без WMI / System.Management — восстановление через AppState.CurrentStrategy

public class WinwsController(string rootDir)
{
    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr FindWindow(string? lpClassName, string? lpWindowName);
    
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool PostMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);
    
    private const uint WM_CLOSE = 0x0010;

    public void StopAll()
    {
        for (int i = 0; i < 5; i++)
        {
            foreach (var p in Process.GetProcessesByName("winws"))
            {
                try { p.Kill(true); } catch { }
            }
            
            foreach (var p in Process.GetProcessesByName("cmd"))
            {
                try { p.Kill(true); } catch { }
            }
            
            try
            {
                var hwnd = FindWindow(null, "zapret: general (ALT)");
                if (hwnd != IntPtr.Zero)
                    PostMessage(hwnd, WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
            }
            catch { }
            
            Thread.Sleep(300);
        }
    }

    public Process? StartConfig(string batFilePath)
    {
        var psi = new ProcessStartInfo("cmd.exe", $"/c \"{batFilePath}\"")
        {
            WorkingDirectory = rootDir,
            WindowStyle = ProcessWindowStyle.Hidden,
            UseShellExecute = true,
            CreateNoWindow = true
        };
        return Process.Start(psi);
    }

        /// <summary>
        /// Перезапускает текущую активную стратегию из AppState.CurrentStrategy.
        /// Не требует WMI. Если стратегия не задана — ничего не делает.
        /// </summary>
        public void RestoreCurrentStrategy(string strategiesDir)
        {
            var strategy = AppState.CurrentStrategy;
            if (string.IsNullOrEmpty(strategy)) return;

            // Пробуем с .bat и без
            var bat = Path.Combine(strategiesDir, strategy.EndsWith(".bat") ? strategy : strategy + ".bat");
            if (!File.Exists(bat)) return;

            try { StartConfig(bat); } catch { }
        }
    }

    // ── Curl Helper (только для DPI) ──────────────────────────────────────────

    internal static class CurlRunner
    {
        private static readonly string? _curlPath = FindCurl();
        public static bool IsAvailable => _curlPath is not null;

        public static async Task<(int ExitCode, string Output, string Stderr)> RunAsync(
            IEnumerable<string> args, int timeoutMs, CancellationToken ct)
        {
            if (_curlPath is null) return (-1, "", "curl.exe not found");

            using var proc = new Process
            {
                StartInfo = new ProcessStartInfo(_curlPath)
                {
                    UseShellExecute        = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError  = true,
                    CreateNoWindow         = true
                }
            };
            foreach (var a in args) proc.StartInfo.ArgumentList.Add(a);
            proc.Start();

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(timeoutMs);

            var stdoutTask = proc.StandardOutput.ReadToEndAsync(cts.Token);
            var stderrTask = proc.StandardError.ReadToEndAsync(cts.Token);

            try   { await proc.WaitForExitAsync(cts.Token); }
            catch { try { proc.Kill(true); } catch { } }

            var stdout = ""; var stderr = "";
            try { stdout = (await stdoutTask).Trim(); } catch { }
            try { stderr = (await stderrTask).Trim(); } catch { }

            return (proc.HasExited ? proc.ExitCode : -1, stdout, stderr);
        }

        private static string? FindCurl()
        {
            // Windows 10 1803+ имеет curl в System32
            var sys32 = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.System), "curl.exe");
            if (File.Exists(sys32)) return sys32;

            var path = Environment.GetEnvironmentVariable("PATH") ?? "";
            foreach (var dir in path.Split(';', StringSplitOptions.RemoveEmptyEntries))
            {
                var p = Path.Combine(dir.Trim(), "curl.exe");
                if (File.Exists(p)) return p;
            }
            return null;
        }
    }

    // ── Standard Tester — нативный HttpClient, curl НЕ нужен ─────────────────

    internal class StandardTester(int timeoutSec = 5, int maxParallel = 8)
    {
        // Три фиксированных профиля TLS
        private static readonly (string Label, SslProtocols Proto, bool ForceHttp11)[] _tests =
        [
            ("HTTP",   SslProtocols.Tls12 | SslProtocols.Tls13, true),
            ("TLS1.2", SslProtocols.Tls12, false),
            ("TLS1.3", SslProtocols.Tls13, false),
        ];

        public async Task<IReadOnlyList<TargetResult>> RunAsync(
            IEnumerable<TestTarget> targets, CancellationToken ct)
        {
            using var sem = new SemaphoreSlim(maxParallel);
            var tasks = targets.Select(async t =>
            {
                await sem.WaitAsync(ct);
                try   { return await TestTargetAsync(t, ct); }
                finally { sem.Release(); }
            });
            return await Task.WhenAll(tasks);
        }

        private async Task<TargetResult> TestTargetAsync(TestTarget t, CancellationToken ct)
        {
            var httpResults = new List<HttpResult>();

            if (!t.IsPingOnly)
            {
                foreach (var (label, proto, forceHttp11) in _tests)
                    httpResults.Add(await TestHttpAsync(t.Url!, label, proto, forceHttp11, ct));
            }

            var ping = t.PingTarget is not null
                ? await PingAsync(t.PingTarget, ct)
                : new PingResult(false, 0);

            return new TargetResult(t, httpResults, ping);
        }

        private async Task<HttpResult> TestHttpAsync(
            string url, string label,
            SslProtocols proto, bool forceHttp11,
            CancellationToken ct)
        {
            // Создаём отдельный handler на каждый запрос — нужен разный SslProtocols
            using var handler = new SocketsHttpHandler
            {
                ConnectTimeout = TimeSpan.FromSeconds(timeoutSec),
                SslOptions     = new SslClientAuthenticationOptions
                {
                    EnabledSslProtocols        = proto,
                    RemoteCertificateValidationCallback =
                        // Принимаем любой сертификат — нас интересует доступность, а не безопасность
                        (_, cert, chain, errors) => true
                }
            };

            using var client = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(timeoutSec)
            };

            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Head, url);
                if (forceHttp11) req.Version = new Version(1, 1);

                using var resp = await client.SendAsync(
                    req, HttpCompletionOption.ResponseHeadersRead, ct);

                return new HttpResult(label, HttpStatus.Ok);
            }
            catch (HttpRequestException ex)
            {
                // SSL-ошибка — отдельный статус для диагностики
                if (ex.Message.Contains("SSL", StringComparison.OrdinalIgnoreCase) ||
                    ex.Message.Contains("certificate", StringComparison.OrdinalIgnoreCase))
                    return new HttpResult(label, HttpStatus.SslError);

                return new HttpResult(label, HttpStatus.Error);
            }
            catch (PlatformNotSupportedException)
            {
                // TLS 1.3 не поддерживается ОС (Windows < 1903 / старый SChannel)
                return new HttpResult(label, HttpStatus.Unsupported);
            }
            catch (AuthenticationException)
            {
                // Запрошенная версия TLS не поддерживается SChannel
                return new HttpResult(label, HttpStatus.Unsupported);
            }
            catch (OperationCanceledException) { throw; }
            catch
            {
                return new HttpResult(label, HttpStatus.Error);
            }
        }

        private static async Task<PingResult> PingAsync(string host, CancellationToken ct)
        {
            using var ping = new Ping();
            var rtts = new List<double>();
            for (int i = 0; i < 3; i++)
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    var reply = await ping.SendPingAsync(host, 2000);
                    if (reply.Status == IPStatus.Success)
                        rtts.Add(reply.RoundtripTime);
                }
                catch { }
                if (i < 2) await Task.Delay(100, ct);
            }
            return rtts.Count > 0
                ? new PingResult(true, rtts.Average())
                : new PingResult(false, 0);
        }
    }

    // ── DPI Tester — curl обязателен (--range + пиннинг TLS) ─────────────────

    public class DpiTester(int timeoutSec = 5, int rangeBytes = 262144,
                              int warnMinKb = 14, int warnMaxKb = 22, int maxParallel = 8)
    {
        private const string SuiteUrl =
            "https://hyperion-cs.github.io/dpi-checkers/ru/tcp-16-20/suite.json";

        private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(15) };

        private static readonly (string Label, string[] Args)[] _tests =
        [
            ("HTTP",   ["--http1.1"]),
            ("TLS1.2", ["--tlsv1.2", "--tls-max", "1.2"]),
            ("TLS1.3", ["--tlsv1.3", "--tls-max", "1.3"]),
        ];

        public record DpiSuiteEntry(string Id, string Provider, string Url, int Times);

        public async Task<IReadOnlyList<DpiSuiteEntry>> LoadSuiteAsync()
        {
            try
            {
                var json = await _http.GetStringAsync(SuiteUrl);
                using var doc = JsonDocument.Parse(json);
                var list = new List<DpiSuiteEntry>();
                foreach (var el in doc.RootElement.EnumerateArray())
                {
                    var id       = el.GetProperty("id").GetString() ?? "";
                    var provider = el.GetProperty("provider").GetString() ?? "";
                    var url      = el.GetProperty("url").GetString() ?? "";
                    var times    = el.TryGetProperty("times", out var t) ? t.GetInt32() : 1;
                    list.Add(new DpiSuiteEntry(id, provider, url, times));
                }
                return list;
            }
            catch { return []; }
        }

        public async Task<IReadOnlyList<DpiTargetResult>> RunAsync(
            IReadOnlyList<DpiSuiteEntry> suite, CancellationToken ct)
        {
            var targets = suite
                .SelectMany(e => Enumerable.Range(0, Math.Max(1, e.Times))
                    .Select((_, i) => (
                        Id:       e.Times > 1 ? $"{e.Id}@{i}" : e.Id,
                        Provider: e.Provider,
                        Url:      e.Url)))
                .ToList();

            using var sem = new SemaphoreSlim(maxParallel);
            var tasks = targets.Select(async t =>
            {
                await sem.WaitAsync(ct);
                try   { return await TestDpiTargetAsync(t.Id, t.Provider, t.Url, ct); }
                finally { sem.Release(); }
            });
            return await Task.WhenAll(tasks);
        }

        private async Task<DpiTargetResult> TestDpiTargetAsync(
            string id, string provider, string url, CancellationToken ct)
        {
            var rangeSpec = $"0-{rangeBytes - 1}";
            var lines = new List<DpiLineResult>();

            foreach (var (label, extra) in _tests)
            {
                var args = new List<string>
                    { "-L", "--range", rangeSpec, "-m", timeoutSec.ToString(),
                      "-w", "%{http_code} %{size_download}", "-o", "NUL", "-s" };
                args.AddRange(extra);
                args.Add(url);

                var (exit, stdout, stderr) = await CurlRunner.RunAsync(
                    args, (timeoutSec + 5) * 1000, ct);

                var (code, sizeBytes) = ParseRangeOutput(stdout);
                var sizeKb = Math.Round(sizeBytes / 1024.0, 1);

                lines.Add(new DpiLineResult(label, code, sizeBytes, sizeKb,
                    ClassifyDpi(exit, code, sizeKb, stderr)));
            }

            return new DpiTargetResult(id, provider, lines);
        }

        private static (string Code, long SizeBytes) ParseRangeOutput(string stdout)
        {
            var m = Regex.Match(stdout.Trim(), @"^(\d{3})\s+(\d+)$");
            if (!m.Success) return ("NA", 0);
            return (m.Groups[1].Value, long.Parse(m.Groups[2].Value));
        }

        private DpiStatus ClassifyDpi(int exit, string code, double sizeKb, string stderr)
        {
            if (exit == 35 || Regex.IsMatch(stderr,
                @"not supported|unsupported|schannel|TLS.*not supported",
                RegexOptions.IgnoreCase))
                return DpiStatus.Unsupported;

            if (exit != 0 && sizeKb >= warnMinKb && sizeKb <= warnMaxKb)
                return DpiStatus.LikelyBlocked;

            if (exit != 0 || code is "ERR" or "NA")
                return DpiStatus.Fail;

            return DpiStatus.Ok;
        }
    }

    // ── Main Runner ───────────────────────────────────────────────────────────

    public class ZapretTestRunner
    {
        public event Action<string>?       Log;
        public event Action<TestProgress>? ProgressChanged;

        private readonly IpsetManager    _ipset;
        private readonly WinwsController _winws;
        private readonly StandardTester  _standard = new();
        private readonly DpiTester       _dpi      = new();
        private readonly string          _strategiesDir;

        public ZapretTestRunner(string rootDir, string listsDir, string strategiesDir)
        {
            _ipset         = new IpsetManager(listsDir);
            _winws         = new WinwsController(rootDir);
            _strategiesDir = strategiesDir;
        }

        // ── Preflight ─────────────────────────────────────────────────────────
        // Возвращает ошибки (блокируют запуск) и предупреждения (только инфо).
        // mode-aware: curl требуется только для DPI, admin — для обоих режимов
        // (winws нужен admin даже для Standard).

        public PreflightResult Preflight(TestMode mode)
        {
            var errors   = new List<string>();
            var warnings = new List<string>();

            var isAdmin = new System.Security.Principal.WindowsPrincipal(
                System.Security.Principal.WindowsIdentity.GetCurrent())
                .IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);

            if (!isAdmin)
                errors.Add("Требуются права администратора (winws использует WinDivert)");

            if (mode == TestMode.Dpi && !CurlRunner.IsAvailable)
                errors.Add("curl.exe не найден — DPI режим недоступен (нужен Windows 10 1803+ или curl в PATH)");

            if (IsZapretServiceInstalled())
                errors.Add("Служба 'zapret' установлена — удалите её перед тестом (service.bat → Remove Services)");

            return new PreflightResult(errors.Count == 0, isAdmin, errors, warnings);
        }

        private static bool IsZapretServiceInstalled()
        {
            try
            {
                return System.ServiceProcess.ServiceController
                    .GetServices()
                    .Any(s => s.ServiceName.Equals("zapret", StringComparison.OrdinalIgnoreCase));
            }
            catch { return false; }
        }

        // ── Config Discovery ──────────────────────────────────────────────────

        public IReadOnlyList<FileInfo> GetConfigs(string strategiesDir)
        {
            if (!Directory.Exists(strategiesDir)) return [];
            return Directory.GetFiles(strategiesDir, "general*.bat")
                .Select(f => new FileInfo(f))
                .OrderBy(f => Regex.Replace(f.Name, @"\d+", m => m.Value.PadLeft(8, '0')))
                .ToList();
        }

        // ── Target Loading ────────────────────────────────────────────────────

        public IReadOnlyList<TestTarget> LoadTargets(string targetsFile)
        {
            var raw = new Dictionary<string, string>();

            if (File.Exists(targetsFile))
            {
                foreach (var line in File.ReadLines(targetsFile))
                {
                    var t = line.Trim();
                    if (t.StartsWith('#') || string.IsNullOrEmpty(t)) continue;
                    var m = Regex.Match(t, @"^(\w+)\s*=\s*""(.+)""");
                    if (m.Success) raw[m.Groups[1].Value] = m.Groups[2].Value;
                }
            }

            if (raw.Count == 0)
            {
                raw["Discord Main"]   = "https://discord.com";
                raw["Discord CDN"]    = "https://cdn.discordapp.com";
                raw["YouTube Web"]    = "https://www.youtube.com";
                raw["YouTube Image"]  = "https://i.ytimg.com";
                raw["Google Main"]    = "https://www.google.com";
                raw["Cloudflare CDN"] = "https://cdnjs.cloudflare.com";
                raw["CF DNS 1.1.1.1"] = "PING:1.1.1.1";
                raw["CF DNS 1.0.0.1"] = "PING:1.0.0.1";
                raw["Google DNS"]     = "PING:8.8.8.8";
                raw["Quad9 DNS"]      = "PING:9.9.9.9";
            }

            return raw.Select(kv => ConvertTarget(kv.Key, kv.Value)).ToList();
        }

        private static TestTarget ConvertTarget(string name, string value)
        {
            if (value.StartsWith("PING:", StringComparison.OrdinalIgnoreCase))
                return new TestTarget(name, null, value[5..].Trim());
            var host = Regex.Replace(value, @"^https?://", "").Split('/')[0];
            return new TestTarget(name, value, host);
        }

        // ── Main Test Loop ────────────────────────────────────────────────────

        public async Task<IReadOnlyList<ConfigResult>> RunAsync(
            TestMode mode,
            IReadOnlyList<FileInfo> configs,
            IReadOnlyList<TestTarget> targets,
            IReadOnlyList<DpiTester.DpiSuiteEntry> suite,
            CancellationToken ct)
        {
            var results    = new List<ConfigResult>();
            var origStatus = _ipset.GetStatus();

            _ipset.RecoverIfNeeded();

            try
            {
                if (mode == TestMode.Dpi && origStatus != IpsetManager.IpsetStatus.Any)
                {
                    Emit("[INFO] Переключаю ipset → any для точных DPI-тестов...");
                    _ipset.SwitchToAny();
                }

                Environment.SetEnvironmentVariable("NO_UPDATE_CHECK", "1");

                for (int i = 0; i < configs.Count; i++)
                {
                    ct.ThrowIfCancellationRequested();
                    var cfg = configs[i];
                    ReportProgress(i + 1, configs.Count, cfg.Name, "Запуск стратегии...");
                    Emit($"\n── [{i + 1}/{configs.Count}] {cfg.Name} ──");

                    _winws.StopAll();
                    var proc = _winws.StartConfig(cfg.FullName);
                    Emit("  Инициализация (5 с)...");
                    try { await Task.Delay(5000, ct); }
                    catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
                    catch (OperationCanceledException) { /* spurious - continue */ }

                    ConfigResult result;
                    try
                    {
                        if (mode == TestMode.Standard)
                        {
                            ReportProgress(i + 1, configs.Count, cfg.Name, "HTTP + Ping тесты...");
                            var r = await _standard.RunAsync(targets, ct);
                            result = new ConfigResult(cfg.Name, r, []);
                            LogStandardResults(r);
                        }
                        else
                        {
                            ReportProgress(i + 1, configs.Count, cfg.Name, "DPI TCP 16-20 тесты...");
                            var r = await _dpi.RunAsync(suite, ct);
                            result = new ConfigResult(cfg.Name, [], r);
                            LogDpiResults(r);
                        }
                    }
                    catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
                    catch (Exception ex)
                    {
                        Emit($"  [ОШИБКА] Стратегия пропущена: {ex.Message}");
                        result = new ConfigResult(cfg.Name, [], []);
                    }

            results.Add(result);
            _winws.StopAll();
            
            if (proc is not null)
            {
                try 
                { 
                    if (!proc.HasExited)
                    {
                        proc.Kill(true);
                        proc.WaitForExit(3000);
                    }
                } 
                catch { }
            }
        }

        Emit("\n✓ Все тесты завершены");
        LogAnalytics(results, mode);
        
        _winws.StopAll();
    }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                Emit("[INFO] Тестирование остановлено пользователем");
            }
            catch (OperationCanceledException ex)
            {
                Emit($"[ОШИБКА] Неожиданная отмена: {ex.Message}");
            }
        finally
        {
            _winws.StopAll();
            
            // Даём время на закрытие окон
            Thread.Sleep(1000);
            _winws.StopAll();

            if (origStatus != IpsetManager.IpsetStatus.Any)
            {
                _ipset.Restore();
                Emit("[INFO] ipset восстановлен");
            }
        }

            return results;
        }

        // ── Best Config Selection ─────────────────────────────────────────────

        public static ConfigResult? FindBest(IReadOnlyList<ConfigResult> results, TestMode mode)
        {
            if (results.Count == 0) return null;
            return mode == TestMode.Standard
                ? results.OrderByDescending(r => r.HttpOk).ThenByDescending(r => r.PingOk).ThenBy(r => r.HttpErr).First()
                : results.OrderByDescending(r => r.DpiOk).ThenBy(r => r.DpiBlocked).First();
        }

        // ── Result Saving ─────────────────────────────────────────────────────

        public void SaveResults(IReadOnlyList<ConfigResult> results, TestMode mode, string outputDir)
        {
            Directory.CreateDirectory(outputDir);
            var path = Path.Combine(outputDir, $"test_results_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.txt");
            using var sw = new StreamWriter(path, false, System.Text.Encoding.UTF8);
            sw.WriteLine($"Zapret Test Results — {DateTime.Now:dd.MM.yyyy HH:mm:ss}");
            sw.WriteLine($"Mode: {mode}");
            sw.WriteLine(new string('─', 60));

            foreach (var r in results)
            {
                sw.WriteLine($"\nConfig: {r.ConfigName}");
                if (mode == TestMode.Standard)
                    foreach (var t in r.Standard)
                    {
                        var http = string.Join("  ", t.Http.Select(h => $"{h.Label}:{h.Status}"));
                        var ping = t.Ping.Success ? $"{t.Ping.AvgMs:F0} ms" : "Timeout";
                        sw.WriteLine($"  {t.Target.Name,-30} {http,-50} Ping: {ping}");
                    }
                else
                    foreach (var d in r.Dpi)
                    {
                        sw.WriteLine($"  {d.Id} [{d.Provider}]");
                        foreach (var l in d.Lines)
                            sw.WriteLine($"    {l.Label}: code={l.Code} size={l.SizeKb} KB status={l.Status}");
                    }
            }

            sw.WriteLine("\n" + new string('─', 60));
            sw.WriteLine("ANALYTICS");
            foreach (var r in results)
                sw.WriteLine(mode == TestMode.Standard
                    ? $"  {r.ConfigName}: OK={r.HttpOk} ERR={r.HttpErr} UNSUP={r.HttpUnsup} PingOK={r.PingOk}"
                    : $"  {r.ConfigName}: OK={r.DpiOk} BLOCKED={r.DpiBlocked}");
            sw.WriteLine($"\nBest: {FindBest(results, mode)?.ConfigName ?? "n/a"}");

            Emit($"✓ Результаты сохранены: {path}");
        }

        // ── Private helpers ───────────────────────────────────────────────────

        private void Emit(string msg) => Log?.Invoke(msg);

        private void ReportProgress(int idx, int total, string name, string phase) =>
            ProgressChanged?.Invoke(new TestProgress(idx, total, name, phase));

        private void LogStandardResults(IReadOnlyList<TargetResult> results)
        {
            int maxLen = results.Max(r => r.Target.Name.Length);
            foreach (var t in results)
            {
                if (t.Target.IsPingOnly)
                    Emit($"  {t.Target.Name.PadRight(maxLen)}  Ping: {(t.Ping.Success ? $"{t.Ping.AvgMs:F0} ms" : "Timeout")}");
                else
                {
                    var http = string.Join("  ", t.Http.Select(h => $"{h.Label}:{StatusIcon(h.Status)}"));
                    var ping = t.Ping.Success ? $"{t.Ping.AvgMs:F0} ms" : "Timeout";
                    Emit($"  {t.Target.Name.PadRight(maxLen)}  {http}  Ping: {ping}");
                }
            }
        }

        private void LogDpiResults(IReadOnlyList<DpiTargetResult> results)
        {
            foreach (var d in results)
            {
                Emit($"  {d.Id} [{d.Provider}]");
                foreach (var l in d.Lines)
                    Emit($"    {l.Label}: {l.Code} {l.SizeKb} KB → {l.Status}");
                if (d.HasBlocked)
                    Emit("    ⚠ Паттерн TCP 16-20 KB — цензор режет стратегию");
            }
        }

        private void LogAnalytics(IReadOnlyList<ConfigResult> results, TestMode mode)
        {
            Emit("\n── Аналитика ──");
            foreach (var r in results)
                Emit(mode == TestMode.Standard
                    ? $"  {r.ConfigName}: OK={r.HttpOk} ERR={r.HttpErr} UNSUP={r.HttpUnsup} PingOK={r.PingOk}"
                    : $"  {r.ConfigName}: OK={r.DpiOk} BLOCKED={r.DpiBlocked}");
            var best = FindBest(results, mode);
            if (best is not null) Emit($"\n★ Лучший конфиг: {best.ConfigName}");
        }

        private static string StatusIcon(HttpStatus s) => s switch
        {
            HttpStatus.Ok          => "OK   ",
            HttpStatus.Unsupported => "UNSUP",
            HttpStatus.SslError    => "SSL  ",
            _                      => "ERROR",
        };
    }
}
