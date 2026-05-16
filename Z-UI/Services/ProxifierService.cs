// ProxifierService.cs - UI-side service for per-app proxy routing (Proxifier)
// Thin IPC wrapper — actual logic runs in Worker (ZUI.Proxy)

using Microsoft.Extensions.Logging;
using ZUI.Ipc;
using ZUI.Models;

namespace ZUI.Services;

/// <summary>
/// UI-side service for managing per-app proxy routing.
/// Sends IPC requests to the Worker which runs the ProxifierEngine.
/// </summary>
public interface IProxifierService
{
    /// <summary>Whether the proxifier is currently active on the Worker.</summary>
    bool IsRunning { get; }

    /// <summary>Current proxifier status (rules, connections, traffic).</summary>
    ProxifierStatus? Status { get; }

    /// <summary>Start per-app proxy routing on the Worker.</summary>
    Task<Result> StartAsync(CancellationToken ct = default);

    /// <summary>Stop per-app proxy routing on the Worker.</summary>
    Task<Result> StopAsync(CancellationToken ct = default);

    /// <summary>Refresh cached status from Worker.</summary>
    Task RefreshStatusAsync(CancellationToken ct = default);

    // ── Proxy Server CRUD ────────────────────────────────

    /// <summary>Add a new proxy server.</summary>
    Task<Result> AddServerAsync(ProxyServerDisplayModel server, CancellationToken ct = default);

    /// <summary>Remove a proxy server by name.</summary>
    Task<Result> RemoveServerAsync(string serverId, CancellationToken ct = default);

    /// <summary>Update an existing proxy server.</summary>
    Task<Result> UpdateServerAsync(string serverId, ProxyServerDisplayModel server, CancellationToken ct = default);

    /// <summary>Get all proxy servers from the current profile.</summary>
    Task<List<ProxyServerDisplayModel>> GetServersAsync(CancellationToken ct = default);

    /// <summary>Check proxy server connectivity.</summary>
    Task<CheckProxyResponse?> CheckServerAsync(ProxyServerDisplayModel server, CancellationToken ct = default);

    // ── Proxy Rule CRUD ──────────────────────────────────

    /// <summary>Add a new routing rule.</summary>
    Task<Result> AddRuleAsync(ProxyRuleDisplayModel rule, CancellationToken ct = default);

    /// <summary>Remove a routing rule by ID.</summary>
    Task<Result> RemoveRuleAsync(string ruleId, CancellationToken ct = default);

    /// <summary>Get all rules from the current profile.</summary>
    Task<List<ProxyRuleDisplayModel>> GetRulesAsync(CancellationToken ct = default);

    /// <summary>Get traffic statistics (global + per-connection).</summary>
    Task<TrafficStatsResponse?> GetTrafficStatsAsync(CancellationToken ct = default);
}

/// <summary>
/// Implementation of IProxifierService using IPC to the Worker.
/// </summary>
public sealed class ProxifierService : IProxifierService
{
    private readonly IIpcClientService _ipc;
    private readonly ILogger<ProxifierService> _logger;

    private ProxifierStatus? _status;
    private readonly object _lock = new();

    public bool IsRunning
    {
        get { lock (_lock) { return _status?.IsRunning ?? false; } }
    }

    public ProxifierStatus? Status
    {
        get { lock (_lock) { return _status; } }
    }

