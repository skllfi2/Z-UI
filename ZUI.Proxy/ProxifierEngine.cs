// ═══════════════════════════════════════════════════════════════
// ZUI.Proxy / ProxifierEngine.cs
// Главный движок проксификатора:
// WinDivert → перехват SYN → PidMapper → RuleEvaluator → TcpRelay
// Per-app маршрутизация: Direct / Proxy / Chain / Block
// ═══════════════════════════════════════════════════════════════

using System.Collections.Concurrent;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ZUI.Core;
using ZUI.Core.Dns;
using ZUI.Core.Traffic;
using ZUI.Proxy.Chain;
using ZUI.Proxy.Client;
using ZUI.Proxy.Profile;
using ZUI.Proxy.Rules;
using ZUI.Core.Intercept;
using ZUI.Core.WinDivert;

namespace ZUI.Proxy;

/// <summary>
/// Статус движка проксификатора.
/// </summary>
public enum ProxifierState
{
    Stopped,
    Starting,
    Running,
    Stopping,
    Failed,
}

/// <summary>
/// Главный движок проксификатора.
/// 
/// Архитектура:
/// 1. WinDivert перехватывает исходящие TCP SYN
/// 2. PidMapper определяет PID → имя процесса
/// 3. RuleEvaluator сопоставляет процесс + адрес → действие
/// 4. По действию:
///    - Direct: переинжектировать SYN без изменений
///    - Block: отбросить SYN (соединение заблокировано)
///    - Proxy: TcpRelay через прокси-сервер
///    - Chain: TcpRelay через цепочку прокси
/// 
/// TcpRelay:
/// - Открывает TcpListener на динамическом порту
/// - Модифицирует SYN: dst → 127.0.0.1:relayPort
/// - Приложение подключается к relayPort
/// - TcpRelay пересылает: app ↔ relay ↔ proxy ↔ target
/// </summary>
public sealed class ProxifierEngine : IAsyncDisposable
{
    private readonly ILogger _logger;
    private readonly PidMapper _pidMapper;
    private readonly RuleEvaluator _ruleEvaluator;
    private readonly TcpRelay _tcpRelay;
    private readonly TrafficMonitor _trafficMonitor;
    private readonly ProxyProfileManager _profileManager;
    private readonly Socks5Client _socks5;
    private readonly Socks4Client _socks4;
    private readonly HttpConnectClient _httpConnect;
    private readonly ChainExecutor _chainExecutor;
    private readonly WinDivertInterceptor _synInterceptor;
    private readonly DnsReverseCache _dnsReverseCache;

    private CancellationTokenSource? _cts;
    private Task? _interceptTask;
    private int _state;

    // ── NAT таблица: оригинальный dst → relay порт ────────
    // Ключ: "srcIp:srcPort" (уникальный для каждого SYN)
    private readonly ConcurrentDictionary<string, NatEntry> _natTable = new();

    // ── Активные relay соединения ──────────────────────────
    private readonly ConcurrentDictionary<string, RelayConnection> _activeRelays = new();

    // ── Live connection events (ring buffer, max 500) ─────
    private const int MaxConnectionEvents = 500;
    private readonly ConcurrentQueue<ConnectionInfo> _connectionEvents = new();
    private int _connectionEventCount;

    // ── TcpListener для relay ──────────────────────────────
    private TcpListener? _relayListener;
    private int _relayPort;

    // ── Текущий профиль ────────────────────────────────────
    private ProxyProfile? _currentProfile;
    private Dictionary<string, ProxyChain> _chainsMap = new(StringComparer.OrdinalIgnoreCase);

    public ProxifierEngine(
        PidMapper pidMapper,
        RuleEvaluator ruleEvaluator,
        TcpRelay tcpRelay,
        TrafficMonitor trafficMonitor,
        ProxyProfileManager profileManager,
        Socks5Client socks5,
        Socks4Client socks4,
        HttpConnectClient httpConnect,
        ChainExecutor chainExecutor,
        WinDivertInterceptor synInterceptor,
        DnsReverseCache dnsReverseCache,
        ILogger<ProxifierEngine>? logger = null)
    {
        _pidMapper = pidMapper;
        _ruleEvaluator = ruleEvaluator;
        _tcpRelay = tcpRelay;
        _trafficMonitor = trafficMonitor;
        _profileManager = profileManager;
        _socks5 = socks5;
        _socks4 = socks4;
        _httpConnect = httpConnect;
        _chainExecutor = chainExecutor;
        _synInterceptor = synInterceptor;
        _dnsReverseCache = dnsReverseCache;
        _logger = logger ?? NullLoggerFactory.Instance.CreateLogger<ProxifierEngine>();
    }

