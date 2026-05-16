// ═══════════════════════════════════════════════════════════════
// ZUI.Worker / Program.cs
// Точка входа Worker Service (SYSTEM)
// Windows Service hosting + DI + logging
// ═══════════════════════════════════════════════════════════════

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ZUI.Core.Desync;
using ZUI.Core.Dns;
using ZUI.Core.Engine;
using ZUI.Core.Intercept;
using ZUI.Core.Rules;
using ZUI.Core.WinDivert;
using ZUI.Ipc;
using ZUI.Proxy;
using ZUI.Proxy.Chain;
using ZUI.Proxy.Client;
using ZUI.Proxy.Profile;
using ZUI.Proxy.Rules;
using ZUI.Core.Traffic;
using ZUI.Telegram.MtProto;
using ZUI.Telegram.MtProxy;
using ZUI.Telegram.Socks5;
using ZUI.Telegram.WebSocket;
using ZUI.Worker;

// ── Построение хоста ───────────────────────────────────────

var builder = Host.CreateApplicationBuilder(args);

// Windows Service конфигурация
builder.Services.AddWindowsService(options =>
{
    options.ServiceName = "Z-UI Worker";
});

// ── Регистрация сервисов в DI ──────────────────────────────

// IPC
builder.Services.AddSingleton<IpcPipeServer>();

// ZUI.Core — WinDivert
builder.Services.AddSingleton<WinDivertInterceptor>();

// ZUI.Core — Intercept
builder.Services.AddSingleton<PidMapper>(); // SniParser, L7ProtocolDetector — static, не в DI

// ZUI.Core — Rules
builder.Services.AddSingleton<BatStrategyConverter>();
builder.Services.AddSingleton<StrategyConfigLoader>();
builder.Services.AddSingleton<DomainListLoader>();
builder.Services.AddSingleton<RuleMatcher>();

// ZUI.Core — Engine
builder.Services.AddSingleton<ConnectionTracker>();
builder.Services.AddSingleton<DpiBypassEngine>();
builder.Services.AddSingleton<PacketInterceptor>();
builder.Services.AddSingleton<PassiveBlockAnalyzer>();

// ZUI.Core — Desync (FakePacketModifier, TcpSplitter, PacketFragmenter — static, не в DI)
// FakePacketBuilder требует zapretDir — определяем через WorkerService.FindZapretDirectory()
builder.Services.AddSingleton(sp =>
{
    var zapretDir = WorkerService.FindZapretDirectory();
    return new FakePacketBuilder(zapretDir);
});

// ZUI.Core — DNS
builder.Services.AddSingleton<DnsCache>();
builder.Services.AddSingleton<DohResolver>();
builder.Services.AddSingleton<FakeDnsResponder>();
builder.Services.AddSingleton<HostsFileManager>();
builder.Services.AddSingleton(new DnsProxyConfig()); // defaults: 127.0.0.1:53, DoH off, FakeDNS off
builder.Services.AddSingleton<DnsProxyService>();
builder.Services.AddSingleton<DnsSniffer>();

// ZUI.Proxy — Proxifier (Phase 7)
builder.Services.AddSingleton<Socks5Client>();
builder.Services.AddSingleton<Socks4Client>();
builder.Services.AddSingleton<HttpConnectClient>();
builder.Services.AddSingleton<ChainExecutor>();
builder.Services.AddSingleton<TrafficMonitor>();
builder.Services.AddSingleton<DnsReverseCache>();
builder.Services.AddSingleton<ProxyProfileManager>();
builder.Services.AddSingleton<RuleEvaluator>();
builder.Services.AddSingleton<TcpRelay>();
// ProxifierEngine получает отдельный WinDivertInterceptor (свой WinDivert handle для SYN фильтра)
builder.Services.AddSingleton(sp =>
{
    var synInterceptor = new WinDivertInterceptor(sp.GetService<ILogger<WinDivertInterceptor>>());
return new ProxifierEngine(
    sp.GetRequiredService<PidMapper>(),
    sp.GetRequiredService<RuleEvaluator>(),
    sp.GetRequiredService<TcpRelay>(),
    sp.GetRequiredService<TrafficMonitor>(),
    sp.GetRequiredService<ProxyProfileManager>(),
    sp.GetRequiredService<Socks5Client>(),
    sp.GetRequiredService<Socks4Client>(),
    sp.GetRequiredService<HttpConnectClient>(),
    sp.GetRequiredService<ChainExecutor>(),
    synInterceptor,
    sp.GetRequiredService<DnsReverseCache>(),
    sp.GetService<ILogger<ProxifierEngine>>());
});

// ZUI.Telegram — Telegram Proxy (Phase 8)
// WsTunnelClient создаётся первым (без состояния, конфиг обновляется через IPC)
builder.Services.AddSingleton<WsTunnelClient>();
// Socks5Server: порт 0 при создании (реальный порт задаётся через StartAsync(port) из IPC запроса)
builder.Services.AddSingleton(sp =>
{
    var wsTunnel = sp.GetRequiredService<WsTunnelClient>();
    var logger = sp.GetService<ILogger<Socks5Server>>();
    return new Socks5Server(port: 0, wsTunnel, WsTunnelConfig.Disabled, logger: logger);
});
// MtProxyServer: порт 0 + placeholder-секрет при создании (реальные значения задаются через StartAsync(port, secret) из IPC запроса)
builder.Services.AddSingleton(sp =>
{
    var logger = sp.GetService<ILogger<MtProxyServer>>();
    return new MtProxyServer(port: 0, SecretConfig.GenerateRandom(), logger: logger);
});

    // Orchestrator + Worker
builder.Services.AddSingleton<Orchestrator>();
builder.Services.AddSingleton<IHostedService, WorkerService>();

// ── Логирование ────────────────────────────────────────────

builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.SetMinimumLevel(LogLevel.Information);

// ── Запуск ─────────────────────────────────────────────────

var host = builder.Build();

var logger = host.Services.GetRequiredService<ILogger<Program>>();
logger.LogInformation("Z-UI Worker Service starting...");

await host.RunAsync();
