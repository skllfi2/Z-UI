// LocalizationService.cs - Localization service with Russian/English dictionaries
// Supports runtime language switching via IAppSettingsService.Language
namespace ZUI.Services;

/// <summary>
/// Provides localized strings by key.
/// Built-in Russian (default) and English dictionaries.
/// Supports runtime language switching.
/// </summary>
public static class LocalizationService
{
    private static readonly Dictionary<string, string> Russian = new(StringComparer.OrdinalIgnoreCase)
    {
        // ── Status ────────────────────────────────────────────
        ["Running"] = "Работает",
        ["Stopped"] = "Остановлен",
        ["ProtectionOff"] = "Защита выключена",
        ["DpiBypassActive"] = "Обход DPI активен",
        ["ServiceStatus"] = "Статус сервиса",
        ["StartedMale"] = "Запущен",
        ["StoppedMale"] = "Остановлен",

        // ── Strategy descriptions ─────────────────────────────
        ["StrategyDescGeneral"] = "Стандартная протекция для обхода популярных сетей",
        ["StrategyDescDiscord"] = "Оптимизировано для Discord (голос, сообщения)",
        ["StrategyDescYouTube"] = "Оптимизировано для YouTube (видео, стримы)",
        ["StrategyDescRussia"] = "Российские провайдеры (МТС, Ростелеком, Билайн)",
        ["StrategyDescGaming"] = "Игровые сервисы (PoE2, Valorant, Steam)",
        ["StrategyDescCustom"] = "Пользовательская стратегия",

// ── Dashboard ─────────────────────────────────────────
    ["Start"] = "Старт",
    ["Stop"] = "Стоп",
    ["NotDetected"] = "Не определён",
        ["Detecting"] = "Определяется...",
        ["Configured"] = "Настроен",
        ["Active"] = "Активен",
        ["Disabled"] = "Выключен",
        ["Default"] = "По умолчанию",
        ["AutoStart"] = "автозапуск",
        ["AutoUpdates"] = "авто-обновления",
        ["PressToCheck"] = "Нажмите для проверки",
        ["VersionCheckError"] = "Ошибка проверки",
        ["AdminRequired"] = "Требуются права администратора",
        ["Error"] = "Ошибка",
        ["PreparingUpdate"] = "Подготовка к обновлению...",
        ["ChangelogLoading"] = "Список изменений загружается...",
        ["UpdateUnavailable"] = "Обновление временно недоступно",

        // ── Ipset ─────────────────────────────────────────────
        ["IpsetAny"] = "Любой IP адрес (any)",
        ["IpsetLoaded"] = "Загружен список IP",
        ["IpsetLoadedCount"] = "Загружен список: {0} записей",
        ["IpsetNone"] = "Фильтрация по IP отключена",

        // ── Diagnostics ───────────────────────────────────────
        ["ReadyToCheck"] = "Готово к проверке",
        ["NotChecked"] = "Не проверено",
        ["DiagnosticsRunning"] = "Выполняется диагностика...",
        ["AdminRightsCheck"] = "Права администратора",
        ["DomainListsCheck"] = "Списки доменов",
        ["BinaryFilesCheck"] = "Бинарные файлы",
        ["NetworkConnectivity"] = "Сетевое подключение",
        ["DiagnosticsPassed"] = "Все проверки пройдены ({0}/{1})",
        ["DiagnosticsFailed"] = "Обнаружены проблемы ({0}/{1})",
        ["DiagnosticsCompleted"] = "Диагностика завершена",
        ["QuickCheckRunning"] = "Выполняется быстрая проверка...",
        ["ExportedTo"] = "Результаты экспортированы: {0}",
        ["ExportError"] = "Ошибка экспорта: {0}",
        ["FixAdminRights"] = "Перезапуск с правами администратора...",
        ["FixAdminFailed"] = "Не удалось перезапуститься: {0}",
        ["FixWorkerNote"] = "Проверьте, что Worker Service запущен. Перезапустите приложение.",
        ["FixDomainListsNote"] = "Списки доменов загружаются через MalwLinkUpdateService при первом запуске.",
        ["FixBinaryFilesNote"] = "Убедитесь, что директория zapret/ содержит winws.exe и другие бинарные файлы.",
        ["FixNetworkNote"] = "Проверьте подключение к интернету и попробуйте открыть сайт вручную.",
        ["Yes"] = "Да",
        ["No"] = "Нет",

        // ── DNS ───────────────────────────────────────────────
        ["Checking"] = "Проверка...",
        ["RestartAppAsAdmin"] = "Перезапустите приложение с правами администратора",
        ["StartingDnsProxy"] = "Запуск DNS Proxy...",
        ["StoppingDnsProxy"] = "Остановка DNS Proxy...",

        // ── Generator ─────────────────────────────────────────
        ["NotSelected"] = "Не выбран",
        ["AutoStrategy"] = "Auto (автовыбор)",
        ["AutoStrategyDesc"] = "Автоматический выбор стратегии",
        ["Testing"] = "Тестируется...",
        ["UnknownError"] = "Неизвестная ошибка",
        ["ProviderDetectionError"] = "Ошибка определения",
        ["ProviderDetectionFailed"] = "Не удалось определить провайдера: {0}",
        ["DetectingProvider"] = "Определение провайдера...",
        ["ProviderNotDetected"] = "Не определён",
        ["ProviderNotDetectedInfo"] = "Провайдер не найден в базе. Будет использована стратегия по умолчанию.",
        ["Method"] = "Метод: {0}",
        ["Confidence"] = "Уверенность: {0}%",
["GenerationError"] = "Ошибка генерации",
    ["ReadyStrategies"] = "Готовые стратегии",
    ["CreateStrategy"] = "Создание стратегии",
    ["DpiMethodSelection"] = "Метод обхода",
    ["DpiMethodSelectionDesc"] = "Выберите метод DPI обхода и настройте параметры:",
    ["FoolingMode"] = "Режим fooling:",
    ["FakeRepeats"] = "Количество повторов:",
    ["SplitParams"] = "Параметры разбиения:",
    ["FakedsplitPattern"] = "Паттерн fakedsplit:",
    ["HostfakesplitMod"] = "Модификатор hostfakesplit:",
    ["CombineMultidisorder"] = "Комбинировать с multidisorder",
    ["CombineMultidisorderOn"] = "syndata + multidisorder",
    ["CombineMultidisorderOff"] = "Только syndata",
    ["Stability"] = "Стабильность",
    ["CurrentStrategy"] = "Текущая стратегия",
    ["StrategySelection"] = "Выбор стратегии",
    ["TestStrategyBtn"] = "Тестировать стратегию",
    ["RefreshList"] = "Обновить список",
    ["SelectMethod"] = "Выберите метод",

    // ── Strategy page ─────────────────────────────────────
        ["Applying"] = "Применяется...",

        // ── Proxifier ─────────────────────────────────────────
        ["ProxifierStopped"] = "Проксификатор выключен",
        ["EnableProxifier"] = "Включить",
        ["StartingProxifier"] = "Запуск проксификатора...",
        ["ProxifierStarted"] = "Проксификатор запущен",
        ["StoppingProxifier"] = "Остановка проксификатора...",
        ["ProxifierActive"] = "Проксификатор активен",
        ["DisableProxifier"] = "Выключить",

        // ── TgProxy ───────────────────────────────────────────
        ["Socks5Started"] = "SOCKS5 proxy запущен",
        ["InvalidWsUrl"] = "Неверный WebSocket URL",
        ["MtProxyStarted"] = "MTProxy запущен",
        ["InvalidMtProxySecret"] = "Неверный секрет MTProxy",
        ["StopError"] = "Ошибка остановки: {0}",

        // ── Settings ──────────────────────────────────────────
        ["BySystem"] = "По системе",
        ["AppVersion"] = "Версия приложения: {0}",
        ["AppVersionUnknown"] = "Версия приложения: неизвестна",
        ["RegistryFailed"] = "Не удалось создать раздел реестра HKCU\\Run",
        ["RegistryError"] = "Ошибка реестра: {0}",
        ["CheckingUpdates"] = "Проверка обновлений...",
        ["UpdatesAvailable"] = "Вы используете последнюю версию.",
        ["UpdateCheckError"] = "Ошибка проверки: {0}",
        ["DownloadingUpdates"] = "Загрузка обновлений...",
        ["UpdatesDownloaded"] = "Обновления загружены (v{0}). Перезапустите приложение.",
        ["UpdateDownloadError"] = "Ошибка загрузки: {0}",
        ["ResetTitle"] = "Сброс настроек",
        ["ResetMessage"] = "Вы уверены, что хотите сбросить все настройки к значениям по умолчанию?",
        ["ResetConfirm"] = "Сбросить",
        ["Cancel"] = "Отмена",

        // ── Common VM strings ────────────────────────────────
        ["NotSelectedF"] = "Не выбрана",
        ["AutoRecommended"] = "Auto (рекомендуется)",
        ["AutoRecommendedDesc"] = "Автоматический перебор стратегий",
        ["TestingInprogress"] = "Тестирование...",
        ["StrategyWorks"] = "✓ Стратегия работает ({0})",
        ["StrategyError"] = "✗ Ошибка: {0}",
        ["StrategyActive"] = "✓ Защита активна ({0})",
        ["ApplyingStrategy"] = "Применение...",
        ["ErrorMsg"] = "Ошибка: {0}",
        ["DohEnabled"] = "✓ Secure DNS включён ({0})",
        ["DohEnableError"] = "✗ Ошибка включения",
        ["DohDisabled"] = "✓ Secure DNS отключён",
        ["DnsResetDhcp"] = "DNS сброшен на DHCP",
        ["DohDisableError"] = "✗ Ошибка отключения",
        ["DnsProxyNotRunning"] = "DNS Proxy не запущен",
        ["DnsProxyActiveWorker"] = "✓ DNS Proxy активен (Worker)",
        ["LocalDohOn"] = "• Локальный DoH: включён",
        ["WorkerDohOn"] = "• Worker DoH: включён",
        ["FakeDnsOn"] = "• Fake DNS: включён",
        ["CacheCount"] = "• Кэш: {0} записей",
        ["FakeDnsOverrides"] = "• Fake DNS переопределения: {0}",
        ["DnsProxyStartError"] = "✗ Ошибка запуска: {0}",
        ["DnsProxyStarted"] = "✓ DNS Proxy запущен (Worker)",
        ["DnsProxyRouting"] = "Маршрутизация:",
        ["DnsProxyBlockedSites"] = "• Заблокированные сайты → DoH resolver",
        ["DnsProxyOtherSites"] = "• Остальные сайты → системный DNS",
        ["DnsProxyFakeDnsNote"] = "• Fake DNS: подмена IP для заблокированных ресурсов",
        ["DnsProxyActivation"] = "Для активации настройте системный DNS на 127.0.0.1",
        ["DnsProxyStopped"] = "DNS Proxy остановлен",
        ["DnsProxyStopError"] = "✗ Ошибка остановки: {0}",
        ["DnsProxyFakeDnsError"] = "✗ Ошибка переключения Fake DNS: {0}",
        ["ProviderDetectionError2"] = "Ошибка",
        ["ProviderDetectionFailed2"] = "Ошибка определения: {0}",
        ["UniversalStrategyNote"] = "Провайдер не найден в базе. Будет использована универсальная стратегия.",
        ["StrategySaved"] = "✓ Сохранено: {0}",
        ["MalwLinkRecommended"] = "dns.malw.link (Рекомендуется)",
        ["RunAsAdminPrompt"] = "Запустите приложение от имени администратора",
        ["CurrentVersion"] = "Текущая версия: {0}",
        ["CurrentVersionUnknown"] = "Текущая версия: неизвестна",
        ["WorkerVersion"] = "Worker: v{0}",
        ["WorkerVersionUnknown"] = "Worker: версия неизвестна",
        ["WorkerNotConnected"] = "Worker: не подключён",
["InstallAndStart"] = "Установить и запустить",
        ["RegistryOpenFailed"] = "Не удалось открыть раздел реестра HKCU\\Run",
        ["ProxifierOff"] = "Проксификатор выключен",
        ["ProxifierStart"] = "Запустить",
        ["ProxifierStop"] = "Остановить",
        ["ProxifierStopped"] = "Проксификатор остановлен",
        ["ProxifierActive2"] = "Проксификатор активен",
        ["DnsModeDoh"] = "DNS over HTTPS (Windows 10+)",
        ["DnsModeProxy"] = "DNS Proxy (Worker, Split DNS + Fake DNS)",
        ["OK"] = "ОК",
        ["DownloadingUpdate"] = "Загрузка обновления...",
        ["StartingProxy"] = "Запуск проксификатора...",
        ["StoppingProxy"] = "Остановка проксификатора...",
        ["ProxifierRunning"] = "Проксификатор запущен",
        ["UpdateError"] = "Ошибка обновления: {0}",
        ["UpdateCheckFailed"] = "Ошибка проверки: {0}",

        // ── Worker service ────────────────────────────────────
        ["WorkerNotInstalled"] = "Служба не установлена",
        ["WorkerStopped"] = "Служба остановлена",
        ["WorkerStarting"] = "Служба запускается...",
        ["WorkerRunning"] = "Служба работает",
        ["WorkerStopping"] = "Служба останавливается...",
        ["WorkerError"] = "Ошибка службы",
        ["WorkerUninstall"] = "Удалить",
        ["WorkerReinstall"] = "Переустановить",
    };

