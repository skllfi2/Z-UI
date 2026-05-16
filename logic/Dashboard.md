# Dashboard — Главная страница

## Обзор

Главная страница приложения — управление защитой, статус, быстрые действия.

## ViewModel

**Класс:** `DashboardViewModel` (`ZUI.ViewModels`)

**Наследование:** `ViewModelBase`, `IDisposable`

**Lifecycle:**
- **Constructor:** DI + lightweight defaults + `CheckAdmin()`, `CheckSetupRequired()`, `LoadFilters()`, `RefreshDashboardState()`, `UpdateStatus()`, запуск таймера, подписка на события
- **InitializeAsync():** Вызывается из `DashboardPage.OnNavigatedTo` — обновляет статус защиты и список стратегий
- **SetDispatcherQueue():** Устанавливает `DispatcherQueue` для UI thread marshalling, запускает 5-секундный таймер
- **Dispose():** Останавливает таймер

## Состояния (ObservableProperty)

### Core state
| Поле | Тип | Описание |
|------|-----|----------|
| `_isServiceRunning` | `bool` | Защита активна |
| `_statusText` | `string` | Текстовый статус |
| `_currentStrategy` | `string` | Текущая стратегия (из `AppSettings.CurrentStrategy`) |
| `_zapretVersion` | `string` | Локальная версия zapret |
| `_isAdmin` | `bool` | Запущено от администратора |
| `_setupRequired` | `bool` | Требуется настройка (нет winws.exe) |
| `_serviceStatus` | `string` | Статус защиты (i18n) |

### Update state
| Поле | Тип | Описание |
|------|-----|----------|
| `_updateAvailable` | `bool` | Доступно обновление |
| `_updateVersion` | `string` | Версия обновления |
| `_isUpdating` | `bool` | Идёт обновление |
| `_updateProgress` | `int` | Прогресс обновления (0-100) |
| `_updateStatusText` | `string` | Текст статуса обновления |
| `_changelog` | `string` | Changelog текст |
| `_changelogVisible` | `bool` | Показывать changelog |
| `_versionStatus` | `string` | Статус версии |
| `_isCheckingVersion` | `bool` | Проверка версии |

### Dashboard UI state
| Поле | Тип | Описание |
|------|-----|----------|
| `_isLoading` | `bool` | Загрузка данных |
| `_isToggling` | `bool` | Переключение защиты |
| `_currentStrategyName` | `string` | Имя текущей стратегии |
| `_toggleButtonText` | `string` | Текст кнопки (Start/Stop) |
| `_workerNotConnectedText` | `string` | Текст "Worker не подключен" |
| `_isWorkerConnected` | `bool` | IPC подключён к Worker |
| `_installWorkerButtonText` | `string` | Текст кнопки установки Worker |
| `_currentMethod` | `string` | Текущий DPI метод |
| `_availableStrategiesCount` | `int` | Количество доступных стратегий |
| `_isSecureDnsEnabled` | `bool` | DoH включён |
| `_isProxifierRunning` | `bool` | Проксификатор активен |
| `_isTgProxyRunning` | `bool` | Telegram прокси активен |
| `_splitDnsStatus` | `string` | Статус Split DNS |
| `_dnsPrimaryServer` | `string` | Основной DNS сервер |
| `_ispName` | `string` | Имя провайдера |
| `_passedChecks` | `int` | Пройденных проверок |
| `_totalChecks` | `int` | Всего проверок |
| `_hasCriticalIssues` | `bool` | Есть критические проблемы |
| `_settingsInfoText` | `string` | Инфо о настройках (AutoStart, AutoUpdates) |

### Adaptive engine state
| Поле | Тип | Описание |
|------|-----|----------|
| `_adaptiveStrategyName` | `string` | Имя адаптивной стратегии (AdaptiveAuto/DpiBypassWorker/DnsBypass) |
| `_dnsBypassStatusText` | `string` | Статус DNS bypass (Active/Checking/Failed/Disabled) |
| `_isDnsBypassActive` | `bool` | DNS bypass активен |

### Worker service state
| Поле | Тип | Описание |
|------|-----|----------|
| `_isWorkerInstalled` | `bool` | Worker установлен |
| `_isWorkerRunning` | `bool` | Worker запущен |
| `_workerStatusText` | `string` | Текст статуса Worker |
| `_isWorkerInstalling` | `bool` | Идёт установка Worker |

