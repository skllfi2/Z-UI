// ═══════════════════════════════════════════════════════════════
// ZUI.Core / Engine / NetworkShieldFacade.cs
// Реализация INetworkShield — фасад ядра системы
// Делегирует: PassiveBlockAnalyzer + PacketInterceptor + TrafficMonitor
// ═══════════════════════════════════════════════════════════════

using Microsoft.Extensions.Logging;
using ZUI.Core.Engine;
using ZUI.Core.Traffic;

namespace ZUI.Core.Engine;

/// <summary>
/// Реализация INetworkShield — фасад ядра системы.
/// Делегирует компонентам: PassiveBlockAnalyzer, PacketInterceptor, TrafficMonitor.
/// </summary>
public sealed class NetworkShieldFacade : INetworkShield
{
    private readonly PassiveBlockAnalyzer _blockAnalyzer;
    private readonly PacketInterceptor _interceptor;
    private readonly TrafficMonitor _trafficMonitor;
    private readonly ILogger<NetworkShieldFacade> _logger;

    public IBlockDetector BlockDetector => _blockDetectorAdapter;
    public IBypassEngine BypassEngine => _bypassEngineAdapter;
    public ITrafficWatch TrafficWatch => _trafficWatchAdapter;

    public bool IsRunning => _interceptor.State == InterceptorState.Running;

    private readonly BlockDetectorAdapter _blockDetectorAdapter;
    private readonly BypassEngineAdapter _bypassEngineAdapter;
    private readonly TrafficWatchAdapter _trafficWatchAdapter;

    public NetworkShieldFacade(
        PassiveBlockAnalyzer blockAnalyzer,
        PacketInterceptor interceptor,
        TrafficMonitor trafficMonitor,
        ILogger<NetworkShieldFacade> logger)
    {
        _blockAnalyzer = blockAnalyzer;
        _interceptor = interceptor;
        _trafficMonitor = trafficMonitor;
        _logger = logger;

        _blockDetectorAdapter = new BlockDetectorAdapter(blockAnalyzer);
        _bypassEngineAdapter = new BypassEngineAdapter(interceptor);
        _trafficWatchAdapter = new TrafficWatchAdapter(trafficMonitor);
    }

    public async Task<Result> StartAsync(string strategyId, CancellationToken ct = default)
    {
        _logger.LogInformation("NetworkShield: starting with strategy {Strategy}", strategyId);

        // Запуск через PacketInterceptor (BypassEngine)
        return await _bypassEngineAdapter.StartAsync(strategyId, ct).ConfigureAwait(false);
    }

    public async Task<Result> StopAsync(CancellationToken ct = default)
    {
        _logger.LogInformation("NetworkShield: stopping");
        return await _bypassEngineAdapter.StopAsync(ct).ConfigureAwait(false);
    }
}

// ── Adapters ────────────────────────────────────────────────

/// <summary>
/// Адаптер PassiveBlockAnalyzer → IBlockDetector.
/// </summary>
internal sealed class BlockDetectorAdapter : IBlockDetector
{
    private readonly PassiveBlockAnalyzer _analyzer;

    public int TotalBlocks => _analyzer.GetRecentBlocks(int.MaxValue).Length;

    public event Action<BlockRecord>? OnBlockDetected
    {
        add => _analyzer.OnBlockDetected += value;
        remove => _analyzer.OnBlockDetected -= value;
    }

    public BlockDetectorAdapter(PassiveBlockAnalyzer analyzer)
    {
        _analyzer = analyzer;
    }

    public BlockRecord[] GetRecentBlocks(int limit = 20) => _analyzer.GetRecentBlocks(limit);
    public BlockStats GetStats() => _analyzer.GetStats();
    public void ClearBlocks() => _analyzer.Clear();
}

/// <summary>
/// Адаптер PacketInterceptor → IBypassEngine.
/// </summary>
internal sealed class BypassEngineAdapter : IBypassEngine
{
    private readonly PacketInterceptor _interceptor;

    public bool IsRunning => _interceptor.State == InterceptorState.Running;
    public string? ActiveStrategy => null; // TODO: получить из стратегии

    public BypassEngineAdapter(PacketInterceptor interceptor)
    {
        _interceptor = interceptor;
    }

    public async Task<Result> StartAsync(string strategyId, CancellationToken ct = default)
    {
        // PacketInterceptor.StartAsync требует StrategyConfig
        // Фасад принимает только ID — загрузка стратегии через StrategyConfigLoader
        // Это заглушка, реальная реализация требует StrategyConfigLoader
        return Result.Failed("Use PacketInterceptor.StartAsync with StrategyConfig");
    }

    public async Task<Result> StopAsync(CancellationToken ct = default)
    {
        await _interceptor.DisposeAsync().ConfigureAwait(false);
        return Result.Success();
    }

    public BypassStats GetStats()
    {
        return new BypassStats
        {
            PacketsProcessed = 0,
            PacketsBypassed = 0,
            UptimeSeconds = 0,
            ActiveConnections = 0,
        };
    }
}

/// <summary>
/// Адаптер TrafficMonitor → ITrafficWatch.
/// </summary>
internal sealed class TrafficWatchAdapter : ITrafficWatch
{
    private readonly TrafficMonitor _monitor;

    public TrafficWatchAdapter(TrafficMonitor monitor)
    {
        _monitor = monitor;
    }

    public TrafficSnapshot GetGlobalStats() => _monitor.GetSnapshot();

    public AppTrafficInfo[] GetAppStats()
    {
        // TrafficMonitor не хранит per-app данные напрямую
        // Это заглушка, полная реализация требует интеграции с ProxifierEngine
        return [];
    }

    public DomainTrafficInfo[] GetDomainStats()
    {
        // Требуется интеграция с DnsReverseCache для per-domain статистики
        return [];
    }

    public void Reset() => _monitor.Reset();
}
