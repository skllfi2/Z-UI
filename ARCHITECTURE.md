# Z-UI — Полная архитектурная справка

> **Файл-хранилище всей собранной информации по архитектуре.**
> Создан: 2026-05-08. Обновлять при каждом изменении плана.

---

## 1. Название проекта

**ZUI = Zero Unrestricted Internet**

- Смысл: ноль ограничений → свободный интернет
- Неформальный слоган: «ЗУИ — свободный интернет без ограничений»
- Брендинг: логотип вокруг `Z` или `0→∞`

---

## 2. Суть продукта

DPI bypass GUI shell, объединяющий:

1. **zapret-discord-youtube** (Flowseal) — WinDivert + winws.exe стратегии
2. **tg-ws-proxy** (Flowseal) — Rust SOCKS5→WebSocket bridge для Telegram
3. **dns.malw.link** (ImMALWARE) — DoH/UDP DNS bypass с хостлистами
4. **Proxifier-функционал** — полная самостоятельная реализация поверх WinDivert (не обёртка!)

Архитектура: **UI (WinUI 3)** ↔ **IPC (Named Pipes)** ↔ **Worker (Windows Service, SYSTEM)** ↔ **WinDivert (kernel driver)**

---

## 3. Текущая архитектура (as-is)

### 3.1 Структура проектов

```
Z-UI/
├── Z-UI/           # Main WinUI 3 project
│   ├── Services/   # 19+ services (DI-registered)
│   ├── Views/      # 4 XAML pages + code-behind
│   ├── ViewModels/ # 7 VMs
│   ├── Windows/    # MainWindow.xaml.cs (NOT at project root!)
│   ├── Models/     # Data models
│   ├── Controls/   # Custom controls
│   ├── Converters/ # Value converters
│   └── zapret/     # DPI bypass binaries + strategies
├── ZUI.Ipc/        # Shared IPC protocol (17 requests, 8 responses, 4 events)
├── ZUI.Worker/     # Windows Service (Native AOT, SYSTEM) — Orchestrator + WinDivert
├── ZUI.Proxy/      # Full native proxifier engine
├── ZUI.SDK/        # Integration SDK
└── ZUI.Tests/      # Unit tests (227/228 pass)
```

### 3.2 Ключевые сервисы UI

| Сервис | Назначение | Строк |
|--------|------------|-------|
| `AdaptiveEngine` | DPI bypass orchestration via IPC to Worker | 591 |
| `IpcClientService` | Named Pipe client to Worker | |
| `WorkerServiceManager` | Windows Service install/start/stop (P/Invoke) | 1187 |
| `StrategyManager` | Strategy loading, ISP detection | 626 |
| `StrategyGeneratorService` | Generate custom strategies | 411 |
| `DiagnosticsService` | System health checks | 514 |
| `DnsProxyHostedService` | Split DNS with blocked domains | |
| `EnhancedDnsManager` | dns.malw.link DoH/UDP bypass | 947 |
| `ProtectionService` | Coordinating DPI bypass + DNS | |
| `AppSettingsService` | Centralized settings sync | |
| `ProxifierService` | Thin IPC wrapper to Worker ProxifierEngine | |
| `HostlistService` | GitHub refresh, local cache | 481 |
| `IspDetectionService` | ISP detection | 216 |
| `StrategyTestService` | Test runner | 384 |
| `WindowSubclass` | Win32 SetWindowSubclass for WM_HOTKEY/WM_COMMAND/tray | |
| `HotkeyService` | Global hotkey registration + WM_HOTKEY handling | |
| `TrayIcon` | System tray icon with CallbackMessageId | |
| `ToastNotifier` | WinRT toast with AUMID registration | ~306 |
| `SoundService` | MediaPlayer for .wav, ElementSoundPlayer fallback | |
| `UpdateChecker` | GitHub API release check | |
| `LocalizationService` | i18n with ru/en dictionaries (~130 keys each) | |

### 3.3 ViewModels

| ViewModel | Строк | Проблемы |
|-----------|-------|----------|
| `DashboardViewModel` | 856 | God object: 6 deps, 30+ ObservableProperty |
| `GeneratorViewModel` | 825 | Tab 1 + Tab 2 coordination |
| `SettingsPage.xaml.cs` | 639 | All settings handlers |
| `ProxifierViewModel` | ~150 | Только Start/Stop + 4 поля статуса, НЕТ настроек |
| `DnsPageViewModel` | | DNS page |
| `StrategyViewModel` | | Strategy selection |
| `DiagnosticsViewModel` | | Health checks |

### 3.4 IPC Protocol

- **Pipe name**: `ZUI_IPC`
- **Format**: 4-byte LE length prefix + JSON body
- **Timeout**: 3000ms, Reconnect: exponential backoff 2s→60s
- **31 concrete IpcMessage derived types** (полиморфный краш fix)
- **17 request types**, **8 response types**, **4 event types** (PacketStats, BypassStopped, LogEntry, TgProxyClientConnected)

### 3.5 Worker (ZUI.Worker)

- **Orchestrator.cs** — 20+ constructor deps, гигантский `HandleRequestAsync` switch
- **WorkerService.cs** — BackgroundService hosting IPC + Orchestrator
- Запускается как SYSTEM через P/Invoke advapi32.dll

### 3.6 ProxifierEngine (ZUI.Proxy)

Полная нативная реализация proxifier поверх WinDivert:

