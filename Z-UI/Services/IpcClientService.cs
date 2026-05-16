// IpcClientService.cs - UI-side IPC wrapper with typed methods
// Wraps ZUI.Ipc.IpcPipeClient for DI-injected consumption by ViewModels/Services
using Microsoft.Extensions.Logging;
using ZUI.Ipc;
using ZUI.Models;

namespace ZUI.Services;

/// <summary>
/// UI-side IPC client interface.
/// Typed methods for all Worker requests + event subscriptions.
/// </summary>
public interface IIpcClientService
{
    /// <summary>Whether the IPC client is connected to the Worker.</summary>
    bool IsConnected { get; }

    /// <summary>Connect to Worker with auto-reconnect.</summary>
    Task ConnectAsync(CancellationToken ct = default);

    /// <summary>Disconnect from Worker.</summary>
    Task DisconnectAsync();

    // ── DPI Bypass ────────────────────────────────────────────

    Task<Result> StartBypassAsync(string strategyId, int gameFilterMode = 0, CancellationToken ct = default);
    Task<Result> StopBypassAsync(CancellationToken ct = default);
    Task<Result<BypassStatus?>> GetBypassStatusAsync(CancellationToken ct = default);
    Task<Result> SetGameFilterAsync(int gameFilterMode, CancellationToken ct = default);

    // ── Strategies ────────────────────────────────────────────

    Task<Result<List<StrategyInfo>>> GetAvailableStrategiesAsync(CancellationToken ct = default);

    // ── Proxifier ─────────────────────────────────────────────

    Task<Result> StartProxifierAsync(CancellationToken ct = default);
    Task<Result> StopProxifierAsync(CancellationToken ct = default);
    Task<Result<ProxifierStatus?>> GetProxifierStatusAsync(CancellationToken ct = default);

    // ── Proxy Server CRUD ─────────────────────────────────────

    Task<Result> AddProxyServerAsync(string name, string host, int port, string proxyType,
        string? username, string? password, string dnsPolicy, CancellationToken ct = default);
    Task<Result> RemoveProxyServerAsync(string serverId, CancellationToken ct = default);
    Task<Result> UpdateProxyServerAsync(string serverId, string? name, string? host, int? port,
        string? proxyType, string? username, string? password, string? dnsPolicy, CancellationToken ct = default);
    Task<ProxyProfileResponse?> GetProxyProfileAsync(bool includeRules = true, bool includeChains = true,
        CancellationToken ct = default);
    Task<CheckProxyResponse?> CheckProxyAsync(string host, int port, string proxyType,
        string? username, string? password, string? testUrl = null, CancellationToken ct = default);

    // ── Proxy Rule CRUD ───────────────────────────────────────

    Task<Result> AddProxyRuleAsync(string? processName, string? processNamePattern, int? processId,
        string? destinationIp, string? destinationPort, string? destinationDomain, string? destinationDomainPattern,
        string action, string? proxyServerId, string? chainName, string dnsPolicy, CancellationToken ct = default);
    Task<Result> RemoveProxyRuleAsync(string ruleId, CancellationToken ct = default);

    // ── Traffic Stats ────────────────────────────────────────

    Task<Result<TrafficStatsResponse?>> GetTrafficStatsAsync(CancellationToken ct = default);

    // ── Block Detection ─────────────────────────────────────

    Task<Result<BlockStatusResponse?>> GetBlockStatusAsync(CancellationToken ct = default);
    Task<Result> ClearBlocksAsync(CancellationToken ct = default);

    // ── Telegram Proxy ────────────────────────────────────────

    Task<Result> StartTgWsProxyAsync(int socks5Port, string wsUrl, string secret, CancellationToken ct = default);
    Task<Result> StopTgWsProxyAsync(CancellationToken ct = default);
    Task<Result> StartMtProxyAsync(int port, string secret, CancellationToken ct = default);
    Task<Result> StopMtProxyAsync(CancellationToken ct = default);
    Task<Result<TgProxyStatus?>> GetTgProxyStatusAsync(CancellationToken ct = default);

    // ── DNS ───────────────────────────────────────────────────

    Task<Result> ConfigureDnsAsync(bool enableDoh, bool enableFakeDns, CancellationToken ct = default);
    Task<Result<WorkerDnsStatus?>> GetDnsStatusAsync(CancellationToken ct = default);

