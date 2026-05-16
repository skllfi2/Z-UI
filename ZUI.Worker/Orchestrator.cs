// ═══════════════════════════════════════════════════════════════
// ZUI.Worker / Orchestrator.cs
// Центральный координатор всех модулей Z-UI Worker
// DPI Bypass + (Phase 7) Proxifier + (Phase 8) TgProxy + (Phase 6) DNS
// Получает IPC запросы от UI и диспетчеризует их
// ═══════════════════════════════════════════════════════════════

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Linq;
using System.Text.Json;
using ZUI.Core;
using ZUI.Core.Desync;
using ZUI.Core.Dns;
using ZUI.Core.Engine;
using ZUI.Core.Intercept;
using ZUI.Core.Rules;
using ZUI.Core.WinDivert;
using ZUI.Ipc;
using ZUI.Proxy;
using ZUI.Proxy.Profile;
using ZUI.Proxy.Rules;
using ZUI.Core.Traffic;
using ZUI.Telegram.MtProto;
using ZUI.Telegram.MtProxy;
using ZUI.Telegram.Socks5;
using ZUI.Telegram.WebSocket;

namespace ZUI.Worker;

/// <summary>
/// Статус компонента.
/// </summary>
public enum ModuleState
{
    Stopped,
    Starting,
    Running,
    Stopping,
    Failed,
}

/// <summary>
/// Центральный координатор Worker Service.
/// Управляет жизненным циклом всех модулей:
/// - DPI Bypass Engine (Phase 1-4)
/// - Proxifier (Phase 7, TODO)
/// - Telegram Proxy (Phase 8, TODO)
/// - DNS (Phase 6, TODO)
/// Обрабатывает IPC запросы от UI и отправляет события.
/// </summary>
public sealed class Orchestrator : IAsyncDisposable
{
    private readonly ILogger _logger;

    // ── DPI Bypass компоненты ──────────────────────────────
    private readonly StrategyConfigLoader _strategyLoader;
    private readonly BatStrategyConverter _batConverter;
    private readonly DomainListLoader _domainLoader;
    private readonly PidMapper _pidMapper;
    private readonly RuleMatcher _ruleMatcher;
    private readonly ConnectionTracker _connectionTracker;
    private readonly FakePacketBuilder _fakeBuilder;
    private readonly DpiBypassEngine _bypassEngine;
    private readonly PacketInterceptor _interceptor;

    // ── DNS компоненты ────────────────────────────────────
    private readonly DnsProxyService _dnsProxy;
    private readonly DohResolver _dohResolver;
    private readonly FakeDnsResponder _fakeDnsResponder;
    private readonly DnsCache _dnsCache;
    private readonly HostsFileManager _hostsFileManager;
    private readonly DnsSniffer _dnsSniffer;

    // ── Proxifier компоненты (Phase 7) ────────────────────
    private readonly ProxifierEngine _proxifierEngine;
    private readonly ProxyProfileManager _profileManager;
    private readonly TrafficMonitor _trafficMonitor;

    // ── Block Detection (Phase 6) ─────────────────────────
    private readonly PassiveBlockAnalyzer _blockAnalyzer;

    // ── Telegram Proxy компоненты (Phase 8) ───────────────
    private readonly Socks5Server _socks5Server;
    private readonly MtProxyServer _mtProxyServer;
    private readonly WsTunnelClient _wsTunnelClient;

    // ── Стратегии ──────────────────────────────────────────
    private StrategyConfig[] _availableStrategies = [];
    private StrategyConfig? _activeStrategy;
    private int _gameFilterMode;

    // ── Статусы модулей ────────────────────────────────────
    private int _bypassState;
    private int _dnsState;
    private int _proxifierState; // Phase 7
    private int _tgProxyState; // Phase 8

    // ── DNS состояние ─────────────────────────────────────
    private bool _dohEnabled;
    private bool _fakeDnsEnabled;
    private DnsProxyConfig _dnsConfig;

    // ── Telegram Proxy состояние (Phase 8) ──────────────
    private int _socks5Port;
    private int _mtProxyPort;
    private string _wsUrl = string.Empty;
    private string _secret = string.Empty;

    // ── Proxifier состояние (Phase 7) ─────────────────────
    private int _proxifierActiveRules;
    private int _proxifierActiveConnections;
    private long _proxifierBytesSent;
    private long _proxifierBytesReceived;
    private ProxyProfile _currentProfile = ProxyProfileManager.CreateDefault();

    // ── IPC сервер ─────────────────────────────────────────
    private readonly IpcPipeServer _ipcServer;

    // ── Время старта для uptime ────────────────────────────
    private DateTime _bypassStartTime;

    // ── Статистика пакетов (дельта-расчёт) ────────────────
    private DateTime _lastStatsTime;
    private long _lastTotalPackets;
    private double _lastPacketsPerSecond;
    private double _lastBytesPerSecond;

    /// <summary>Событие: отправить IPC сообщение всем клиентам.</summary>
    public event Func<IpcMessage, Task>? OnSendMessage;

#pragma warning disable CS0067 // Будет использоваться в будущих фазах для отправки логов в UI
    /// <summary>Событие: лог для передачи в UI.</summary>
    public event Action<int, string>? OnLog;
#pragma warning restore CS0067

