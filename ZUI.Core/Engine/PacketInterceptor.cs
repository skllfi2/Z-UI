// ═══════════════════════════════════════════════════════════════
// ZUI.Core / Engine / PacketInterceptor.cs
// Главный цикл перехвата и обработки пакетов
// Open WinDivert → Read → DpiBypassEngine → Send replacements
// IAsyncDisposable, CancellationToken, graceful shutdown
// ═══════════════════════════════════════════════════════════════

using System.Net;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ZUI.Core.Desync;
using ZUI.Core.Intercept;
using ZUI.Core.Rules;
using ZUI.Core.WinDivert;

namespace ZUI.Core.Engine;

/// <summary>
/// Статус перехватчика.
/// </summary>
public enum InterceptorState
{
    Stopped,
    Starting,
    Running,
    Stopping,
    Failed,
}

/// <summary>
/// Главный цикл перехвата и обработки пакетов DPI bypass.
/// Жизненный цикл: StartAsync → (работает) → StopAsync.
/// Использует WinDivertInterceptor для I/O и DpiBypassEngine для логики.
/// </summary>
public sealed class PacketInterceptor : IAsyncDisposable
{
    private readonly ILogger _logger;
    private readonly WinDivertInterceptor _interceptor;
    private readonly DpiBypassEngine _engine;
    private readonly PidMapper _pidMapper;
    private readonly DomainListLoader _domainLoader;

    private CancellationTokenSource? _cts;
    private Task? _processingTask;
    private int _state;

    /// <summary>Текущий статус перехватчика.</summary>
    public InterceptorState State => (InterceptorState)Volatile.Read(ref _state);

    /// <summary>Событие изменения состояния.</summary>
    public event Action<InterceptorState>? StateChanged;

    /// <summary>Событие ошибки.</summary>
    public event Action<string>? OnError;

    /// <summary>Событие обработки пакета (для диагностики/UI).</summary>
    public event Action<PacketAction, string?>? OnPacketProcessed;

    public PacketInterceptor(
        WinDivertInterceptor interceptor,
        DpiBypassEngine engine,
        PidMapper pidMapper,
        DomainListLoader domainLoader,
        ILogger<PacketInterceptor>? logger = null)
    {
        _interceptor = interceptor;
        _engine = engine;
        _pidMapper = pidMapper;
        _domainLoader = domainLoader;
        _logger = logger ?? NullLoggerFactory.Instance.CreateLogger<PacketInterceptor>();
    }

    // ── Start / Stop ────────────────────────────────────────