    // ── Events from Worker ────────────────────────────────────

    event Action? OnConnected;
    event Action? OnDisconnected;
    event Action<PacketStatsEvent>? OnPacketStats;
    event Action<BypassStoppedEvent>? OnBypassStopped;
    event Action<LogEntryEvent>? OnLogEntry;
    event Action<TgProxyClientConnectedEvent>? OnTgProxyClientConnected;
    event Action<BlockDetectedEvent>? OnBlockDetected;
}

// ── Local result types (no dependency on ZUI.Core Result) ──

public readonly struct Result
{
    public bool IsSuccess { get; init; }
    public string? Error { get; init; }
    public static Result Success() => new() { IsSuccess = true };
    public static Result Failed(string error) => new() { IsSuccess = false, Error = error };
}

public readonly struct Result<T>
{
    public bool IsSuccess { get; init; }
    public T? Value { get; init; }
    public string? Error { get; init; }
    public static Result<T> Success(T value) => new() { IsSuccess = true, Value = value };
    public static Result<T> Failed(string error) => new() { IsSuccess = false, Error = error };
}

// ── Local status models (decoupled from IPC record types) ──

public sealed class BypassStatus
{
    public bool IsRunning { get; set; }
    public string? StrategyId { get; set; }
    public int GameFilterMode { get; set; }
    public long PacketsProcessed { get; set; }
    public long PacketsBypassed { get; set; }
    public double UptimeSeconds { get; set; }
}

public sealed class ProxifierStatus
{
    public bool IsRunning { get; set; }
    public int ActiveRules { get; set; }
    public int ActiveConnections { get; set; }
    public long TotalBytesSent { get; set; }
    public long TotalBytesReceived { get; set; }
}

public sealed class TgProxyStatus
{
    public bool Socks5Running { get; set; }
    public int Socks5Port { get; set; }
    public bool MtProxyRunning { get; set; }
    public int MtProxyPort { get; set; }
    public int ActiveConnections { get; set; }
}

public sealed class WorkerDnsStatus
{
    public bool DohEnabled { get; set; }
    public bool FakeDnsEnabled { get; set; }
    public int CachedEntries { get; set; }
    public int FakeDnsOverrides { get; set; }
    public bool SnifferRunning { get; set; }
    public long SnifferPackets { get; set; }
    public long SnifferRecords { get; set; }
}

/// <summary>
/// UI-side IPC client implementation.
/// Wraps IpcPipeClient with typed request/response methods.
/// </summary>
public sealed class IpcClientService : IIpcClientService, IAsyncDisposable
{
    private readonly IpcPipeClient _client;
    private readonly ILogger<IpcClientService> _logger;

    public bool IsConnected => _client.IsConnected;

    // ── Events ────────────────────────────────────────────────

    public event Action? OnConnected;
    public event Action? OnDisconnected;
    public event Action<PacketStatsEvent>? OnPacketStats;
    public event Action<BypassStoppedEvent>? OnBypassStopped;
    public event Action<LogEntryEvent>? OnLogEntry;
    public event Action<TgProxyClientConnectedEvent>? OnTgProxyClientConnected;
    public event Action<BlockDetectedEvent>? OnBlockDetected;

    public IpcClientService(ILogger<IpcClientService> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _client = new IpcPipeClient();

        _client.OnConnected += () => OnConnected?.Invoke();
        _client.OnDisconnected += () => OnDisconnected?.Invoke();
        _client.OnEventReceived += HandleEvent;
    }

    // ── Connection ────────────────────────────────────────────

    public async Task ConnectAsync(CancellationToken ct = default)
    {
        _logger.LogInformation("Connecting to Worker via IPC...");
        await _client.ConnectWithRetryAsync(ct).ConfigureAwait(false);
        _logger.LogInformation("Connected to Worker");
    }

    public async Task DisconnectAsync()
    {
        _logger.LogInformation("Disconnecting from Worker...");
        await _client.DisposeAsync().ConfigureAwait(false);
        _logger.LogInformation("Disconnected");
    }

    // ── DPI Bypass ────────────────────────────────────────────

    public async Task<Result> StartBypassAsync(string strategyId, int gameFilterMode = 0, CancellationToken ct = default)
    {
        return await SendRequestAsync(new StartBypassRequest(strategyId, gameFilterMode), ct).ConfigureAwait(false);
    }

