# Z-UI

Z-UI — графическая оболочка для управления сетевой оптимизацией на базе [zapret-discord-youtube](https://github.com/Flowseal/zapret-discord-youtube). Приложение предоставляет интуитивный интерфейс в стиле Fluent Design для настройки DPI bypass, управления сервисом winws, тестирования стратегий и мониторинга обновлений.

## Возможности

- **Управление сервисом** — запуск, остановка и установка winws как службы Windows
- **Тестирование стратегий** — автоматическое тестирование HTTP/TLS/DPI с оценкой каждой конфигурации
- **DPI-анализ** — проверка обхода блокировок через curl (TCP 16-20 KB range test)
- **Мастер настройки** — пошаговый wizard при первом запуске
- **Системный трей** — работа в фоновом режиме с контекстным меню
- **Toast-уведомления** — уведомления Windows о статусе сервиса
- **Автозапуск** — опциональный запуск zapret при старте приложения
- **Обновления** — автоматическая проверка новых версий
- **Тёмная/светлая тема** — следование системной теме Windows

## Системные требования

- **ОС:** Windows 10 (19041+) / Windows 11, x64
- **Права:** Администратор (для работы с winws/WinDivert)
- **curl.exe:** Требуется для DPI-тестирования (встроен в Windows 10 1803+)

## Установка

### Готовый инсталлятор

Запустите [`build.ps1`](build.ps1) для сборки:

```powershell
.\build.ps1 -Version "1.0.0"
```

Скрипт выполнит:
1. Очистку предыдущих сборок
2. `dotnet publish` (Release, win-x64, self-contained)
3. Компиляцию Inno Setup → `installer/Z-UI-Setup.exe`

Инсталлятор включает Windows App Runtime и устанавливает всё необходимое.

### Ручная сборка

```powershell
dotnet publish .\Z-UI\Z-UI.csproj -c Release -r win-x64 --self-contained true -o .\publish
```

## Структура проекта

```
Z-UI/
├── Z-UI.slnx                    # Решение (основной проект + тесты)
├── build.ps1                    # Скрипт сборки + инсталлятор
├── setup.iss                    # Inno Setup скрипт (тема, палитры, код)
├── Z-UI/
│   ├── Program.cs               # Точка входа (Bootstrap initialization)
│   ├── App.xaml.cs              # Инициализация, трей, автозапуск
│   ├── MainWindow.xaml.cs       # Навигация, кастомный title bar
│   ├── AppSettings.cs           # Настройки (JSON, %APPDATA%)
│   ├── ZapretPaths.cs           # Пути к файлам zapret
│   ├── TrayIcon.cs              # Системный трей (P/Invoke)
│   ├── Views/                   # Страницы приложения
│   │   ├── DashboardPage        # Главная панель, статус сервиса
│   │   ├── StrategiesPage       # Список стратегий с рейтингами
│   │   ├── ServicesPage         # Тестирование (HTTP + DPI)
│   │   ├── SettingsPage         # Параметры приложения
│   │   ├── SetupWizardPage      # Мастер первоначальной настройки
│   │   ├── DiagnosticsPage      # Диагностика и логи
│   │   ├── UpdatesPage          # Обновления
│   │   ├── ServicePage          # Управление службой
│   │   └── AboutPage            # О программе
│   ├── Services/                # Бизнес-логика
│   │   ├── WinwsService.cs      # Управление процессом winws
│   │   ├── ServiceManager.cs    # Установка/удаление службы (sc.exe)
│   │   ├── ZapretTester.cs      # Система тестирования (784 строки)
│   │   ├── BatStrategyParser.cs # Парсинг .bat стратегий, ipset
│   │   ├── UpdateChecker.cs     # Проверка обновлений
│   │   ├── ToastNotifier.cs     # Windows toast-уведомления
│   │   ├── TestResultStore.cs   # Кэш результатов тестов
│   │   └── AppState.cs          # Глобальное состояние
│   └── zapret/                  # Вложенный проект zapret
│       ├── strategies/          # .bat стратегии + bin/ (winws, WinDivert)
│       └── utils/               # Утилиты
└── Z-UI.Tests/                  # Модульные тесты
```

## Технологический стек

| Компонент | Технология |
|-----------|-----------|
| **Язык** | C# 14 (nullable enabled) |
| **Фреймворк** | .NET 10, WinUI 3 |
| **UI** | Windows App SDK 2.0-experimental6 |
| **Платформа** | Windows 10 x64 (SDK 19041+) |
| **Инсталлятор** | Inno Setup 6 (кастомная тема) |
| **Тесты** | MSTest v4, FluentAssertions, Moq |
| **Конфигурация** | System.Text.Json |

## Архитектура

Приложение использует статический `AppState` как единый источник состояния. Сервисы (`WinwsService`, `ServiceManager`, `ZapretTester`) инкапсулируют бизнес-логику. Навигация реализована через `Frame` + `NavigationView` с 8 страницами.

### Ключевые потоки

1. **Запуск** → `Program.Main` → `Bootstrap.Initialize` → `App.OnLaunched` → `MainWindow`
2. **Запуск сервиса** → `WinwsService.StartAsync` → процесс winws с аргументами из `.bat` стратегии
3. **Тестирование** → `ZapretTestRunner.RunAsync` → HTTP/TLS через `HttpClient` + DPI через `curl.exe`
4. **Установка службы** → `ServiceManager.InstallAsync` → `sc.exe create zapret`
5. **Обновления** → `UpdateChecker.CheckAsync` → запрос версии с GitHub

## Разработка

### Предварительные требования

- .NET 10 SDK
- Windows App SDK 2.0-experimental6
- Inno Setup 6 (для сборки инсталлятора)
- Visual Studio 2022 с workload "WinUI 3"

### Запуск из IDE

Откройте `Z-UI.slnx` в Visual Studio и запустите отладку (F5). Требуется запуск от имени администратора для работы с сервисами.

### XAML Tools

В папке `Z-UI/` находятся утилиты для работы с XAML:
- `xaml_uid_generator.py` — генерация UID
- `xaml_tools_gui.py` — GUI-обёртка
- `xaml_resw_cleanup.py` — очистка ресурсов

## Лицензирование

Проект доступен по лицензии [MIT License](LICENSE).

## Ссылки

- [CHANGELOG.md](CHANGELOG.md) — История версий
- [CONTRIBUTING.md](CONTRIBUTING.md) — Руководство для контрибьюторов
- [Оригинальный проект](https://github.com/Flowseal/zapret-discord-youtube) — ядро системы оптимизации

## Правовая информация (Disclaimer)

**Назначение:** Z-UI — инструмент для отладки, тестирования и оптимизации параметров сетевых протоколов.

**Ответственность:** Разработчик не несёт ответственности за любой ущерб, возникший в результате использования или невозможности использования данного ПО.

**Соблюдение законов:** Использование программы должно осуществляться в строгом соответствии с Федеральным законом «Об информации, информационных технологиях и о защите информации» и другими нормативно-правовыми актами РФ.

**Контент:** Программа не предоставляет доступ к контенту сама по себе, а лишь управляет техническими параметрами передачи данных.

---
*Последнее обновление: 2026-04-01*