```
WinDivert SYN intercept → PidMapper (PID→processName) → RuleEvaluator (process+addr→action) → Action
```

**Действия**: Direct (pass), Proxy (TcpRelay через прокси), Chain (TcpRelay через цепочку), Block (drop SYN)

**Компоненты**:

| Компонент | Файл | Назначение |
|-----------|------|------------|
| `ProxifierEngine` | ProxifierEngine.cs (721 строк) | Главный движок: SYN intercept loop, NAT table |
| `ProxyRule` | Rules/ProxyRule.cs | Модель правила: ProcessName, ProcessNamePattern, ProcessId, DestinationIp, DestinationPort → Action |
| `RuleEvaluator` | Rules/RuleEvaluator.cs | Сопоставление: PID → имя → wildcard/regex → IP/CIDR → port range |
| `ProxyTarget` | Rules/ProxyRule.cs | Целевой прокси: Host, Port, Type (Socks4/4a/Socks5/HttpConnect), Username, Password |
| `ProxyAction` | Rules/ProxyRule.cs | Enum: Direct, Proxy, Chain, Block |
| `ProxyType` | Rules/ProxyRule.cs | Enum: Socks4, Socks4a, Socks5, HttpConnect |
| `DnsPolicy` | Rules/ProxyRule.cs | Enum: Local, ThroughProxy |
| `ProxyProfileManager` | Profile/ProxyProfile.cs | ✅ Существует (LoadAsync, SaveAsync, AddServer, RemoveServer, UpdateServer, AddRule, RemoveRule) |
| `ProxyChain` | Chain/ProxyChain.cs | Именованная цепочка прокси-серверов |
| `ChainExecutor` | Chain/ChainExecutor.cs | Последовательное прохождение chain |
| `FailoverPolicy` | Chain/FailoverPolicy.cs | Стратегии отработки отказа |
| `Socks5Client` | Client/Socks5Client.cs | SOCKS5 протокол (TCP + UDP, auth) |
| `Socks4Client` | Client/Socks4Client.cs | SOCKS4/4a протокол |
| `HttpConnectClient` | Client/HttpConnectClient.cs | HTTP CONNECT протокол |
| `TcpRelay` | TcpRelay.cs | App ↔ relay ↔ proxy ↔ target туннель |
| `TrafficMonitor` | Traffic/TrafficMonitor.cs | Per-connection + global aggregation (ConcurrentDictionary) |
| `TrafficStats` | Traffic/TrafficStats.cs | Single connection: bytes, duration, speed |
| `PidMapper` | | PID → processName resolution |

---

## 4. Слабые места (выявлено 10)

| # | Проблема | Где | Влияние |
|---|----------|-----|---------|
| 1 | **Нет ядра** — нет единого фасада | `IAdaptiveEngine` слишком узкий (DNS-first→DPI fallback) | Каждая функция добавляется как отдельный сервис без связности |
| 2 | **DashboardVM — god object** | 6 deps, 30+ ObservableProperty | Любое изменение ломает dashboard |
| 3 | **Orchestrator — god switch** | 20+ deps, гигантский HandleRequestAsync | Добавление IPC-команды = изменение гигантского файла |
| 4 | **DNS сервисы дублируются** | `IDnsService` (PowerShell DoH) + `IEnhancedDnsManager` (dns.malw.link DoH/UDP) | Конкурируют за одни и те же настройки, путаница в UI |
| 5 | **Трафик-мониторинг существует, но невидим** | `TrafficMonitor` в ZUI.Proxy не экспортирует данные через IPC | Dashboard не видит per-app/per-domain статистику |
| 6 | **Нет понимания блокировок** | Нет системы определения что заблокировано и как | Обход «вслепую» — применяем стратегию не зная, заблокирован ли ресурс |
| 7 | **Proxifier UI отсутствует** | ProxifierViewModel = только Start/Stop | Нет UI для: серверов, цепочек, правил, проверки |
| 8 | **Доменная маршрутизация отсутствует** | `ProxyRule` не содержит `DestinationDomain` | Правила только по процессу + IP/порт, нет `*.google.com` |
| 9 | **IPC протокол неполный** | Нет запросов для: proxy CRUD, rule CRUD, stats, block-detection | Worker функциональность не доступна из UI |
| 10 | **Тонкие IPC-обёртки** | `ProxifierService`, `TelegramProxyService` — facade без реальной логики | Дублирование, путаница между facade и kernel |

---

## 5. Что взято от Proxifier (сравнение)

### 5.1 Реализовано ✅

| Функция | Оригинальный Proxifier | Z-UI |
|---------|----------------------|------|
| Per-app маршрутизация | LSP/WFP | WinDivert SYN intercept → PidMapper → RuleEvaluator |
| SOCKS4/4a/5 | ✅ | ✅ (Socks4Client, Socks5Client) |
| HTTP CONNECT (HTTPS) | ✅ | ✅ (HttpConnectClient) |
| Аутентификация | Username/Password | ✅ (ProxyTarget.Username/Password) |
| Цепочки прокси | ✅ (Pro feature) | ✅ (ProxyChain + ChainExecutor) |
| Failover | ✅ | ✅ (FailoverPolicy) |
| Блокировка соединений | ✅ (Block rule) | ✅ (ProxyAction.Block → drop SYN) |
| DNS через прокси | ✅ | ✅ (DnsPolicy.ThroughProxy) |
| Трафик-мониторинг | ✅ | ⚠️ (TrafficMonitor существует, но НЕ экспортируется в UI) |