    public async Task<Result> StopBypassAsync(CancellationToken ct = default)
    {
        return await SendRequestAsync(new StopBypassRequest(), ct).ConfigureAwait(false);
    }

    public async Task<Result<BypassStatus?>> GetBypassStatusAsync(CancellationToken ct = default)
    {
        var response = await SendRequestTypedAsync(new GetBypassStatusRequest(), ct).ConfigureAwait(false);
        if (!response.IsSuccess)
            return Result<BypassStatus?>.Failed(response.Error ?? "Request failed");

        if (response.Value is BypassStatusResponse bypass)
        {
            return Result<BypassStatus?>.Success(new BypassStatus
            {
                IsRunning = bypass.IsRunning,
                StrategyId = bypass.StrategyId,
                GameFilterMode = bypass.GameFilterMode,
                PacketsProcessed = bypass.PacketsProcessed,
                PacketsBypassed = bypass.PacketsBypassed,
                UptimeSeconds = bypass.UptimeSeconds
            });
        }

        if (response.Value is ErrorResponse err)
            return Result<BypassStatus?>.Failed(err.Message);

        return Result<BypassStatus?>.Failed("Unexpected response type");
    }

    public async Task<Result> SetGameFilterAsync(int gameFilterMode, CancellationToken ct = default)
    {
        return await SendRequestAsync(new SetGameFilterRequest(gameFilterMode), ct).ConfigureAwait(false);
    }

    // ── Strategies ────────────────────────────────────────────

    public async Task<Result<List<StrategyInfo>>> GetAvailableStrategiesAsync(CancellationToken ct = default)
    {
        var response = await SendRequestTypedAsync(new GetAvailableStrategiesRequest(), ct).ConfigureAwait(false);
        if (!response.IsSuccess)
            return Result<List<StrategyInfo>>.Failed(response.Error ?? "Request failed");

        // Worker returns SuccessResponse for now — strategies are loaded locally from JSON
        // In future, Worker may return a dedicated StrategiesResponse
        if (response.Value is SuccessResponse)
        {
            // Load from local JSON files
            return Result<List<StrategyInfo>>.Success(LoadLocalStrategies());
        }

        if (response.Value is ErrorResponse err)
            return Result<List<StrategyInfo>>.Failed(err.Message);

        return Result<List<StrategyInfo>>.Failed("Unexpected response type");
    }

    // ── Proxifier ─────────────────────────────────────────────

    public async Task<Result> StartProxifierAsync(CancellationToken ct = default)
    {
        return await SendRequestAsync(new StartProxifierRequest(), ct).ConfigureAwait(false);
    }

    public async Task<Result> StopProxifierAsync(CancellationToken ct = default)
    {
        return await SendRequestAsync(new StopProxifierRequest(), ct).ConfigureAwait(false);
    }

    public async Task<Result<ProxifierStatus?>> GetProxifierStatusAsync(CancellationToken ct = default)
    {
        var response = await SendRequestTypedAsync(new GetProxifierStatusRequest(), ct).ConfigureAwait(false);
        if (!response.IsSuccess)
            return Result<ProxifierStatus?>.Failed(response.Error ?? "Request failed");

        if (response.Value is ProxifierStatusResponse prox)
        {
            return Result<ProxifierStatus?>.Success(new ProxifierStatus
            {
                IsRunning = prox.IsRunning,
                ActiveRules = prox.ActiveRules,
                ActiveConnections = prox.ActiveConnections,
                TotalBytesSent = prox.TotalBytesSent,
                TotalBytesReceived = prox.TotalBytesReceived
            });
        }

        if (response.Value is ErrorResponse err)
            return Result<ProxifierStatus?>.Failed(err.Message);

        return Result<ProxifierStatus?>.Failed("Unexpected response type");
    }

    // ── Proxy Server CRUD ─────────────────────────────────────

    public async Task<Result> AddProxyServerAsync(string name, string host, int port, string proxyType,
        string? username, string? password, string dnsPolicy, CancellationToken ct = default)
    {
        return await SendRequestAsync(
            new AddProxyServerRequest(name, host, port, proxyType, username, password, dnsPolicy), ct)
            .ConfigureAwait(false);
    }

    public async Task<Result> RemoveProxyServerAsync(string serverId, CancellationToken ct = default)
    {
        return await SendRequestAsync(new RemoveProxyServerRequest(serverId), ct).ConfigureAwait(false);
    }

