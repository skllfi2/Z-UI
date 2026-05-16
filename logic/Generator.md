# Generator — Генератор стратегий

## Обзор

Страница генерации DPI bypass стратегий. Две вкладки:
- **Tab 1:** Готовые стратегии — тестирование существующих
- **Tab 2:** Создание стратегии — генерация + тестирование + применение

## ViewModel

**Класс:** `GeneratorViewModel` (`ZUI.ViewModels`)

**Наследование:** `ObservableObject`

**Lifecycle:**
- **Constructor:** DI + `LoadReadyStrategies()` (Tab 1)
- **SetDispatcherQueue():** Устанавливает `DispatcherQueue` для UI thread
- **InitializeAsync():** Вызывается из `GeneratorPage.OnNavigatedTo` — загружает параметры, ISP профили, определяет провайдера (Tab 2). Имеет `_isInitialized` guard.

## Состояния (ObservableProperty)

### Tab 1: Ready Strategies
| Поле | Тип | Описание |
|------|-----|----------|
| `_currentStrategyName` | `string` | Имя выбранной стратегии |
| `_availableStrategies` | `ObservableCollection<StrategyInfo>` | Список стратегий |
| `_selectedStrategy` | `StrategyInfo?` | Выбранная стратегия |
| `_isTesting` | `bool` | Идёт тестирование |
| `_testResult` | `string` | Результат теста |

### Tab 2: Create Strategy
| Поле | Тип | Описание |
|------|-----|----------|
| `_availableServices` | `IReadOnlyList<ServiceConfig>` | Доступные сервисы |
| `_selectedServices` | `IList<object>` | Выбранные сервисы |
| `_detectedProviderName` | `string` | Имя обнаруженного провайдера |
| `_detectedProviderInfo` | `string` | Инфо о провайдере (ASN, метод, confidence) |
| `_isDetectingProvider` | `bool` | Идёт определение провайдера |
| `_selectedTestMode` | `int` | 0=Quick, 1=Full, 2=None |
| `_hasTestResults` | `bool` | Есть результаты тестов |
| `_generatedStrategyName` | `string` | Имя сгенерированной стратегии |
| `_testResults` | `IReadOnlyList<ServiceTestResultDisplay>` | Результаты тестов |
| `_isRunningTest` | `bool` | Идёт тест |
| `_isApplying` | `bool` | Идёт применение |
| `_generatedWinwsArgs` | `string` | CLI аргументы winws для стратегии |

### ISP Profile Dialog
| Поле | Тип | Описание |
|------|-----|----------|
| `_isChangeProviderDialogOpen` | `bool` | Диалог выбора провайдера открыт |
| `_availableProfiles` | `ObservableCollection<IspProfile>` | Доступные профили |
| `_dialogSelectedProfile` | `IspProfile?` | Выбранный профиль в диалоге |

### Custom Domains
| Поле | Тип | Описание |
|------|-----|----------|
| `_customDomains` | `ObservableCollection<string>` | Пользовательские домены |

### Tab Navigation
| Поле | Тип | Описание |
|------|-----|----------|
| `_selectedTab` | `int` | 0 = Ready Strategies, 1 = Create Strategy |

### DPI Method Selection
| Поле | Тип | Описание |
|------|-----|----------|
| `_availableDpiMethods` | `ObservableCollection<DpiMethod>` | Доступные DPI методы |
| `_selectedDpiMethod` | `DpiMethod?` | Выбранный метод |

### Method Parameters
| Поле | Тип | Описание |
|------|-----|----------|
| `_selectedFooling` | `string` | Fooling mode (default: "badseq") |
| `_fakeRepeats` | `int` | Количество повторов (default: 11) |
| `_splitPos` | `string` | Позиция split (default: "2") |
| `_splitSeqovl` | `int` | SplitSeqovl (default: 652) |
| `_fakedsplitPattern` | `string` | Паттерн fakedsplit (default: "0x00") |
| `_hostfakesplitMod` | `string` | Модификатор hostfakesplit |
| `_combineMultidisorder` | `bool` | Комбинировать multidisorder |