    private static readonly Dictionary<string, string> English = new(StringComparer.OrdinalIgnoreCase)
    {
        // ── Status ────────────────────────────────────────────
        ["Running"] = "Running",
        ["Stopped"] = "Stopped",
        ["ProtectionOff"] = "Protection off",
        ["DpiBypassActive"] = "DPI bypass active",
        ["ServiceStatus"] = "Service status",
        ["StartedMale"] = "Started",
        ["StoppedMale"] = "Stopped",

        // ── Strategy descriptions ─────────────────────────────
        ["StrategyDescGeneral"] = "Standard protection for bypassing popular networks",
        ["StrategyDescDiscord"] = "Optimized for Discord (voice, messages)",
        ["StrategyDescYouTube"] = "Optimized for YouTube (video, streams)",
        ["StrategyDescRussia"] = "Russian ISPs (MTS, Rostelecom, Beeline)",
        ["StrategyDescGaming"] = "Gaming services (PoE2, Valorant, Steam)",
        ["StrategyDescCustom"] = "Custom strategy",

// ── Dashboard ─────────────────────────────────────────
    ["Start"] = "Start",
    ["Stop"] = "Stop",
    ["NotDetected"] = "Not detected",
        ["Detecting"] = "Detecting...",
        ["Configured"] = "Configured",
        ["Active"] = "Active",
        ["Disabled"] = "Disabled",
        ["Default"] = "Default",
        ["AutoStart"] = "autostart",
        ["AutoUpdates"] = "auto-updates",
        ["PressToCheck"] = "Click to check",
        ["VersionCheckError"] = "Check error",
        ["AdminRequired"] = "Administrator rights required",
        ["Error"] = "Error",
        ["PreparingUpdate"] = "Preparing update...",
        ["ChangelogLoading"] = "Loading changelog...",
        ["UpdateUnavailable"] = "Update temporarily unavailable",

        // ── Ipset ─────────────────────────────────────────────
        ["IpsetAny"] = "Any IP address (any)",
        ["IpsetLoaded"] = "IP list loaded",
        ["IpsetLoadedCount"] = "List loaded: {0} entries",
        ["IpsetNone"] = "IP filtering disabled",

        // ── Diagnostics ───────────────────────────────────────
        ["ReadyToCheck"] = "Ready to check",
        ["NotChecked"] = "Not checked",
        ["DiagnosticsRunning"] = "Running diagnostics...",
        ["AdminRightsCheck"] = "Administrator rights",
        ["DomainListsCheck"] = "Domain lists",
        ["BinaryFilesCheck"] = "Binary files",
        ["NetworkConnectivity"] = "Network connectivity",
        ["DiagnosticsPassed"] = "All checks passed ({0}/{1})",
        ["DiagnosticsFailed"] = "Issues found ({0}/{1})",
        ["DiagnosticsCompleted"] = "Diagnostics completed",
        ["QuickCheckRunning"] = "Running quick check...",
        ["ExportedTo"] = "Results exported: {0}",
        ["ExportError"] = "Export error: {0}",
        ["FixAdminRights"] = "Restarting with admin rights...",
        ["FixAdminFailed"] = "Failed to restart: {0}",
        ["FixWorkerNote"] = "Make sure Worker Service is running. Restart the application.",
        ["FixDomainListsNote"] = "Domain lists are downloaded via MalwLinkUpdateService on first launch.",
        ["FixBinaryFilesNote"] = "Make sure the zapret/ directory contains winws.exe and other binaries.",
        ["FixNetworkNote"] = "Check internet connection and try opening a website manually.",
        ["Yes"] = "Yes",
        ["No"] = "No",

        // ── DNS ───────────────────────────────────────────────
        ["Checking"] = "Checking...",
        ["RestartAppAsAdmin"] = "Restart the app as administrator",
        ["StartingDnsProxy"] = "Starting DNS Proxy...",
        ["StoppingDnsProxy"] = "Stopping DNS Proxy...",

        // ── Generator ─────────────────────────────────────────
        ["NotSelected"] = "Not selected",
        ["AutoStrategy"] = "Auto (auto-select)",
        ["AutoStrategyDesc"] = "Automatic strategy selection",
        ["Testing"] = "Testing...",
        ["UnknownError"] = "Unknown error",
        ["ProviderDetectionError"] = "Detection error",
        ["ProviderDetectionFailed"] = "Failed to detect provider: {0}",
        ["DetectingProvider"] = "Detecting provider...",
        ["ProviderNotDetected"] = "Not detected",
        ["ProviderNotDetectedInfo"] = "Provider not found in database. Default strategy will be used.",
        ["Method"] = "Method: {0}",
        ["Confidence"] = "Confidence: {0}%",
["GenerationError"] = "Generation error",
    ["ReadyStrategies"] = "Ready Strategies",
    ["CreateStrategy"] = "Create Strategy",
    ["DpiMethodSelection"] = "Bypass Method",
    ["DpiMethodSelectionDesc"] = "Select a DPI bypass method and configure parameters:",
    ["FoolingMode"] = "Fooling mode:",
    ["FakeRepeats"] = "Fake repeats:",
    ["SplitParams"] = "Split parameters:",
    ["FakedsplitPattern"] = "Fakedsplit pattern:",
    ["HostfakesplitMod"] = "Hostfakesplit modifier:",
    ["CombineMultidisorder"] = "Combine with multidisorder",
    ["CombineMultidisorderOn"] = "syndata + multidisorder",
    ["CombineMultidisorderOff"] = "syndata only",
    ["Stability"] = "Stability",
    ["CurrentStrategy"] = "Current Strategy",
    ["StrategySelection"] = "Strategy Selection",
    ["TestStrategyBtn"] = "Test Strategy",
    ["RefreshList"] = "Refresh List",
    ["SelectMethod"] = "Select method",

    // ── Strategy page ─────────────────────────────────────
        ["Applying"] = "Applying...",

        // ── Proxifier ─────────────────────────────────────────
        ["ProxifierStopped"] = "Proxifier stopped",
        ["EnableProxifier"] = "Enable",
        ["StartingProxifier"] = "Starting Proxifier...",
        ["ProxifierStarted"] = "Proxifier started",
        ["StoppingProxifier"] = "Stopping Proxifier...",
        ["ProxifierActive"] = "Proxifier active",
        ["DisableProxifier"] = "Disable",

        // ── TgProxy ───────────────────────────────────────────
        ["Socks5Started"] = "SOCKS5 proxy started",
        ["InvalidWsUrl"] = "Invalid WebSocket URL",
        ["MtProxyStarted"] = "MTProxy started",
        ["InvalidMtProxySecret"] = "Invalid MTProxy secret",
        ["StopError"] = "Stop error: {0}",

        // ── Settings ──────────────────────────────────────────
        ["BySystem"] = "Follow system",
        ["AppVersion"] = "App version: {0}",
        ["AppVersionUnknown"] = "App version: unknown",
        ["RegistryFailed"] = "Failed to create HKCU\\Run registry key",
        ["RegistryError"] = "Registry error: {0}",
        ["CheckingUpdates"] = "Checking for updates...",
        ["UpdatesAvailable"] = "You are using the latest version.",
        ["UpdateCheckError"] = "Check error: {0}",
        ["DownloadingUpdates"] = "Downloading updates...",
        ["UpdatesDownloaded"] = "Updates downloaded (v{0}). Restart the app.",
        ["UpdateDownloadError"] = "Download error: {0}",
        ["ResetTitle"] = "Reset settings",
        ["ResetMessage"] = "Are you sure you want to reset all settings to defaults?",
        ["ResetConfirm"] = "Reset",
        ["Cancel"] = "Cancel",

        // ── Common VM strings ────────────────────────────────
        ["NotSelectedF"] = "Not selected",
        ["AutoRecommended"] = "Auto (recommended)",
        ["AutoRecommendedDesc"] = "Automatic strategy rotation",
        ["TestingInprogress"] = "Testing...",
        ["StrategyWorks"] = "✓ Strategy works ({0})",
        ["StrategyError"] = "✗ Error: {0}",
        ["StrategyActive"] = "✓ Protection active ({0})",
        ["ApplyingStrategy"] = "Applying...",
        ["ErrorMsg"] = "Error: {0}",
        ["DohEnabled"] = "✓ Secure DNS enabled ({0})",
        ["DohEnableError"] = "✗ Enable error",
        ["DohDisabled"] = "✓ Secure DNS disabled",
        ["DnsResetDhcp"] = "DNS reset to DHCP",
        ["DohDisableError"] = "✗ Disable error",
        ["DnsProxyNotRunning"] = "DNS Proxy not running",
        ["DnsProxyActiveWorker"] = "✓ DNS Proxy active (Worker)",
        ["LocalDohOn"] = "• Local DoH: enabled",
        ["WorkerDohOn"] = "• Worker DoH: enabled",
        ["FakeDnsOn"] = "• Fake DNS: enabled",
        ["CacheCount"] = "• Cache: {0} entries",
        ["FakeDnsOverrides"] = "• Fake DNS overrides: {0}",
        ["DnsProxyStartError"] = "✗ Start error: {0}",
        ["DnsProxyStarted"] = "✓ DNS Proxy started (Worker)",
        ["DnsProxyRouting"] = "Routing:",
        ["DnsProxyBlockedSites"] = "• Blocked sites → DoH resolver",
        ["DnsProxyOtherSites"] = "• Other sites → system DNS",
        ["DnsProxyFakeDnsNote"] = "• Fake DNS: IP substitution for blocked resources",
        ["DnsProxyActivation"] = "To activate, set system DNS to 127.0.0.1",
        ["DnsProxyStopped"] = "DNS Proxy stopped",
        ["DnsProxyStopError"] = "✗ Stop error: {0}",
        ["DnsProxyFakeDnsError"] = "✗ Fake DNS toggle error: {0}",
        ["ProviderDetectionError2"] = "Error",
        ["ProviderDetectionFailed2"] = "Detection error: {0}",
        ["UniversalStrategyNote"] = "Provider not found in database. Universal strategy will be used.",
        ["StrategySaved"] = "✓ Saved: {0}",
        ["MalwLinkRecommended"] = "dns.malw.link (Recommended)",
        ["RunAsAdminPrompt"] = "Run the application as administrator",
        ["CurrentVersion"] = "Current version: {0}",
        ["CurrentVersionUnknown"] = "Current version: unknown",
        ["WorkerVersion"] = "Worker: v{0}",
        ["WorkerVersionUnknown"] = "Worker: version unknown",
        ["WorkerNotConnected"] = "Worker: not connected",
["InstallAndStart"] = "Install & Start",
        ["RegistryOpenFailed"] = "Failed to open HKCU\\Run registry key",
        ["ProxifierOff"] = "Proxifier off",
        ["ProxifierStart"] = "Start",
        ["ProxifierStop"] = "Stop",
        ["ProxifierStopped"] = "Proxifier stopped",
        ["ProxifierActive2"] = "Proxifier active",
        ["DnsModeDoh"] = "DNS over HTTPS (Windows 10+)",
        ["DnsModeProxy"] = "DNS Proxy (Worker, Split DNS + Fake DNS)",
        ["OK"] = "OK",
        ["DownloadingUpdate"] = "Downloading update...",
        ["StartingProxy"] = "Starting Proxifier...",
        ["StoppingProxy"] = "Stopping Proxifier...",
        ["ProxifierRunning"] = "Proxifier started",
        ["UpdateError"] = "Update error: {0}",
        ["UpdateCheckFailed"] = "Check error: {0}",

        // ── Worker service ────────────────────────────────────
        ["WorkerNotInstalled"] = "Service not installed",
        ["WorkerStopped"] = "Service stopped",
        ["WorkerStarting"] = "Service starting...",
        ["WorkerRunning"] = "Service running",
        ["WorkerStopping"] = "Service stopping...",
        ["WorkerError"] = "Service error",
        ["WorkerUninstall"] = "Uninstall",
        ["WorkerReinstall"] = "Reinstall",
    };