### 5.2 НЕ реализовано ❌

| Функция | Оригинальный Proxifier | Z-UI | Приоритет |
|---------|----------------------|------|-----------|
| **UI: Proxy Servers list** | ✅ Dialog: Address, Port, Protocol | ❌ Нет UI | ВЫСОКИЙ |
| **UI: Authentication dialog** | ✅ Enable + Username/Password | ❌ Нет UI | ВЫСОКИЙ |
| **UI: Check proxy** | ✅ Кнопка "Check" | ❌ Нет | СРЕДНИЙ |
| **UI: Advanced (DNS через прокси)** | ✅ | ❌ Нет UI (модель есть) | СРЕДНИЙ |
| **UI: Rules list** | ✅ App→Proxy table | ❌ Нет UI | ВЫСОКИЙ |
| **UI: Chain builder** | ✅ Drag-drop chain builder | ❌ Нет UI | СРЕДНИЙ |
| **Домены в правилах** | ✅ `*.google.com`, `*.discord.com` | ❌ Только IP/CIDR | ВЫСОКИЙ |
| **NTLM / Kerberos** | ✅ Корпоративные протоколы | ❌ Нет (не в модели) | НИЗКИЙ |
| **Send User-Agent** | ✅ Опция | ❌ Нет (не в модели) | НИЗКИЙ |
| **Per-app/per-domain traffic stats** | ✅ | ❌ (TrafficMonitor есть, но не виден) | ВЫСОКИЙ |

### 5.3 Архитектурная разница

- **Proxifier** работает через LSP (Layered Service Provider) — видит `connect()` вызов приложения, где домен ещё доступен до резолвинга
- **Z-UI** работает через WinDivert — видит уже IP-пакеты, DNS уже произошёл
- Для доменной маршрутизации нужен **DNS-сниффер** или **TLS SNI extraction** (см. раздел 6.2)

---

## 6. Предлагаемая архитектура (to-be)

### 6.1 Ядро: INetworkShield (вместо IAdaptiveEngine)

Три столпа:

```
INetworkShield
├── IBlockDetector     — понимание что заблокировано и как (passive + active пробы)
├── IBypassEngine      — DPI + DNS обход (расширение текущего AdaptiveEngine)
└── ITrafficWatch      — полная статистика трафика (per-app, per-domain, global)
```

**Зачем**: `IAdaptiveEngine` — это «DNS-first→DPI fallback», слишком узкий. `INetworkShield` — это полноценное ядро, которое:
1. Понимает что заблокировано (BlockDetector) — не работает вслепую
2. Обходит блокировки (BypassEngine) — DPI + DNS + proxy routing
3. Контролирует трафик (TrafficWatch) — статистика, мониторинг, аномалии

### 6.2 DNS-сниффер + DnsCache (ФУНДАМЕНТ)

**Проблема**: WinDivert видит только IP в SYN-пакете. Домен уже резолвлен. Правила по доменам невозможны.

**Решение**: Два подхода, комбинируются:

1. **DNS-сниффер** (надёжнее)
   - Второй WinDivert фильтр: `udp.DstPort == 53` или `udp.SrcPort == 53`
   - Парсить DNS-ответы (A/AAAA записи)
   - Строить кэш: `IP → [домены]` с TTL
   - В `EvaluateOutboundSyn` — lookup IP в кэше, передать домен в `Evaluate()`

2. **TLS SNI extraction** (для HTTPS, порт 443)
   - Парсить ClientHello из перехваченного пакета
   - Извлечь SNI (Server Name Indication)
   - Работает даже если DNS был закэширован

**Зависимости от DnsCache**:
- **Доменная маршрутизация** (ProxyRule.DestinationDomain) — нужен DnsCache
- **Per-domain traffic stats** (TrafficWatch) — нужен DnsCache
- **PassiveBlockAnalyzer** (определение блокировок по доменам) — нужен DnsCache

**Поэтому DnsCache — первый инфраструктурный кусок, от которого зависят три модуля.**

### 6.3 Доменная маршрутизация в ProxyRule

Расширение модели:

```csharp
public sealed class ProxyRule
{
    // Существующие:
    public string? ProcessName { get; set; }
    public string? ProcessNamePattern { get; set; }
    public int? ProcessId { get; set; }
    public string? DestinationIp { get; set; }
    public string? DestinationPort { get; set; }

    // НОВОЕ:
    /// <summary>Целевой домен (точное совпадение: "discord.com").</summary>
    public string? DestinationDomain { get; set; }

    /// <summary>Шаблон домена (wildcard: "*.google.com", "*.discord.com").</summary>
    public string? DestinationDomainPattern { get; set; }

    // Обновить IsDefault:
    [JsonIgnore]
    public bool IsDefault => string.IsNullOrEmpty(ProcessName)
        && string.IsNullOrEmpty(ProcessNamePattern)
        && !ProcessId.HasValue
        && string.IsNullOrEmpty(DestinationIp)
        && string.IsNullOrEmpty(DestinationPort)
        && string.IsNullOrEmpty(DestinationDomain)
        && string.IsNullOrEmpty(DestinationDomainPattern);
}
```

Расширение RuleEvaluator.Evaluate():

