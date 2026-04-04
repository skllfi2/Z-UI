using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.UI;
using ZUI.Services;

namespace ZUI.ViewModels;

public partial class StrategiesViewModel : ViewModelBase, IDisposable
{
    private const int MaxLogLines = 1000;
    private CancellationTokenSource? _testCts;

    [ObservableProperty]
    private ObservableCollection<StrategyItem> _strategies = [];

    [ObservableProperty]
    private StrategyItem? _selectedStrategy;

    [ObservableProperty]
    private bool _isTesting;

    [ObservableProperty]
    private string _testProgress = "";

    [ObservableProperty]
    private double _testProgressValue;

    [ObservableProperty]
    private ObservableCollection<string> _logLines = [];

    [ObservableProperty]
    private string _searchText = "";

    [ObservableProperty]
    private int _strategyCount;

    [ObservableProperty]
    private bool _hasSelection;

    [ObservableProperty]
    private string _selectedStrategyName = "";

    [ObservableProperty]
    private string _selectedStrategyType = "";

 [ObservableProperty]
 private string _selectedStrategyUsage = "";

 [ObservableProperty]
 private string _selectedStrategyDescription = "";

 [ObservableProperty]
 private string _selectedStrategyRecommendedFor = "";

 [ObservableProperty]
 private string _selectedStrategyRating = "";

 [ObservableProperty]
 private string _selectedStrategyIconGlyph = "";

 [ObservableProperty]
 private string _selectedStrategyIconColor = "#FF0078D4";

 [ObservableProperty]
 private bool _selectedStrategyIsActive;

    public StrategiesViewModel()
    {
        LoadStrategies();
        TestResultStore.ResultsUpdated += LoadStrategies;
    }

private void LoadStrategies()
{
    Strategies.Clear();
    var dir = ZapretPaths.StrategiesDir;
    if (!Directory.Exists(dir)) return;

    var excludedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "service", "install", "uninstall", "run-as-service", "run-as-admin"
    };

    var files = Directory.GetFiles(dir, "*.bat")
        .Select(f => new FileInfo(f))
        .Where(f => !excludedFiles.Contains(Path.GetFileNameWithoutExtension(f.Name)))
        .OrderBy(f => f.Name);

    foreach (var file in files)
    {
        var name = Path.GetFileNameWithoutExtension(file.Name);
        var snapshot = TestResultStore.Get(name);
        var item = new StrategyItem
        {
            ConfigName = name,
            DisplayName = name,
            FilePath = file.FullName
        };
        item.ApplySnapshot(snapshot);
        Strategies.Add(item);
    }

    StrategyCount = Strategies.Count;