    public ProxifierService(IIpcClientService ipc, ILogger<ProxifierService> logger)
    {
        _ipc = ipc ?? throw new ArgumentNullException(nameof(ipc));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<Result> StartAsync(CancellationToken ct = default)
    {
        _logger.LogInformation("Starting proxifier via IPC");

        if (!_ipc.IsConnected)
            return Result.Failed("Нет связи с сервисом (Worker)");

        var result = await _ipc.StartProxifierAsync(ct).ConfigureAwait(false);

        if (result.IsSuccess)
        {
            await RefreshStatusAsync(ct).ConfigureAwait(false);
            _logger.LogInformation("Proxifier started");
        }
        else
        {
            _logger.LogWarning("Failed to start proxifier: {Error}", result.Error);
        }

        return result;
    }

    public async Task<Result> StopAsync(CancellationToken ct = default)
    {
        _logger.LogInformation("Stopping proxifier via IPC");

        if (!_ipc.IsConnected)
            return Result.Failed("Нет связи с сервисом (Worker)");

        var result = await _ipc.StopProxifierAsync(ct).ConfigureAwait(false);

        if (result.IsSuccess)
        {
            lock (_lock) { _status = new ProxifierStatus { IsRunning = false }; }
            _logger.LogInformation("Proxifier stopped");
        }
        else
        {
            _logger.LogWarning("Failed to stop proxifier: {Error}", result.Error);
        }

        return result;
    }

    public async Task RefreshStatusAsync(CancellationToken ct = default)
    {
        if (!_ipc.IsConnected)
            return;

        var result = await _ipc.GetProxifierStatusAsync(ct).ConfigureAwait(false);
        if (result.IsSuccess && result.Value != null)
        {
            lock (_lock) { _status = result.Value; }
        }
    }

    // ── Proxy Server CRUD ─────────────────────────────────────

    public async Task<Result> AddServerAsync(ProxyServerDisplayModel server, CancellationToken ct = default)
    {
        if (!_ipc.IsConnected)
            return Result.Failed("Нет связи с сервисом (Worker)");

        return await _ipc.AddProxyServerAsync(
            server.Name, server.Host, server.Port, server.ProxyType,
            server.Username, server.Password, server.DnsPolicy, ct).ConfigureAwait(false);
    }

    public async Task<Result> RemoveServerAsync(string serverId, CancellationToken ct = default)
    {
        if (!_ipc.IsConnected)
            return Result.Failed("Нет связи с сервисом (Worker)");

        return await _ipc.RemoveProxyServerAsync(serverId, ct).ConfigureAwait(false);
    }

    public async Task<Result> UpdateServerAsync(string serverId, ProxyServerDisplayModel server,
        CancellationToken ct = default)
    {
        if (!_ipc.IsConnected)
            return Result.Failed("Нет связи с сервисом (Worker)");

        return await _ipc.UpdateProxyServerAsync(serverId, server.Name, server.Host, server.Port,
            server.ProxyType, server.Username, server.Password, server.DnsPolicy, ct).ConfigureAwait(false);
    }

    public async Task<List<ProxyServerDisplayModel>> GetServersAsync(CancellationToken ct = default)
    {
        if (!_ipc.IsConnected)
            return [];

        var profile = await _ipc.GetProxyProfileAsync(includeRules: false, includeChains: false, ct: ct)
            .ConfigureAwait(false);
        if (profile is null)
            return [];

        return profile.Servers.Select(s => new ProxyServerDisplayModel
        {
            Name = s.Name,
            Host = s.Host,
            Port = s.Port,
            ProxyType = s.ProxyType,
            AuthenticationEnabled = s.AuthenticationEnabled,
            Username = s.Username,
            DnsPolicy = s.DnsPolicy,
        }).ToList();
    }

    public async Task<CheckProxyResponse?> CheckServerAsync(ProxyServerDisplayModel server,
        CancellationToken ct = default)
    {
        if (!_ipc.IsConnected)
            return null;

        return await _ipc.CheckProxyAsync(
            server.Host, server.Port, server.ProxyType,
            server.Username, server.Password, ct: ct).ConfigureAwait(false);
    }

    // ── Proxy Rule CRUD ───────────────────────────────────────

    public async Task<Result> AddRuleAsync(ProxyRuleDisplayModel rule, CancellationToken ct = default)
    {
        if (!_ipc.IsConnected)
            return Result.Failed("Нет связи с сервисом (Worker)");

        return await _ipc.AddProxyRuleAsync(
            rule.ProcessName, rule.ProcessNamePattern, rule.ProcessId,
            rule.DestinationIp, rule.DestinationPort,
            rule.DestinationDomain, rule.DestinationDomainPattern,
            rule.Action, rule.ProxyServerId, rule.ChainName, rule.DnsPolicy, ct).ConfigureAwait(false);
    }

    public async Task<Result> RemoveRuleAsync(string ruleId, CancellationToken ct = default)
    {
        if (!_ipc.IsConnected)
            return Result.Failed("Нет связи с сервисом (Worker)");

        return await _ipc.RemoveProxyRuleAsync(ruleId, ct).ConfigureAwait(false);
    }

    public async Task<List<ProxyRuleDisplayModel>> GetRulesAsync(CancellationToken ct = default)
    {
        if (!_ipc.IsConnected)
            return [];

        var profile = await _ipc.GetProxyProfileAsync(includeChains: false, ct: ct).ConfigureAwait(false);
        if (profile is null)
            return [];

        return profile.Rules.Select(r => new ProxyRuleDisplayModel
        {
            Id = r.Id,
            Name = r.Name,
            IsEnabled = r.IsEnabled,
            Priority = r.Priority,
            ProcessName = r.ProcessName,
            ProcessNamePattern = r.ProcessNamePattern,
            ProcessId = r.ProcessId,
            DestinationIp = r.DestinationIp,
            DestinationPort = r.DestinationPort,
            DestinationDomain = r.DestinationDomain,
            DestinationDomainPattern = r.DestinationDomainPattern,
            Action = r.Action,
            ProxyServerId = r.ProxyServerId,
            ChainName = r.ChainName,
            DnsPolicy = r.DnsPolicy,
        }).ToList();
    }

    public async Task<TrafficStatsResponse?> GetTrafficStatsAsync(CancellationToken ct = default)
    {
        if (!_ipc.IsConnected)
            return null;

        var result = await _ipc.GetTrafficStatsAsync(ct).ConfigureAwait(false);
        return result.IsSuccess ? result.Value : null;
    }
}