```csharp
public RuleEvalResult Evaluate(
    string processName,
    IPAddress destinationIp,
    int destinationPort,
    string? domainName = null)  // НОВОЕ
```

### 6.4 Proxifier UI (три секции)

#### Секция 1: Proxy Servers

Список сохранённых прокси-серверов. Диалог добавления:

| Поле | Тип | Пример | Описание |
|------|-----|--------|----------|
| Name | TextBox | "My SOCKS5" | Название для UI |
| Address | TextBox | "143.14.205.27" | IP или домен прокси-сервера |
| Port | NumberBox | 10000 | Порт |
| Protocol | ComboBox | SOCKS4/SOCKS4a/SOCKS5/HTTP CONNECT | Тип прокси |
| Authentication | ToggleSwitch | On/Off | Включить аутентификацию |
| Username | TextBox | "rH9LB5ip8Kq14CEko" | Логин |
| Password | PasswordBox | •••••• | Пароль |
| DNS Policy | ComboBox | Local / ThroughProxy | Куда резолвить DNS |
| Check | Button | | Проверить работоспособность |
| Advanced | Button | | Расширенные настройки |

#### Секция 2: Proxy Chains

- Список цепочек
- Builder: drag-drop серверов из секции 1 в цепочку
- Failover policy на цепочку

#### Секция 3: Rules

Таблица правил маршрутизации:

| Колонка | Тип | Описание |
|---------|-----|----------|
| Priority | Number | Порядок применения |
| Process | TextBox/Wildcard | Имя процесса или шаблон (`*chrome*`) |
| Domain | TextBox/Wildcard | Домен или шаблон (`*.discord.com`) — **НОВОЕ** |
| IP/Port | TextBox | IP/CIDR + порт/диапазон |
| Action | ComboBox | Direct / Proxy / Chain / Block |
| Target | ComboBox | Выбор прокси из секции 1 или цепочки из секции 2 |

### 6.5 StatsModule на Worker

`TrafficMonitor` уже собирает per-connection статистику. Нужно:

1. Добавить IPC-событие `TrafficStatsEvent` с per-app + per-domain агрегацией
2. Добавить IPC-запрос `GetTrafficStats` для получения текущих данных
3. В UI — подписаться на событие, показать в Dashboard/Proxifier page

### 6.6 BlockDetector

Два уровня:

**PassiveBlockAnalyzer** (Worker-side, из WinDivert событий):
- RST после SYN → возможная блокировка
- Timeout (no SYN-ACK) → возможная блокировка
- Малые пакеты (16-20 KB drop) → типичный DPI-паттерн
- Аномалии TTL → возможная манипуляция