    public Orchestrator(
        IpcPipeServer ipcServer,
        StrategyConfigLoader strategyLoader,
        BatStrategyConverter batConverter,
        DomainListLoader domainLoader,
        PidMapper pidMapper,
        RuleMatcher ruleMatcher,
        ConnectionTracker connectionTracker,
        FakePacketBuilder fakeBuilder,
        DpiBypassEngine bypassEngine,
        PacketInterceptor interceptor,
        DnsProxyService dnsProxy,
        DohResolver dohResolver,
        FakeDnsResponder fakeDnsResponder,
        DnsCache dnsCache,
        HostsFileManager hostsFileManager,
        DnsSniffer dnsSniffer,
        ProxifierEngine proxifierEngine,
        ProxyProfileManager profileManager,
        TrafficMonitor trafficMonitor,
        PassiveBlockAnalyzer blockAnalyzer,
        Socks5Server socks5Server,
        MtProxyServer mtProxyServer,
        WsTunnelClient wsTunnelClient,
        ILogger<Orchestrator>? logger = null)
    {
        _ipcServer = ipcServer;
        _strategyLoader = strategyLoader;
        _batConverter = batConverter;
        _domainLoader = domainLoader;
        _pidMapper = pidMapper;
        _ruleMatcher = ruleMatcher;
        _connectionTracker = connectionTracker;
        _fakeBuilder = fakeBuilder;
        _bypassEngine = bypassEngine;
        _interceptor = interceptor;
        _dnsProxy = dnsProxy;
        _dohResolver = dohResolver;
        _fakeDnsResponder = fakeDnsResponder;
        _dnsCache = dnsCache;
        _hostsFileManager = hostsFileManager;
        _dnsSniffer = dnsSniffer;
        _proxifierEngine = proxifierEngine;
        _profileManager = profileManager;
        _trafficMonitor = trafficMonitor;
        _blockAnalyzer = blockAnalyzer;
        _socks5Server = socks5Server;
        _mtProxyServer = mtProxyServer;
        _wsTunnelClient = wsTunnelClient;
        _logger = logger ?? NullLoggerFactory.Instance.CreateLogger<Orchestrator>();
        _dnsConfig = new DnsProxyConfig();

        // Подписка на события PacketInterceptor
        _interceptor.StateChanged += OnInterceptorStateChanged;
        _interceptor.OnError += OnInterceptorError;
        _interceptor.OnPacketProcessed += OnPacketProcessed;

        // Подписка на события BlockAnalyzer
        _blockAnalyzer.OnBlockDetected += OnBlockDetected;
    }

    // ── Инициализация ──────────────────────────────────────

    /// <summary>
    /// Инициализировать оркестратор: загрузить стратегии.
    /// </summary>
    public async Task<Result> InitializeAsync(string zapretDir, CancellationToken ct = default)
    {
        _logger.LogInformation("Initializing Orchestrator with zapret dir: {Dir}", zapretDir);

        // 1. Определить директорию стратегий
        var strategiesDir = Path.Combine(zapretDir, "strategies");
        if (!Directory.Exists(strategiesDir))
        {
            return Result.Failed($"Strategies directory not found: {strategiesDir}");
        }

        // 2. Загрузить стратегии
        var loadResult = await _strategyLoader.LoadAllAsync(strategiesDir, ct).ConfigureAwait(false);
        if (!loadResult.IsSuccess)
        {
            return Result.Failed($"Failed to load strategies: {loadResult.Error}");
        }

        _availableStrategies = loadResult.Value;
        _logger.LogInformation("Loaded {Count} strategies", _availableStrategies.Length);

        // 3. Подписаться на IPC сообщения
        _ipcServer.OnMessageReceived += OnIpcMessageReceived;

        return Result.Success();
    }

    // ── Обработка IPC запросов ─────────────────────────────

