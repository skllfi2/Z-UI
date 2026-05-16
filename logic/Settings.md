# Settings — Настройки приложения

## Обзор

Страница настроек — тема, язык, автозапуск, уведомления, DNS, обновления, импорт/экспорт.

## ViewModel

**Класс:** `SettingsViewModel` (`ZUI.ViewModels`)

**Наследование:** `ObservableObject`

**Lifecycle:**
- **Constructor:** DI + `LoadFromSettings()`, `LoadStrategies()`, `LoadVersion()`, `ReadAutostartFromRegistry()`. Подписка на `_settings.SettingChanged`.
- **SetDispatcherQueue():** Устанавливает `DispatcherQueue`, запускает `LoadWorkerVersionAsync()`.
- **Нет OnNavigatedTo** — все данные загружаются в конструкторе.

## Состояния (ObservableProperty)

### Protection
| Поле | Тип | Описание |
|------|-----|----------|
| `_autoProtect` | `bool` | Автозащита при старте |
| `_selectedStrategyIndex` | `int` | Индекс стратегии по умолчанию |
| `_availableStrategies` | `ObservableCollection<StrategyInfo>` | Доступные стратегии |
| `_runAsAdmin` | `bool` | Запуск от администратора |

### DNS
| Поле | Тип | Описание |
|------|-----|----------|
| `_selectedDnsModeIndex` | `int` | 0=Proxy, 1=DoH, 2=None |
| `_dnsPort` | `int` | Порт DNS (default: 5353) |

### Notifications
| Поле | Тип | Описание |
|------|-----|----------|
| `_notificationsEnabled` | `bool` | Уведомления включены |
| `_notifyOnStart` | `bool` | Уведомление при старте |
| `_notifyOnStop` | `bool` | Уведомление при остановке |
| `_notifyOnErrors` | `bool` | Уведомление об ошибках |

### Appearance
| Поле | Тип | Описание |
|------|-----|----------|
| `_selectedThemeIndex` | `int` | 0=Light, 1=Dark, 2=Default |
| `_animationsEnabled` | `bool` | Анимации включены |

### Language
| Поле | Тип | Описание |
|------|-----|----------|
| `_selectedLanguageIndex` | `int` | 0=Русский, 1=English |

### Tray
| Поле | Тип | Описание |
|------|-----|----------|
| `_minimizeToTray` | `bool` | Сворачивать в трей |
| `_startInTray` | `bool` | Запускать в трее |
| `_showTrayIcon` | `bool` | Показывать иконку трея |

### Sound
| Поле | Тип | Описание |
|------|-----|----------|
| `_soundEffects` | `bool` | Звуковые эффекты |

### Logging
| Поле | Тип | Описание |
|------|-----|----------|
| `_selectedLogLevelIndex` | `int` | 0=Debug, 1=Information, 2=Error, 3=None |

### Startup
| Поле | Тип | Описание |
|------|-----|----------|
| `_autostart` | `bool` | Автозапуск Windows |
| `_startMinimized` | `bool` | Запуск свёрнутым |

### Updates
| Поле | Тип | Описание |
|------|-----|----------|
| `_autoUpdate` | `bool` | Автообновление |
| `_checkUpdatesOnStart` | `bool` | Проверка при старте |
| `_versionText` | `string` | Текущая версия приложения |
| `_workerVersion` | `string` | Версия Worker |
| `_isCheckingUpdates` | `bool` | Проверка обновлений |
| `_updateStatusText` | `string` | Статус обновления |
| `_isDownloadingUpdate` | `bool` | Загрузка обновления |
| `_autostartError` | `string` | Ошибка записи автозапуска |

### Computed Properties
| Свойство | Тип | Описание |
|----------|-----|----------|
| `HasAutostartError` | `bool` | Есть ошибка автозапуска |

## События

| Событие | Тип | Описание |
|---------|-----|----------|
| `ThemeChangeRequested` | `Action<ElementTheme>` | Запрос смены темы |
| `DialogRequested` | `Func<string, string, string, string, Task<bool>>` | Запрос ContentDialog (import/export/reset) |

## Команды (RelayCommand)

| Команда | Метод | Описание |
|---------|-------|----------|
| `ResetDnsPortCommand` | `ResetDnsPort()` | Сброс порта DNS на 5353 |
| `CheckUpdatesCommand` | `CheckUpdatesAsync()` | Проверить обновления |
| `DownloadUpdateCommand` | `DownloadUpdateAsync()` | Скачать обновление |
| `ExportSettingsCommand` | `ExportSettingsAsync()` | Экспорт настроек в JSON |
| `ImportSettingsCommand` | `ImportSettingsAsync()` | Импорт настроек из JSON |
| `ResetSettingsCommand` | `ResetSettingsAsync()` | Сброс всех настроек |
| `OpenLogsFolderCommand` | `OpenLogsFolderAsync()` | Открыть папку логов |

## Используемые сервисы

| Сервис | Интерфейс | Назначение |
|--------|-----------|------------|
| `AppSettingsService` | `IAppSettingsService` | Чтение/запись настроек |
| `StrategyManager` | `IStrategyManager` | Список стратегий |
| `MalwLinkUpdateService` | — | Проверка обновлений |

## Логика работы

### Auto-save (Property Changed)
Все настройки сохраняются автоматически через `partial void On{Property}Changed()`:
- `OnAutoProtectChanged` → `_settings.AutoProtect`
- `OnSelectedStrategyIndexChanged` → `_settings.DefaultStrategy`
- `OnSelectedThemeIndexChanged` → `_settings.AppTheme` + `ThemeChangeRequested`
- `OnSelectedLanguageIndexChanged` → `_settings.Language`
- `OnAutostartChanged` → `_settings.Autostart` + `WriteAutostartToRegistry()`
- И т.д. для всех настроек

### Autostart (Registry)
- **Ключ:** `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`
- **Имя:** `Z-UI`
- **Значение:** `"path\to\Z-UI.exe" --minimized` (если StartMinimized)
- **ReadAutostartFromRegistry():** Проверка наличия ключа
- **WriteAutostartToRegistry():** Запись/удаление ключа

### LoadFromSettings()
Загрузка всех настроек из `IAppSettingsService` при старте:
- Маппинг enum/index значений (theme, language, dns mode, log level)

### LoadStrategies()
1. Добавление "auto" стратегии
2. Загрузка из `StrategyManager.GetAvailableStrategies()`
3. Выбор текущей стратегии по `_settings.DefaultStrategy`

### ResetSettingsAsync
1. Запрос подтверждения через `DialogRequested`
2. Сброс всех настроек к дефолтным значениям
3. Удаление автозапуска из реестра
4. Перезагрузка списков стратегий

### Export/Import
- **Export:** FileSavePicker → JSON из `settings.json`
- **Import:** FileOpenPicker → десериализация → `SetSetting()` → `LoadFromSettings()`
