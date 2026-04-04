# Z-UI

[English](#english) | [Русский](#русский)

---

<a name="русский"></a>
## Русский

Z-UI — графическая оболочка для управления обходом DPI на базе [zapret-discord-youtube](https://github.com/Flowseal/zapret-discord-youtube). Современный интерфейс в стиле Fluent Design для Windows 10/11.

### Возможности

- **Управление стратегиями** — выбор и тестирование стратегий обхода DPI
- **Автоматический выбор** — тестирование с выбором лучшей стратегии
- **Рейтинговая система** — отслеживание успешности стратегий
- **Локализация** — русский и английский языки
- **Темы** — светлая, тёмная, следование системе
- **Автозапуск** — запуск при входе в Windows
- **Админ-права** — опциональный запуск от имени администратора
- **Сворачивание в трей** — работа в фоновом режиме
- **Обновление списков** — hosts и ipset с GitHub
- **Диагностика** — проверка компонентов zapret
- **Экспорт/импорт настроек**

### Системные требования

- **ОС:** Windows 10 (19041+) / Windows 11, x64
- **Права:** Администратор (для работы с WinDivert)

### Установка

Скачайте инсталлятор из [Releases](https://github.com/your-repo/Z-UI/releases).

### Сборка из исходников

```powershell
# Требования: .NET 10 SDK, Visual Studio 2022 с WinUI 3
dotnet build -c Release
```

### Скриншоты

| Главная | Стратегии | Настройки |
|---------|-----------|-----------|
| Dashboard | Strategies | Settings |

### Технологии

- C# 14, .NET 10
- WinUI 3, Windows App SDK
- CommunityToolkit.Mvvm
- MVVM архитектура

### Лицензия

[MIT License](LICENSE)

---

<a name="english"></a>
## English

Z-UI is a graphical shell for managing DPI bypass based on [zapret-discord-youtube](https://github.com/Flowseal/zapret-discord-youtube). Modern Fluent Design interface for Windows 10/11.

### Features

- **Strategy management** — select and test DPI bypass strategies
- **Automatic selection** — testing with best strategy selection
- **Rating system** — track strategy success rate
- **Localization** — Russian and English languages
- **Themes** — light, dark, follow system
- **Autostart** — launch on Windows startup
- **Admin rights** — optional run as administrator
- **Tray minimize** — background operation
- **List updates** — hosts and ipset from GitHub
- **Diagnostics** — zapret components check
- **Settings export/import**

### System Requirements

- **OS:** Windows 10 (19041+) / Windows 11, x64
- **Rights:** Administrator (for WinDivert)

### Installation

Download installer from [Releases](https://github.com/your-repo/Z-UI/releases).

### Build from Source

```powershell
# Requirements: .NET 10 SDK, Visual Studio 2022 with WinUI 3
dotnet build -c Release
```

### Technologies

- C# 14, .NET 10
- WinUI 3, Windows App SDK
- CommunityToolkit.Mvvm
- MVVM architecture

### License

[MIT License](LICENSE)

---

*Last updated: 2026-04-04*
