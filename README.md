# Z-UI

> **Graphical shell for DPI bypass based on zapret**

[![Windows](https://img.shields.io/badge/Platform-Windows%2010%2F11-blue?logo=windows)](https://www.microsoft.com/windows)
[![.NET](https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet)](https://dotnet.microsoft.com)
[![WinUI](https://img.shields.io/badge/WinUI-3.0-0078D4?logo=microsoft)](https://docs.microsoft.com/windows/apps/winui)
[![License](https://img.shields.io/badge/License-MIT-yellow)](LICENSE)
[![GitHub Release](https://img.shields.io/github/v/release/your-repo/Z-UI?include_prereleases)](https://github.com/your-repo/Z-UI/releases)

**English** | [Русский](README.ru.md)

## Description

Z-UI is a modern graphical shell for managing DPI bypass based on [zapret-discord-youtube](https://github.com/Flowseal/zapret-discord-youtube). Intuitive Fluent Design interface for Windows 10/11.

## Features

- **Strategy management** — select and test DPI bypass strategies
- **Automatic selection** — testing with best strategy selection
- **Rating system** — track strategy success rate
- **Localization** — Russian and English languages
- **Themes** — light, dark, follow system
- **Autostart** — launch on Windows startup
- **Tray minimize** — background operation
- **List updates** — hosts and ipset from GitHub

## Screenshots

| Main | Strategies | Settings |
|------|------------|----------|
| Dashboard | Strategy selection | Parameters |

## Installation

### Download Installer

⬇️ [Download latest release](https://github.com/your-repo/Z-UI/releases/latest)

### System Requirements

- **OS:** Windows 10 (19041+) / Windows 11, x64
- **Rights:** Administrator (for WinDivert)

### Build from Source

```powershell
# Clone repository
git clone https://github.com/your-repo/Z-UI.git
cd Z-UI

# Build
dotnet build -c Release

# Or create installer
.\build.ps1
```

## Usage

1. Launch Z-UI
2. Select a strategy on the "Strategies" tab
3. Click "Start" on the main page
4. The app will minimize to tray and run in background

## Development

### Technologies

| Component | Technology |
|-----------|------------|
| Language | C# 14 |
| Framework | .NET 10, WinUI 3 |
| UI | Windows App SDK |
| Architecture | MVVM |
| DI | Microsoft.Extensions.DependencyInjection |

### Project Structure

```
Z-UI/
├── Z-UI/                  # Main project
│   ├── Views/             # XAML pages
│   ├── ViewModels/        # View models
│   ├── Services/          # Business logic
│   └── Windows/           # Main window
├── installer/             # Installer
├── build.ps1              # Build script
└── setup.iss              # Inno Setup
```

## License

[MIT License](LICENSE) | [Disclaimer](DISCLAIMER.md)

## Acknowledgments

- [Flowseal/zapret-discord-youtube](https://github.com/Flowseal/zapret-discord-youtube) — DPI bypass core
- [CommunityToolkit.Mvvm](https://github.com/CommunityToolkit/dotnet) — MVVM toolkit

---

**⭐ Star this repo if it's useful!**