### Computed Properties
| Свойство | Тип | Описание |
|----------|-----|----------|
| `HasCustomDomains` | `bool` | Есть пользовательские домены |
| `ShowFoolingSelector` | `bool` | Показать selector fooling (fake/fakedsplit/hostfakesplit/syndata) |
| `ShowFakeRepeats` | `bool` | Показать fake repeats (fake/fakedsplit/udplen) |
| `ShowSplitParams` | `bool` | Показать split params (multisplit/multidisorder/syndata) |
| `ShowFakedsplitPattern` | `bool` | Показать fakedsplit pattern |
| `ShowHostfakesplitMod` | `bool` | Показать hostfakesplit mod |
| `ShowCombineMultidisorder` | `bool` | Показать combine multidisorder |
| `CanRunTest` | `bool` | Можно запустить тест |
| `CanApply` | `bool` | Можно применить стратегию |

## Команды (RelayCommand)

| Команда | Метод | Описание |
|---------|-------|----------|
| `RefreshStrategiesCommand` | `RefreshStrategiesAsync()` | Обновить список стратегий (Tab 1) |
| `TestStrategyCommand` | `TestStrategyAsync()` | Тестировать выбранную стратегию (Tab 1) |
| `ChangeProviderCommand` | `ChangeProviderAsync()` | Открыть диалог выбора провайдера (Tab 2) |
| `RunTestCommand` | `RunTestAsync()` | Сгенерировать + тестировать (Tab 2) |
| `ApplyStrategyCommand` | `ApplyStrategyAsync()` | Сохранить + применить стратегию (Tab 2) |

### Non-command методы
| Метод | Описание |
|-------|----------|
| `ConfirmProviderChange()` | Вызывается из code-behind при подтверждении диалога |
| `CancelProviderChange()` | Вызывается из code-behind при отмене диалога |
| `AddCustomDomain(domain)` | Добавить пользовательский домен |
| `RemoveCustomDomain(domain)` | Удалить пользовательский домен |
| `NotifyCanRunTestChanged()` | Уведомить об изменении CanRunTest/CanApply |

## Используемые сервисы

| Сервис | Интерфейс | Назначение |
|--------|-----------|------------|
| `StrategyGeneratorService` | `IStrategyGeneratorService` | Генерация стратегий, ISP detection |
| `StrategyManager` | `IStrategyManager` | Список стратегий, SetCustomStrategy |
| `AdaptiveEngine` | `IAdaptiveEngine` | Тестирование стратегий (IPC to Worker) |
| `LocalizationService` | — | i18n строки |

## Логика работы

### Tab 1: Ready Strategies
1. `LoadReadyStrategies()` — загрузка из `StrategyManager.GetAvailableStrategies()`
2. Добавление "Auto" в начало списка
3. `TestStrategyAsync()`:
   - Остановить защиту если активна
   - Установить стратегию через `StrategyManager.SetStrategy()`
   - Запустить через `AdaptiveEngine.StartWithStrategyAsync()`
   - Остановить после теста

### Tab 2: Create Strategy
1. `InitializeAsync()`:
   - Загрузить параметры (`LoadParametersAsync`)
   - Загрузить ISP профили (`LoadIspProfilesAsync`)
   - Определить провайдера (`DetectIspAsync`)
2. `RunTestAsync()`:
   - Выбранные сервисы + кастомные домены
   - Генерация стратегии (`GenerateAsync`)
   - Тестирование (`TestStrategyAsync`)
   - Quick/Full/None режим
3. `ApplyStrategyAsync()`:
   - Сохранить в user config (`SaveUserServicesAsync`)
   - Установить как активную (`StrategyManager.SetCustomStrategy()`)
   - В режиме Skip (2) — создать из текущего DPI метода

### DPI Method Parameters
- При выборе метода → установка дефолтных параметров из `Method.Params`
- Изменение любого параметра → обновление `GeneratedWinwsArgs` через `WinwsArgsBuilder.BuildMethodPreview()`