    // ── Публичные свойства ─────────────────────────────────

    /// <summary>Текущий статус движка.</summary>
    public ProxifierState State => (ProxifierState)Volatile.Read(ref _state);

    /// <summary>Количество активных правил.</summary>
    public int ActiveRuleCount => _ruleEvaluator.RuleCount;

    /// <summary>Количество активных соединений.</summary>
    public int ActiveConnectionCount => _activeRelays.Count;

    /// <summary>Монитор трафика.</summary>
    public TrafficMonitor Traffic => _trafficMonitor;

    /// <summary>Событие изменения состояния.</summary>
    public event Action<ProxifierState>? StateChanged;

    /// <summary>Событие ошибки.</summary>
#pragma warning disable CS0067 // Event is never used — reserved for future error notifications
    public event Action<string>? OnError;
#pragma warning restore CS0067

    // ── Start / Stop ────────────────────────────────────────

    /// <summary>
    /// Запустить проксификатор с указанным профилем.
    /// </summary>
    public async Task<Result> StartAsync(
        ProxyProfile profile,
        CancellationToken ct = default)
    {
        if (!SetState(ProxifierState.Stopped, ProxifierState.Starting))
            return Result.Failed($"Cannot start: current state is {State}");

        try
        {
            // 1. Загрузить профиль
            _currentProfile = profile;
            _chainsMap = profile.Chains
                .Where(c => c.IsEnabled)
                .ToDictionary(c => c.Name, StringComparer.OrdinalIgnoreCase);

            // 2. Загрузить правила в оценщик
            _ruleEvaluator.LoadRules(profile.Rules.ToArray());

            _logger.LogInformation(
                "Starting ProxifierEngine: {Rules} rules, {Chains} chains",
                profile.Rules.Count, profile.Chains.Count);

            // 3. Запустить relay listener на динамическом порту
            _relayListener = new TcpListener(IPAddress.Loopback, 0);
            _relayListener.Start();
            _relayPort = ((IPEndPoint)_relayListener.LocalEndpoint).Port;
            _logger.LogInformation("Relay listener started on port {Port}", _relayPort);

            // 4. Запустить обработку relay соединений
            _cts = new CancellationTokenSource();
            var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token, ct);

        // Задача приёма relay соединений
        _ = AcceptRelayConnectionsAsync(linkedCts.Token);

        // 5. Открыть WinDivert для перехвата SYN
        const string synFilter = "outbound and tcp.Syn and !tcp.Ack and !loopback";
        var openResult = _synInterceptor.Open(synFilter);
        if (!openResult.IsSuccess)
        {
            SetState(ProxifierState.Failed, ProxifierState.Starting);
            _logger.LogError("Failed to open WinDivert for SYN intercept: {Error}", openResult.Error);
            return Result.Failed($"Failed to open WinDivert: {openResult.Error}");
        }

        // 6. Запустить цикл перехвата SYN
        _interceptTask = InterceptSynLoopAsync(linkedCts.Token);

        SetState(ProxifierState.Starting, ProxifierState.Running);
            _logger.LogInformation("ProxifierEngine started successfully");

            return Result.Success();
        }
        catch (SocketException ex)
        {
            SetState(ProxifierState.Failed, ProxifierState.Starting);
            _logger.LogError(ex, "Failed to start ProxifierEngine");
            return Result.Failed($"Start failed: {ex.Message}");
        }
        catch (IOException ex)
        {
            SetState(ProxifierState.Failed, ProxifierState.Starting);
            _logger.LogError(ex, "Failed to start ProxifierEngine");
            return Result.Failed($"Start failed: {ex.Message}");
        }
    catch (InvalidOperationException ex)
    {
        SetState(ProxifierState.Failed, ProxifierState.Starting);
        _logger.LogError(ex, "Failed to start ProxifierEngine");
        return Result.Failed($"Start failed: {ex.Message}");
    }
    catch (System.ComponentModel.Win32Exception ex)
    {
        SetState(ProxifierState.Failed, ProxifierState.Starting);
        _logger.LogError(ex, "Failed to start ProxifierEngine");
        return Result.Failed($"Start failed: {ex.Message}");
    }
    }

    /// <summary>
    /// Запустить проксификатор из JSON файла профиля.
    /// </summary>
    public async Task<Result> StartFromProfileFileAsync(
        string profilePath, CancellationToken ct = default)
    {
        var loadResult = await _profileManager.LoadAsync(profilePath, ct).ConfigureAwait(false);
        if (!loadResult.IsSuccess)
            return Result.Failed($"Failed to load profile: {loadResult.Error}");

        return await StartAsync(loadResult.Value, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Остановить проксификатор.
    /// </summary>
    public async Task StopAsync()
    {
        if (State is not (ProxifierState.Running or ProxifierState.Failed))
            return;

        SetState(ProxifierState.Running, ProxifierState.Stopping);
        _logger.LogInformation("Stopping ProxifierEngine...");

        // 1. Shutdown WinDivert (прерывает WinDivertRecv)
        _synInterceptor.Shutdown();

        // 2. Отменить токен
        _cts?.Cancel();

        // 3. Закрыть relay listener
        _relayListener?.Stop();

        // 4. Закрыть все активные relay
        foreach (var relay in _activeRelays.Values)
        {
            try { relay.AppClient.Close(); } catch (ObjectDisposedException) { /* ignore */ } catch (IOException) { /* ignore */ }
            try { relay.ProxyClient.Close(); } catch (ObjectDisposedException) { /* ignore */ } catch (IOException) { /* ignore */ }
        }
        _activeRelays.Clear();

        // 5. Очистить NAT таблицу
        _natTable.Clear();

        // 6. Дождаться завершения задач
        if (_interceptTask is not null)
        {
            try
            {
                await _interceptTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Expected
            }
            catch (IOException ex)
            {
                _logger.LogWarning(ex, "Error during intercept task shutdown");
            }
            catch (SocketException ex)
            {
                _logger.LogWarning(ex, "Error during intercept task shutdown");
            }
        }

        // 7. Закрыть WinDivert handle
        await _synInterceptor.DisposeAsync().ConfigureAwait(false);

        _cts?.Dispose();
        _cts = null;
        _interceptTask = null;
        _relayListener = null;

        SetState(ProxifierState.Stopping, ProxifierState.Stopped);
        _logger.LogInformation("ProxifierEngine stopped");
    }

    // ── Обработка перехваченного SYN ───────────────────────

    /// <summary>
    /// Обработать перехваченный TCP SYN пакет.
    /// Вызывается из PacketInterceptor или отдельного WinDivert цикла.
    /// </summary>
    /// <param name="srcIp">IP источника.</param>
    /// <param name="srcPort">Порт источника.</param>
    /// <param name="dstIp">IP назначения.</param>
    /// <param name="dstPort">Порт назначения.</param>
    /// <param name="pid">PID процесса (от WinDivert или PidMapper).</param>
    /// <returns>Действие: Pass (переинжектировать), Drop (отбросить), Redirect (модифицировать dst).</returns>
    public ProxifierAction EvaluateOutboundSyn(
        IPAddress srcIp, int srcPort,
        IPAddress dstIp, int dstPort,
        int pid)
    {
        if (State != ProxifierState.Running)
            return ProxifierAction.Pass;

        // Определить имя процесса
        var processName = _pidMapper.GetProcessName((uint)pid);
        if (string.IsNullOrEmpty(processName))
            processName = $"PID:{pid}";

        // Попробовать получить домен из обратного DNS кэша
        var resolvedDomain = _dnsReverseCache.TryGetDomain(dstIp);

        // Оценить правило
        var evalResult = _ruleEvaluator.Evaluate(processName, dstIp, dstPort, resolvedDomain);

        switch (evalResult.Action)
        {
            case ProxyAction.Direct:
                return ProxifierAction.Pass;

            case ProxyAction.Block:
                _logger.LogInformation("Blocked: {Process} → {DstIp}:{DstPort}",
                    processName, dstIp, dstPort);
                return ProxifierAction.Drop;

        case ProxyAction.Proxy:
        case ProxyAction.Chain:
            // Создать NAT запись и запустить relay
            var natKey = $"{srcIp}:{srcPort}";

            // Попробовать получить домен из reverse DNS cache
            var domainName = _dnsReverseCache.TryGetDomain(dstIp, out var cachedDomain)
                ? cachedDomain
                : null;

            var entry = new NatEntry
            {
                OriginalDstIp = dstIp,
                OriginalDstPort = dstPort,
                OriginalDomainName = domainName,
                OriginalProcessName = processName,
                EvalResult = evalResult,
                Pid = pid,
            };

                _natTable[natKey] = entry;

                _logger.LogDebug(
                    "Redirect: {Process} → {DstIp}:{DstPort} via {Action}",
                    processName, dstIp, dstPort,
                    evalResult.Action == ProxyAction.Chain
                        ? $"chain:{evalResult.ChainName}"
                        : $"proxy:{evalResult.Proxy}");

                // Указать, что SYN нужно перенаправить на relay port
                entry.RelayPort = _relayPort;
                return ProxifierAction.Redirect(_relayPort);

            default:
                return ProxifierAction.Pass;
        }
    }

    /// <summary>
    /// Получить NAT запись для соединения.
    /// </summary>
    public NatEntry? GetNatEntry(string key)
    {
        return _natTable.TryGetValue(key, out var entry) ? entry : null;
    }

    /// <summary>
    /// Удалить NAT запись.
    /// </summary>
    public void RemoveNatEntry(string key)
    {
        _natTable.TryRemove(key, out _);
    }

    // ── Relay: приём соединений от приложений ──────────────

    // ── SYN intercept loop ──────────────────────────────────

    /// <summary>
    /// Бесконечный цикл перехвата TCP SYN через WinDivert.
    /// Для каждого SYN: определяет PID, оценивает правило,
    /// и выполняет Pass / Drop / Redirect.
    /// </summary>
    private async Task InterceptSynLoopAsync(CancellationToken ct)
    {
        _logger.LogDebug("SYN intercept loop started");

        try
        {
            await foreach (var (packet, addr) in _synInterceptor.ReadPacketsAsync(ct).ConfigureAwait(false))
            {
                try
                {
                    if (!packet.IsTcp)
                    {
                        // Не TCP — переинжектировать как есть
                        var passAddr = addr;
                        _synInterceptor.SendPacket(packet.RawPacket, ref passAddr);
                        continue;
                    }

                    // Определить PID через PidMapper (IP Helper API)
                    var sendAddr = addr;
                    uint pid = _pidMapper.GetPidForConnection(packet.SrcPort, packet.IsIPv6, isTcp: true);

                    // Оценить правило
                    var action = EvaluateOutboundSyn(
                        packet.SrcIp, packet.SrcPort,
                        packet.DstIp, packet.DstPort,
                        (int)pid);

                    switch (action.Type)
                    {
                        case ProxifierActionType.Pass:
                            // Переинжектировать без изменений
                            _synInterceptor.SendPacket(packet.RawPacket, ref sendAddr);
                            break;

                        case ProxifierActionType.Drop:
                            // Не переинжектировать — пакет отброшен
                            _logger.LogDebug("Dropped SYN: {SrcIp}:{SrcPort} → {DstIp}:{DstPort}",
                                packet.SrcIp, packet.SrcPort, packet.DstIp, packet.DstPort);
                            break;

                        case ProxifierActionType.Redirect:
                            // Модифицировать dst на 127.0.0.1:relayPort
                            var modified = ModifySynDestination(packet.RawPacket, action.RelayPort);
                            _synInterceptor.SendPacket(modified, ref sendAddr);
                            _logger.LogDebug(
                                "Redirected SYN: {SrcIp}:{SrcPort} → 127.0.0.1:{RelayPort} (orig: {DstIp}:{DstPort})",
                                packet.SrcIp, packet.SrcPort, action.RelayPort,
                                packet.DstIp, packet.DstPort);
                            break;
                    }
                }
                catch (IOException ex)
                {
                    _logger.LogWarning(ex, "Error processing intercepted SYN");
                }
                catch (ArgumentOutOfRangeException ex)
                {
                    _logger.LogWarning(ex, "Error processing intercepted SYN");
                }
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogDebug("SYN intercept loop cancelled");
        }
        catch (IOException ex)
        {
            _logger.LogError(ex, "SYN intercept loop crashed");
            SetState(ProxifierState.Running, ProxifierState.Failed);
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            _logger.LogError(ex, "SYN intercept loop crashed");
            SetState(ProxifierState.Running, ProxifierState.Failed);
        }

        _logger.LogDebug("SYN intercept loop ended");
    }

    /// <summary>
    /// Модифицировать IPv4 TCP SYN пакет: заменить dst IP на 127.0.0.1,
    /// dst port на relayPort. Checksum пересчитается в SendPacket.
    /// </summary>
    private static byte[] ModifySynDestination(byte[] raw, int relayPort)
    {
        var modified = (byte[])raw.Clone();

        // IPv4 header length
        int ipHdrLen = (modified[0] & 0x0F) * 4;

        // Заменить dst IP (bytes 16-19) на 127.0.0.1
        modified[16] = 0x7F;
        modified[17] = 0x00;
        modified[18] = 0x00;
        modified[19] = 0x01;

        // Заменить dst port (TCP header bytes 2-3) на relayPort (network byte order)
        modified[ipHdrLen + 2] = (byte)(relayPort >> 8);
        modified[ipHdrLen + 3] = (byte)(relayPort & 0xFF);

        return modified;
    }

    private async Task AcceptRelayConnectionsAsync(CancellationToken ct)
    {
        _logger.LogDebug("Relay accept loop started on port {Port}", _relayPort);

        while (!ct.IsCancellationRequested)
        {
            try
            {
                var appClient = await _relayListener!.AcceptTcpClientAsync(ct).ConfigureAwait(false);

                // Обработка relay в отдельной задаче (fire-and-forget с логированием)
                _ = HandleRelayConnectionAsync(appClient, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (SocketException ex)
            {
                _logger.LogWarning(ex, "Error accepting relay connection");
            }
            catch (IOException ex)
            {
                _logger.LogWarning(ex, "Error accepting relay connection");
            }
        }

        _logger.LogDebug("Relay accept loop ended");
    }

    /// <summary>
    /// Обработать relay соединение: приложение подключилось к relay порту.
    /// Определяем целевой адрес из NAT таблицы и устанавливаем прокси.
    /// </summary>
    private async Task HandleRelayConnectionAsync(TcpClient appClient, CancellationToken ct)
    {
        var appEndpoint = (IPEndPoint?)appClient.Client.RemoteEndPoint;
        if (appEndpoint is null)
        {
            appClient.Close();
            return;
        }

        var natKey = $"{appEndpoint.Address}:{appEndpoint.Port}";
        if (!_natTable.TryGetValue(natKey, out var natEntry))
        {
            // Попробуем найти по последнему добавленному (временное решение)
            // В реальной архитектуре NAT маппинг точный
            _logger.LogWarning("No NAT entry for relay connection from {Endpoint}", appEndpoint);
            appClient.Close();
            return;
        }

        var connectionId = Guid.NewGuid().ToString("N")[..8];

        _logger.LogDebug(
            "Relay [{Conn}]: {Process} → {DstIp}:{DstPort} (action={Action})",
            connectionId, natEntry.OriginalProcessName,
            natEntry.OriginalDstIp, natEntry.OriginalDstPort,
            natEntry.EvalResult.Action);

    try
    {
        // Определить targetHost: домен (если DnsPolicy=ThroughProxy и домен известен)
        // или IP (по умолчанию). SOCKS5 автоматически использует ATYP_DOMAINNAME
        // когда targetHost — домен, а не IP (см. Socks5Client.SendConnectAsync).
        string targetHost;
        if (natEntry.EvalResult.DnsPolicy == DnsPolicy.ThroughProxy &&
            natEntry.OriginalDomainName is not null)
        {
            targetHost = natEntry.OriginalDomainName;
            _logger.LogDebug(
                "Relay [{Conn}]: DNS-through-proxy — using domain {Domain} instead of IP {Ip}",
                connectionId, targetHost, natEntry.OriginalDstIp);
        }
        else
        {
            targetHost = natEntry.OriginalDstIp.ToString();
        }

        var targetPort = natEntry.OriginalDstPort;

            Result<TcpClient> proxyResult;

            if (natEntry.EvalResult.Action == ProxyAction.Chain &&
                natEntry.EvalResult.ChainName is not null &&
                _chainsMap.TryGetValue(natEntry.EvalResult.ChainName, out var chain))
            {
                proxyResult = await _tcpRelay.ConnectThroughChainAsync(chain, targetHost, targetPort, ct).ConfigureAwait(false);
            }
            else if (natEntry.EvalResult.Proxy is not null)
            {
                proxyResult = await _tcpRelay.ConnectToProxyAsync(natEntry.EvalResult.Proxy, targetHost, targetPort, ct).ConfigureAwait(false);
            }
            else
            {
                _logger.LogError("Relay [{Conn}]: no proxy target for action {Action}",
                    connectionId, natEntry.EvalResult.Action);
                appClient.Close();
                return;
            }

            if (!proxyResult.IsSuccess)
            {
                _logger.LogWarning("Relay [{Conn}]: proxy connection failed: {Error}",
                    connectionId, proxyResult.Error);
                appClient.Close();
                return;
            }

        var proxyClient = proxyResult.Value;

        // Зарегистрировать активное соединение
        var relay = new RelayConnection
        {
            ConnectionId = connectionId,
            AppClient = appClient,
            ProxyClient = proxyClient,
            NatEntry = natEntry,
            StartedAt = DateTime.UtcNow,
        };
        _activeRelays[connectionId] = relay;

        // Записать событие подключения в ring buffer
        EnqueueConnectionEvent(new ConnectionInfo
        {
            ConnectionId = connectionId,
            Pid = natEntry.Pid,
            ProcessName = natEntry.OriginalProcessName,
            TargetHost = natEntry.OriginalDomainName ?? natEntry.OriginalDstIp.ToString(),
            TargetPort = natEntry.OriginalDstPort,
            TargetIp = natEntry.OriginalDstIp.ToString(),
            Action = natEntry.EvalResult.Action.ToString(),
            ProxyName = natEntry.EvalResult.Proxy?.Name ?? natEntry.EvalResult.ChainName,
            DnsPolicy = natEntry.EvalResult.DnsPolicy.ToString(),
            StartedAt = relay.StartedAt,
            Status = ConnectionStatus.Active,
        });

        // Запустить двунаправленный relay
        await _tcpRelay.StartRelayAsync(appClient, proxyClient, connectionId, ct).ConfigureAwait(false);
        }
        catch (SocketException ex)
        {
            _logger.LogWarning(ex, "Relay [{Conn}]: error during relay", connectionId);
            EnqueueConnectionEvent(CloseEvent(natEntry, connectionId, ConnectionStatus.Failed));
        }
        catch (IOException ex)
        {
            _logger.LogWarning(ex, "Relay [{Conn}]: error during relay", connectionId);
            EnqueueConnectionEvent(CloseEvent(natEntry, connectionId, ConnectionStatus.Failed));
        }
        catch (OperationCanceledException)
        {
            // Relay cancelled — expected during shutdown
            EnqueueConnectionEvent(CloseEvent(natEntry, connectionId, ConnectionStatus.Closed));
        }
        finally
        {
            _activeRelays.TryRemove(connectionId, out _);
            _natTable.TryRemove(natKey, out _);
        }
    }

    // ── Live connection events (ring buffer) ──────────────

    /// <summary>
    /// Получить все соединения из ring buffer (последние N событий).
    /// </summary>
    public ConnectionInfo[] GetRecentConnections()
    {
        return _connectionEvents.ToArray();
    }

    /// <summary>
    /// Получить активные соединения (текущие relay).
    /// </summary>
    public ConnectionInfo[] GetActiveConnections()
    {
        return _connectionEvents
            .Where(c => c.Status == ConnectionStatus.Active)
            .ToArray();
    }

    private void EnqueueConnectionEvent(ConnectionInfo info)
    {
        _connectionEvents.Enqueue(info);

        // Поддерживать размер ring buffer
        while (_connectionEvents.Count > MaxConnectionEvents &&
               _connectionEvents.TryDequeue(out _))
        {
            Interlocked.Decrement(ref _connectionEventCount);
        }

        Interlocked.Increment(ref _connectionEventCount);
    }

    /// <summary>
    /// Создать ConnectionInfo для закрытого соединения.
    /// </summary>
    private ConnectionInfo CloseEvent(NatEntry natEntry, string connectionId, ConnectionStatus status)
    {
        var trafficStats = _trafficMonitor.GetConnectionStats(connectionId);

        return new ConnectionInfo
        {
            ConnectionId = connectionId,
            Pid = natEntry.Pid,
            ProcessName = natEntry.OriginalProcessName,
            TargetHost = natEntry.OriginalDomainName ?? natEntry.OriginalDstIp.ToString(),
            TargetPort = natEntry.OriginalDstPort,
            TargetIp = natEntry.OriginalDstIp.ToString(),
            Action = natEntry.EvalResult.Action.ToString(),
            ProxyName = natEntry.EvalResult.Proxy?.Name ?? natEntry.EvalResult.ChainName,
            DnsPolicy = natEntry.EvalResult.DnsPolicy.ToString(),
            StartedAt = DateTime.UtcNow, // Approximation — actual start time is in RelayConnection
            EndedAt = DateTime.UtcNow,
            BytesSent = trafficStats?.BytesSent ?? 0,
            BytesReceived = trafficStats?.BytesReceived ?? 0,
            Status = status,
        };
    }

    // ── State management ────────────────────────────────────

    private bool SetState(ProxifierState expected, ProxifierState newValue)
    {
        int oldValue = (int)expected;
        int newVal = (int)newValue;
        if (Interlocked.CompareExchange(ref _state, newVal, oldValue) == oldValue)
        {
            StateChanged?.Invoke(newValue);
            return true;
        }
        return false;
    }

    // ── Dispose ─────────────────────────────────────────────

    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
    }
}

// ── Вспомогательные типы ─────────────────────────────────────

/// <summary>
/// Действие проксификатора для перехваченного SYN.
/// </summary>
public sealed class ProxifierAction
{
    /// <summary>Тип действия.</summary>
    public ProxifierActionType Type { get; init; }

    /// <summary>Порт relay (если Type = Redirect).</summary>
    public int RelayPort { get; init; }

    /// <summary>Пропустить (переинжектировать без изменений).</summary>
    public static ProxifierAction Pass { get; } = new() { Type = ProxifierActionType.Pass };

    /// <summary>Отбросить (заблокировать соединение).</summary>
    public static ProxifierAction Drop { get; } = new() { Type = ProxifierActionType.Drop };

    /// <summary>Перенаправить на relay порт.</summary>
    public static ProxifierAction Redirect(int relayPort) => new()
    {
        Type = ProxifierActionType.Redirect,
        RelayPort = relayPort,
    };
}

/// <summary>
/// Тип действия проксификатора.
/// </summary>
public enum ProxifierActionType
{
    /// <summary>Пропустить пакет.</summary>
    Pass,
    /// <summary>Отбросить пакет.</summary>
    Drop,
    /// <summary>Перенаправить на relay порт.</summary>
    Redirect,
}

/// <summary>
/// Запись в NAT таблице: сопоставление оригинального адреса с relay.
/// </summary>
public sealed class NatEntry
{
    /// <summary>Оригинальный IP назначения.</summary>
    public required IPAddress OriginalDstIp { get; init; }

    /// <summary>Оригинальный порт назначения.</summary>
    public required int OriginalDstPort { get; init; }

    /// <summary>
    /// Оригинальное доменное имя (если известно через reverse DNS cache).
    /// Используется для DNS-through-proxy: SOCKS5 отправит домен вместо IP.
    /// </summary>
    public string? OriginalDomainName { get; init; }

    /// <summary>Имя процесса инициировавшего соединение.</summary>
    public required string OriginalProcessName { get; init; }

    /// <summary>Результат оценки правила.</summary>
    public required RuleEvalResult EvalResult { get; init; }

    /// <summary>PID процесса.</summary>
    public required int Pid { get; init; }

    /// <summary>Порт relay listener (для перенаправления).</summary>
    public int RelayPort { get; set; }
}

/// <summary>
/// Активное relay соединение.
/// </summary>
public sealed class RelayConnection
{
    public required string ConnectionId { get; init; }
    public required TcpClient AppClient { get; init; }
    public required TcpClient ProxyClient { get; init; }
    public required NatEntry NatEntry { get; init; }
    public required DateTime StartedAt { get; init; }
}
