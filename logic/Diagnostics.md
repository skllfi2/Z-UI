# Diagnostics — Диагностика системы

## Обзор

Страница диагностики — проверка системы, WinDivert, админ прав, сети, доменных списков, бинарных файлов.

## ViewModel

**Класс:** `DiagnosticsViewModel` (`ZUI.ViewModels`)

**Наследование:** `ViewModelBase`, `IDisposable`

**Lifecycle:**
- **Constructor:** DI только (lightweight)
- **RunDiagnosticsAsync():** Запускает все 6 проверок параллельно
- **RunQuickCheckAsync():** Быстрая проверка (auto-run при навигации)

## Проверки (6 штук)

| # | Проверка | Метод | Что проверяет |
|---|----------|-------|---------------|
| 1 | Администратор | `CheckAdminRightsAsync()` | Запущено ли от администратора |
| 2 | Worker Service | `CheckWorkerProcessAsync()` | Доступность Worker через IPC |
| 3 | WinDivert | `CheckWinDivertAsync()` | Наличие WinDivert.dll и драйвера |
| 4 | Domain Lists | `CheckDomainListsAsync()` | Наличие файлов доменных списков |
| 5 | Binary Files | `CheckBinaryFilesAsync()` | Наличие бинарных файлов zapret |
| 6 | Network | `TestConnectivityAsync()` | Доступность Google/YouTube/Discord |

## Состояния (ObservableProperty)

### Summary
| Поле | Тип | Описание |
|------|-----|----------|
| `_passedChecks` | `int` | Пройденных проверок |
| `_totalChecks` | `int` | Всего проверок |
| `_overallStatus` | `string` | Общий статус |
| `_summaryText` | `string` | Текст.summary |

### Per-check properties (каждая проверка имеет 4 поля)
| Поле | Тип | Описание |
|------|-----|----------|
| `_{check}StatusText` | `string` | Статус (ОК/Ошибка) |
| `_{check}InfoText` | `string` | Детальная информация |
| `_{check}FixAction` | `string` | Рекомендация по исправлению |
| `Is{check}` | `bool` | Проверка пройдена |

### Admin check
| Поле | Тип |
|------|-----|
| `_isAdmin` | `bool` |
| `_adminStatusText` | `string` |
| `_adminInfoText` | `string` |
| `_adminFixAction` | `string` |

### Worker check
| Поле | Тип |
|------|-----|
| `_isWorkerReachable` | `bool` |
| `_workerStatusText` | `string` |
| `_workerInfoText` | `string` |
| `_workerFixAction` | `string` |

### WinDivert check
| Поле | Тип |
|------|-----|
| `_isWinDivertOk` | `bool` |
| `_winDivertStatusText` | `string` |
| `_winDivertInfoText` | `string` |
| `_winDivertFixAction` | `string` |

### Domain Lists check
| Поле | Тип |
|------|-----|
| `_isDomainListsOk` | `bool` |
| `_domainListsStatusText` | `string` |
| `_domainListsInfoText` | `string` |
| `_domainListsFixAction` | `string` |

### Binary Files check
| Поле | Тип |
|------|-----|
| `_isBinaryFilesOk` | `bool` |
| `_binaryFilesStatusText` | `string` |
| `_binaryFilesInfoText` | `string` |
| `_binaryFilesFixAction` | `string` |

### Network check
| Поле | Тип |
|------|-----|
| `_isNetworkOk` | `bool` |
| `_networkStatusText` | `string` |
| `_networkInfoText` | `string` |
| `_networkFixAction` | `string` |

### Results / Logs
| Поле | Тип | Описание |
|------|-----|----------|
| `_diagnosticResults` | `ObservableCollection<DiagnosticResult>` | Результаты всех проверок |
| `_logLines` | `ObservableCollection<string>` | Лог действий (макс. 500 строк) |
| `_isRunning` | `bool` | Диагностика выполняется |

### Computed Properties
| Свойство | Тип | Описание |
|----------|-----|----------|
| `HasResults` | `bool` | Есть результаты |
| `CanExport` | `bool` | Можно экспортировать отчёт |

## Команды (RelayCommand)

### Основные
| Команда | Метод | Описание |
|---------|-------|----------|
| `RunDiagnosticsCommand` | `RunDiagnosticsAsync()` | Запустить все 6 проверок параллельно |
| `RunQuickCheckCommand` | `RunQuickCheckAsync()` | Быстрая проверка (health check) |
| `ExportReportCommand` | `ExportReportAsync()` | Экспорт отчёта в .txt |
| `ClearLogsCommand` | `ClearLogs()` | Очистить лог |

### Fix команды
| Команда | Метод | Описание |
|---------|-------|----------|
| `FixAdminCommand` | `FixAdmin()` | Перезапуск от администратора |
| `FixWorkerCommand` | `FixWorkerAsync()` | Установка/запуск Worker + IPC reconnect |
| `FixWinDivertCommand` | `FixWinDivert()` | Инфо (WinDivert ставится автоматически) |
| `FixDomainListsCommand` | `FixDomainLists()` | Инфо о восстановлении списков |
| `FixBinaryFilesCommand` | `FixBinaryFiles()` | Инфо о восстановлении файлов |
| `FixNetworkCommand` | `FixNetwork()` | Инфо о сетевых проблемах |

## Используемые сервисы

| Сервис | Интерфейс | Назначение |
|--------|-----------|------------|
| `DiagnosticsService` | `IDiagnosticsService` | Все проверки системы |
| `WorkerServiceManager` | `IWorkerServiceManager` | Fix Worker команды |
| `IpcClientService` | `IIpcClientService` | IPC reconnect после Fix Worker |

## Логика работы

### RunDiagnosticsAsync
1. Запуск 6 проверок параллельно (`Task.WhenAll`)
2. Применение результатов к per-check свойствам через `ApplyCheckResult()`
3. Получение полного списка результатов (`RunAllChecksAsync()`)
4. Вычисление summary (passed/total)
5. Логирование завершения

### RunQuickCheckAsync
1. `QuickHealthCheckAsync()` — быстрая проверка
2. Обновление passed/total
3. Логирование проблем

### FixWorkerAsync
1. Если не установлен → `InstallAsync()`
2. Если не запущен → `StartAsync()`
3. IPC reconnect (`ConnectAsync()`)
4. Обновление статуса

### ExportReportAsync
1. Создание файла `z-ui-diagnostics-{date}.txt` в `Documents/Z-UI/`
2. Запись статуса, результатов, лога
