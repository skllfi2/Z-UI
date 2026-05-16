// ═══════════════════════════════════════════════════════════════
// ZUI.Worker / WorkerService.cs
// BackgroundService для запуска Worker Service (SYSTEM)
// Хостит IPC сервер + Orchestrator, периодически отправляет
// статистику подключённым UI клиентам
// ═══════════════════════════════════════════════════════════════

using System.ComponentModel;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ZUI.Ipc;

namespace ZUI.Worker;

/// <summary>
/// Worker Service — фоновый сервис, запускаемый под SYSTEM.
/// Жизненный цикл:
/// 1. StartAsync: запускает IPC сервер + инициализирует Orchestrator
/// 2. ExecuteAsync: основной цикл (отправка статистики, обработка событий)
/// 3. StopAsync: останавливает Orchestrator + IPC сервер
/// </summary>
public sealed class WorkerService : BackgroundService
{
    private const int StatsIntervalMs = 2000; // Отправка статистики каждые 2 сек

    private readonly ILogger _logger;
    private readonly IpcPipeServer _ipcServer;
    private readonly Orchestrator _orchestrator;
    private readonly string _zapretDir;

    /// <summary>
    /// Создать Worker Service.
    /// </summary>
    public WorkerService(
        IpcPipeServer ipcServer,
        Orchestrator orchestrator,
        ILogger<WorkerService> logger)
    {
        _ipcServer = ipcServer;
        _orchestrator = orchestrator;
        _logger = logger;

        // Определяем директорию zapret: рядом с исполняемым файлом
        _zapretDir = FindZapretDirectory();
    }

    // ── Запуск ─────────────────────────────────────────────

    public override async Task StartAsync(CancellationToken ct)
    {
        _logger.LogInformation("Z-UI Worker Service starting...");

        // Немедленно сообщаем SCM что служба запущена (избегаем timeout 1053)
        await base.StartAsync(ct).ConfigureAwait(false);

        _logger.LogInformation("Zapret directory: {Dir}", _zapretDir);
    }

    // ── Основной цикл ──────────────────────────────────────

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        // 1. Запустить IPC сервер
        var ipcResult = _ipcServer.Start(ct);
        if (!ipcResult.IsSuccess)
        {
            _logger.LogError("Failed to start IPC server: {Error}", ipcResult.Error);
        }

        // 2. Подписаться на отправку сообщений от Orchestrator
        _orchestrator.OnSendMessage += async message =>
        {
            await _ipcServer.SendToAllAsync(message, ct).ConfigureAwait(false);
        };

        // 3. Инициализировать Orchestrator (загрузка стратегий)
        var initResult = await _orchestrator.InitializeAsync(_zapretDir, ct).ConfigureAwait(false);
        if (!initResult.IsSuccess)
        {
            _logger.LogError("Failed to initialize Orchestrator: {Error}", initResult.Error);
        }

        // 4. Загрузить списки доменов для DNS (Fake DNS)
        var listsDir = Path.Combine(_zapretDir, "lists");
        if (Directory.Exists(listsDir))
        {
            var dnsListResult = await _orchestrator.LoadDnsDomainListsAsync(listsDir, ct).ConfigureAwait(false);
            if (!dnsListResult.IsSuccess)
            {
                _logger.LogWarning("Failed to load DNS domain lists: {Error}", dnsListResult.Error);
            }
        }

        _logger.LogInformation("Z-UI Worker Service initialized successfully");

        // 5. Основной цикл отправки статистики
        _logger.LogInformation("Worker service execution loop started");

        using var statsTimer = new PeriodicTimer(TimeSpan.FromMilliseconds(StatsIntervalMs));

        try
        {
            while (!ct.IsCancellationRequested)
            {
                await statsTimer.WaitForNextTickAsync(ct).ConfigureAwait(false);

                // Периодическая отправка статистики подключённым UI клиентам
                if (_ipcServer.IsRunning)
                {
                    try
                    {
                        var stats = _orchestrator.GetPacketStats();
                        await _ipcServer.SendToAllAsync(stats, ct).ConfigureAwait(false);

                        // Проверить таймауты соединений для block analyzer
                        _orchestrator.CheckBlockTimeouts();
                    }
                    catch (IOException ex)
                    {
                        _logger.LogDebug(ex, "Failed to send stats to UI clients");
                    }
                    catch (InvalidOperationException ex)
                    {
                        _logger.LogDebug(ex, "Failed to send stats to UI clients");
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Worker service execution loop cancelled");
        }
        catch (IOException ex)
        {
            _logger.LogError(ex, "Worker service execution loop crashed");
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogError(ex, "Worker service execution loop crashed");
        }
        catch (Win32Exception ex)
        {
            _logger.LogError(ex, "Worker service execution loop crashed");
        }
    }

    // ── Остановка ──────────────────────────────────────────

    public override async Task StopAsync(CancellationToken ct)
    {
        _logger.LogInformation("Z-UI Worker Service stopping...");

        // 1. Остановить Orchestrator (остановит все модули)
        await _orchestrator.DisposeAsync().ConfigureAwait(false);

        // 2. Остановить IPC сервер
        await _ipcServer.DisposeAsync().ConfigureAwait(false);

        _logger.LogInformation("Z-UI Worker Service stopped");

        await base.StopAsync(ct).ConfigureAwait(false);
    }

    // ── Поиск директории zapret ────────────────────────────

    /// <summary>
    /// Найти директорию zapret с WinDivert и стратегиями.
    /// Порядок поиска:
    /// 1. Рядом с исполняемым файлом (Z-UI/zapret/)
    /// 2. На уровень выше (Z-UI/ — для разработки)
    /// 3. Текущая директория
    /// </summary>
    internal static string FindZapretDirectory()
    {
        var baseDir = AppContext.BaseDirectory;

        // 1. Текущая директория (для разработки: Z-UI/bin/.../ZUI.Worker/)
        //    zapret находится на уровень выше в base output
        var parentPath = Path.GetFullPath(Path.Combine(baseDir, ".."));
        var parentZapret = Path.Combine(parentPath, "zapret");
        if (Directory.Exists(parentZapret) &&
            File.Exists(Path.Combine(parentZapret, "WinDivert.dll")))
        {
            return parentZapret;
        }

        // 2. Рядом с исполняемым файлом (production: ZUI.Worker/zapret/)
        var localPath = Path.Combine(baseDir, "zapret");
        if (Directory.Exists(localPath) &&
            File.Exists(Path.Combine(localPath, "WinDivert.dll")))
        {
            return localPath;
        }

        // 3. ZUI.Worker/../../Z-UI/zapret/ (для разработки из repo root)
        var devPath = Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "..",
            "Z-UI", "zapret"));
        if (Directory.Exists(devPath) &&
            File.Exists(Path.Combine(devPath, "WinDivert.dll")))
        {
            return devPath;
        }

        // 4. Fallback — рядом с исполняемым (даже если WinDivert.dll нет)
        return localPath;
    }
}