    private static Dictionary<string, string> _current = Russian;
    private static string _language = "ru";
    private static bool _initialized;

    /// <summary>Raised when the application language changes.</summary>
    public static event Action? LanguageChanged;

    /// <summary>Current language code ("ru" or "en").</summary>
    public static string Language
    {
        get => _language;
        set
        {
            if (_language == value) return;
            _language = value;
            _current = value.Equals("en", StringComparison.OrdinalIgnoreCase) ? English : Russian;
            LanguageChanged?.Invoke();
        }
    }

    /// <summary>
    /// Initialize the localization service with the given language.
    /// </summary>
    public static void Initialize(string? language = null)
    {
        if (_initialized) return;
        _initialized = true;

        if (!string.IsNullOrEmpty(language))
            Language = language;

        System.Diagnostics.Debug.WriteLine($"[Z-UI] LocalizationService: Initialized with language={Language}, {_current.Count} keys");
    }

    /// <summary>
    /// Get a localized string by key. Returns the key itself as fallback.
    /// </summary>
    public static string Get(string key)
    {
        if (_current.TryGetValue(key, out var value))
            return value;

        // Fallback: try the other language
        var fallback = _current == Russian ? English : Russian;
        if (fallback.TryGetValue(key, out var fallbackValue))
            return fallbackValue;

        // Last resort: return the key itself
        return key;
    }

    /// <summary>
    /// Get a localized string with format arguments.
    /// </summary>
    public static string Get(string key, params object[] args)
    {
        var template = Get(key);
        try
        {
            return string.Format(template, args);
        }
        catch (FormatException)
        {
            return template;
        }
    }
}
