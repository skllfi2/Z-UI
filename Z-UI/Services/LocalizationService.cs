using System;
using System.Collections.Generic;

namespace ZUI.Services;

public static class LocalizationService
{
    private static string _currentLanguage = "ru";

    public static event Action? LanguageChanged;

    public static string CurrentLanguage
    {
        get => _currentLanguage;
        set
        {
            if (_currentLanguage != value)
            {
                _currentLanguage = value;
                LanguageChanged?.Invoke();
            }
        }
    }

    public static void Initialize()
    {
        var savedLang = AppSettings.Language;
        if (string.IsNullOrEmpty(savedLang) || savedLang == "Default")
        {
            var systemLang = Windows.Globalization.ApplicationLanguages.Languages[0];
            _currentLanguage = systemLang.StartsWith("ru") ? "ru" : "en";
        }
        else
        {
            _currentLanguage = savedLang == "en" ? "en" : "ru";
        }
    }

    private static Dictionary<string, string> RussianStrings { get; } = new()
    {
        ["NavDashboard"] = "Главная",
        ["NavStrategies"] = "Стратегии",
        ["NavServices"] = "Сервисы",
        ["NavSettings"] = "Параметры",

        ["QuickActions"] = "БЫСТРЫЕ ДЕЙСТВИЯ",
        ["SetupWizard"] = "Мастер настройки",
        ["InstallZapret"] = "Установка zapret",
        ["AdvancedFilters"] = "Расширенные настройки фильтров",
        ["GameFilter"] = "Игровой фильтр",
        ["GameFilterDesc"] = "Обход DPI на игровые порты",
        ["IpsetFilter"] = "IPSet фильтр",
        ["Changelog"] = "Список изменений",

        ["Administrator"] = "Администратор",
        ["On"] = "Вкл.",
        ["Off"] = "Откл.",
        ["Running"] = "Запущено",
        ["Stopped"] = "Остановлено",
        ["StartedMale"] = "Запущен",
        ["StoppedMale"] = "Остановлен",
        ["ServiceStatus"] = "Статус сервиса",
        ["StrategyStarted"] = "Запущена стратегия",
        ["StrategyApplied"] = "Применена стратегия",
        ["TestingStarted"] = "Запущено тестирование",
        ["StrategiesCount"] = "стратегий",
        ["BestConfig"] = "Лучший конфиг",
        ["Unknown"] = "Неизвестно",
        ["TestingComplete"] = "Тестирование завершено",
        ["BestStrategyFound"] = "Лучшая стратегия",
        ["BestStrategyHint"] = "Нажмите «Применить» чтобы использовать эту стратегию",
        ["ServiceStopped"] = "Сервис остановлен",
        ["ZuiStopped"] = "Z-UI остановлен",
        ["DpiBypassActive"] = "Обход DPI активен",
        ["ProtectionOff"] = "Защита выключена",
        ["StrategyDescGeneral"] = "Стандартная защита для обхода популярных сетей",
        ["StrategyDescDiscord"] = "Оптимизировано для Discord и голосовых сервисов",
        ["StrategyDescYouTube"] = "Оптимизировано для YouTube и видеостриминга",
        ["StrategyDescRussia"] = "Специализировано для российских провайдеров",
        ["StrategyDescGaming"] = "Оптимизировано для игровых сервисов",
        ["StrategyDescCustom"] = "Пользовательская стратегия",
        ["Launch"] = "Запустить",
        ["Stop"] = "Остановить",
        ["Update"] = "Обновить",
        ["Check"] = "Проверить",
        ["Download"] = "Скачать",
        ["Install"] = "Установить",
        ["Cancel"] = "Отмена",
        ["Next"] = "Далее",
        ["Back"] = "Назад",
        ["Finish"] = "Готово",
        ["Skip"] = "Пропустить",

        ["Disabled"] = "Отключён",
        ["TcpAndUdp"] = "TCP и UDP",
        ["TcpOnly"] = "Только TCP",
        ["UdpOnly"] = "Только UDP",
        ["AnyIp"] = "Любой IP (any)",
        ["FromList"] = "Только из списка",
        ["None"] = "Никакой (none)",

        ["ZapretNotInstalled"] = "zapret не установлен",
        ["ZapretNotInstalledMsg"] = "Для работы защиты необходимо установить zapret через мастер настройки.",
        ["LaunchSetupWizard"] = "Запустить мастер настройки",
        ["UpdateAvailable"] = "Доступно обновление",

        ["Language"] = "Язык интерфейса",
        ["LanguageDesc"] = "Перезапустите приложение для применения",
        ["Theme"] = "Тема",
        ["ThemeDesc"] = "Выберите светлую, тёмную или системную тему",
        ["System"] = "Системная",
        ["Light"] = "Светлая",
        ["Dark"] = "Тёмная",
        ["Russian"] = "Русский",
        ["English"] = "English",

        ["SoundEffects"] = "Звуковые эффекты",
        ["ToastNotifications"] = "Toast-уведомления",
        ["AutoStartWithWindows"] = "Автозапуск zapret",
        ["AutoStartWithWindowsDesc"] = "Запускать приложение при входе в Windows",
        ["AutoStartProtection"] = "Запускать защиту автоматически",
        ["MinimizeToTray"] = "Сворачивать в трей при запуске",
        ["AutoStartAdmin"] = "Автозапуск с правами администратора",
        ["AutoStartAdminDesc"] = "Запускать с повышенными правами без UAC-запроса",

        ["Version"] = "Версия",
        ["Status"] = "Статус",
["Logs"] = "Логи",
			["Save"] = "Сохранить",
        ["Diagnostics"] = "Диагностика",
        ["Results"] = "Результаты",
        ["Testing"] = "Тестирование",

        ["SetupWizardTitle"] = "Добро пожаловать в Z-UI",
        ["SetupWizard"] = "Мастер настройки",
        ["SetupWizardDesc"] = "Мастер поможет настроить обход DPI за несколько шагов",
        ["DownloadZapret"] = "Скачать zapret",
        ["SelectStrategy"] = "Выбор стратегии",
        ["AllDone"] = "Всё готово!",

        ["SelectStrategies"] = "Выбрать стратегии",
        ["StrategiesSelected"] = "выбрано",
        ["All"] = "Все",
        ["Reset"] = "Сбросить",
        ["Apply"] = "Применить",
        ["ApplyStrategy"] = "Применить стратегию",
        ["RunTest"] = "Запустить тест",

        ["Open"] = "Открыть",
        ["OpenFolder"] = "Открыть папку",
        ["ReportIssue"] = "Сообщить об ошибке",

        ["ProtectionStatus"] = "Статус защиты",
        ["StrategyNotSelected"] = "Стратегия не выбрана",
        ["ActiveStrategy"] = "Активная стратегия",
        ["ActiveStrategyDesc"] = "Эта стратегия используется сейчас",

        ["Export"] = "Экспорт",
        ["Import"] = "Импорт",

        ["Main"] = "Основные",
        ["Appearance"] = "Внешний вид",
        ["Updates"] = "Обновления",
        ["Hotkeys"] = "Горячие клавиши",
        ["HotkeysDesc"] = "Ctrl+Shift+T — переключить, Ctrl+Alt+Z — показать",
        ["EnableHotkeys"] = "Включить горячие клавиши",
        ["Filters"] = "Фильтры (расширенные)",
        ["GameFilterLabel"] = "Фильтр игр",
        ["IpsetFilterLabel"] = "Фильтр ipset",
        ["HostsAutoUpdate"] = "Автообновление hosts",
        ["AutoUpdateCheck"] = "Автопроверка обновлений",
        ["AutoUpdateDownload"] = "Автоматическое обновление",
        ["AutoUpdateDownloadDesc"] = "Скачивать и устанавливать обновления автоматически",

        ["CollapseAll"] = "Свернуть все",
        ["ExpandAll"] = "Развернуть все",

        ["SearchStrategy"] = "Поиск стратегии...",
        ["ResetTestResults"] = "Сбросить результаты тестирования",
        ["SelectStrategyTitle"] = "Выберите стратегию",
        ["SelectStrategyHint"] = "Нажмите на любую стратегию в списке слева",
        ["TestResult"] = "Результат тестирования",
        ["TestResultDesc"] = "Оценка на основе HTTP/Ping тестов",
        ["StrategyType"] = "Тип стратегии",
        ["RecommendedFor"] = "Рекомендуется для",

        ["Mode"] = "Режим",
        ["StandardMode"] = "HTTP + Ping",
        ["DpiMode"] = "DPI 16-20 KB",
["TestLog"] = "Лог тестирования",
			["OperationLog"] = "Лог операций",
        ["UpdateHosts"] = "Обновить hosts",
        ["HostsFile"] = "Файл hosts",

        ["StrategiesLabel"] = "Стратегии",
        ["AnimNavIcons"] = "Анимация иконок навигации",
        ["AnimButtons"] = "Анимация кнопок",
        ["AnimCards"] = "Анимация карточек",
    };

