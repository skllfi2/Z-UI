# Z-UI — Project Knowledge Base

**Updated:** 2026-05-03
**Stack:** C# 14, .NET 10, WinUI 3, MVVM
**IDE:** Visual Studio 2026 (required, .slnx format) | Zed (AI-assisted, opencode ACP)

> ⚡ **При старте НОВОЙ сессии** — прочитай [`.zed/CONTEXT.md`](.zed/CONTEXT.md).
> Это файл-загрузчик с полной историей работы, решениями и TODO.
> Кодовая фраза: `ZUI-BOOT-2026`

---

## OVERVIEW

DPI bypass GUI shell for [zapret-discord-youtube](https://github.com/Flowseal/zapret-discord-youtube).
Architecture: **UI (WinUI 3 user app)** ↔ **IPC (Named Pipes)** ↔ **Worker (Windows Service, SYSTEM)** ↔ **WinDivert (kernel driver)**

---

## STRUCTURE

```
Z-UI/
├── Z-UI/                  # Main WinUI 3 project
│   ├── Services/          # 19+ services (DI-registered)
│ ├── Views/ # 4 XAML pages (Dashboard, Generator, Network, Diagnostics, Settings) + code-behind
│   ├── ViewModels/        # 7 VMs (Dashboard, Generator, Dns, Strategy, Proxifier, Settings, Diagnostics)
│   ├── Windows/           # MainWindow.xaml.cs (NOT at project root!)
│   ├── Models/            # Data models
│   ├── Controls/          # Custom controls
│   ├── Converters/        # Value converters
│   └── zapret/            # DPI bypass binaries + strategies
├── ZUI.Ipc/               # Shared IPC protocol (17 request types, 8 response types)
├── ZUI.Worker/            # Windows Service (Native AOT, SYSTEM) — Orchestrator + WinDivert
├── ZUI.SDK/               # Integration SDK
└── ZUI.Tests/             # Unit tests (247 tests, all passing)
```

---

## WHERE TO LOOK

| Task | Location | Notes |
|------|----------|-------|
| Add new page | `Z-UI/Views/` + `ViewModels/` | Register in DI (App.xaml.cs) |
| Add new service | `Z-UI/Services/` | Interface `IXxxService` + implementation |
| Modify DPI strategy | `Z-UI/zapret/strategies/*.bat` | UTF-8 BOM encoding required |
| Change DNS settings | `Z-UI/Services/DnsService.cs` | Windows DNS API |
| UI theming | `Z-UI/App.xaml` | Theme-aware brushes (SuccessBrush, etc.) |
| IPC protocol | `ZUI.Ipc/` | IpcRequest.cs, IpcResponse.cs |
| Worker orchestration | `ZUI.Worker/Orchestrator.cs` | WinDivert lifecycle, packet stats |
| MainWindow | `Z-UI/Windows/MainWindow.xaml.cs` | NOT at project root |

---

## KEY SERVICES

| Service | Purpose |
|---------|---------|
| `AdaptiveEngine` | DPI bypass orchestration via IPC to Worker |
| `IpcClientService` | Named Pipe client to Worker |
| `WorkerServiceManager` | Windows Service install/start/stop |
| `StrategyManager` | Strategy loading, ISP detection |
| `StrategyGeneratorService` | Generate custom strategies (1349 lines) |
| `DiagnosticsService` | System health checks (514 lines) |
| `DnsProxyHostedService` | Split DNS with blocked domains |
| `ProtectionService` | Coordinating DPI bypass + DNS |
| `AppSettingsService` | Centralized settings sync |
| `WindowSubclass` | Win32 SetWindowSubclass for WM_HOTKEY/WM_COMMAND/tray |
| `HotkeyService` | Global hotkey registration + WM_HOTKEY handling |
| `TrayIcon` | System tray icon with CallbackMessageId |
| `ToastNotifier` | WinRT toast with AUMID registration (HKCU + COM shortcut) |
| `SoundService` | MediaPlayer for .wav, ElementSoundPlayer fallback |
| `UpdateChecker` | GitHub API release check |
| `TestResultStore` | CachedStrategyResult JSON cache |
| `BatStrategyParser` | Parse .bat strategy files, ApplyIpsetFilter |
| `LocalizationService` | i18n with ru/en dictionaries (~130 keys each) |

---

## LARGE FILES (Complexity Hotspots)

| File | Lines | Notes |
|------|-------|-------|
| `StrategyGeneratorService.cs` | 1349 | ISP profiles, test runner |
| `GeneratorViewModel.cs` | 686 | Tab 1 + Tab 2 coordination |
| `SettingsPage.xaml.cs` | 639 | All settings handlers |
| `DashboardViewModel.cs` | 631 | Main dashboard logic |
| `StrategyManager.cs` | 626 | Strategy CRUD + ISP detection |
| `DiagnosticsService.cs` | 514 | Network/admin/WinDivert checks |
| `ToastNotifier.cs` | ~306 | AUMID registry + COM shortcut + WinRT toast |

---

## CONVENTIONS

### C#
- `[ObservableProperty]` → use **`private <type> _fieldName`** style — **NEVER partial properties** (causes WinRT.Runtime.dll crash)
- `partial class` for code-behind
- `_camelCase` for private fields, `PascalCase` for public
- All IO operations → `async/await`

### XAML
- `x:Name` with type suffix: `StartButton`, `StatusTextBlock`
- Theme-aware colors: `{ThemeResource SuccessBrush}`
- Converters in `Converters/`

### DI (App.xaml.cs)
```csharp
services.AddSingleton<IStrategyManager, StrategyManager>();
services.AddSingleton<IIpcClientService, IpcClientService>();
services.AddSingleton<IWindowSubclass, WindowSubclass>();
// etc.
```

---

## CRITICAL GOTCHAS (MUST READ)

### WinUI 3 / .NET 10 API Quirks
- **`App.MainWindow`** — instance property (NOT static) → use `(App.Current as App)?.MainWindow`
- **`DispatcherTimer`** → namespace `Microsoft.UI.Xaml`; **`DispatcherQueueTimer`** → `Microsoft.UI.Dispatching` (preferred)
- **`UseWinUI=true`** does NOT add implicit usings for `Microsoft.UI.Xaml` or `Microsoft.UI.Dispatching`
- **`ElementSoundValue`** enum NOT available in .NET 10 WinUI 3 projections
- **`SecurityException`** → `System.Security`; catch derived types BEFORE base types
- **`System.Net.HostResolutionException`** does NOT exist in .NET → DNS errors throw `SocketException`
- **WinUI 3 projects do NOT reference System.Windows.Forms** → use P/Invoke `GetCursorPos` instead of `Cursor.Position`
- **`IntPtr` cannot be `const`** → use `static readonly IntPtr`

### Architecture
- **`AppSettings`** (static, ZUI namespace) ≠ **`IAppSettingsService`** (DI, ZUI.Services namespace) — different classes
- **Worker** runs as SYSTEM, UI runs as user → communication only via Named Pipe `ZUI_IPC`
- **IPC Protocol**: 4-byte LE length prefix + JSON body, 3000ms timeout, 2000ms reconnect
- **Orchestrator delta stats**: `_lastStatsTime`, `_lastTotalPackets`, `_lastPacketsPerSecond`, `_lastBytesPerSecond`

### WindowSubclass (Win32 interop)
- `SetWindowSubclass` from comctl32.dll (NOT HwndSource.AddHook)
- `SUBCLASSPROC` delegate MUST be stored as instance field (GC prevention)
- Routes: WM_HOTKEY → HotkeyService, WM_COMMAND → tray, CallbackMessageId → TrayIcon

### ToastNotifier (COM interop)
- AUMID: `Z-UI` → registry `HKCU\Software\Classes\AppUserModelId\Z-UI`
- COM: IShellLinkW → IPersistFile → IPropertyStore → SetStringValue(PKEY_AppUserModel_Id)
- PKEY_AppUserModel_Id = `{9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3}`,5

### SoundService
- Expects .wav files at `Path.Combine(AppContext.BaseDirectory, "Assets", "Sounds", "{name}.wav")`
- Names: success, error, click, notification, toggle, warning
- `PlaySystemSound()` is no-op stub (ElementSoundValue unavailable)

---

## ANTI-PATTERNS (THIS PROJECT)

- **NEVER** use `[ObservableProperty]` with partial properties → causes WinRT.Runtime.dll crash
- **NEVER** use `HwndSource.AddHook` → use `SetWindowSubclass` instead
- **NEVER** use `const IntPtr` → use `static readonly IntPtr`
- **NEVER** catch `HostResolutionException` → it doesn't exist; use `SocketException`
- **NEVER** reference `System.Windows.Forms` → use P/Invoke instead
- **NEVER** suppress type errors with `as any`, `@ts-ignore`
- **NEVER** run `dotnet build` for diagnostics — use Visual Studio 2026
- **NEVER** use `DispatcherTimer` for new code → use `DispatcherQueueTimer`
- **NEVER** call PowerShell/Process in VM constructor → use `Task.Run()` + async `Refresh()`
- **NEVER** call `RelayCommand.Execute()` on async commands from `OnNavigatedTo` → use `.ExecuteAsync()`
- **NEVER** skip `_isInitialized` guard in `OnNavigatedTo` → first visit = Refresh, repeat = cached
- **NEVER** use hardcoded colors for card icons → use `{ThemeResource}` brushes

---

## BUILD & RUN

### Requirements
- **Visual Studio 2026** (`.slnx` format supported, but we use `.sln` for Roslyn compatibility)
- Windows 10 (19041+) / Windows 11, x64
- Administrator rights (WinDivert)
- .NET 10 SDK

### Commands
```powershell
# Build (use VS, not CLI)
dotnet build -c Release # May fail with XamlCompiler error

# Run tests (247 tests)
dotnet test

# Create installer
.\build.ps1 -Installer
```

### Known Issues
- `dotnet build` may fail with XamlCompiler — use Visual Studio 2026
- XamlCompiler exit code 1 / file-lock on output.json — known .NET 10 issue (ignored in Directory.Build.props)
- LSP may show phantom CS0234/CS0246 for WinUI 3 SDK types (Window, LaunchActivatedEventArgs, etc.) — these compile fine in VS

---

## INFRASTRUCTURE

### LSP
- **Roslyn Language Server** (`roslyn-language-server 5.5.0`) configured in opencode with `--autoLoadProjects`
- Solution: `Z-UI.slnx` (9 projects, filled) — VS 2026 opens this
- Fallback: `ZUI-all.sln` (classic format, for tools that don't support `.slnx`)
- `.vscode/settings.json` → `dotnet.defaultSolution: "Z-UI.slnx"` — Roslyn reads this on init
- **LSP limitation**: Roslyn creates ad-hoc "Canonical" projects instead of loading `.slnx`; go-to-definition only works within same file
- Cross-project navigation → use **Serena MCP** or **codebase-memory** instead
- LSP does NOT resolve WinUI 3 SDK types → phantom errors, ignore them

### MCP
- **Serena** (enabled) — LSP-backed semantic navigation, project `Z-UI` indexed (136 C# files)
- **codebase-memory** (enabled) — knowledge graph: 4088 nodes, 8563 edges, project `F-Development-Z-UI`

### API Keys (NIM)
- 3 NVIDIA NIM API keys configured (nvidia, nvidia-2, nvidia-3)
- Agent→Key mapping: KEY_1=main+consultants, KEY_2=search+review, KEY_3=code-executors

### Zed Editor (primary AI-assisted IDE)
- **Config**: `%APPDATA%\Zed\settings.json` — Roslyn LSP + opencode ACP server + MCP context servers
- **Project settings**: `.zed/settings.json` — C# formatter, file exclusions, LSP override
- **Roslyn LSP**: `lsp.csharp` block → `roslyn-language-server.cmd --stdio --autoLoadProjects`
- **opencode ACP**: `agent_servers.OpenCode` → `opencode acp` with Sisyphus mode
- **MCP servers**: `context_servers` block → Serena + codebase-memory (stdio transport)
- **Keymap**: VSCode-compatible (`base_keymap: "VSCode"`)
- **Go-to-definition**: Roslyn LSP via Zed's native LSP client — should resolve cross-project types

### Windsurf (alternative, not used)
- **Install**: `winget install Codeium.Windsurf` or download from https://windsurf.com
- **MCP config**: `~/.codeium/windsurf/mcp_config.json` (Serena + codebase-memory pre-configured)
- **C# extension**: Install `C# Dev Kit` (ms-dotnettools.csharp) — provides full Roslyn go-to-definition
- **Custom LLM (NIM)**: Windsurf native BYOK = Anthropic only; for NIM OpenAI-compatible → install **Roo Code** extension
  - Provider: `OpenAI Compatible`
  - Base URL: `https://integrate.api.nvidia.com/v1`
  - API Key: from `auth.json` (nvidia key)
  - Model: `z-ai/glm-5.1`
- **Go-to-definition**: Windsurf = VS Code fork → Roslyn C# extension works fully (cross-project, WinUI 3 SDK types)

---

## LESSONS LEARNED

> 📖 **Deep-dive каталог:** [`LESSONS_LEARNED.md`](LESSONS_LEARNED.md) — детали, контекст, stack traces.
> **Краткие правила ниже — auto-loaded при старте сессии.**

| # | Правило | Контекст |
|---|---------|----------|
| 1 | Читай файл через `read` перед `edit` — копируй `oldString` побуквенно. Не угадывай whitespace | #1 |
| 2 | Удаляй потребителей (commands/handlers) ПЕРЕД объявлениями (events). Или один bulk edit | #2 |
| 3 | `[ObservableProperty]` = ONLY `private _fieldName`. NEVER partial properties → WinRT crash | #3 |
| 4 | `JsonElement` в `object?` полях → unwrap через `ValueKind` switch. Никогда не cast напрямую | #4 |
| 5 | `load_skills` — ВСЕГДА минимум 1 релевантный skill при делегировании | #5 |
| 6 | Конструктор VM = ТОЛЬКО DI assignment + lightweight defaults. PowerShell/Process → `Task.Run()` + async `Refresh()` | #15 |
| 7 | `OnNavigatedTo` → ВСЕГДА `_isInitialized` guard. Первый визит = Refresh, повторный = cached state | #16 |
| 8 | `RelayCommand` async → НИКОГДА не вызывай `.Execute()` из OnNavigatedTo. Только `.ExecuteAsync()` | #17 |
| 9 | VM с periodic refresh (timer) → OnNavigatedTo не нужен повторный InitializeAsync | #18 |
| 10 | Dashboard карточки = пользовательские сценарии, не технические функции. Дублирование = потеря | #19 |
| 11 | Иконки в карточках → `ThemeResource` brushes (`LayerFillColorDefaultBrush`). НЕ hardcoded цвета | #20 |
| 12 | IPC reconnect → exponential backoff + log throttle. Timeout → LogDebug, не LogError | #11 |
| 13 | COM interop → ВСЕГДА проверяй HRESULT. Catch `InvalidCastException` ПЕРЕД `COMException` | #12,#14 |
| 14 | VM → наследуй от ViewModelBase + `SetDispatcherQueue()`. НЕ `GetForCurrentThread()` в конструкторе | #13 |
| 15 | WinRT API вызовы → ВСЕГДА try/catch, даже «очевидные» property setter'ы | #14 |
| 16 | Fire-and-forget `_ = Task` → НИКОГДА без try/catch внутри. Async метод вместо `ContinueWith` | #14 |
| 17 | Конструктор/публичный API changed → тесты updated СРАЗУ | #9 |
| 18 | XAML элемент удалён → grep code-behind на handlers с этим `x:Name` | #10 |
| 19 | Z-UI.exe запущен → НЕ `dotnet build/test` на slnx. Только `ZUI.Tests.csproj` | #7 |
| 20 | `dotnet` CLI → ВСЕГДА указывай файл проекта/решения | #8 |

## NOTES

- All 7 ViewModels migrated to `LocalizationService.Get()` for i18n
- Profiles (PoE2, Discord, YouTube) → recommend unified domain list
- Theme-aware icons use `{ThemeResource}` brushes from App.xaml
- Floating back button in MainWindow.xaml (bottom-left, CornerRadius="8")
- GitHub repo for updates: `Flowseal/zapret-discord-youtube`
- Cache path: `%LocalAppData%\Z-UI\cache\test-results.json`
- NuGet: CommunityToolkit.Mvvm 8.4.2, WindowsAppSDK 1.8.260416003, Microsoft.Extensions.Hosting 10.0.7
- Target: net10.0-windows10.0.19041.0
- **opencode desktop** — NOT a VS Code fork; standalone product. Roslyn go-to-definition broken (Canonical ad-hoc project mode) → use Zed instead