### Filters
| Поле | Тип | Описание |
|------|-----|----------|
| `_gameFilterIndex` | `int` | Индекс Game Filter (0=disabled, 1=all, 2=tcp, 3=udp) |
| `_ipsetFilterIndex` | `int` | Индекс Ipset Filter (0=any, 1=loaded, 2=none) |
| `_ipsetStatusText` | `string` | Статус ipset |
| `_strategyDescription` | `string` | Описание стратегии |

### Computed properties
| Свойство | Тип | Описание |
|----------|-----|----------|
| `IsProtected` | `bool` | Alias для `IsServiceRunning` (XAML binding) |

## Команды (RelayCommand)

| Команда | Метод | Описание |
|---------|-------|----------|
| `ToggleProtectionCommand` | `ToggleProtectionAsync()` | Вкл/выкл защиту (основная) |
| `ToggleServiceCommand` | `ToggleServiceAsync()` | Legacy alias для ToggleProtection |
| `CheckVersionCommand` | `CheckVersionAsync()` | Проверить версию zapret |
| `InstallWorkerCommand` | `InstallWorkerAsync()` | Установить Worker службу |
| `StartWorkerCommand` | `StartWorkerAsync()` | Запустить Worker |
| `StopWorkerCommand` | `StopWorkerAsync()` | Остановить Worker |
| `UninstallWorkerCommand` | `UninstallWorkerAsync()` | Удалить Worker службу |
| `ReinstallWorkerCommand` | `ReinstallWorkerAsync()` | Переустановить Worker |
| `OpenWizardCommand` | `OpenWizard()` | Навигация к Setup Wizard |
| `OpenUpdatesCommand` | `OpenUpdates()` | Навигация к Updates |
| `OpenSettingsCommand` | `OpenSettings()` | Навигация к Settings |
| `StartUpdateCommand` | `StartUpdateAsync()` | Начать обновление |
| `CancelUpdateCommand` | `CancelUpdate()` | Отменить обновление |
| `UpdateDomainListsCommand` | `UpdateDomainListsAsync()` | Обновить списки доменов |

## События

| Событие | Тип | Описание |
|---------|-----|----------|
| `NavigateToSetup` | `Action` | Запрос навигации к Setup |
| `NavigateToUpdates` | `Action` | Запрос навигации к Updates |
| `NavigateToSettings` | `Action` | Запрос навигации к Settings |

## Используемые сервисы

| Сервис | Интерфейс | Назначение |
|--------|-----------|------------|
| `AdaptiveEngine` | `IAdaptiveEngine` | Запуск/остановка защиты |
| `StrategyManager` | `IStrategyManager` | Список стратегий, текущий метод |
| `DashboardStatusService` | `IDashboardStatusService` | Агрегированный статус (ISP, проверки) |
| `MalwLinkUpdateService` | — | Проверка обновлений, changelog |
| `WorkerServiceManager` | `IWorkerServiceManager` | Управление Worker службой |
| `IpcClientService` | `IIpcClientService` | IPC подключение |
| `LocalizationService` | — | i18n строки |
| `DispatcherQueue` | — | UI thread timer (5 сек) |

## Логика работы

### ToggleProtectionAsync
1. Если `IsToggling` — игнорировать
2. Если нет `winws.exe` → `NavigateToSetup`
3. Если защита включена → `StopAsync()`
4. Если выключена:
   - Стратегия "auto" → `StartAdaptiveAsync()`
   - Конкретная стратегия → `StartWithStrategyAsync()`
5. Обновить состояние, логировать действие

### InitializeAsync
1. `IsLoading = true`
2. `RefreshStatusAsync()` — обновить статус защиты
3. `RefreshDashboardState()` — обновить все UI поля
4. `RefreshStatusServiceAsync()` (fire-and-forget) — обновить ISP статус
5. `RefreshWorkerStatusAsync()` — обновить Worker статус
6. `IsLoading = false`

### Timer (5 сек)
- `RefreshServiceStatus()` — проверка `IsProtected`
- `RefreshWorkerStatusAsync()` — проверка Worker статуса через SCM
- При изменении статуса → обновление UI

### Game Filter / Ipset Filter
- Изменение индекса → сохранение в `AppSettings` + `AppSettings.Save()`
- Ipset Filter → вызов `BatStrategyParser.ApplyIpsetFilter()`
