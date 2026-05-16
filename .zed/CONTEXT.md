# Z-UI — Context Bootloader

> **ПРОЧТИ ЭТОТ ФАЙЛ ПЕРВЫМ при старте новой сессии.**
> Кодовая фраза: `ZUI-BOOT-2026` — если пользователь произнёс её, сразу загрузи контекст отсюда.

---

## 1. Кто я

Я — Sisyphus, AI-агент проекта Z-UI. Работаю через opencode.
Язык общения с пользователем — **русский**.

## 2. Что такое Z-UI

DPI bypass GUI shell для [zapret-discord-youtube](https://github.com/Flowseal/zapret-discord-youtube).
Архитектура: **UI (WinUI 3)** ↔ **IPC (Named Pipes)** ↔ **Worker (Windows Service, SYSTEM)** ↔ **WinDivert (kernel driver)**

## 3. Стек

- C# 14, .NET 10, WinUI 3, MVVM (CommunityToolkit.Mvvm 8.4.2)
- Visual Studio 2026 (.slnx), Zed (AI-assisted IDE) / opencode desktop
- Target: net10.0-windows10.0.19041.0
- 5 production проектов + 1 test проект, 228 unit tests (227 pass, 1 pre-existing fail)

## 4. Статус проекта

### СБОРКА: 5/5 ✅ (0 ошибок, 0 предупреждений)

| Проект | Статус |
|--------|--------|
| ZUI.Core | ✅ |
| ZUI.Ipc | ✅ |
| ZUI.Proxy | ✅ |
| Z-UI | ✅ |
| ZUI.Worker | ✅ |
| ZUI.Tests | ✅ (227/228 tests pass) |

### DONE ✅

**W1-W6 (ранее):**
- IProtectionService→IAdaptiveEngine миграция
- Все 7 ViewModel на LocalizationService.Get()
- Dashboard редизайн: 8→4 карточки 2×2
- Navigation speed fix (_isInitialized guard)
- NetworkPage Expander→TabView
- SettingsPage split borders, GeneratorPage border normalization

**W7 — Worker Service (Native AOT):**
- Worker AOT publish + DI fixes (PidMapper, FakePacketBuilder, DnsProxyConfig)
- IpcSerializer freeze fix (5MB buffer → chunked read)
- ProxyJsonContext source generator (AOT reflection fix)
- Worker service installed + RUNNING as Windows Service
- IpcMessage полиморфный краш fix (31 concrete типа вместо polymorphic deserialization)

**W8 — WorkerServiceManager (P/Invoke):**
- WorkerServiceManager: 465→1187 строк, P/Invoke advapi32.dll (SafeServiceHandle, [LibraryImport])
- IWorkerServiceManager: +UninstallAsync, +ReinstallAsync, StatusChanged event
- UAC self-elevation: Program.cs кастомный Main() + `--elevated-worker-action` CLI
- install-worker.ps1 + scripts/*.ps1 — УДАЛЕНЫ (нативно только)
- Dashboard Uninstall/Reinstall кнопки
- IPC pipe E2E аудит + 3 бага: auto-reconnect, ReadExactAsync, Ping fire-and-forget

**W9 — ProxifierEngine + Рефакторинг:**
- ProxifierEngine WinDivert SYN intercept loop (586→721 строк, 0 TODO)
- GeneratorViewModel рефакторинг (999→825 строк): ServiceTestResultDisplay→Models/, JsonElementHelper→Models/, WinwsArgsBuilder.BuildMethodPreview()→Services/
- StrategyGeneratorService рефакторинг (1711→411 строк, -76%): IspDetectionService (216 строк), StrategyTestService (384 строки), WinwsArgsBuilder расширен (+BuildWinwsArgs, AppendMethodParams, GenerateHostlist, GetBinPath, GetListsPath, UnwrapValue → 599 строк), DefaultStrategyConfigs (366 строк)
- DI регистрация: IIspDetectionService, IStrategyTestService в App.xaml.cs

**W10 — Тесты + Warnings fix:**
- DashboardViewModelTests: добавлен Mock<IWorkerServiceManager>, 5-й параметр во все вызовы, Constructor_NullWorkerServiceManager_Throws
- DnsPageViewModel: result?.Error → result.Error ?? "Unknown error" (struct не поддерживает ?.)
- HostlistService: дублирующийся catch (TaskCanceledException) → catch when (ex.InnerException is TimeoutException) + общий catch
- ProxifierViewModel: result.Error → result.Error ?? "Unknown" (2 места, CS8604)

### PRE-EXISTING FAIL (не связано с нашими правками)

- `DnsPageViewModelTests.Constructor_UpdatesDnsProxyStatus_RunningWithDnsBypassActive` — падает, Assert.True() Failure

## 5. NIM Models (актуально на 2026-05-12)

### Протестированные модели (C# WinUI 3 + TS agent orchestration + stability)

| Модель | C# Score | Stability | Latency | Вердикт |
|--------|----------|-----------|---------|---------|
| `meta/llama-4-maverick-17b-128e-instruct` | 85 | 3/3 | ~10s | ✅ BEST overall |
| `meta/llama-3.3-70b-instruct` | 75 | 3/3 | 1.8s | ✅ MOST reliable |
| `qwen/qwen3.5-397b-a17b` | 80 | 3/3 | ~8s | ✅ BEST reasoning |
| `qwen/qwen3-coder-480b-a35b-instruct` | 78 | 2/3 | ~12s | ✅ GOOD coder |
| `moonshotai/kimi-k2.6` | 70 | 2/3 | ~6s | ✅ MED, Claude-like |
| `minimaxai/minimax-m2.7` | 65 | 2/3 | ~4s | ✅ FAST fallback |
| `deepseek-ai/deepseek-v4-pro` | 90 | 1/3 | 30-55s | ⚠️ BEST quality, FLAKY |
| `z-ai/glm-5.1` | 25 | 2/3 | ~5s | ❌ WEAK WinUI 3 knowledge |
| `nvidia/llama-3.1-nemotron-ultra-253b-v1` | — | 0/3 | — | ❌ DEAD 404 |
| `mistralai/mistral-large-3-675b-instruct-2512` | — | 0/3 | 120s+ | ❌ TIMEOUT |
| `openai/gpt-oss-120b` | 70 | 0/3 | — | ❌ UNSTABLE (was OK, then DEAD) |

### oh-my-openagent конфиг (`~/.config/opencode/oh-my-openagent.json`)

| Агент/Категория | Primary Model | Fallback 1 | Fallback 2 |
|------------------|---------------|------------|------------|
| sisyphus | kimi-k2.6 | llama-4-maverick | llama-3.3-70b |
| prometheus | llama-4-maverick | qwen3.5-397b | deepseek-v4-pro |
| atlas | llama-3.3-70b | llama-4-maverick | minimax-m2.7 |
| hephaestus | deepseek-v4-pro | qwen3-coder | llama-4-maverick |
| oracle | qwen3.5-397b | deepseek-v4-pro | llama-4-maverick |
| metis | llama-4-maverick | kimi-k2.6 | llama-3.3-70b |
| momus | qwen3-coder | qwen3.5-397b | llama-4-maverick |
| explore | llama-3.3-70b | minimax-m2.7 | llama-4-maverick |
| librarian | llama-3.3-70b | minimax-m2.7 | qwen3-coder |
| multimodal-looker | llama-4-maverick | llama-3.3-70b | — |
| sisyphus-junior | llama-3.3-70b | minimax-m2.7 | — |
| visual-engineering | llama-4-maverick | kimi-k2.6 | — |
| deep | deepseek-v4-pro | qwen3-coder | — |
| quick | llama-3.3-70b | minimax-m2.7 | — |
| ultrabrain | qwen3.5-397b | deepseek-v4-pro | llama-4-maverick |
| artistry | kimi-k2.6 | llama-4-maverick | — |
| unspecified-low | llama-3.3-70b | minimax-m2.7 | — |
| unspecified-high | llama-4-maverick | kimi-k2.6 | qwen3.5-397b |
| writing | kimi-k2.6 | llama-4-maverick | — |

**Плагин:** `oh-my-openagent@latest` зарегистрирован в `~/.config/opencode/opencode.json`

### Мёртвые модели (НЕ ИСПОЛЬЗОВАТЬ)
- `nvidia/llama-3.1-nemotron-ultra-253b-v1` — 404
- `deepseek-ai/deepseek-r1` — 410 EOL с 2026-01-26
- `deepseek-ai/deepseek-v4-flash` — FLAKY
- `openai/gpt-oss-120b` — нестабильный, то работает то 404
- `mistralai/mistral-large-3-675b-instruct-2512` — постоянные таймауты

**Предыдущее решение:** glm-5.1 ОСТАВИТЬ как основную → ПЕРЕСМОТРЕНО: glm-5.1 слаба в C# WinUI 3 (score 25), заменена на llama-4-maverick/qwen3.5.

## 6. Критические готчи (MUST KNOW)

1. `[ObservableProperty]` → ONLY `private <type> _fieldName` style. **NEVER partial properties** → WinRT.Runtime.dll crash
2. `App.MainWindow` — instance property, NOT static → `(App.Current as App)?.MainWindow`
3. `IntPtr` cannot be `const` → `static readonly IntPtr`
4. `HostResolutionException` НЕ существует → используй `SocketException`
5. `HwndSource.AddHook` → NEVER. Use `SetWindowSubclass` from comctl32.dll
6. `DispatcherTimer` → `DispatcherQueueTimer` (preferred)
7. `ElementSoundValue` NOT available in .NET 10 WinUI 3
8. `System.Windows.Forms` → NOT referenced → P/Invoke `GetCursorPos`
9. `AppSettings` (static, ZUI namespace) ≠ `IAppSettingsService` (DI, ZUI.Services namespace) — РАЗНЫЕ классы
10. Worker runs as SYSTEM, UI runs as user → communication only via Named Pipe `ZUI_IPC`
11. `dotnet build` может падать с XamlCompiler → используй VS 2026
12. LSP phantom CS0234/CS0246 для WinUI 3 SDK types → ignore
13. Converters с `NotImplementedException` в `ConvertBack` — это НОРМА, НЕ ТРОГАТЬ.
14. `SUBCLASSPROC` delegate MUST be stored as instance field (GC prevention)
15. `SecurityException` → `System.Security`; catch derived types BEFORE base types
16. `IpcSerializer.Result` — `readonly struct` (value type), оператор `?.` НЕ применим
17. `catch (TaskCanceledException)` + `catch (TaskCanceledException ex)` — дублирование. Используй `when` фильтр

## 7. Ключевые файлы

| Файл | Что | Строк |
|------|-----|-------|
| `Z-UI/App.xaml.cs` | DI + WindowSubclass + ExitApp disposal | |
| `Z-UI/Windows/MainWindow.xaml.cs` | MainWindow (NOT at project root!) | |
| `Z-UI/ViewModels/DashboardViewModel.cs` | Main dashboard logic, Worker commands | 856 |
| `Z-UI/ViewModels/GeneratorViewModel.cs` | Tab 1 + Tab 2 coordination | 825 |
| `Z-UI/Services/StrategyGeneratorService.cs` | Thin coordinator (делегирует) | 411 |
| `Z-UI/Services/WinwsArgsBuilder.cs` | Shared static: BuildMethodPreview + BuildWinwsArgs + AppendMethodParams | 599 |
| `Z-UI/Services/DefaultStrategyConfigs.cs` | Чистые дефолтные данные | 366 |
| `Z-UI/Services/IspDetectionService.cs` | ISP detection | 216 |
| `Z-UI/Services/StrategyTestService.cs` | Strategy test runner | 384 |
| `Z-UI/Services/WorkerServiceManager.cs` | P/Invoke advapi32.dll, SafeServiceHandle | 1187 |
| `Z-UI/Services/IWorkerServiceManager.cs` | Interface + WorkerServiceStatus enum + WorkerServiceResult | 94 |
| `Z-UI/Program.cs` | Custom Main() + `--elevated-worker-action` UAC | |
| `Z-UI/Models/ServiceTestResultDisplay.cs` | Record (extracted from VM) | 62 |
| `Z-UI/Models/JsonElementHelper.cs` | UnwrapToInt/UnwrapToString static | 35 |
| `ZUI.Proxy/ProxifierEngine.cs` | WinDivert SYN intercept loop | 721 |
| `ZUI.Worker/Orchestrator.cs` | WinDivert lifecycle, packet stats | |
| `ZUI.Ipc/IpcSerializer.cs` | Serialization + Result/Result<T> structs | 114 |
| `ZUI.Tests/DashboardViewModelTests.cs` | Unit tests (with IWorkerServiceManager mock) | 452 |

## 8. IPC Protocol

- Pipe name: `ZUI_IPC`
- Format: 4-byte LE length prefix + JSON body
- Timeout: 3000ms, Reconnect: exponential backoff 2s→60s
- Ping: fire-and-forget SendMessageAsync (NOT SendRequestAsync)
- PingInterval: 15s
- 18 request types, 10 response types
- 33+ concrete IpcMessage derived types (полиморфный краш fix)

## 9. Worker Service

- **ServiceName**: "Z-UI Worker"
- **PipeName**: "ZUI_IPC"
- **WorkerExePath**: ZUI.Worker.exe (Native AOT)
- **Runs as**: SYSTEM (Windows Service)
- **Install/Start/Stop/Uninstall/Reinstall**: P/Invoke advapi32.dll (НЕ sc.exe, НЕ PowerShell)
- **UAC elevation**: Self-elevation via ProcessStartInfo.Verb="runas" + `--elevated-worker-action`
- **StatusChanged event**: DashboardVM подписан → UpdateWorkerStatus

## 10. ProxifierEngine (WinDivert SYN Intercept)

- Filter: `"outbound and tcp.Syn and !tcp.Ack and !loopback"`
- PID: `_pidMapper.GetPidForConnection` (NETWORK layer не предоставляет PID — known limitation)
- Checksum: `SendPacket` вызывает `WinDivertHelperCalcChecksums` внутренне
- DI: Отдельный `WinDivertInterceptor` экземпляр через фабрику — PacketInterceptor и ProxifierEngine НЕ делят singleton

## 11. Адаптивный Engine (Zero-Config)

```
StartAdaptiveAsync →
1. EnhancedDnsManager.EnableDnsBypassAsync() → dns.malw.link DoH/UDP
2. IPC StartBypassAsync("auto", 0) + ConfigureDnsAsync → Worker (SYSTEM)
3. DirectWinwsService.StartAsync → winws.exe standalone fallback
```

### Новые сервисы (DI-зарегистрированы)

| Сервис | Файл | Строк | Назначение |
|--------|------|-------|------------|
| IAdaptiveEngine / AdaptiveEngine | Services/AdaptiveEngine.cs | 591 | 3-phase fallback |
| IEnhancedDnsManager / EnhancedDnsManager | Services/EnhancedDnsManager.cs | 947 | DNS bypass, DoH, hostlists |
| IWinwsManager / WinwsManager | Services/WinwsManager.cs | 369 | SemaphoreSlim, state tracking |
| IHostlistService / HostlistService | Services/HostlistService.cs | 481 | GitHub refresh, local cache |
| IIspDetectionService / IspDetectionService | Services/IspDetectionService.cs | 216 | ISP detection |
| IStrategyTestService / StrategyTestService | Services/StrategyTestService.cs | 384 | Test runner |

## 12. Конфигурация окружения

| Файл | Назначение |
|------|------------|
| `~/.config/opencode/opencode.json` | opencode config (providers, LSP, MCP) |
| `~/.local/share/opencode/auth.json` | NIM API keys (3 ключа) |
| `~/.config/opencode/oh-my-openagent.json` | agent/category model mappings |
| `.serena/project.yml` | Serena project config (Z-UI, csharp, 136 files) |
| `AGENTS.md` | Полная документация проекта (auto-loaded) |
| `.zed/CONTEXT.md` | Этот файл — контекстный загрузчик |

## 13. История ключевых решений

| Решение | Почему |
|---------|--------|
| glm-5.1 для code subagents | 45 sec vs 30 min timeout на старых моделях |
| 3-key architecture | KEY_1=main+consultants, KEY_2=search/review, KEY_3=code-executors |
| WorkerServiceManager P/Invoke | "в программе не должно быть запуска скриптов, только нативно" |
| UAC self-elevation | Нативный запуск служб требует админ-прав |
| IpcMessage 31 concrete типа | .NET 10 STJ polymorphic deserialization краш |
| IpcPipeClient auto-reconnect | SetDisconnected → fire-and-forget ReconnectLoopAsync |
| Ping = fire-and-forget | SendMessageAsync вместо SendRequestAsync (не блокирует) |
| StrategyGeneratorService thin coordinator | Делегирует IIspDetectionService, IStrategyTestService, WinwsArgsBuilder |
| WinwsArgsBuilder shared static | Используется GeneratorViewModel (BuildMethodPreview) + StrategyGeneratorService (BuildWinwsArgs) |
| glm-5.1 ОСТАВИТЬ | Пользователь решил терпеть FLAKY 3/5, retry есть в opencode |

## 14. Подводные камни из прошлых сессий

> 📖 **Полный каталог ошибок:** [`LESSONS_LEARNED.md`](../LESSONS_LEARNED.md)

### Navigation (WinUI 3)
- PowerShell/Process в конструкторе VM → страница открывается 1-2 сек. ВСЕГДА `Task.Run()` + async `Refresh()`
- `OnNavigatedTo` без `_isInitialized` guard → повторная навигация дёргает Refresh
- `RelayCommand.Execute()` на async команде → блокирует UI. Только `.ExecuteAsync()`
- VM с periodic refresh (timer) → OnNavigatedTo не нужен повторный InitializeAsync

### UI / Дизайн
- Dashboard карточки = сценарии, не технические функции
- Иконки в карточках → `ThemeResource` brushes, НЕ hardcoded цвета
- Настройки всегда последняя карточка
- Переключатели/комбобоксы строго справа, описание слева

### Агенты
- Старые модели субагентов (llama-3.3-70b-instruct) — таймаут 30 мин. Все на glm-5.1
- 7+ subagent outputs REJECTED на старой модели
- Агент может «отчитаться об успехе» но НЕ сохранить файл — ВСЕГДА проверяй содержимое
- `result?.Error` на struct → CS0023. Result = readonly struct, `?.` не работает

## 15. Что НЕ делать

- **git не трогать** — явное указание пользователя
- **НЕ использовать `[ObservableProperty]` с partial properties** — WinRT.Runtime.dll crash
- **НЕ использовать `HwndSource.AddHook`** — только `SetWindowSubclass`
- **НЕ использовать `const IntPtr`** — только `static readonly IntPtr`
- **НЕ catch `HostResolutionException`** — не существует; use `SocketException`
- **НЕ reference `System.Windows.Forms`** — P/Invoke вместо
- **НЕ suppress type errors** — `as any`, `@ts-ignore` запрещены
- **НЕ использовать `dotnet build` для диагностики** — VS 2026 (кроме ZUI.Tests.csproj)
- **НЕ использовать `DispatcherTimer`** — `DispatcherQueueTimer`
- **НЕ трогать Converters `ConvertBack` NotImplementedException** — норма
- **НЕ запускать скрипты из программы** — только нативно (P/Invoke)
- **НЕ duplicate catch blocks** — используй `when` фильтр
- **НЕ применять `?.` к struct** — проверяй value type vs reference type

## 16. Что дальше (remaining)

1. ~~Исправить DashboardViewModelTests.cs~~ ✅ DONE
2. ~~Исправить warnings~~ ✅ DONE
3. ~~Сборка 5/5~~ ✅ DONE
4. Исправить падающий тест `DnsPageViewModelTests.Constructor_UpdatesDnsProxyStatus_RunningWithDnsBypassActive` (pre-existing)
5. Удалить мёртвые модели из oh-my-openagent.json (nemotron-ultra, deepseek-r1, deepseek-v4-flash)
6. E2E тест Worker↔UI через IPC pipe (ручной запуск)
7. AI-slop чистка по новым файлам рефакторинга (~1700 строк от агентов)
8. Runtime верификация: HotkeyService/TrayIcon/Toast/Sound

## 17. Новая работа (2026-05-15)

### Реализовано ✅

**Шаг 1 — DNS Sniffer + DnsCache:**
- `ZUI.Core/Intercept/DnsSniffer.cs` — WinDivert UDP 53 фильтр, парсинг DNS-ответов (A/AAAA), заполнение DnsCache + DnsReverseCache
- `ZUI.Core/Dns/DnsReverseCache.cs` — перенесён из ZUI.Proxy (IP → domain кэш с TTL)
- `ZUI.Worker/Orchestrator.cs` — запуск/остановка сниффера вместе с DNS прокси
- `ZUI.Ipc/IpcResponse.cs` — DnsStatusResponse расширен (SnifferRunning, SnifferPackets, SnifferRecords)
- `Z-UI/Services/IpcClientService.cs` — WorkerDnsStatus расширен полями сниффера

**Шаг 2 — DestinationDomain в ProxyRule:**
- `ZUI.Proxy/Rules/ProxyRule.cs` — добавлены DestinationDomain + DestinationDomainPattern, обновлён IsDefault
- `ZUI.Proxy/Rules/RuleEvaluator.cs` — Evaluate() принимает domainName, добавлен MatchesDomain() (точное + wildcard *.example.com)
- `ZUI.Proxy/ProxifierEngine.cs` — EvaluateOutboundSyn() получает домен из DnsReverseCache перед оценкой правила

**Шаг 3 — IPC расширение (StatsModule):**
- `ZUI.Ipc/IpcRequest.cs` — GetTrafficStatsRequest
- `ZUI.Ipc/IpcResponse.cs` — TrafficStatsResponse + ConnectionStatsInfo
- `ZUI.Ipc/IpcMessage.cs` — JsonDerivedType для новых типов
- `ZUI.Worker/Orchestrator.cs` — HandleGetTrafficStats() (мапит ConnectionInfo → ConnectionStatsInfo)
- `Z-UI/Services/IpcClientService.cs` — GetTrafficStatsAsync()
- `Z-UI/Services/ProxifierService.cs` — GetTrafficStatsAsync()

**Proxifier UI (полностью реализован):**
- `Z-UI/Views/ProxifierPage.xaml` + `.xaml.cs` — страница с TabView (Servers, Rules, Chains)
- `Z-UI/ViewModels/ProxifierViewModel.cs` — 365 строк, все CRUD команды
- `Z-UI/Services/ProxifierService.cs` — полный интерфейс IProxifierService
- `Z-UI/Models/ProxyServerDisplayModel.cs` + `ProxyRuleDisplayModel.cs` — UI-модели
- DI регистрация + навигация из DashboardPage ✅

### Сборка: 5/5 ✅ (0 ошибок, 0 предупреждений)

---

*Последнее обновление: 2026-05-15 (DNS Sniffer, Domain Routing, StatsModule, Proxifier UI — всё реализовано)*
