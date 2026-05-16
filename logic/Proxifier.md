# Proxifier — Проксификатор

## Обзор

Страница проксификатора — маршрутизация трафика приложений через прокси-серверы. Управление серверами, правилами и цепочками.

## ViewModel

**Класс:** `ProxifierViewModel` (`ZUI.ViewModels`)

**Наследование:** `ObservableObject`

**Lifecycle:**
- **Constructor:** DI только (lightweight)
- **SetDispatcherQueue():** Устанавливает `DispatcherQueue` для UI thread
- **InitializeAsync():** Вызывается из `ProxifierPage.OnNavigatedTo` — обновляет статус, серверы, правила

## Состояния (ObservableProperty)

### Status
| Поле | Тип | Описание |
|------|-----|----------|
| `_isRunning` | `bool` | Проксификатор активен |
| `_isToggling` | `bool` | Переключение |
| `_statusText` | `string` | Текстовый статус |
| `_toggleButtonText` | `string` | Текст кнопки (Start/Stop) |

### Stats
| Поле | Тип | Описание |
|------|-----|----------|
| `_activeRules` | `int` | Активных правил |
| `_activeConnections` | `int` | Активных соединений |
| `_trafficSent` | `string` | Отправленный трафик (форматированный) |
| `_trafficReceived` | `string` | Полученный трафик (форматированный) |

### Collections
| Поле | Тип | Описание |
|------|-----|----------|
| `_servers` | `ObservableCollection<ProxyServerDisplayModel>` | Список серверов |
| `_rules` | `ObservableCollection<ProxyRuleDisplayModel>` | Список правил |
| `_chains` | `ObservableCollection<ProxyChainDisplayModel>` | Список цепочек |
| `_selectedServer` | `ProxyServerDisplayModel?` | Выбранный сервер |
| `_selectedRule` | `ProxyRuleDisplayModel?` | Выбранное правило |

## Команды (RelayCommand)

| Команда | Метод | Описание |
|---------|-------|----------|
| `ToggleProxifierCommand` | `ToggleProxifierAsync()` | Вкл/выкл проксификатор |
| `RefreshStatusCommand` | `RefreshStatusAsync()` | Обновить статус и статистику |
| `AddServerCommand` | `AddServerAsync(server)` | Добавить сервер |
| `RemoveServerCommand` | `RemoveServerAsync(server)` | Удалить сервер |
| `CheckServerCommand` | `CheckServerAsync(server)` | Проверить сервер |
| `AddRuleCommand` | `AddRuleAsync(rule)` | Добавить правило |
| `RemoveRuleCommand` | `RemoveRuleAsync(rule)` | Удалить правило |
| `RefreshServersCommand` | `RefreshServersAsync()` | Обновить список серверов |
| `RefreshRulesCommand` | `RefreshRulesAsync()` | Обновить список правил |

## Используемые сервисы

| Сервис | Интерфейс | Назначение |
|--------|-----------|------------|
| `ProxifierService` | `IProxifierService` | CRUD серверов/правил, Start/Stop, статистика |

## Логика работы

### ToggleProxifierAsync
1. Если `IsRunning` → `StopAsync()`
2. Если не запущен → `StartAsync()`
3. Обновить `ToggleButtonText` и статус

### InitializeAsync
1. `RefreshStatusAsync()` — статус + статистика
2. `RefreshServersAsync()` — список серверов
3. `RefreshRulesAsync()` — список правил
4. Все вызовы с обработкой `IOException`, `TimeoutException`, `ObjectDisposedException`

### RefreshStatusAsync
1. `ProxifierService.RefreshStatusAsync()`
2. Обновление `IsRunning`, `ActiveRules`, `ActiveConnections`, `TrafficSent`, `TrafficReceived`

### Server/Rule CRUD
- Добавление → `ProxifierService.AddServerAsync/AddRuleAsync()` → `Refresh`
- Удаление → `ProxifierService.RemoveServerAsync/RemoveRuleAsync()` → `Refresh`
- Проверка сервера → `ProxifierService.CheckServerAsync()`

### Traffic Stats
- Форматирование: B → KB → MB → GB
- Источник: `ProxifierService.Status.TotalBytesSent/TotalBytesReceived`