    public async Task<Result> UpdateProxyServerAsync(string serverId, string? name, string? host, int? port,
        string? proxyType, string? username, string? password, string? dnsPolicy, CancellationToken ct = default)
    {
        return await SendRequestAsync(
            new UpdateProxyServerRequest(serverId, name, host, port, proxyType, username, password, dnsPolicy), ct)
            .ConfigureAwait(false);
    }

    public async Task<ProxyProfileResponse?> GetProxyProfileAsync(bool includeRules = true, bool includeChains = true,
        CancellationToken ct = default)
    {
        var response = await SendRequestTypedAsync(
            new GetProxyProfileRequest(includeRules, includeChains), ct).ConfigureAwait(false);
        return response.IsSuccess ? response.Value as ProxyProfileResponse : null;
    }

    public async Task<CheckProxyResponse?> CheckProxyAsync(string host, int port, string proxyType,
        string? username, string? password, string? testUrl = null, CancellationToken ct = default)
    {
        var response = await SendRequestTypedAsync(
            new CheckProxyRequest(host, port, proxyType, username, password, testUrl), ct).ConfigureAwait(false);
        return response.IsSuccess ? response.Value as CheckProxyResponse : null;
    }

    // ── Proxy Rule CRUD ───────────────────────────────────────

    public async Task<Result> AddProxyRuleAsync(string? processName, string? processNamePattern, int? processId,
        string? destinationIp, string? destinationPort, string? destinationDomain, string? destinationDomainPattern,
        string action, string? proxyServerId, string? chainName, string dnsPolicy, CancellationToken ct = default)
    {
        return await SendRequestAsync(
            new AddProxyRuleRequest(processName, processNamePattern, processId, destinationIp, destinationPort,
                destinationDomain, destinationDomainPattern, action, proxyServerId, chainName, dnsPolicy), ct)
            .ConfigureAwait(false);
    }

    public async Task<Result> RemoveProxyRuleAsync(string ruleId, CancellationToken ct = default)
    {
        return await SendRequestAsync(new RemoveProxyRuleRequest(ruleId), ct).ConfigureAwait(false);
    }

    public async Task<Result<TrafficStatsResponse?>> GetTrafficStatsAsync(CancellationToken ct = default)
    {
        var response = await SendRequestTypedAsync(new GetTrafficStatsRequest(), ct).ConfigureAwait(false);
        if (!response.IsSuccess)
            return Result<TrafficStatsResponse?>.Failed(response.Error ?? "Request failed");

        if (response.Value is TrafficStatsResponse stats)
            return Result<TrafficStatsResponse?>.Success(stats);

        if (response.Value is ErrorResponse err)
            return Result<TrafficStatsResponse?>.Failed(err.Message);

        return Result<TrafficStatsResponse?>.Failed("Unexpected response type");
    }

    public async Task<Result<BlockStatusResponse?>> GetBlockStatusAsync(CancellationToken ct = default)
    {
        var response = await SendRequestTypedAsync(new GetBlockStatusRequest(), ct).ConfigureAwait(false);
        if (!response.IsSuccess)
            return Result<BlockStatusResponse?>.Failed(response.Error ?? "Request failed");

        if (response.Value is BlockStatusResponse status)
            return Result<BlockStatusResponse?>.Success(status);

        if (response.Value is ErrorResponse err)
            return Result<BlockStatusResponse?>.Failed(err.Message);

        return Result<BlockStatusResponse?>.Failed("Unexpected response type");
    }

    public async Task<Result> ClearBlocksAsync(CancellationToken ct = default)
    {
        return await SendRequestAsync(new ClearBlocksRequest(), ct).ConfigureAwait(false);
    }

    // ── Telegram Proxy ────────────────────────────────────────

    public async Task<Result> StartTgWsProxyAsync(int socks5Port, string wsUrl, string secret, CancellationToken ct = default)
    {
        return await SendRequestAsync(new StartTgWsProxyRequest(socks5Port, wsUrl, secret), ct).ConfigureAwait(false);
    }

    public async Task<Result> StopTgWsProxyAsync(CancellationToken ct = default)
    {
        return await SendRequestAsync(new StopTgWsProxyRequest(), ct).ConfigureAwait(false);
    }