    private static Dictionary<string, string> EnglishStrings { get; } = new()
    {
        ["NavDashboard"] = "Home",
        ["NavStrategies"] = "Strategies",
        ["NavServices"] = "Services",
        ["NavSettings"] = "Settings",

        ["QuickActions"] = "QUICK ACTIONS",
        ["SetupWizard"] = "Setup wizard",
        ["InstallZapret"] = "Install zapret",
        ["AdvancedFilters"] = "Advanced filter settings",
        ["GameFilter"] = "Game filter",
        ["GameFilterDesc"] = "DPI bypass for game ports",
        ["IpsetFilter"] = "IPSet filter",
        ["Changelog"] = "Changelog",

        ["Administrator"] = "Administrator",
        ["On"] = "On",
        ["Off"] = "Off",
        ["Running"] = "Running",
        ["Stopped"] = "Stopped",
        ["StartedMale"] = "Started",
        ["StoppedMale"] = "Stopped",
        ["ServiceStatus"] = "Service status",
        ["StrategyStarted"] = "Strategy started",
        ["StrategyApplied"] = "Strategy applied",
        ["TestingStarted"] = "Testing started",
        ["StrategiesCount"] = "strategies",
        ["BestConfig"] = "Best config",
        ["Unknown"] = "Unknown",
        ["TestingComplete"] = "Testing complete",
        ["BestStrategyFound"] = "Best strategy found",
        ["BestStrategyHint"] = "Click 'Apply' to use this strategy",
        ["ServiceStopped"] = "Service stopped",
        ["ZuiStopped"] = "Z-UI stopped",
        ["DpiBypassActive"] = "DPI bypass active",
        ["ProtectionOff"] = "Protection off",
        ["StrategyDescGeneral"] = "Standard protection for popular networks",
        ["StrategyDescDiscord"] = "Optimized for Discord and voice services",
        ["StrategyDescYouTube"] = "Optimized for YouTube and video streaming",
        ["StrategyDescRussia"] = "Specialized for Russian ISPs",
        ["StrategyDescGaming"] = "Optimized for gaming services",
        ["StrategyDescCustom"] = "Custom strategy",
        ["Launch"] = "Launch",
        ["Stop"] = "Stop",
        ["Update"] = "Update",
        ["Check"] = "Check",
        ["Download"] = "Download",
        ["Install"] = "Install",
        ["Cancel"] = "Cancel",
        ["Next"] = "Next",
        ["Back"] = "Back",
        ["Finish"] = "Finish",
        ["Skip"] = "Skip",

        ["Disabled"] = "Disabled",
        ["TcpAndUdp"] = "TCP and UDP",
        ["TcpOnly"] = "TCP only",
        ["UdpOnly"] = "UDP only",
        ["AnyIp"] = "Any IP (any)",
        ["FromList"] = "From list only",
        ["None"] = "None",

        ["ZapretNotInstalled"] = "zapret not installed",
        ["ZapretNotInstalledMsg"] = "To enable protection, install zapret via the setup wizard.",
        ["LaunchSetupWizard"] = "Launch setup wizard",
        ["UpdateAvailable"] = "Update available",

        ["Language"] = "Interface language",
        ["LanguageDesc"] = "Restart app to apply changes",
        ["Theme"] = "Theme",
        ["ThemeDesc"] = "Choose light, dark or system theme",
        ["System"] = "System",
        ["Light"] = "Light",
        ["Dark"] = "Dark",
        ["Russian"] = "Русский",
        ["English"] = "English",

        ["SoundEffects"] = "Sound effects",
        ["ToastNotifications"] = "Toast notifications",
        ["AutoStartWithWindows"] = "Auto-start zapret",
        ["AutoStartWithWindowsDesc"] = "Start application on Windows login",
        ["AutoStartProtection"] = "Start protection automatically",
        ["MinimizeToTray"] = "Minimize to tray on launch",
        ["AutoStartAdmin"] = "Auto-start with admin rights",
        ["AutoStartAdminDesc"] = "Start with elevated privileges without UAC prompt",

        ["Version"] = "Version",
        ["Status"] = "Status",
        ["Logs"] = "Logs",
        ["Clear"] = "Clear",
        ["ClearLog"] = "Clear log",
        ["Save"] = "Save",
        ["Diagnostics"] = "Diagnostics",
        ["Results"] = "Results",
        ["Testing"] = "Testing",

        ["SetupWizardTitle"] = "Welcome to Z-UI",
        ["SetupWizard"] = "Setup wizard",
        ["SetupWizardDesc"] = "The wizard will help you configure DPI bypass in a few steps",
        ["DownloadZapret"] = "Download zapret",
        ["SelectStrategy"] = "Strategy selection",
        ["AllDone"] = "All set!",

        ["SelectStrategies"] = "Select strategies",
        ["StrategiesSelected"] = "selected",
        ["All"] = "All",
        ["Reset"] = "Reset",
        ["Apply"] = "Apply",
        ["ApplyStrategy"] = "Apply strategy",
        ["RunTest"] = "Run test",

        ["Open"] = "Open",
        ["OpenFolder"] = "Open folder",
        ["ReportIssue"] = "Report an issue",

        ["ProtectionStatus"] = "Protection status",
        ["StrategyNotSelected"] = "Strategy not selected",
        ["ActiveStrategy"] = "Active strategy",
        ["ActiveStrategyDesc"] = "This strategy is currently in use",

        ["Export"] = "Export",
        ["Import"] = "Import",

        ["Main"] = "Main",
        ["Appearance"] = "Appearance",
        ["Updates"] = "Updates",
        ["Hotkeys"] = "Hotkeys",
        ["HotkeysDesc"] = "Ctrl+Shift+T — toggle, Ctrl+Alt+Z — show",
        ["EnableHotkeys"] = "Enable hotkeys",
        ["Filters"] = "Filters (advanced)",
        ["GameFilterLabel"] = "Game filter",
        ["IpsetFilterLabel"] = "Ipset filter",
        ["HostsAutoUpdate"] = "Auto-update hosts",
        ["AutoUpdateCheck"] = "Auto-update checks",
        ["AutoUpdateDownload"] = "Automatic update",
        ["AutoUpdateDownloadDesc"] = "Download and install updates automatically",

        ["CollapseAll"] = "Collapse all",
        ["ExpandAll"] = "Expand all",

        ["SearchStrategy"] = "Search strategy...",
        ["ResetTestResults"] = "Reset test results",
        ["SelectStrategyTitle"] = "Select a strategy",
        ["SelectStrategyHint"] = "Click any strategy in the left list",
        ["TestResult"] = "Test result",
        ["TestResultDesc"] = "Score based on HTTP/Ping tests",
        ["StrategyType"] = "Strategy type",
        ["RecommendedFor"] = "Recommended for",

        ["Mode"] = "Mode",
        ["StandardMode"] = "HTTP + Ping",
        ["DpiMode"] = "DPI 16-20 KB",
        ["TestLog"] = "Test log",

        ["ComponentChecks"] = "Component checks",
        ["Checking"] = "Checking...",
        ["CheckDesc"] = "Checks for all necessary files and services",
        ["CheckAgain"] = "Check again",

        ["SystemLog"] = "System log",
        ["Lines"] = "lines",
        ["RealtimeUpdate"] = "Updates in real time",

        ["AboutTitle"] = "About",
        ["AboutDesc"] = "Management interface for zapret DPI bypass",
        ["Versions"] = "Versions",
        ["AppVersion"] = "Application version",
        ["ZapretVersion"] = "Zapret version",
        ["ZapretVersionDesc"] = "DPI bypass for Discord, YouTube and other services",
        ["WinUIVersion"] = "WinUI 3",
        ["WinUIVersionDesc"] = "Native Windows 11 interface",
        ["Links"] = "Links",
        ["ProjectGitHub"] = "Project GitHub",
        ["TelegramChannel"] = "Telegram channel",
        ["OriginalZapret"] = "Original zapret",
        ["Credits"] = "Credits",
        ["CreditsFlowseal"] = "Flowseal for the original zapret-discord-youtube",
        ["CreditsCommunity"] = "Community for testing and feedback",
        ["CreditsMicrosoft"] = "Microsoft for WinUI 3 and Windows App SDK",

        ["FileCheck"] = "File check",
        ["AllFilesFound"] = "All required files found",
        ["SelectStrategyWizard"] = "Select a strategy",
        ["StrategyDeterminesDpi"] = "Strategy determines how DPI will be bypassed",
        ["DefaultStrategy"] = "Default strategy",
        ["Recommendation"] = "Recommendation",
        ["LaunchSettings"] = "Launch settings",
        ["AutoStartDesc"] = "Z-UI will automatically launch the selected strategy",
        ["StartProtectionOnLaunch"] = "Start protection on launch",
        ["AutoStartAdminWizard"] = "Auto-start with admin rights",
        ["AutoStartAdminWizardDesc"] = "Start with elevated privileges without UAC prompt",

        ["UpdateTab"] = "Updates",
        ["TestingTab"] = "Testing",
["DiagnosticsTab"] = "Diagnostics",
			["OperationLog"] = "Operation log",
        ["UpdateHosts"] = "Update hosts",
        ["HostsFile"] = "Hosts file",

        ["StrategiesLabel"] = "Strategies",
        ["AnimNavIcons"] = "Navigation icon animation",
        ["AnimButtons"] = "Button animation",
        ["AnimCards"] = "Card animation",
    };

    public static string Get(string key)
    {
        if (CurrentLanguage == "en" && EnglishStrings.TryGetValue(key, out var enValue))
            return enValue;
        if (RussianStrings.TryGetValue(key, out var value))
            return value;
        return key;
    }
}