        if (Strategies.Count > 0)
        {
            var current = AppSettings.CurrentStrategy;
            SelectedStrategy = Strategies.FirstOrDefault(s => s.ConfigName == current) ?? Strategies[0];
        }
    }

 partial void OnSelectedStrategyChanged(StrategyItem? value)
 {
 HasSelection = value != null;
 if (value == null) return;

 SelectedStrategyName = value.DisplayName;
 SelectedStrategyIsActive = value.ConfigName == AppSettings.CurrentStrategy;
 SelectedStrategyRating = value.RatingLabel;
 SelectedStrategyType = GetStrategyType(value.ConfigName);
 SelectedStrategyUsage = GetStrategyUsage(value.ConfigName);
 SelectedStrategyDescription = value.Description;
 SelectedStrategyRecommendedFor = value.RecommendedFor;
 SelectedStrategyIconGlyph = value.IconGlyph;
 SelectedStrategyIconColor = value.IconColor;
 }

    private static string GetStrategyType(string name) => name.ToLowerInvariant() switch
    {
        var n when n.Contains("general") => "Универсальная",
        var n when n.Contains("discord") => "Для Discord",
        var n when n.Contains("youtube") => "Для YouTube",
        var n when n.Contains("russia") => "Для российских провайдеров",
        var n when n.Contains("gaming") => "Для игр",
        _ => "Пользовательская"
    };

    private static string GetStrategyUsage(string name) => name.ToLowerInvariant() switch
    {
        var n when n.Contains("general") => "Подходит для большинства сайтов и сервисов",
        var n when n.Contains("discord") => "Оптимизировано для Discord и голосовых чатов",
        var n when n.Contains("youtube") => "Для YouTube, Twitch и видеостриминга",
        var n when n.Contains("russia") => "Для российских провайдеров с DPI",
        var n when n.Contains("gaming") => "Для игровых серверов и сервисов",
        _ => "Пользовательская конфигурация"
    };

    private void AddLog(string line)
    {
        LogLines.Add(line);
        while (LogLines.Count > MaxLogLines)
            LogLines.RemoveAt(0);
    }

 [RelayCommand]
 private void ApplyStrategy()
 {
 if (SelectedStrategy == null) return;
 AppSettings.CurrentStrategy = SelectedStrategy.ConfigName;
 AppSettings.Save();
 SelectedStrategyIsActive = true;
 ActionLogger.LogStrategyApply(SelectedStrategy.ConfigName);

 foreach (var s in Strategies)
 s.ApplySnapshot(TestResultStore.Get(s.ConfigName));
 }

 [RelayCommand]
 private void ClearRatings()
 {
 TestResultStore.ClearCache();
 LoadStrategies();
 }

 [RelayCommand]
 private void SelectBestStrategy()
 {
 var best = Strategies
 .Where(s => s.Rating == StrategyRating.Recommended)
 .OrderByDescending(s => s.Rating)
 .FirstOrDefault();

 if (best == null)
 {
 best = Strategies
 .Where(s => s.Rating == StrategyRating.Acceptable)
 .FirstOrDefault();
 }

 if (best != null)
 {
 SelectedStrategy = best;
 ApplyStrategy();
 ActionLogger.Log("AUTO_SELECT", $"Автоматически выбрана лучшая стратегия: {best.ConfigName}");
 }
 }

    [RelayCommand]
    private async Task RunTestsAsync()
    {
        if (IsTesting) return;

        IsTesting = true;
        TestProgress = "Запуск тестирования...";
        TestProgressValue = 0;
        LogLines.Clear();
        AddLog("Начало тестирования стратегий");

        _testCts = new CancellationTokenSource();

        try
        {
            var strategies = Strategies.Select(s => s.ConfigName).ToArray();
            var total = strategies.Length;

            for (int i = 0; i < total; i++)
            {
                if (_testCts.IsCancellationRequested) break;

                var strategy = strategies[i];
                TestProgress = $"Тестирование {i + 1}/{total}: {strategy}";
                TestProgressValue = (double)(i + 1) / total * 100;
                AddLog($"[{i + 1}/{total}] {strategy}");

                await Task.Delay(100, _testCts.Token);

                var item = Strategies.FirstOrDefault(s => s.ConfigName == strategy);
                if (item != null)
                {
                    item.ApplySnapshot(TestResultStore.Get(strategy));
                    AddLog($" → Стратегия '{strategy}' обновлена");
                }
            }

            AddLog("Тестирование завершено");
            TestProgress = "Готово";
        }
        catch (OperationCanceledException)
        {
            AddLog("Тестирование отменено");
            TestProgress = "Отменено";
        }
        catch (Exception ex)
        {
            AddLog($"Ошибка: {ex.Message}");
            TestProgress = "Ошибка";
        }
        finally
        {
            IsTesting = false;
            _testCts?.Dispose();
            _testCts = null;
        }
    }

    [RelayCommand]
    private void CancelTests()
    {
        _testCts?.Cancel();
    }

    public void Dispose()
    {
        TestResultStore.ResultsUpdated -= LoadStrategies;
        _testCts?.Cancel();
        _testCts?.Dispose();
    }
}

 public sealed partial class StrategyItem : ObservableObject
 {
 private StrategyTestSnapshot? _snap;

 public string ConfigName { get; init; } = "";
 public string DisplayName { get; init; } = "";
 public string FilePath { get; init; } = "";

 public string Description => GetDescription(ConfigName);

 public string IconGlyph => GetIconGlyph(ConfigName);

 public string IconColor => GetIconColor(ConfigName);

 private static string GetIconGlyph(string name) => name.ToLowerInvariant() switch
 {
 var n when n.Contains("discord") => "\xE8CD",
 var n when n.Contains("youtube") => "\xE8F2",
 var n when n.Contains("gaming") => "\xE7FC",
 var n when n.Contains("russia") => "\xE774",
 var n when n.Contains("quic") => "\xE774",
 var n when n.Contains("fake") => "\xE72E",
 var n when n.Contains("split") => "\xE7C3",
 _ => "\xE8CB"
 };

 private static string GetIconColor(string name) => name.ToLowerInvariant() switch
 {
 var n when n.Contains("discord") => "#FF5865F2",
 var n when n.Contains("youtube") => "#FFFF0000",
 var n when n.Contains("gaming") => "#FF1DB954",
 var n when n.Contains("russia") => "#FF0078D4",
 _ => "#FF0078D4"
 };

 private static string GetDescription(string name) => name.ToLowerInvariant() switch
 {
 var n when n.Contains("general") => "Универсальная стратегия для обхода DPI на большинстве сайтов. Рекомендуется для общего использования.",
 var n when n.Contains("discord") => "Оптимизирована для Discord. Обход блокировки голосовых серверов и API.",
 var n when n.Contains("youtube") => "Для YouTube и видеостриминга. Обход блокировки видео и комментариев.",
 var n when n.Contains("russia") => "Специализирована для российских провайдеров с активным DPI.",
 var n when n.Contains("gaming") => "Для игровых сервисов. Оптимизирована для низкого пинга.",
 var n when n.Contains("quic") => "Использует QUIC протокол. Экспериментальная стратегия.",
 var n when n.Contains("fake") => "Использует fake-пакеты для обхода DPI.",
 var n when n.Contains("split") => "Использует split-технику для разделения пакетов.",
 _ => "Пользовательская стратегия. Описание недоступно."
 };

 public string RecommendedFor => GetRecommendedFor(ConfigName);

 private static string GetRecommendedFor(string name) => name.ToLowerInvariant() switch
 {
 var n when n.Contains("general") => "Все сайты, социальные сети, мессенджеры",
 var n when n.Contains("discord") => "Discord, голосовые чаты, сервера",
 var n when n.Contains("youtube") => "YouTube, Twitch, видеоплатформы",
 var n when n.Contains("russia") => "Российские провайдеры (Ростелеком, МТС, Билайн)",
 var n when n.Contains("gaming") => "Steam, Epic Games, игровые сервера",
 _ => "Зависит от конфигурации"
 };

 public StrategyRating Rating => _snap?.Rating ?? StrategyRating.Unknown;

    public string RatingShortLabel => Rating switch
    {
        StrategyRating.Recommended => "OK",
        StrategyRating.Acceptable => "~OK",
        StrategyRating.NotRecommended => "ERR",
        _ => ""
    };

    public string RatingEmoji => Rating switch
    {
        StrategyRating.Recommended => "✓",
        StrategyRating.Acceptable => "~",
        StrategyRating.NotRecommended => "✗",
        _ => ""
    };

public string RatingLabel => _snap?.RatingLabel ?? "Требуется тестирование";

public string RatingTooltip => Rating switch
{
StrategyRating.Recommended => "Рекомендуется. Хорошо работает на большинстве провайдеров",
StrategyRating.Acceptable => "Приемлемо. Может работать с ограничениями",
StrategyRating.NotRecommended => "Не рекомендуется. Много проблем",
_ => "Рейтинг неизвестен. Запустите тестирование"
};

public Visibility RatingVisible =>
        Rating == StrategyRating.Unknown ? Visibility.Collapsed : Visibility.Visible;

    private static readonly Color _green = Color.FromArgb(255, 76, 195, 125);
    private static readonly Color _yellow = Color.FromArgb(255, 220, 165, 30);
    private static readonly Color _red = Color.FromArgb(255, 220, 80, 70);

    private Color AccentColor => Rating switch
    {
        StrategyRating.Recommended => _green,
        StrategyRating.Acceptable => _yellow,
        StrategyRating.NotRecommended => _red,
        _ => Color.FromArgb(255, 130, 130, 140)
    };

    public SolidColorBrush RatingBackground =>
        new(Color.FromArgb(35, AccentColor.R, AccentColor.G, AccentColor.B));

    public SolidColorBrush RatingForeground =>
        new(AccentColor);

    public void ApplySnapshot(StrategyTestSnapshot? snap)
    {
        _snap = snap;
        OnPropertyChanged(nameof(Rating));
        OnPropertyChanged(nameof(RatingShortLabel));
        OnPropertyChanged(nameof(RatingEmoji));
        OnPropertyChanged(nameof(RatingLabel));
        OnPropertyChanged(nameof(RatingVisible));
        OnPropertyChanged(nameof(RatingBackground));
        OnPropertyChanged(nameof(RatingForeground));
    }
}