    public async Task<Result> StartMtProxyAsync(int port, string secret, CancellationToken ct = default)
    {
        return await SendRequestAsync(new StartMtProxyRequest(port, secret), ct).ConfigureAwait(false);
    }

    public async Task<Result> StopMtProxyAsync(CancellationToken ct = default)
    {
        return await SendRequestAsync(new StopMtProxyRequest(), ct).ConfigureAwait(false);
    }

    public async Task<Result<TgProxyStatus?>> GetTgProxyStatusAsync(CancellationToken ct = default)
    {
        var response = await SendRequestTypedAsync(new GetTgProxyStatusRequest(), ct).ConfigureAwait(false);
        if (!response.IsSuccess)
            return Result<TgProxyStatus?>.Failed(response.Error ?? "Request failed");

        if (response.Value is TgProxyStatusResponse tg)
        {
            return Result<TgProxyStatus?>.Success(new TgProxyStatus
            {
                Socks5Running = tg.Socks5Running,
                Socks5Port = tg.Socks5Port,
                MtProxyRunning = tg.MtProxyRunning,
                MtProxyPort = tg.MtProxyPort,
                ActiveConnections = tg.ActiveConnections
            });
        }

        if (response.Value is ErrorResponse err)
            return Result<TgProxyStatus?>.Failed(err.Message);

        return Result<TgProxyStatus?>.Failed("Unexpected response type");
    }

    // ── DNS ───────────────────────────────────────────────────

    public async Task<Result> ConfigureDnsAsync(bool enableDoh, bool enableFakeDns, CancellationToken ct = default)
    {
        return await SendRequestAsync(new ConfigureDnsRequest(enableDoh, enableFakeDns), ct).ConfigureAwait(false);
    }

    public async Task<Result<WorkerDnsStatus?>> GetDnsStatusAsync(CancellationToken ct = default)
    {
        var response = await SendRequestTypedAsync(new GetDnsStatusRequest(), ct).ConfigureAwait(false);
        if (!response.IsSuccess)
            return Result<WorkerDnsStatus?>.Failed(response.Error ?? "Request failed");

        if (response.Value is DnsStatusResponse dns)
        {
            return Result<WorkerDnsStatus?>.Success(new WorkerDnsStatus
            {
                DohEnabled = dns.DohEnabled,
                FakeDnsEnabled = dns.FakeDnsEnabled,
                CachedEntries = dns.CachedEntries,
                FakeDnsOverrides = dns.FakeDnsOverrides,
                SnifferRunning = dns.SnifferRunning,
                SnifferPackets = dns.SnifferPackets,
                SnifferRecords = dns.SnifferRecords
            });
        }

        if (response.Value is ErrorResponse err)
            return Result<WorkerDnsStatus?>.Failed(err.Message);

        return Result<WorkerDnsStatus?>.Failed("Unexpected response type");
    }

    // ── Diagnostics ───────────────────────────────────────────

    public async Task<Result<List<DiagnosticResultItem>>> RunDiagnosticsAsync(CancellationToken ct = default)
    {
        var response = await SendRequestTypedAsync(new RunDiagnosticsRequest(), ct).ConfigureAwait(false);
        if (!response.IsSuccess)
            return Result<List<DiagnosticResultItem>>.Failed(response.Error ?? "Request failed");

        if (response.Value is DiagnosticResultsResponse diag)
        {
            return Result<List<DiagnosticResultItem>>.Success(diag.Results.ToList());
        }

        if (response.Value is ErrorResponse err)
            return Result<List<DiagnosticResultItem>>.Failed(err.Message);

        return Result<List<DiagnosticResultItem>>.Failed("Unexpected response type");
    }

    // ── Maintenance ───────────────────────────────────────────

    public async Task<Result> UpdateDomainListsAsync(CancellationToken ct = default)
    {
        return await SendRequestAsync(new UpdateDomainListsRequest(), ct).ConfigureAwait(false);
    }

    // ── IAsyncDisposable ──────────────────────────────────────

    public async ValueTask DisposeAsync()
    {
        await _client.DisposeAsync().ConfigureAwait(false);
    }

    // ── Internal helpers ──────────────────────────────────────