    private async Task OnIpcMessageReceived(IpcMessage message)
    {
        if (message is not IpcRequest request)
        {
            _logger.LogDebug("Received non-request IPC message: {Type}", message.GetType().Name);
            return;
        }

        _logger.LogDebug("Processing IPC request: {Type}", message.GetType().Name);

        var response = await HandleRequestAsync(request).ConfigureAwait(false);
        response = response with { RequestId = request.MessageId };

        // Отправить ответ
        if (OnSendMessage is not null)
        {
            await OnSendMessage(response).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Обработать IPC запрос и вернуть ответ.
    /// </summary>
    public Task<IpcResponse> HandleRequestAsync(IpcRequest request)
    {
        return request switch
        {
            StartBypassRequest r => HandleStartBypassAsync(r),
            StopBypassRequest => HandleStopBypassAsync(),
            GetBypassStatusRequest => HandleGetBypassStatus(),
            GetAvailableStrategiesRequest => HandleGetAvailableStrategies(),
            SetGameFilterRequest r => HandleSetGameFilter(r),
            StartProxifierRequest => HandleStartProxifier(),
            StopProxifierRequest => HandleStopProxifier(),
            GetProxifierStatusRequest => HandleGetProxifierStatus(),
        GetProxifierConnectionsRequest => HandleGetProxifierConnections(),
            AddProxyServerRequest r => HandleAddProxyServer(r),
            RemoveProxyServerRequest r => HandleRemoveProxyServer(r),
            UpdateProxyServerRequest r => HandleUpdateProxyServer(r),
            GetProxyProfileRequest r => HandleGetProxyProfile(r),
            CheckProxyRequest r => HandleCheckProxy(r),
            AddProxyRuleRequest r => HandleAddProxyRule(r),
            RemoveProxyRuleRequest r => HandleRemoveProxyRule(r),
            StartTgWsProxyRequest r => HandleStartTgWsProxy(r),
            StopTgWsProxyRequest => HandleStopTgWsProxy(),
            StartMtProxyRequest r => HandleStartMtProxy(r),
            StopMtProxyRequest => HandleStopMtProxy(),
            GetTgProxyStatusRequest => HandleGetTgProxyStatus(),
            ConfigureDnsRequest r => HandleConfigureDns(r),
            GetDnsStatusRequest => HandleGetDnsStatus(),
            RunDiagnosticsRequest => HandleRunDiagnostics(),
            UpdateDomainListsRequest => HandleUpdateDomainLists(),
            PingRequest => Task.FromResult<IpcResponse>(new PongResponse()),
            GetTrafficStatsRequest => HandleGetTrafficStats(),
            GetBlockStatusRequest => HandleGetBlockStatus(),
            ClearBlocksRequest => HandleClearBlocks(),
            _ => Task.FromResult<IpcResponse>(
                new ErrorResponse($"Unknown request type: {request.GetType().Name}")),
        };
    }

    // ── DPI Bypass ─────────────────────────────────────────

    private async Task<IpcResponse> HandleStartBypassAsync(StartBypassRequest request)
    {
        if ((ModuleState)Volatile.Read(ref _bypassState) == ModuleState.Running)
            return new ErrorResponse("DPI bypass is already running.");

        // Найти стратегию
        var strategy = _availableStrategies.FirstOrDefault(
            s => s.Id.Equals(request.StrategyId, StringComparison.OrdinalIgnoreCase));

        if (strategy is null)
            return new ErrorResponse($"Strategy not found: {request.StrategyId}");

        _activeStrategy = strategy;
        _gameFilterMode = request.GameFilterMode;

        Volatile.Write(ref _bypassState, (int)ModuleState.Starting);
        _logger.LogInformation("Starting DPI bypass with strategy: {Strategy}", strategy.Name);

        var result = await _interceptor.StartAsync(strategy).ConfigureAwait(false);

        if (result.IsSuccess)
        {
            Volatile.Write(ref _bypassState, (int)ModuleState.Running);
            _bypassStartTime = DateTime.UtcNow;
            _logger.LogInformation("DPI bypass started successfully");
            return new SuccessResponse();
        }
        else
        {
            Volatile.Write(ref _bypassState, (int)ModuleState.Failed);
            _logger.LogError("DPI bypass start failed: {Error}", result.Error);
            return new ErrorResponse($"Failed to start DPI bypass: {result.Error}");
        }
    }

    private async Task<IpcResponse> HandleStopBypassAsync()
    {
        var state = (ModuleState)Volatile.Read(ref _bypassState);
        if (state is not ModuleState.Running and not ModuleState.Failed)
            return new ErrorResponse($"Cannot stop DPI bypass in state: {state}");

        Volatile.Write(ref _bypassState, (int)ModuleState.Stopping);
        _logger.LogInformation("Stopping DPI bypass...");

        await _interceptor.StopAsync().ConfigureAwait(false);

        Volatile.Write(ref _bypassState, (int)ModuleState.Stopped);
        _activeStrategy = null;
        _logger.LogInformation("DPI bypass stopped");

        return new SuccessResponse();
    }

    private Task<IpcResponse> HandleGetBypassStatus()
    {
        var state = (ModuleState)Volatile.Read(ref _bypassState);
        var isRunning = state == ModuleState.Running;
        var uptime = isRunning ? (DateTime.UtcNow - _bypassStartTime).TotalSeconds : 0;

        return Task.FromResult<IpcResponse>(new BypassStatusResponse(
            IsRunning: isRunning,
            StrategyId: _activeStrategy?.Id,
            GameFilterMode: _gameFilterMode,
            PacketsProcessed: _bypassEngine.TotalPackets,
            PacketsBypassed: _bypassEngine.BypassedPackets,
            UptimeSeconds: uptime));
    }

    private Task<IpcResponse> HandleGetAvailableStrategies()
    {
        var ids = _availableStrategies.Select(s => s.Id).ToArray();
        return Task.FromResult<IpcResponse>(new AvailableStrategiesResponse(ids));
    }

    private Task<IpcResponse> HandleSetGameFilter(SetGameFilterRequest request)
    {
        _gameFilterMode = request.GameFilterMode;
        _logger.LogInformation("Game filter mode set to: {Mode}", request.GameFilterMode);
        return Task.FromResult<IpcResponse>(new SuccessResponse());
    }

    // ── Proxifier (Phase 7) ───────────────────────────────

    private async Task<IpcResponse> HandleStartProxifier()
    {
        if ((ModuleState)Volatile.Read(ref _proxifierState) == ModuleState.Running)
            return new ErrorResponse("Proxifier is already running.");

        Volatile.Write(ref _proxifierState, (int)ModuleState.Starting);
        _logger.LogInformation("Starting Proxifier...");

        // Загрузить профиль из файла или создать по умолчанию
        ProxyProfile profile;
        var profilePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Z-UI", "proxifier-profile.json");
        try
        {
            if (File.Exists(profilePath))
            {
                var profileJson = await File.ReadAllTextAsync(profilePath).ConfigureAwait(false);
                var loaded = JsonSerializer.Deserialize(profileJson, ProxyJsonContext.Default.ProxyProfile);
                if (loaded is not null)
                {
                    profile = loaded;
                    _logger.LogInformation("Loaded proxy profile from {Path}", profilePath);
                }
                else
                {
                    profile = ProxyProfileManager.CreateDefault();
                    _logger.LogInformation("Profile file was empty, using default");
                }
            }
            else
            {
                profile = ProxyProfileManager.CreateDefault();
                _logger.LogInformation("No profile file found, using default");
            }
        }
        catch (Exception ex) when (ex is IOException or System.Text.Json.JsonException or UnauthorizedAccessException)
        {
            profile = ProxyProfileManager.CreateDefault();
            _logger.LogWarning(ex, "Failed to load proxy profile from {Path}, using default", profilePath);
        }
        var result = await _proxifierEngine.StartAsync(profile).ConfigureAwait(false);

        if (result.IsSuccess)
        {
            Volatile.Write(ref _proxifierState, (int)ModuleState.Running);
            _logger.LogInformation("Proxifier started successfully");
            return new SuccessResponse();
        }
        else
        {
            Volatile.Write(ref _proxifierState, (int)ModuleState.Failed);
            _logger.LogError("Proxifier start failed: {Error}", result.Error);
            return new ErrorResponse($"Failed to start Proxifier: {result.Error}");
        }
    }

    private async Task<IpcResponse> HandleStopProxifier()
    {
        if ((ModuleState)Volatile.Read(ref _proxifierState) is not ModuleState.Running and not ModuleState.Failed)
            return new ErrorResponse("Proxifier is not running.");

        Volatile.Write(ref _proxifierState, (int)ModuleState.Stopping);
        _logger.LogInformation("Stopping Proxifier...");

        await _proxifierEngine.StopAsync().ConfigureAwait(false);

        Volatile.Write(ref _proxifierState, (int)ModuleState.Stopped);
        _logger.LogInformation("Proxifier stopped");

        return new SuccessResponse();
    }

    private Task<IpcResponse> HandleGetProxifierStatus()
    {
        var isRunning = _proxifierEngine.State == ProxifierState.Running;
        var snapshot = _trafficMonitor.GetSnapshot();

        Volatile.Write(ref _proxifierActiveRules, _proxifierEngine.ActiveRuleCount);
        Volatile.Write(ref _proxifierActiveConnections, _proxifierEngine.ActiveConnectionCount);
        Volatile.Write(ref _proxifierBytesSent, snapshot.TotalBytesSent);
        Volatile.Write(ref _proxifierBytesReceived, snapshot.TotalBytesReceived);

        return Task.FromResult<IpcResponse>(new ProxifierStatusResponse(
            IsRunning: isRunning,
            ActiveRules: _proxifierEngine.ActiveRuleCount,
            ActiveConnections: _proxifierEngine.ActiveConnectionCount,
            TotalBytesSent: snapshot.TotalBytesSent,
            TotalBytesReceived: snapshot.TotalBytesReceived));
    }

    private Task<IpcResponse> HandleGetProxifierConnections()
    {
        var connections = _proxifierEngine.GetRecentConnections();
        var ipcConnections = connections.Select(c => new ProxifierConnectionInfo(
            ConnectionId: c.ConnectionId,
            Pid: c.Pid,
            ProcessName: c.ProcessName,
            TargetHost: c.TargetHost,
            TargetPort: c.TargetPort,
            TargetIp: c.TargetIp,
            Action: c.Action,
            ProxyName: c.ProxyName,
            DnsPolicy: c.DnsPolicy,
            StartedAt: c.StartedAt,
            EndedAt: c.EndedAt,
            BytesSent: c.BytesSent,
            BytesReceived: c.BytesReceived,
            Status: c.Status.ToString())).ToArray();

        return Task.FromResult<IpcResponse>(new ProxifierConnectionsResponse(
            Connections: ipcConnections));
    }

    // ── Proxy Server CRUD ───────────────────────────────────

    private Task<IpcResponse> HandleAddProxyServer(AddProxyServerRequest r)
    {
        try
        {
            var server = new ProxyTarget
            {
                Name = r.Name,
                Host = r.Host,
                Port = r.Port,
                Type = Enum.TryParse<ProxyType>(r.ProxyType, out var pt) ? pt : ProxyType.Socks5,
                Username = r.Username,
                Password = r.Password,
            };
            _profileManager.AddServer(_currentProfile, server);
            return Task.FromResult<IpcResponse>(new SuccessResponse());
        }
        catch (ArgumentException ex)
        {
            return Task.FromResult<IpcResponse>(new ErrorResponse(ex.Message));
        }
    }

    private Task<IpcResponse> HandleRemoveProxyServer(RemoveProxyServerRequest r)
    {
        if (_profileManager.RemoveServer(_currentProfile, r.ServerId))
            return Task.FromResult<IpcResponse>(new SuccessResponse());

        return Task.FromResult<IpcResponse>(new ErrorResponse($"Server not found: {r.ServerId}"));
    }

    private Task<IpcResponse> HandleUpdateProxyServer(UpdateProxyServerRequest r)
    {
        if (_profileManager.UpdateServer(_currentProfile, r.ServerId, r.Name, r.Host, r.Port,
                r.ProxyType, r.Username, r.Password, r.DnsPolicy))
            return Task.FromResult<IpcResponse>(new SuccessResponse());

        return Task.FromResult<IpcResponse>(new ErrorResponse($"Server not found: {r.ServerId}"));
    }

    private Task<IpcResponse> HandleGetProxyProfile(GetProxyProfileRequest r)
    {
        var servers = _currentProfile.Servers.Select(s => new ProxyServerInfo(
            Id: s.Name,
            Name: s.Name,
            Host: s.Host,
            Port: s.Port,
            ProxyType: s.Type.ToString(),
            AuthenticationEnabled: s.RequiresAuth,
            Username: s.Username,
            DnsPolicy: DnsPolicy.Local.ToString()))
            .ToArray();

        var rules = r.IncludeRules
            ? _currentProfile.Rules.Select(rule => new ProxyRuleInfo(
                Id: rule.Id,
                Name: rule.Name,
                IsEnabled: rule.IsEnabled,
                Priority: rule.Priority,
                ProcessName: rule.ProcessName,
                ProcessNamePattern: rule.ProcessNamePattern,
                ProcessId: rule.ProcessId,
                DestinationIp: rule.DestinationIp,
                DestinationPort: rule.DestinationPort,
                DestinationDomain: null,
                DestinationDomainPattern: null,
                Action: rule.Action.ToString(),
                ProxyServerId: rule.Proxy?.Name,
                ChainName: rule.ChainName,
                DnsPolicy: rule.DnsPolicy.ToString()))
            .ToArray()
            : [];

        var chains = r.IncludeChains
            ? _currentProfile.Chains.Select(chain => new ProxyChainInfo(
                Id: chain.Name,
                Name: chain.Name,
                ServerIds: chain.Nodes.Select(n => n.Name).ToArray(),
                FailoverPolicy: "NextOnError"))
            .ToArray()
            : [];

        return Task.FromResult<IpcResponse>(new ProxyProfileResponse(Servers: servers, Rules: rules, Chains: chains));
    }

    private Task<IpcResponse> HandleCheckProxy(CheckProxyRequest r)
    {
        return Task.FromResult<IpcResponse>(new CheckProxyResponse(
            Success: false,
            Error: "Proxy check not yet implemented",
            LatencyMs: 0));
    }

    // ── Proxy Rule CRUD ─────────────────────────────────────

    private Task<IpcResponse> HandleAddProxyRule(AddProxyRuleRequest r)
    {
        try
        {
            var rule = new ProxyRule
            {
                Name = $"Rule {_currentProfile.Rules.Count + 1}",
                Priority = _currentProfile.Rules.Count > 0
                    ? _currentProfile.Rules.Max(r => r.Priority) + 1
                    : 100,
                ProcessName = r.ProcessName,
                ProcessNamePattern = r.ProcessNamePattern,
                ProcessId = r.ProcessId,
                DestinationIp = r.DestinationIp,
                DestinationPort = r.DestinationPort,
                Action = Enum.TryParse<ProxyAction>(r.Action, out var action) ? action : ProxyAction.Direct,
                Proxy = !string.IsNullOrEmpty(r.ProxyServerId)
                    ? _currentProfile.Servers.FirstOrDefault(s => s.Name.Equals(r.ProxyServerId, StringComparison.OrdinalIgnoreCase))
                    : null,
                ChainName = r.ChainName,
                DnsPolicy = Enum.TryParse<DnsPolicy>(r.DnsPolicy, out var dns) ? dns : DnsPolicy.Local,
            };
            _profileManager.AddRule(_currentProfile, rule);
            return Task.FromResult<IpcResponse>(new SuccessResponse());
        }
        catch (Exception ex)
        {
            return Task.FromResult<IpcResponse>(new ErrorResponse(ex.Message));
        }
    }

    private Task<IpcResponse> HandleRemoveProxyRule(RemoveProxyRuleRequest r)
    {
        try
        {
            if (_profileManager.RemoveRule(_currentProfile, r.RuleId))
                return Task.FromResult<IpcResponse>(new SuccessResponse());

            return Task.FromResult<IpcResponse>(new ErrorResponse($"Rule not found: {r.RuleId}"));
        }
        catch (InvalidOperationException ex)
        {
            return Task.FromResult<IpcResponse>(new ErrorResponse(ex.Message));
        }
    }

    // ── Telegram Proxy (Phase 8) ──────────────────────────

    private async Task<IpcResponse> HandleStartTgWsProxy(StartTgWsProxyRequest request)
    {
        if (_socks5Server.IsRunning)
            return new ErrorResponse("Telegram SOCKS5→WS proxy is already running.");

        Volatile.Write(ref _tgProxyState, (int)ModuleState.Starting);
        _logger.LogInformation("Starting Telegram SOCKS5→WS proxy on port {Port}...", request.Socks5Port);

        // Обновить конфигурацию WebSocket туннеля
        var wsConfig = WsTunnelConfig.FromIpcParams(request.WsUrl, request.Secret);
        _wsTunnelClient.UpdateConfig(wsConfig);

        // Обновить порт SOCKS5 сервера (если отличается от текущего)
        _socks5Port = request.Socks5Port;
        _wsUrl = request.WsUrl;
        _secret = request.Secret;

        // Запустить SOCKS5 сервер на указанном порту
        var result = await _socks5Server.StartAsync(request.Socks5Port, CancellationToken.None).ConfigureAwait(false);

        if (result.IsSuccess)
        {
            Volatile.Write(ref _tgProxyState, (int)ModuleState.Running);
            _logger.LogInformation("Telegram SOCKS5→WS proxy started on port {Port}", request.Socks5Port);
            return new SuccessResponse();
        }
        else
        {
            Volatile.Write(ref _tgProxyState, (int)ModuleState.Failed);
            _logger.LogError("Telegram SOCKS5→WS proxy start failed: {Error}", result.Error);
            return new ErrorResponse($"Failed to start Telegram SOCKS5→WS proxy: {result.Error}");
        }
    }

    private async Task<IpcResponse> HandleStopTgWsProxy()
    {
        if (!_socks5Server.IsRunning)
            return new ErrorResponse("Telegram SOCKS5→WS proxy is not running.");

        Volatile.Write(ref _tgProxyState, (int)ModuleState.Stopping);
        _logger.LogInformation("Stopping Telegram SOCKS5→WS proxy...");

        await _socks5Server.StopAsync().ConfigureAwait(false);

        Volatile.Write(ref _tgProxyState, (int)ModuleState.Stopped);
        _logger.LogInformation("Telegram SOCKS5→WS proxy stopped");

        return new SuccessResponse();
    }

    private async Task<IpcResponse> HandleStartMtProxy(StartMtProxyRequest request)
    {
        if (_mtProxyServer.IsRunning)
            return new ErrorResponse("MTProxy server is already running.");

        // Валидация секрета
        var secretConfig = SecretConfig.TryParse(request.Secret);
        if (secretConfig is null)
            return new ErrorResponse($"Invalid MTProxy secret format: '{request.Secret}'. Expected 32 hex chars (simple) or 'dd'+32 hex chars (dd-secret).");

        Volatile.Write(ref _tgProxyState, (int)ModuleState.Starting);
        _mtProxyPort = request.Port;
        _logger.LogInformation("Starting MTProxy server on port {Port}...", request.Port);

        var result = await _mtProxyServer.StartAsync(request.Port, secretConfig, CancellationToken.None).ConfigureAwait(false);

        if (result.IsSuccess)
        {
            Volatile.Write(ref _tgProxyState, (int)ModuleState.Running);
            _logger.LogInformation("MTProxy server started on port {Port}", request.Port);
            return new SuccessResponse();
        }
        else
        {
            Volatile.Write(ref _tgProxyState, (int)ModuleState.Failed);
            _logger.LogError("MTProxy server start failed: {Error}", result.Error);
            return new ErrorResponse($"Failed to start MTProxy server: {result.Error}");
        }
    }

    private async Task<IpcResponse> HandleStopMtProxy()
    {
        if (!_mtProxyServer.IsRunning)
            return new ErrorResponse("MTProxy server is not running.");

        _logger.LogInformation("Stopping MTProxy server...");

        await _mtProxyServer.StopAsync().ConfigureAwait(false);

        _logger.LogInformation("MTProxy server stopped");

        return new SuccessResponse();
    }

    private Task<IpcResponse> HandleGetTgProxyStatus()
    {
        var socks5Running = _socks5Server.IsRunning;
        var mtProxyRunning = _mtProxyServer.IsRunning;
        var activeConnections = _socks5Server.ActiveConnectionCount + _mtProxyServer.ActiveConnectionCount;

        return Task.FromResult<IpcResponse>(new TgProxyStatusResponse(
            Socks5Running: socks5Running,
            Socks5Port: _socks5Server.Port,
            MtProxyRunning: mtProxyRunning,
            MtProxyPort: _mtProxyServer.Port,
            ActiveConnections: activeConnections));
    }

    // ── DNS ───────────────────────────────────────────────

    private async Task<IpcResponse> HandleConfigureDns(ConfigureDnsRequest request)
    {
        _dohEnabled = request.EnableDoh;
        _fakeDnsEnabled = request.EnableFakeDns;

        _logger.LogInformation("DNS configured: DoH={DoH}, FakeDns={FakeDns}", _dohEnabled, _fakeDnsEnabled);

        // Если DNS прокси уже запущен — обновить конфиг на лету
        if (_dnsProxy.IsRunning)
        {
            _dnsProxy.UpdateConfig(_dohEnabled, _fakeDnsEnabled);
            return new SuccessResponse();
        }

        // Иначе запустить DNS прокси с новой конфигурацией
        _dnsConfig = new DnsProxyConfig
        {
            EnableDoh = _dohEnabled,
            EnableFakeDns = _fakeDnsEnabled,
        };

        Volatile.Write(ref _dnsState, (int)ModuleState.Starting);
        var result = await _dnsProxy.StartAsync().ConfigureAwait(false);

        if (result.IsSuccess)
        {
            // Запустить DNS-сниффер вместе с прокси
            var snifferResult = _dnsSniffer.Start();
            if (!snifferResult.IsSuccess)
            {
                _logger.LogWarning("DNS sniffer failed to start: {Error}", snifferResult.Error);
                // Не блокируем запуск DNS прокси из-за сниффера
            }
            else
            {
                _logger.LogInformation("DNS sniffer started");
            }

            Volatile.Write(ref _dnsState, (int)ModuleState.Running);
            _logger.LogInformation("DNS proxy started successfully");
            return new SuccessResponse();
        }
        else
        {
            Volatile.Write(ref _dnsState, (int)ModuleState.Failed);
            _logger.LogError("DNS proxy start failed: {Error}", result.Error);
            return new ErrorResponse($"Failed to start DNS proxy: {result.Error}");
        }
    }

    private Task<IpcResponse> HandleGetDnsStatus()
    {
        var dnsRunning = (ModuleState)Volatile.Read(ref _dnsState) == ModuleState.Running;
        var snifferRunning = _dnsSniffer.IsRunning;

        return Task.FromResult<IpcResponse>(new DnsStatusResponse(
            DohEnabled: _dohEnabled,
            FakeDnsEnabled: _fakeDnsEnabled,
            CachedEntries: dnsRunning ? _dnsCache.Count : 0,
            FakeDnsOverrides: dnsRunning ? (int)_fakeDnsResponder.FakeResponsesSent : 0,
            SnifferRunning: snifferRunning,
            SnifferPackets: snifferRunning ? _dnsSniffer.PacketsSniffed : 0,
            SnifferRecords: snifferRunning ? _dnsSniffer.RecordsExtracted : 0));
    }

    /// <summary>
    /// Загрузить списки доменов для Fake DNS (из zapret/lists/).
    /// </summary>
    public async Task<Result> LoadDnsDomainListsAsync(string listsDir, CancellationToken ct = default)
    {
        var fakeListPath = Path.Combine(listsDir, "list-general.txt");
        var excludeListPath = Path.Combine(listsDir, "list-exclude.txt");

        // Загрузить список доменов для подмены (если есть)
        if (File.Exists(fakeListPath))
        {
            var result = await _fakeDnsResponder.LoadFakeDomainListAsync(fakeListPath, ct).ConfigureAwait(false);
            if (!result.IsSuccess)
                _logger.LogWarning("Failed to load fake DNS domain list: {Error}", result.Error);
        }

        // Загрузить список исключений (если есть)
        if (File.Exists(excludeListPath))
        {
            var result = await _fakeDnsResponder.LoadExcludeDomainListAsync(excludeListPath, ct).ConfigureAwait(false);
            if (!result.IsSuccess)
                _logger.LogWarning("Failed to load DNS exclude list: {Error}", result.Error);
        }

        return Result.Success();
    }

    // ── Traffic Stats ──────────────────────────────────────

    private Task<IpcResponse> HandleGetTrafficStats()
    {
        var snapshot = _trafficMonitor.GetSnapshot();
        _trafficMonitor.UpdateSpeed();

        var activeConnections = _proxifierEngine.GetActiveConnections();
        var connections = activeConnections.Select(c =>
        {
            var trafficStats = _trafficMonitor.GetConnectionStats(c.ConnectionId);
            return new ConnectionStatsInfo(
                ConnectionId: c.ConnectionId,
                ProcessName: c.ProcessName,
                Pid: c.Pid,
                TargetHost: c.TargetHost,
                TargetPort: c.TargetPort,
                BytesSent: trafficStats?.BytesSent ?? 0,
                BytesReceived: trafficStats?.BytesReceived ?? 0,
                StartedAt: c.StartedAt,
                Status: c.Status.ToString());
        }).ToArray();

        return Task.FromResult<IpcResponse>(new TrafficStatsResponse(
            TotalBytesSent: snapshot.TotalBytesSent,
            TotalBytesReceived: snapshot.TotalBytesReceived,
            TotalConnections: snapshot.TotalConnections,
            ActiveConnections: snapshot.ActiveConnections,
            BytesPerSecond: snapshot.CurrentBytesPerSecond,
            Connections: connections));
    }

    // ── Block Detection ────────────────────────────────────

    private Task<IpcResponse> HandleGetBlockStatus()
    {
        var stats = _blockAnalyzer.GetStats();
        var recentBlocks = _blockAnalyzer.GetRecentBlocks(20).Select(b => new BlockInfo(
            Target: b.Target,
            Type: b.Type.ToString(),
            Confidence: b.Confidence.ToString(),
            Description: b.Description,
            DetectedAt: b.DetectedAt,
            Occurrences: b.Occurrences)).ToArray();

        return Task.FromResult<IpcResponse>(new BlockStatusResponse(
            Stats: new BlockStatsInfo(
                TotalBlocks: stats.TotalBlocks,
                TcpResets: stats.TcpResets,
                SilentDrops: stats.SilentDrops,
                DpiDrops: stats.DpiDrops,
                TtlAnomalies: stats.TtlAnomalies,
                DnsMismatches: stats.DnsMismatches,
                ActiveConnections: stats.ActiveConnections),
            RecentBlocks: recentBlocks));
    }

    private Task<IpcResponse> HandleClearBlocks()
    {
        _blockAnalyzer.Clear();
        _logger.LogInformation("Block history cleared");
        return Task.FromResult<IpcResponse>(new SuccessResponse());
    }

    private void OnBlockDetected(BlockRecord block)
    {
        _logger.LogInformation("Block detected: {Type} on {Target} ({Confidence})",
            block.Type, block.Target, block.Confidence);

        var evt = new BlockDetectedEvent(
            Target: block.Target,
            Type: block.Type.ToString(),
            Confidence: block.Confidence.ToString(),
            Description: block.Description,
            Occurrences: block.Occurrences);

        _ = _ipcServer.SendToAllAsync(evt);
    }

    // ── Diagnostics ────────────────────────────────────────

    private Task<IpcResponse> HandleRunDiagnostics()
    {
        var results = new List<DiagnosticResultItem>();

        // 1. Проверка WinDivert
        var windivertDll = Path.Combine(AppContext.BaseDirectory, "WinDivert.dll");
        var windivertSys = Path.Combine(AppContext.BaseDirectory, "WinDivert64.sys");
        results.Add(new DiagnosticResultItem(
            Name: "WinDivert DLL",
            Passed: File.Exists(windivertDll),
            Message: File.Exists(windivertDll) ? $"Found: {windivertDll}" : "WinDivert.dll not found",
            Remediation: File.Exists(windivertDll) ? null : "Ensure WinDivert.dll is in the application directory"));

        results.Add(new DiagnosticResultItem(
            Name: "WinDivert Driver",
            Passed: File.Exists(windivertSys),
            Message: File.Exists(windivertSys) ? $"Found: {windivertSys}" : "WinDivert64.sys not found",
            Remediation: File.Exists(windivertSys) ? null : "Ensure WinDivert64.sys is in the application directory"));

        // 2. Проверка стратегий
        results.Add(new DiagnosticResultItem(
            Name: "Strategies",
            Passed: _availableStrategies.Length > 0,
            Message: $"Loaded {_availableStrategies.Length} strategies",
            Remediation: _availableStrategies.Length > 0 ? null : "Check strategies directory"));

        // 3. Проверка администраторских прав
        var isAdmin = IsRunningAsAdmin();
        results.Add(new DiagnosticResultItem(
            Name: "Administrator Rights",
            Passed: isAdmin,
            Message: isAdmin ? "Running as administrator" : "Not running as administrator",
            Remediation: isAdmin ? null : "Worker service must run under SYSTEM or administrator account"));

        // 4. Проверка DNS
        var dnsRunning = (ModuleState)Volatile.Read(ref _dnsState) == ModuleState.Running;
        results.Add(new DiagnosticResultItem(
            Name: "DNS Proxy",
            Passed: true, // DNS не обязателен для работы
            Message: dnsRunning
                ? $"DNS proxy running (DoH={_dohEnabled}, FakeDns={_fakeDnsEnabled}, cached={_dnsCache.Count})"
                : $"DNS proxy stopped (DoH={_dohEnabled}, FakeDns={_fakeDnsEnabled})",
            Remediation: null));

        return Task.FromResult<IpcResponse>(new DiagnosticResultsResponse(results.ToArray()));
    }

    private async Task<IpcResponse> HandleUpdateDomainLists()
    {
        // Перезагрузить стратегии (hot-reload)
        var checkResult = await _strategyLoader.CheckForChangesAsync().ConfigureAwait(false);
        if (checkResult.IsSuccess && checkResult.Value.Length > 0)
        {
            _availableStrategies = _strategyLoader.AllFromCache.ToArray();
            _logger.LogInformation("Updated {Count} strategies", checkResult.Value.Length);
        }

        return new SuccessResponse();
    }

    // ── События от PacketInterceptor ────────────────────────

    private async void OnInterceptorStateChanged(InterceptorState state)
    {
        _logger.LogInformation("PacketInterceptor state changed: {State}", state);

        if (state == InterceptorState.Failed)
        {
            Volatile.Write(ref _bypassState, (int)ModuleState.Failed);
            await SendEventAsync(new BypassStoppedEvent("Packet interceptor failed")).ConfigureAwait(false);
        }
    }

    private async void OnInterceptorError(string error)
    {
        _logger.LogError("PacketInterceptor error: {Error}", error);
        await SendEventAsync(new BypassStoppedEvent(error)).ConfigureAwait(false);
    }

    private void OnPacketProcessed(PacketAction action, string? reason)
    {
        // Статистика периодически отправляется через WorkerService таймер
    }

    // ── Отправка событий ───────────────────────────────────

    /// <summary>
    /// Отправить событие всем подключённым UI клиентам.
    /// </summary>
    public async Task SendEventAsync(IpcEvent evt)
    {
        if (OnSendMessage is not null)
        {
            await OnSendMessage(evt).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Получить текущую статистику пакетов (для периодической отправки).
    /// Calculates PacketsPerSecond and BytesPerSecond from delta with previous call.
    /// </summary>
    public PacketStatsEvent GetPacketStats()
    {
        var now = DateTime.UtcNow;
        var totalPackets = _bypassEngine.TotalPackets;

        // First call or reset — initialize tracking
        if (_lastStatsTime == default)
        {
            _lastStatsTime = now;
            _lastTotalPackets = totalPackets;
            _lastBytesPerSecond = 0;
            _lastPacketsPerSecond = 0;
        return new PacketStatsEvent(
            PacketsPerSecond: (int)_lastPacketsPerSecond,
            TotalPackets: totalPackets,
            BytesPerSecond: (long)_lastBytesPerSecond);
    }

    var deltaTime = (now - _lastStatsTime).TotalSeconds;

    // Avoid jitter: if less than 0.5s since last call, return previous values
    if (deltaTime < 0.5)
    {
        return new PacketStatsEvent(
            PacketsPerSecond: (int)_lastPacketsPerSecond,
            TotalPackets: totalPackets,
            BytesPerSecond: (long)_lastBytesPerSecond);
    }

    var deltaPackets = totalPackets - _lastTotalPackets;
    var pps = deltaTime > 0 ? deltaPackets / deltaTime : 0;
    // Estimate bytes from packets (average ~800 bytes for mixed traffic)
    var bps = pps * 800;

    _lastStatsTime = now;
    _lastTotalPackets = totalPackets;
    _lastPacketsPerSecond = pps;
    _lastBytesPerSecond = bps;

    return new PacketStatsEvent(
        PacketsPerSecond: (int)pps,
        TotalPackets: totalPackets,
        BytesPerSecond: (long)bps);
    }

    /// <summary>
    /// Проверить таймауты соединений для block analyzer (вызывать периодически).
    /// </summary>
    public void CheckBlockTimeouts()
    {
        _blockAnalyzer.CheckTimeouts();
    }

    // ── Проверка прав ──────────────────────────────────────

    private static bool IsRunningAsAdmin()
    {
        try
        {
            var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
            var principal = new System.Security.Principal.WindowsPrincipal(identity);
            return principal.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
        }
        catch
        {
            return false;
        }
    }

    // ── Dispose ────────────────────────────────────────────

    public async ValueTask DisposeAsync()
    {
        _logger.LogInformation("Disposing Orchestrator...");

        // Остановить все модули
        if ((ModuleState)Volatile.Read(ref _bypassState) == ModuleState.Running)
        {
            await _interceptor.StopAsync().ConfigureAwait(false);
        }

        if ((ModuleState)Volatile.Read(ref _dnsState) == ModuleState.Running)
        {
            await _dnsProxy.StopAsync().ConfigureAwait(false);
            await _dnsSniffer.StopAsync().ConfigureAwait(false);
        }

        if (_proxifierEngine.State == ProxifierState.Running)
        {
            await _proxifierEngine.StopAsync().ConfigureAwait(false);
        }

        // Telegram Proxy (Phase 8)
        if (_socks5Server.IsRunning)
        {
            await _socks5Server.StopAsync().ConfigureAwait(false);
        }

        if (_mtProxyServer.IsRunning)
        {
            await _mtProxyServer.StopAsync().ConfigureAwait(false);
        }

        await _interceptor.DisposeAsync().ConfigureAwait(false);
        await _dnsProxy.DisposeAsync().ConfigureAwait(false);
        await _dnsSniffer.DisposeAsync().ConfigureAwait(false);
        await _proxifierEngine.DisposeAsync().ConfigureAwait(false);
        await _socks5Server.DisposeAsync().ConfigureAwait(false);
        await _mtProxyServer.DisposeAsync().ConfigureAwait(false);

        _logger.LogInformation("Orchestrator disposed");
    }
}