**ActiveBlockProber** (UI-side):
- DNS probe: сравнить локальный резолв vs DoH резолв
- TCP probe: попытка соединения с целевым хостом
- HTTP probe: проверка HTTP status code
- TLS probe: проверка certificate/SNI
- Методология из [dpi-detector](https://github.com/Runnin4ik/dpi-detector) — но нативная реализация на C#, не Python

### 6.7 Слияние DNS сервисов

```
IDnsService (PowerShell DoH, 947 строк)
    +
IEnhancedDnsManager (dns.malw.link DoH/UDP)
    ↓
IDnsBypassService (единый интерфейс)
    ├── WindowsDohMode  — системный DoH через PowerShell
    └── ExternalDohMode — dns.malw.link DoH/UDP с хостлистами
```

### 6.8 Разборка Orchestrator

```
Orchestrator (гигантский switch)
    ↓
Orchestrator (тонкий маршрутизатор)
    ├── DpiBypassModule    — StartBypass/StopBypass/PacketStats
    ├── DnsModule          — ConfigureDns/GetDnsStatus
    ├── ProxifierModule    — StartProxifier/StopProxifier/ProxyCRUD/RuleCRUD
    ├── TgProxyModule      — StartTgProxy/StopTgProxy
    └── StatsModule        — GetTrafficStats/TrafficEvents
```

---

## 7. План реализации (10 шагов)

| Шаг | Что | Зависимости | Приоритет |
|-----|-----|-------------|-----------|
| 1 | **DNS-сниффер + DnsCache** — WinDivert UDP фильтр, парсинг DNS-ответов, IP→домен кэш с TTL | Нет | ВЫСОКИЙ (фундамент для 2,3,4,5) |
| 2 | **DestinationDomain в ProxyRule** + расширение `Evaluate()` | Шаг 1 | ВЫСОКИЙ |
| 3 | **IPC расширение** — запросы: ProxyCRUD, RuleCRUD, CheckProxy, GetTrafficStats, GetBlockStatus | Нет | ВЫСОКИЙ |
| 4 | **Proxifier UI** — три секции (Servers, Chains, Rules) | Шаг 3 | ВЫСОКИЙ |
| 5 | **StatsModule на Worker** — экспорт TrafficMonitor через IPC (per-app + per-domain) | Шаг 1 | ВЫСОКИЙ |
| 6 | **PassiveBlockAnalyzer** — анализ WinDivert событий (RST, timeout, малые пакеты) | Шаг 1 | СРЕДНИЙ |
| 7 | **ActiveBlockProber** — DNS+TCP+HTTP+TLS пробы на UI-стороне | Нет | СРЕДНИЙ |
| 8 | **INetworkShield фасад** — объединяет BlockDetector + BypassEngine + TrafficWatch | Шаги 5,6,7 | СРЕДНИЙ |
| 9 | **Слияние DNS сервисов** — IDnsService + IEnhancedDnsManager → IDnsBypassService | Нет | СРЕДНИЙ |
| 10 | **Разборка Orchestrator** — модули: DpiBypass, Dns, Proxifier, TgProxy, Stats | Шаг 3 | НИЗКИЙ |

---

## 8. Исследованные внешние проекты

### 8.1 dpi-detector (Runnin4ik)

- **URL**: https://github.com/Runnin4ik/dpi-detector
- **Язык**: Python
- **Что делает**: Определяет тип блокировки (TLS, TCP, HTTP, DNS) + обнаруживает 16-20KB пакеты (DPI drop signature)
- **Как используем**: НЕ встраиваем как бинарник. Берём **методологию** и реализуем нативно в C# как `ActiveBlockProber`
- **Ключевые техники**: DNS comparison (локальный vs DoH), TCP connect probe, TLS handshake probe, HTTP status code check

### 8.2 rkn-block-checker (MayersScott)

- **URL**: https://github.com/MayersScott/rkn-block-checker
- **Статус**: Репозиторий 404 — недоступен
- **Альтернатива**: Используем passive detection через HostlistService + DNS comparison вместо отдельного checker

### 8.3 tg-ws-proxy (Flowseal)

- **URL**: https://github.com/Flowseal/tg-ws-proxy
- **Язык**: Rust
- **Что делает**: SOCKS5→WebSocket bridge для Telegram (обход блокировки Telegram)
- **Архитектура**: SOCKS5 listener → WebSocket connection к Telegram API
- **Как используем**: Встроенный в Worker как отдельный модуль, не внешний процесс

### 8.4 zapret-discord-youtube (Flowseal)

- **URL**: https://github.com/Flowseal/zapret-discord-youtube
- **Что делает**: WinDivert + winws.exe стратегии для DPI обхода
- **Как используем**: Ядро DPI обхода, управляемое через Worker. Стратегии загружаются из `Z-UI/zapret/strategies/*.bat`

---

## 9. Существующие IPC-запросы (для расширения)

### Запросы (17 типов)

StartBypass, StopBypass, GetBypassStatus, ConfigureDns, GetDnsStatus,
StartProxifier, StopProxifier, GetProxifierStatus,
StartTgProxy, StopTgProxy, GetTgProxyStatus,
Ping, GetStats, GetWorkerVersion, SetStrategy, GetStrategy, ConfigureHostlist

### Ответы (8 типов)

BypassStatus, DnsStatus, ProxifierStatus, TgProxyStatus,
StatsResponse, WorkerVersion, StrategyInfo, ResultResponse

### События (4 типа)

PacketStatsEvent, BypassStoppedEvent, LogEntryEvent, TgProxyClientConnectedEvent

### Новые IPC-запросы (для добавления)

| Запрос | Тип ответа | Описание |
|--------|-----------|----------|
| `AddProxyServer` | ResultResponse | Добавить прокси-сервер |
| `RemoveProxyServer` | ResultResponse | Удалить прокси-сервер |
| `UpdateProxyServer` | ResultResponse | Обновить прокси-сервер |
| `CheckProxyServer` | CheckProxyResponse | Проверить работоспособность прокси |
| `AddProxyRule` | ResultResponse | Добавить правило маршрутизации |
| `RemoveProxyRule` | ResultResponse | Удалить правило |
| `UpdateProxyRule` | ResultResponse | Обновить правило |
| `GetProxyProfile` | ProxyProfileResponse | Получить текущий профиль (серверы + правила + цепочки) |
| `GetTrafficStats` | TrafficStatsResponse | Per-app + per-domain + global статистика |
| `GetBlockStatus` | BlockStatusResponse | Текущие обнаруженные блокировки |
| `ProbeDomain` | ProbeResultResponse | Результат active пробы домена |

### Новые IPC-события

| Событие | Описание |
|---------|----------|
| `TrafficStatsEvent` | Периодическая отправка агрегированной статистики (per-app, per-domain) |
| `BlockDetectedEvent` | Обнаружена блокировка (passive: RST/timeout/anomaly) |

---

## 10. Модель данных ProxyTarget (уже существует)

```csharp
public sealed class ProxyTarget
{
    public string Name { get; set; } = string.Empty;       // "My SOCKS5"
    public ProxyType Type { get; set; } = ProxyType.Socks5; // Socks4/Socks4a/Socks5/HttpConnect
    public string Host { get; set; } = "127.0.0.1";        // 143.14.205.27
    public int Port { get; set; } = 1080;                  // 10000
    public string? Username { get; set; }                  // rH9LB5ip8Kq14CEko
    public string? Password { get; set; }                  // скрыто
    [JsonIgnore] public bool RequiresAuth => !string.IsNullOrEmpty(Username);
}
```

**Для кейса пользователя** (143.14.205.27:10000, HTTPS, логин rH9LB5ip8Kq14CEko):
```csharp
new ProxyTarget
{
    Name = "My HTTPS Proxy",
    Type = ProxyType.HttpConnect,  // HTTPS = HTTP CONNECT
    Host = "143.14.205.27",
    Port = 10000,
    Username = "rH9LB5ip8Kq14CEko",
    Password = "..."
}
```

---

## 11. Критические готчи (из AGENTS.md + CONTEXT.md)

1. `[ObservableProperty]` → ONLY `private <type> _fieldName` style. **NEVER partial properties** → WinRT.Runtime.dll crash
2. `App.MainWindow` — instance property, NOT static → `(App.Current as App)?.MainWindow`
3. `IntPtr` cannot be `const` → `static readonly IntPtr`
4. `HostResolutionException` НЕ существует → используй `SocketException`
5. `HwndSource.AddHook` → NEVER. Use `SetWindowSubclass` from comctl32.dll
6. `DispatcherTimer` → `DispatcherQueueTimer` (preferred)
7. Worker runs as SYSTEM, UI runs as user → communication only via Named Pipe `ZUI_IPC`
8. `IpcSerializer.Result` — `readonly struct`, оператор `?.` НЕ применим
9. `dotnet build` может падать с XamlCompiler → VS 2026
10. Converters с `NotImplementedException` в `ConvertBack` — НОРМА, НЕ ТРОГАТЬ
11. `SUBCLASSPROC` delegate MUST be stored as instance field (GC prevention)
12. VM → наследуй от ViewModelBase + `SetDispatcherQueue()`. НЕ `GetForCurrentThread()` в конструкторе
13. Конструктор VM = ТОЛЬКО DI assignment + lightweight defaults. PowerShell/Process → `Task.Run()` + async `Refresh()`
14. `OnNavigatedTo` → ВСЕГДА `_isInitialized` guard
15. `RelayCommand` async → НИКОГДА не вызывай `.Execute()` из OnNavigatedTo. Только `.ExecuteAsync()`

---

---

## 12. Конкретный план реализации — очередность и файлы

### 12.0 Приоритеты и зависимости

```
Приоритет 1 (БЛОКИРУЕТ всё остальное):
├── Шаг A: IPC-расширение (ZUI.Ipc) — запросы/ответы для proxy CRUD
├── Шаг B: Worker-обработчики (ZUI.Worker) — обработка новых IPC-запросов
└── Шаг C: ProxifierService (Z-UI/Services) — UI-side методы для proxy CRUD

Приоритет 2:
├── Шаг D: ProxifierPage (Z-UI/Views) — XAML + code-behind
└── Шаг E: ProxifierViewModel — расширение, поля для Proxy Servers UI

Приоритет 3:
└── Шаг F: DI регистрация + навигация — подключить страницу в App.xaml.cs + MainWindow
```

**Почему именно такой порядок**:
- IPC-запросы и ответы = контракт. Без контракта нельзя писать ни Worker, ни UI
- Worker-обработчики работают с ProxyProfileManager (✅ существует, расширен AddServer/RemoveServer/UpdateServer/AddRule/RemoveRule) — Orchestrator.HandleRequestAsync имеет 7 новых case'ов
- ProxifierService — копирует паттерн из существующего кода (StartAsync/StopAsync) для новых методов
- UI можно писать последним, когда IPC и Worker готовы — тестировать можно отдельно

---

### 12.1 Шаг A: IPC-расширение (ZUI.Ipc)

**Файл для изменения**: `ZUI.Ipc/IpcRequest.cs` — добавить записи (records)

**Новые запросы**:

```csharp
// ── Proxy Server CRUD ─────────────────────────────────
public sealed record AddProxyServerRequest(
    string Name, string Host, int Port,
    string ProxyType,   // "Socks4" | "Socks4a" | "Socks5" | "HttpConnect"
    string? Username, string? Password,
    string DnsPolicy    // "Local" | "ThroughProxy"
) : IpcRequest;

public sealed record RemoveProxyServerRequest(string ServerId) : IpcRequest;
public sealed record UpdateProxyServerRequest(string ServerId, string? Name, string? Host, int? Port,
    string? ProxyType, string? Username, string? Password, string? DnsPolicy) : IpcRequest;
public sealed record GetProxyProfileRequest(bool IncludeRules = true, bool IncludeChains = true) : IpcRequest;

// ── Proxy Check ───────────────────────────────────────
public sealed record CheckProxyRequest(
    string Host, int Port, string ProxyType,
    string? Username, string? Password, string? TestUrl = null
) : IpcRequest;

// ── Proxy Rule CRUD ───────────────────────────────────
public sealed record AddProxyRuleRequest(
    string? ProcessName, string? ProcessNamePattern, int? ProcessId,
    string? DestinationIp, string? DestinationPort,
    string? DestinationDomain,        // НОВОЕ поле
    string? DestinationDomainPattern, // НОВОЕ поле
    string Action,   // "Direct" | "Proxy" | "Chain" | "Block"
    string? ProxyServerId, string? ChainName, string DnsPolicy
) : IpcRequest;

public sealed record RemoveProxyRuleRequest(string RuleId) : IpcRequest;
```

**Файл для изменения**: `ZUI.Ipc/IpcResponse.cs` — добавить записи

**Новые ответы**:

```csharp
public sealed record ProxyProfileResponse(
    List<ProxyServerInfo> Servers,
    List<ProxyChainInfo> Chains,
    List<ProxyRuleInfo> Rules
) : IpcResponse;

public sealed record CheckProxyResponse(
    bool Success, string? Error, long LatencyMs
) : IpcResponse;
```

**Файл для изменения**: `ZUI.Ipc/IpcMessage.cs` — добавить `[JsonDerivedType]` дискриминаторы для всех новых запросов/ответов

**Файл для проверки**: `ZUI.Ipc/IpcSerializer.cs` — убедиться что новые типы в ProxyJsonContext (source gen для AOT)

---

### 12.2 Шаг B: Worker-обработчики (ZUI.Worker)

**Файл для изменения**: `ZUI.Worker/Orchestrator.cs`

Добавить case'ы в `HandleRequestAsync` switch:

```csharp
case AddProxyServerRequest addProxyReq:
    var proxy = new ProxyTarget
    {
        Name = addProxyReq.Name,
        Host = addProxyReq.Host,
        Port = addProxyReq.Port,
        Type = Enum.Parse<ProxyType>(addProxyReq.ProxyType),
        Username = addProxyReq.Username,
        Password = addProxyReq.Password,
    };
    return await _proxyProfileManager.AddServerAsync(proxy);

case RemoveProxyServerRequest removeReq:
    return await _proxyProfileManager.RemoveServerAsync(removeReq.ServerId);

case GetProxyProfileRequest getProfileReq:
    return await _proxyProfileManager.GetProfileAsync(getProfileReq.IncludeRules, getProfileReq.IncludeChains);

case CheckProxyRequest checkReq:
    return await _proxifierEngine.CheckProxyAsync(checkReq.Host, checkReq.Port, checkReq.ProxyType,
        checkReq.Username, checkReq.Password);

case AddProxyRuleRequest addRuleReq:
    return await _proxyProfileManager.AddRuleAsync(/* map to ProxyRule */);
```

**Файл для изменения / новый файл**: `ZUI.Worker/ProxifierModule.cs` (опционально — если пользователь хочет начать модульность уже сейчас)

**Примечание**: `_proxyProfileManager` уже существует в конструкторе Orchestrator. `CheckProxyAsync` в ProxifierEngine — новый метод, принимает параметры, пытается TCP connect + протокольный handshake, возвращает CheckProxyResponse.

---

### 12.3 Шаг C: ProxifierService (Z-UI/Services)

**Файл для изменения**: `Z-UI/Services/ProxifierService.cs`

Добавить методы в интерфейс `IProxifierService` и реализацию:

```csharp
public interface IProxifierService
{
    // Существующие:
    bool IsRunning { get; }
    ProxifierStatus? Status { get; }
    Task<Result> StartAsync(CancellationToken ct = default);
    Task<Result> StopAsync(CancellationToken ct = default);
    Task RefreshStatusAsync(CancellationToken ct = default);

    // ── НОВОЕ: Proxy Server CRUD ─────────────────
    Task<Result> AddProxyServerAsync(ProxyServerDisplayModel server, CancellationToken ct = default);
    Task<Result> RemoveProxyServerAsync(string serverId, CancellationToken ct = default);
    Task<Result> UpdateProxyServerAsync(string serverId, ProxyServerDisplayModel server, CancellationToken ct = default);
    Task<List<ProxyServerDisplayModel>> GetProxyServersAsync(CancellationToken ct = default);

    // ── НОВОЕ: Proxy Check ───────────────────────
    Task<CheckProxyResult> CheckProxyAsync(ProxyServerDisplayModel server, CancellationToken ct = default);

    // ── НОВОЕ: Proxy Rule CRUD ───────────────────
    Task<Result> AddRuleAsync(ProxyRuleDisplayModel rule, CancellationToken ct = default);
    Task<Result> RemoveRuleAsync(string ruleId, CancellationToken ct = default);
    Task<List<ProxyRuleDisplayModel>> GetRulesAsync(CancellationToken ct = default);

    // ── События ──────────────────────────────────
    event EventHandler<ProxyProfileChangedEventArgs>? ProfileChanged;
}
```

**Новый файл**: `Z-UI/Models/ProxyServerDisplayModel.cs` — UI-модель для отображения прокси-сервера (без Password в памяти)
**Новый файл**: `Z-UI/Models/ProxyRuleDisplayModel.cs` — UI-модель для правила маршрутизации

---

### 12.4 Шаг D: ProxifierPage (Z-UI/Views)

**Новый файл**: `Z-UI/Views/ProxifierPage.xaml`

Страница с тремя вкладками (Pivot / TabView):

```xml
<TabView>
    <TabViewItem Header="Proxy Servers">
        <!-- Список серверов + кнопка Add/Edit/Remove -->
        <ListView ItemsSource="{Binding Servers}" />
    </TabViewItem>
    <TabViewItem Header="Rules">
        <!-- Таблица правил -->
        <DataGrid ItemsSource="{Binding Rules}" />
    </TabViewItem>
    <TabViewItem Header="Chains">
        <!-- Список цепочек -->
        <ListView ItemsSource="{Binding Chains}" />
    </TabViewItem>
</TabView>
```

**Новый файл**: `Z-UI/Views/ProxifierPage.xaml.cs` — code-behind (минимум логики, подписки на события VM)

**Новый файл**: `Z-UI/Views/Dialogs/ProxyServerDialog.xaml` — ContentDialog для добавления/редактирования прокси-сервера

**Новый файл**: `Z-UI/Views/Dialogs/ProxyServerDialog.xaml.cs` — code-behind для диалога

**Новый файл**: `Z-UI/Views/Dialogs/ProxyRuleDialog.xaml` — ContentDialog для добавления/редактирования правила

**Новый файл**: `Z-UI/Views/Dialogs/ProxyRuleDialog.xaml.cs` — code-behind для диалога

---

### 12.5 Шаг E: ProxifierViewModel — расширение

**Файл для изменения**: `Z-UI/ViewModels/ProxifierViewModel.cs`

Добавить ObservableProperty:

```csharp
// ── Существующие (оставить) ─────────────────────
IsRunning, IsToggling, StatusText, ToggleButtonText,
ActiveRules, ActiveConnections, TrafficSent, TrafficReceived

// ── НОВОЕ: Proxy Servers ─────────────────────────
[ObservableProperty] private ObservableCollection<ProxyServerDisplayModel> _servers;
[ObservableProperty] private ProxyServerDisplayModel? _selectedServer;

// ── НОВОЕ: Rules ─────────────────────────────────
[ObservableProperty] private ObservableCollection<ProxyRuleDisplayModel> _rules;
[ObservableProperty] private ProxyRuleDisplayModel? _selectedRule;

// ── НОВОЕ: Chains ────────────────────────────────
[ObservableProperty] private ObservableCollection<ProxyChainDisplayModel> _chains;

// ── НОВОЕ: Commands ──────────────────────────────
[RelayCommand] private async Task AddServerAsync();
[RelayCommand] private async Task EditServerAsync(ProxyServerDisplayModel? server);
[RelayCommand] private async Task RemoveServerAsync(ProxyServerDisplayModel? server);
[RelayCommand] private async Task CheckServerAsync(ProxyServerDisplayModel? server);
[RelayCommand] private async Task AddRuleAsync();
[RelayCommand] private async Task RemoveRuleAsync(ProxyRuleDisplayModel? rule);
```

**Примечание**: Все ObservableProperty — `private <type> _fieldName` (правило проекта №1)

---

### 12.6 Шаг F: DI регистрация + навигация

**Файл для изменения**: `Z-UI/App.xaml.cs`

```csharp
// Регистрация ViewModel (если ещё нет — проверить):
services.AddTransient<ProxifierPage>();
services.AddSingleton<ProxifierViewModel>();
```

**Файл для изменения**: `Z-UI/Windows/MainWindow.xaml`

Добавить NavigationViewItem для страницы Proxifier:
```xml
<NavigationViewItem Content="Proxifier" Tag="ProxifierPage" />
```

**Файл для изменения**: `Z-UI/Windows/MainWindow.xaml.cs`

Добавить case в switch/MainWindow:
```csharp
case "ProxifierPage":
    ContentFrame.Navigate(typeof(ProxifierPage));
    break;
```

---

### 12.7 Резюме — что создаётся и изменяется

| Приоритет | Файл | Действие | Что содержит |
|-----------|------|----------|--------------|
| 1-A | `ZUI.Ipc/IpcRequest.cs` | ИЗМЕНИТЬ | 7 новых record'ов запросов |
| 1-A | `ZUI.Ipc/IpcResponse.cs` | ИЗМЕНИТЬ | 2 новых record'а ответов |
| 1-A | `ZUI.Ipc/IpcMessage.cs` | ИЗМЕНИТЬ | JsonDerivedType дискриминаторы |
| 1-B | `ZUI.Worker/Orchestrator.cs` | ИЗМЕНИТЬ | 6 новых case'ов в switch |
| 1-C | `Z-UI/Services/ProxifierService.cs` | ИЗМЕНИТЬ | Новые методы в интерфейс + реализацию |
| 2-D | `Z-UI/Views/ProxifierPage.xaml` | СОЗДАТЬ | XAML страница с TabView (3 вкладки) |
| 2-D | `Z-UI/Views/ProxifierPage.xaml.cs` | СОЗДАТЬ | Code-behind |
| 2-D | `Z-UI/Views/Dialogs/ProxyServerDialog.xaml` | СОЗДАТЬ | ContentDialog: Address, Port, Protocol, Auth |
| 2-D | `Z-UI/Views/Dialogs/ProxyServerDialog.xaml.cs` | СОЗДАТЬ | Code-behind диалога |
| 2-D | `Z-UI/Views/Dialogs/ProxyRuleDialog.xaml` | СОЗДАТЬ | ContentDialog: Process, Domain, Action, Target |
| 2-D | `Z-UI/Views/Dialogs/ProxyRuleDialog.xaml.cs` | СОЗДАТЬ | Code-behind диалога |
| 2-E | `Z-UI/ViewModels/ProxifierViewModel.cs` | ИЗМЕНИТЬ | ObservableProperty + RelayCommand для CRUD |
| 2-E | `Z-UI/Models/ProxyServerDisplayModel.cs` | СОЗДАТЬ | UI-модель (без Password в plaintext) |
| 2-E | `Z-UI/Models/ProxyRuleDisplayModel.cs` | СОЗДАТЬ | UI-модель для правила |
| 3-F | `Z-UI/App.xaml.cs` | ИЗМЕНИТЬ | DI регистрация |
| 3-F | `Z-UI/Windows/MainWindow.xaml` | ИЗМЕНИТЬ | NavigationViewItem |
| 3-F | `Z-UI/Windows/MainWindow.xaml.cs` | ИЗМЕНИТЬ | Навигационный case |

**Итого: 6 новых файлов, 10 изменяемых файлов**

---

*Последнее обновление: 2026-05-08 (архитектурный анализ + proxifier аудит + домены + название ZUI + план реализации Proxy Servers UI)*