    /// <summary>
    /// Запустить перехват пакетов с указанной стратегией.
    /// Открывает WinDivert handle, загружает списки, запускает цикл обработки.
    /// </summary>
    public async Task<Result> StartAsync(StrategyConfig strategy, CancellationToken ct = default)
    {
        if (!SetState(InterceptorState.Stopped, InterceptorState.Starting))
            return Result.Failed($"Cannot start: current state is {State}");

        try
        {
            // 1. Загрузить списки доменов и IP
            var loadResult = await LoadAllListsAsync(strategy, ct).ConfigureAwait(false);
            if (!loadResult.IsSuccess)
            {
                SetState(InterceptorState.Failed, InterceptorState.Starting);
                return Result.Failed($"Failed to load lists: {loadResult.Error}");
            }

            // 2. Установить стратегию в движок
            _engine.CurrentStrategy = strategy;
            _engine.IsEnabled = true;
            _engine.ResetStats();

            // 3. Построить WinDivert фильтр
            var filterResult = FilterStringBuilder.Build(strategy);
            if (!filterResult.IsSuccess)
            {
                SetState(InterceptorState.Failed, InterceptorState.Starting);
                return Result.Failed($"Failed to build filter: {filterResult.Error}");
            }

            string filter = filterResult.Value;
            _logger.LogInformation("Starting PacketInterceptor with filter: {Filter}", filter);

            // 4. Открыть WinDivert handle
            var openResult = _interceptor.Open(filter);
            if (!openResult.IsSuccess)
            {
                SetState(InterceptorState.Failed, InterceptorState.Starting);
                return Result.Failed($"Failed to open WinDivert: {openResult.Error}");
            }

            // 5. Запустить цикл обработки
            _cts = new CancellationTokenSource();
            var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token, ct);

            _processingTask = ProcessPacketsLoopAsync(linkedCts.Token);

            SetState(InterceptorState.Starting, InterceptorState.Running);
            _logger.LogInformation("PacketInterceptor started successfully");

            return Result.Success();
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            SetState(InterceptorState.Failed, InterceptorState.Starting);
            _logger.LogError(ex, "Failed to start PacketInterceptor");
            return Result.Failed($"Start failed: {ex.Message}");
        }
        catch (DllNotFoundException ex)
        {
            SetState(InterceptorState.Failed, InterceptorState.Starting);
            _logger.LogError(ex, "Failed to start PacketInterceptor");
            return Result.Failed($"Start failed: {ex.Message}");
        }
        catch (InvalidOperationException ex)
        {
            SetState(InterceptorState.Failed, InterceptorState.Starting);
            _logger.LogError(ex, "Failed to start PacketInterceptor");
            return Result.Failed($"Start failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Остановить перехват пакетов.
    /// </summary>
    public async Task StopAsync()
    {
        if (State is not (InterceptorState.Running or InterceptorState.Failed))
            return;

        SetState(InterceptorState.Running, InterceptorState.Stopping);
        _logger.LogInformation("Stopping PacketInterceptor...");

        // 1. Shutdown WinDivert (прерывает Recv)
        _interceptor.Shutdown();

        // 2. Отменить токен
        _cts?.Cancel();

        // 3. Дождаться завершения цикла обработки
        if (_processingTask is not null)
        {
            try
            {
                await _processingTask.ConfigureAwait(false);
            }
        catch (OperationCanceledException)
        {
            // Expected
        }
        catch (IOException ex)
        {
            _logger.LogWarning(ex, "Error during processing task shutdown");
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "Error during processing task shutdown");
        }
        }

        // 4. Закрыть WinDivert handle
        await _interceptor.DisposeAsync().ConfigureAwait(false);

        // 5. Отключить движок
        _engine.IsEnabled = false;

        _cts?.Dispose();
        _cts = null;
        _processingTask = null;

        SetState(InterceptorState.Stopping, InterceptorState.Stopped);
        _logger.LogInformation("PacketInterceptor stopped");
    }

    // ── Главный цикл обработки пакетов ──────────────────────

    private async Task ProcessPacketsLoopAsync(CancellationToken ct)
    {
        _logger.LogDebug("Packet processing loop started");

        try
        {
            await foreach (var (packet, addr) in _interceptor.ReadPacketsAsync(ct).ConfigureAwait(false))
            {
                try
                {
                ProcessSinglePacket(packet, addr);
            }
            catch (IOException ex)
            {
                _logger.LogWarning(ex, "Error processing packet");
            }
            catch (ArgumentOutOfRangeException ex)
            {
                _logger.LogWarning(ex, "Error processing packet");
            }
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogDebug("Packet processing loop cancelled");
        }
        catch (IOException ex)
        {
            _logger.LogError(ex, "Packet processing loop crashed");
            SetState(InterceptorState.Running, InterceptorState.Failed);
            OnError?.Invoke($"Processing loop crashed: {ex.Message}");
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            _logger.LogError(ex, "Packet processing loop crashed");
            SetState(InterceptorState.Running, InterceptorState.Failed);
            OnError?.Invoke($"Processing loop crashed: {ex.Message}");
        }

        _logger.LogDebug("Packet processing loop ended");
    }

    /// <summary>
    /// Обработать один перехваченный пакет.
    /// </summary>
    private void ProcessSinglePacket(ParsedPacket packet, WinDivertAddress addr)
    {
        // Заполняем PID из PidMapper
        if (packet.SrcPort != 0)
        {
            // ParsedPacket.ProcessId = 0 от WinDivertInterceptor
            // PidMapper заполняет через IP Helper API
            // (ProcessId readonly — заполняется через Flow layer или PidMapper)
        }

        // Обработка через движок десинхронизации
        var result = _engine.ProcessPacket(packet, addr);

        switch (result.Action)
        {
            case PacketAction.Pass:
                // Переинжектировать оригинальный пакет без изменений
                _interceptor.SendPacket(packet.RawPacket, ref addr);
                break;

            case PacketAction.Drop:
                // Не переинжектировать — пакет отброшен
                break;

            case PacketAction.Replace:
                // Отправить заменяющие пакеты, оригинальный отбросить
                if (result.Replacements is not null)
                {
                    // Для disorder: сначала отправить сегменты с SendBeforeOriginal=true
                    foreach (var replacement in result.Replacements.Where(r => r.SendBeforeOriginal))
                    {
                        var sendAddr = replacement.Addr;
                        _interceptor.SendPacket(replacement.Packet, ref sendAddr);
                    }

                    // Потом отправить остальные заменяющие пакеты
                    foreach (var replacement in result.Replacements.Where(r => !r.SendBeforeOriginal))
                    {
                        var sendAddr = replacement.Addr;
                        _interceptor.SendPacket(replacement.Packet, ref sendAddr);
                    }

                    // Для Fake: после fake-пакетов отправить оригинальный
                    // (DpiBypassResult.Replacements содержит fake пакеты,
                    //  оригинальный нужно отправить отдельно)
                    if (ShouldSendOriginalAfterFake(result))
                    {
                        _interceptor.SendPacket(packet.RawPacket, ref addr);
                    }
                }
                break;
        }

        OnPacketProcessed?.Invoke(result.Action, result.Reason);
    }

    /// <summary>
    /// Нужно ли отправить оригинальный пакет после fake.
    /// Для fake/fakedsplit: оригинальный пакет отправляется после fake-пакетов.
    /// Для multisplit/disorder: оригинальный заменяется сегментами.
    /// </summary>
    private static bool ShouldSendOriginalAfterFake(DpiBypassResult result)
    {
        if (result.Replacements is null)
            return false;

        // Если replacements содержат fake-пакеты (Impostor=true),
        // оригинальный пакет нужно отправить после них
        // Простая эвристика: если Result имеет Action=Replace и
        // содержит fake пакеты — отправить оригинальный тоже
        // Более точная логика: DpiBypassEngine помечает fake пакеты

        // В текущей архитектуре: Fake и FakeSplit отправляют fake-пакеты
        // ПЕРЕД оригинальным. Оригинальный нужно переинжектировать.
        // MultiSplit и MultiDisorder ЗАМЕНЯЮТ оригинальный.
        // Различие: если есть Impostor-пакеты → отправить оригинальный.

        // Упрощение: проверяем Reason
        return result.Reason is "Fake" or "FakeSplit";
    }

    // ── Загрузка списков ────────────────────────────────────

    private async Task<Result> LoadAllListsAsync(StrategyConfig strategy, CancellationToken ct)
    {
        if (strategy.Rules is null || strategy.Rules.Length == 0)
            return Result.Success();

        var errors = new List<string>();

        foreach (var rule in strategy.Rules)
        {
            // HostLists
            if (rule.HostLists is not null)
            {
                foreach (var list in rule.HostLists)
                {
                    if (!_domainLoader.IsDomainListLoaded(list))
                    {
                        var r = await _domainLoader.LoadDomainListAsync(list, ct).ConfigureAwait(false);
                        if (!r.IsSuccess) errors.Add(r.Error!);
                    }
                    else
                    {
                        await _domainLoader.ReloadIfChangedAsync(list, ct).ConfigureAwait(false);
                    }
                }
            }

            // HostExcludeLists
            if (rule.HostExcludeLists is not null)
            {
                foreach (var list in rule.HostExcludeLists)
                {
                    if (!_domainLoader.IsDomainListLoaded(list))
                    {
                        var r = await _domainLoader.LoadDomainListAsync(list, ct).ConfigureAwait(false);
                        if (!r.IsSuccess) errors.Add(r.Error!);
                    }
                    else
                    {
                        await _domainLoader.ReloadIfChangedAsync(list, ct).ConfigureAwait(false);
                    }
                }
            }

            // IpsetLists
            if (rule.IpsetLists is not null)
            {
                foreach (var list in rule.IpsetLists)
                {
                    if (!_domainLoader.IsIpsetLoaded(list))
                    {
                        var r = await _domainLoader.LoadIpsetAsync(list, ct).ConfigureAwait(false);
                        if (!r.IsSuccess) errors.Add(r.Error!);
                    }
                    else
                    {
                        await _domainLoader.ReloadIpsetIfChangedAsync(list, ct).ConfigureAwait(false);
                    }
                }
            }

            // IpsetExcludeLists
            if (rule.IpsetExcludeLists is not null)
            {
                foreach (var list in rule.IpsetExcludeLists)
                {
                    if (!_domainLoader.IsIpsetLoaded(list))
                    {
                        var r = await _domainLoader.LoadIpsetAsync(list, ct).ConfigureAwait(false);
                        if (!r.IsSuccess) errors.Add(r.Error!);
                    }
                    else
                    {
                        await _domainLoader.ReloadIpsetIfChangedAsync(list, ct).ConfigureAwait(false);
                    }
                }
            }
        }

        // Ошибки загрузки отдельных файлов — не критичны
        // (стратегия может работать с частично загруженными списками)
        if (errors.Count > 0)
            _logger.LogWarning("Some lists failed to load: {Errors}", string.Join("; ", errors));

        return Result.Success();
    }

    // ── State management ────────────────────────────────────

    private bool SetState(InterceptorState expected, InterceptorState newValue)
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
