# Network — Сетевые настройки

## Обзор

Страница сетевых настроек — DNS over HTTPS, DNS Proxy, Fake DNS, Block Detection.

## ViewModel

**Класс:** `DnsPageViewModel` (`ZUI.ViewModels`)

**Наследование:** `ViewModelBase`

**Lifecycle:**
- **Constructor:** DI + загрузка `DnsPort` из `AppSettings`. НЕ запускает PowerShell процессы.
- **Refresh():** Вызывается из `NetworkPage.OnNavigatedTo` — проверяет DoH статус + Worker DNS статус.
- **SetDispatcherQueue():** Устанавливается извне для UI thread marshalling.

## Состояния (ObservableProperty)

### DNS over HTTPS (local Windows)
| Поле | Тип | Описание |
|------|-----|----------|
| `_isSecureDnsEnabled` | `bool` | DoH включён в Windows |
| `_isDohSupported` | `bool` | Windows поддерживает DoH |
| `_statusMessage` | `string` | Статус DoH |
| `_providerName` | `string?` | Текущий провайдер |
| `_recommendation` | `string?` | Рекомендация |
| `_isApplying` | `bool` | Применение настроек |
| `_selectedProviderIndex` | `int` | Индекс провайдера (0=MalwLink, 1=Google, 2=Cloudflare, 3=Quad9) |

### DNS Proxy / Worker DNS
| Поле | Тип | Описание |
|------|-----|----------|
| `_isDnsProxyRunning` | `bool` | DNS Proxy активен |
| `_dnsProxyStatus` | `string` | Статус DNS Proxy |
| `_isDnsProxyApplying` | `bool` | Применение DNS Proxy |
| `_isFakeDnsEnabled` | `bool` | Fake DNS включён |
| `_selectedDnsMode` | `int` | 0 = DoH (Windows), 1 = DNS Proxy (Worker) |
| `_dnsPort` | `int` | Порт DNS Proxy (default: 5353) |

### Списки
| Свойство | Тип | Описание |
|----------|-----|----------|
| `Providers` | `List<string>` | MalwLink Recommended, Google DNS, Cloudflare, Quad9 |
| `DnsModes` | `List<string>` | DoH, DNS Proxy |

## Команды (RelayCommand)

| Команда | Метод | Описание |
|---------|-------|----------|
| `EnableDohCommand` | `EnableDohAsync()` | Включить DoH с выбранным провайдером |
| `DisableDohCommand` | `DisableDohAsync()` | Отключить DoH |
| `StartDnsProxyCommand` | `StartDnsProxyAsync()` | Запустить DNS Proxy (Worker + EnhancedDnsManager) |
| `StopDnsProxyCommand` | `StopDnsProxyAsync()` | Остановить DNS Proxy |
| `ToggleFakeDnsCommand` | `ToggleFakeDnsAsync()` | Переключить Fake DNS |
| `RefreshCommand` | `Refresh()` | Обновить все статусы (DoH + Worker DNS) |

## Используемые сервисы

| Сервис | Интерфейс | Назначение |
|--------|-----------|------------|
| `DnsService` | `IDnsService` | DoH управление (PowerShell) |
| `EnhancedDnsManager` | `IEnhancedDnsManager` | DNS bypass управление |
| `IpcClientService` | `IIpcClientService` | Worker DNS конфигурация |
| `AppSettingsService` | `IAppSettingsService` | DNS порт |

## Логика работы

### EnableDohAsync
1. Выбрать провайдера по индексу (malw/google/cloudflare/quad9)
2. `DnsService.EnableSecureDnsAsync(providerId)`
3. Обновить статус

### StartDnsProxyAsync
1. `EnhancedDnsManager.EnableDnsBypassAsync()` — включить DNS bypass
2. `IpcClientService.ConfigureDnsAsync(enableDoh: true, enableFakeDns: ...)` — Worker DNS
3. `DnsService.EnableSecureDnsAsync("google")` — локальный DoH
4. Обновить статус

### StopDnsProxyAsync
1. `IpcClientService.ConfigureDnsAsync(enableDoh: false, enableFakeDns: false)` — отключить Worker DNS
2. `EnhancedDnsManager.DisableDnsBypassAsync()` — отключить DNS bypass
3. `DnsService.DisableSecureDnsAsync()` — отключить локальный DoH
4. Обновить статус

### Refresh()
1. `Task.Run(() => DnsService.GetDnsStatus())` — проверка DoH (PowerShell, 500-1500ms)
2. `RefreshWorkerDnsStatusAsync()` — IPC к Worker
3. `Task.Run(() => DnsService.IsSecureDnsEnabled())` — проверка локального DoH
4. `UpdateDnsProxyStatusWithCache(isSecureDnsEnabled)` — обновление UI

### DNS Port
- Диапазон: 1024-65535
- Сохраняется в `AppSettings.DnsPort` при изменении
