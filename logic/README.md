# Z-UI — Архитектура и логика приложения

## Обзор

Z-UI — GUI оболочка для DPI bypass (zapret-discord-youtube).

**Архитектура:** UI (WinUI 3) ↔ IPC (Named Pipes) ↔ Worker (Windows Service, SYSTEM) ↔ WinDivert (kernel driver)

## Структура страниц

| Страница | Файл | Назначение |
|----------|------|------------|
| [Dashboard](Dashboard.md) | `DashboardPage.xaml` | Главная страница — управление защитой, статус, быстрые действия |
| [Generator](Generator.md) | `GeneratorPage.xaml` | Генератор DPI bypass стратегий |
| [Network](Network.md) | `NetworkPage.xaml` | Сетевые настройки — DNS, блокировки, probing |
| [Diagnostics](Diagnostics.md) | `DiagnosticsPage.xaml` | Диагностика системы — проверка WinDivert, админ прав, сети |
| [Proxifier](Proxifier.md) | `ProxifierPage.xaml` | Проксификатор — маршрутизация трафика через прокси |
| [TelegramProxy](TelegramProxy.md) | `TgProxyPage.xaml` | Telegram прокси — SOCKS5→WS + MTProxy |
| [Settings](Settings.md) | `SettingsPage.xaml` | Настройки приложения — тема, язык, автозапуск |

## Компоненты

### UI слой (Z-UI)
- **Views** — XAML страницы
- **ViewModels** — MVVM логика (CommunityToolkit.Mvvm)
- **Services** — бизнес-логика (DI-registered)
- **Windows** — MainWindow (tray, hotkeys, navigation)

### IPC слой (ZUI.Ipc)
- **IpcPipeClient** — UI клиент
- **IpcPipeServer** — Worker сервер
- **IpcMessage** — базовый тип (31 request, 22 response, 5 event типов)

### Worker слой (ZUI.Worker)
- **WorkerService** — Windows Service (SYSTEM, Native AOT)
- **Orchestrator** — координация модулей
- **WinDivertInterceptor** — перехват пакетов
- **DpiBypassEngine** — DPI bypass логика
- **ProxifierEngine** — проксификация трафика

## Поток данных

```
User Action → ViewModel → Service → IpcClient → Named Pipe → IpcServer → Orchestrator → WinDivert
                                                                                               ↓
UI Update ← ViewModel ← Service ← IpcClient ← Named Pipe ← IpcServer ← Event/Stats ← Engine
```

## Ключевые сервисы

### Основные сервисы
| Сервис | Назначение |
|--------|------------|
| `IpcClientService` | IPC подключение к Worker (Named Pipe `ZUI_IPC`) |
| `WorkerServiceManager` | Установка/запуск/остановка Worker службы |
| `StrategyManager` | Загрузка/выбор DPI стратегий |
| `DnsService` | Управление DNS (DoH через PowerShell) |
| `DiagnosticsService` | Проверка системы (6 проверок) |
| `IAdaptiveEngine` | Координация DPI bypass + DNS (через IPC) |
| `ActiveBlockProber` | Активное probing блокировок |
| `TelegramProxyService` | Управление Telegram прокси (SOCKS5→WS + MTProxy) |
| `ProxifierService` | Управление проксификатором |
| `MalwLinkUpdateService` | Проверка обновлений + changelog |
| `AppSettingsService` | Хранение настроек (централизованное) |
| `LocalizationService` | i18n (ru/en) |
| `HotkeyService` | Глобальные горячие клавиши |
| `TrayIcon` | Системный трей |
| `ToastNotifier` | WinRT уведомления |
| `SoundService` | Звуковые эффекты |
| `UpdateChecker` | Проверка обновлений GitHub |

### Дополнительные сервисы
| Сервис | Назначение |
|--------|------------|
| `StrategyGeneratorService` | Генерация кастомных стратегий |
| `IspDetectionService` | Определение провайдера |
| `HostlistService` | Управление списками доменов |
| `WinwsArgsBuilder` | Построение аргументов winws |
| `DashboardStatusService` | Агрегированный статус для Dashboard |
| `EnhancedDnsManager` | DNS bypass управление |
| `WindowSubclass` | Win32 subclassing (WM_HOTKEY, WM_COMMAND, tray) |
| `TestResultStore` | Кэш результатов тестирования |
| `BatStrategyParser` | Парсинг .bat файлов стратегий |
| `ActionLogger` | Логирование действий пользователя |
| `NavigationService` | Навигация между страницами |
| `DefaultStrategyConfigs` | Конфигурации стратегий по умолчанию |
