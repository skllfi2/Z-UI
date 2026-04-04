# Z-UI

> **Графическая оболочка для обхода DPI на базе zapret**

[![Windows](https://img.shields.io/badge/Platform-Windows%2010%2F11-blue?logo=windows)](https://www.microsoft.com/windows)
[![.NET](https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet)](https://dotnet.microsoft.com)
[![WinUI](https://img.shields.io/badge/WinUI-3.0-0078D4?logo=microsoft)](https://docs.microsoft.com/windows/apps/winui)
[![License](https://img.shields.io/badge/License-MIT-yellow)](LICENSE)

[English](README.md) | **Русский**

## Описание

Z-UI — современная графическая оболочка для управления обходом DPI на базе [zapret-discord-youtube](https://github.com/Flowseal/zapret-discord-youtube). Интуитивный интерфейс в стиле Fluent Design для Windows 10/11.

## Возможности

- **Управление стратегиями** — выбор и тестирование стратегий обхода DPI
- **Автоматический выбор** — тестирование с выбором лучшей стратегии
- **Рейтинговая система** — отслеживание успешности стратегий
- **Локализация** — русский и английский языки
- **Темы** — светлая, тёмная, следование системе
- **Автозапуск** — запуск при входе в Windows
- **Сворачивание в трей** — работа в фоновом режиме
- **Обновление списков** — hosts и ipset с GitHub

## Скриншоты

| Главная | Стратегии | Настройки |
|---------|-----------|-----------|
| Панель управления | Выбор стратегии | Параметры |

## Установка

### Скачать инсталлятор

⬇️ [Скачать последнюю версию](https://github.com/your-repo/Z-UI/releases/latest)

### Системные требования

- **ОС:** Windows 10 (19041+) / Windows 11, x64
- **Права:** Администратор (для работы с WinDivert)

### Установка из исходников

```powershell
# Клонировать репозиторий
git clone https://github.com/your-repo/Z-UI.git
cd Z-UI

# Сборка
dotnet build -c Release

# Или создать инсталлятор
.\build.ps1
```

## Использование

1. Запустите Z-UI
2. Выберите стратегию на вкладке "Стратегии"
3. Нажмите "Запустить" на главной странице
4. Приложение свернётся в трей и будет работать в фоне

## Разработка

### Технологии

| Компонент | Технология |
|-----------|------------|
| Язык | C# 14 |
| Фреймворк | .NET 10, WinUI 3 |
| UI | Windows App SDK |
| Архитектура | MVVM |
| DI | Microsoft.Extensions.DependencyInjection |

### Структура проекта

```
Z-UI/
├── Z-UI/                  # Основной проект
│   ├── Views/             # XAML страницы
│   ├── ViewModels/        # View модели
│   ├── Services/          # Бизнес-логика
│   └── Windows/           # Главное окно
├── installer/             # Инсталлятор
├── build.ps1              # Скрипт сборки
└── setup.iss              # Inno Setup
```

## Лицензия

[MIT License](LICENSE)

## Благодарности

- [Flowseal/zapret-discord-youtube](https://github.com/Flowseal/zapret-discord-youtube) — ядро обхода DPI
- [CommunityToolkit.Mvvm](https://github.com/CommunityToolkit/dotnet) — MVVM инструментарий

---

**⭐ Поставьте звезду, если проект полезен!**