    private async Task<Result> SendRequestAsync(IpcRequest request, CancellationToken ct)
    {
        try
        {
            var result = await _client.SendRequestAsync(request, ct).ConfigureAwait(false);
            if (!result.IsSuccess)
                return Result.Failed(result.Error ?? "IPC request failed");

            if (result.Value is ErrorResponse err)
                return Result.Failed(err.Message);

            return Result.Success();
        }
            catch (IOException ex)
            {
                _logger.LogWarning(ex, "IPC request failed: {Type}", request.GetType().Name);
                return Result.Failed($"IPC error: {ex.Message}");
            }
            catch (TimeoutException ex)
            {
                _logger.LogDebug(ex, "IPC request timed out: {Type}", request.GetType().Name);
                return Result.Failed($"IPC timeout: {ex.Message}");
            }
            catch (ObjectDisposedException ex)
            {
                _logger.LogDebug(ex, "IPC request on disposed pipe: {Type}", request.GetType().Name);
                return Result.Failed($"IPC disconnected: {ex.Message}");
            }
    }

    private async Task<Result<IpcResponse>> SendRequestTypedAsync(IpcRequest request, CancellationToken ct)
    {
        try
        {
            var result = await _client.SendRequestAsync(request, ct).ConfigureAwait(false);
            if (!result.IsSuccess)
                return Result<IpcResponse>.Failed(result.Error ?? "IPC request failed");

            return Result<IpcResponse>.Success(result.Value!);
        }
        catch (IOException ex)
        {
            _logger.LogWarning(ex, "IPC typed request failed: {Type}", request.GetType().Name);
            return Result<IpcResponse>.Failed($"IPC error: {ex.Message}");
        }
        catch (TimeoutException ex)
        {
            _logger.LogDebug(ex, "IPC typed request timed out: {Type}", request.GetType().Name);
            return Result<IpcResponse>.Failed($"IPC timeout: {ex.Message}");
        }
        catch (ObjectDisposedException ex)
        {
            _logger.LogDebug(ex, "IPC typed request on disposed pipe: {Type}", request.GetType().Name);
 return Result<IpcResponse>.Failed($"IPC error: {ex.Message}");
 }
    }

    private void HandleEvent(IpcEvent evt)
    {
        switch (evt)
        {
            case PacketStatsEvent stats:
                OnPacketStats?.Invoke(stats);
                break;
            case BypassStoppedEvent stopped:
                OnBypassStopped?.Invoke(stopped);
                break;
            case LogEntryEvent log:
                OnLogEntry?.Invoke(log);
                break;
            case TgProxyClientConnectedEvent tg:
                OnTgProxyClientConnected?.Invoke(tg);
                break;
            case BlockDetectedEvent block:
                OnBlockDetected?.Invoke(block);
                break;
        }
    }

    /// <summary>
    /// Load strategy list from local JSON files.
    /// Strategies are stored alongside the BAT files in zapret/strategies/.
    /// </summary>
    private List<StrategyInfo> LoadLocalStrategies()
    {
        var strategies = new List<StrategyInfo>();
        var zapretDir = FindZapretDirectory();
        var strategiesPath = Path.Combine(zapretDir, "strategies");

        if (!Directory.Exists(strategiesPath))
        {
            _logger.LogWarning("Strategies directory not found: {Path}", strategiesPath);
            return strategies;
        }

        // Load JSON strategies (Phase 4 converted)
        foreach (var jsonFile in Directory.GetFiles(strategiesPath, "*.json"))
        {
            try
            {
                var name = Path.GetFileNameWithoutExtension(jsonFile);
                strategies.Add(new StrategyInfo
                {
                    Id = $"json-{name}",
                    Name = name,
                    Source = "JSON",
                    FilePath = jsonFile,
                    IsAvailable = true
                });
            }
 catch (IOException ex)
 {
 _logger.LogDebug(ex, "Failed to load JSON strategy: {Path}", jsonFile);
 }
 catch (UnauthorizedAccessException ex)
 {
 _logger.LogDebug(ex, "Failed to load JSON strategy: {Path}", jsonFile);
 }
        }

        return strategies;
    }

    private static string FindZapretDirectory()
    {
        var baseDir = AppContext.BaseDirectory;
        var candidates = new[]
        {
            Path.Combine(baseDir, "zapret"),
            Path.Combine(baseDir, "..", "zapret"),
        };

        foreach (var dir in candidates)
        {
            var fullPath = Path.GetFullPath(dir);
            if (Directory.Exists(fullPath))
                return fullPath;
        }

        return Path.Combine(baseDir, "zapret");
    }
}
