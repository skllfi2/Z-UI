# TelegramProxy — Telegram прокси

## Обзор

Страница Telegram прокси — SOCKS5→WebSocket прокси и MTProxy для обхода блокировок Telegram.

## ViewModel

**Класс:** `TgProxyViewModel` (`ZUI.ViewModels`)

**Наследование:** `ObservableObject`

**Lifecycle:**
- **Constructor:** DI только (lightweight)
- **SetDispatcherQueue():** Устанавливает `DispatcherQueue` для UI thread
- **InitializeAsync():** Вызывается из `TgProxyPage.OnNavigatedTo` — обновляет статус

## Состояния (ObservableProperty)

### SOCKS5→WS Proxy
| Поле | Тип | Описание |
|------|-----|----------|
| `_isSocks5Running` | `bool` | SOCKS5 прокси активен |
| `_socks5Port` | `int` | Порт SOCKS5 (default: 1080) |
| `_wsUrl` | `string` | WebSocket URL (default: wss://web.telegram.org/ws) |
| `_wsSecret` | `string` | Секрет WebSocket |
| `_isSocks5Toggling` | `bool` | Переключение SOCKS5 |
| `_socks5Status` | `string` | Статус SOCKS5 |

### MTProxy
| Поле | Тип | Описание |
|------|-----|----------|
| `_isMtProxyRunning` | `bool` | MTProxy активен |
| `_mtProxyPort` | `int` | Порт MTProxy (default: 8888) |
| `_mtProxySecret` | `string` | Секрет MTProxy |
| `_isMtProxyToggling` | `bool` | Переключение MTProxy |
| `_mtProxyStatus` | `string` | Статус MTProxy |

### Common
| Поле | Тип | Описание |
|------|-----|----------|
| `_activeConnections` | `int` | Активных соединений |
| `_proxyLink` | `string` | Ссылка для подключения |

## Команды (RelayCommand)

| Команда | Метод | Описание |
|---------|-------|----------|
| `ToggleSocks5Command` | `ToggleSocks5Async()` | Вкл/выкл SOCKS5→WS прокси |
| `ToggleMtProxyCommand` | `ToggleMtProxyAsync()` | Вкл/выкл MTProxy |
| `StopAllCommand` | `StopAllAsync()` | Остановить все прокси |
| `RefreshStatusCommand` | `RefreshStatusAsync()` | Обновить статус |
| `CopyProxyLinkCommand` | `CopyProxyLink()` | Копировать ссылку в буфер |

## Используемые сервисы

| Сервис | Интерфейс | Назначение |
|--------|-----------|------------|
| `TelegramProxyService` | `ITelegramProxyService` | Start/Stop SOCKS5/MTProxy, статус, генерация ссылок |

## Логика работы

### ToggleSocks5Async
1. Если запущен → `StopSocks5Async()`
2. Если не запущен:
   - Проверка `WsUrl` не пустой
   - `StartSocks5Async(port, wsUrl, wsSecret)`
   - Генерация `ProxyLink`

### ToggleMtProxyAsync
1. Если запущен → `StopMtProxyAsync()`
2. Если не запущен:
   - Проверка `MtProxySecret` не пустой
   - `StartMtProxyAsync(port, secret)`
   - Генерация `ProxyLink`

### StopAllAsync
1. `StopAllAsync()` — остановка всех прокси
2. Сброс всех состояний

### RefreshStatusAsync
1. `RefreshStatusAsync()` — получение статуса
2. Обновление всех полей из `Status` объекта

### CopyProxyLink
1. `DataPackage.SetText(ProxyLink)`
2. `Clipboard.SetContent(dp)`
3. Обработка `InvalidOperationException`, `COMException`
