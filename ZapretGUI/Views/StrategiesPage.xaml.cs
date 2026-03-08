using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using ZapretGUI.Services;

namespace ZapretGUI.Views
{
    public sealed partial class StrategiesPage : Page
    {
        private WinwsService _service => AppState.WinwsService;
        private string _selectedBatPath = "";
        private Dictionary<string, string> _strategyPaths = new();

        // описания стратегий
        private static readonly Dictionary<string, (string Type, string Usage)> StrategyInfo = new()
        {
            ["general"] = ("Основная", "Discord, YouTube, Google. Рекомендуется для большинства пользователей."),
            ["general (ALT)"] = ("Альтернативная 1", "Используй если general не работает."),
            ["general (ALT2)"] = ("Альтернативная 2", "Используй если ALT не помог."),
            ["general (ALT3)"] = ("Альтернативная 3", "Ещё один вариант обхода DPI."),
            ["general (ALT4)"] = ("Альтернативная 4", "Для провайдеров с глубокой инспекцией."),
            ["general (ALT5)"] = ("Альтернативная 5", "Лёгкая версия без лишних фильтров."),
            ["general (ALT6)"] = ("Альтернативная 6", "Комбинированный метод обхода."),
            ["general (ALT7)"] = ("Альтернативная 7", "Расширенные параметры десинхронизации."),
            ["general (ALT8)"] = ("Альтернативная 8", "Усиленная версия для сложных провайдеров."),
            ["general (ALT9)"] = ("Альтернативная 9", "Максимальная совместимость."),
            ["general (ALT10)"] = ("Альтернативная 10", "Экспериментальный метод."),
            ["general (ALT11)"] = ("Альтернативная 11", "Для специфических провайдеров."),
            ["general (FAKE TLS AUTO)"] = ("Fake TLS Auto", "Автоматический подбор Fake TLS параметров."),
            ["general (FAKE TLS AUTO ALT)"] = ("Fake TLS Auto ALT", "Альтернативный Fake TLS."),
            ["general (FAKE TLS AUTO ALT2)"] = ("Fake TLS Auto ALT2", "Второй альтернативный Fake TLS."),
            ["general (FAKE TLS AUTO ALT3)"] = ("Fake TLS Auto ALT3", "Третий альтернативный Fake TLS."),
            ["general (SIMPLE FAKE)"] = ("Simple Fake", "Простой метод подмены пакетов."),
            ["general (SIMPLE FAKE ALT)"] = ("Simple Fake ALT", "Альтернативный простой метод."),
            ["general (SIMPLE FAKE ALT2)"] = ("Simple Fake ALT2", "Второй альтернативный простой метод."),
        };

        public StrategiesPage()
        {
            this.InitializeComponent();
            LoadStrategies();
        }

        private void LoadStrategies()
        {
            var strategiesPath = Path.Combine(AppContext.BaseDirectory, "winws", "strategies");
            if (!Directory.Exists(strategiesPath)) return;

            var batFiles = Directory.GetFiles(strategiesPath, "*.bat").OrderBy(f => f).ToList();
            _strategyPaths.Clear();

            foreach (var bat in batFiles)
            {
                var name = Path.GetFileNameWithoutExtension(bat);
                _strategyPaths[name] = bat;
                StrategyListView.Items.Add(name);
            }

            if (StrategyListView.Items.Count > 0)
                StrategyListView.SelectedIndex = 0;
        }

        private void StrategyListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (StrategyListView.SelectedItem is not string name) return;

            _selectedBatPath = _strategyPaths.TryGetValue(name, out var path) ? path : "";
            StrategyTitleText.Text = name;
            StrategyInfoPanel.Visibility = Visibility.Visible;

            if (StrategyInfo.TryGetValue(name, out var info))
            {
                StrategyTypeText.Text = info.Type;
                StrategyUsageText.Text = info.Usage;
                StrategyDescText.Text = "";
            }
            else
            {
                StrategyTypeText.Text = "Стратегия zapret";
                StrategyUsageText.Text = "Обход DPI для Discord и YouTube.";
                StrategyDescText.Text = "";
            }

            // показываем активна ли стратегия
            if (AppState.CurrentStrategy == name)
            {
                StatusInfoBar.IsOpen = true;
                StatusInfoBar.Severity = InfoBarSeverity.Success;
                StatusInfoBar.Message = "Эта стратегия сейчас активна";
            }
            else
            {
                StatusInfoBar.IsOpen = false;
            }
        }

        private async void ApplyStrategyButton_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_selectedBatPath)) return;

            var binPath = Path.Combine(AppContext.BaseDirectory, "winws") + "\\";
            var listsPath = Path.Combine(AppContext.BaseDirectory, "winws", "lists") + "\\";

            var arguments = BatStrategyParser.ExtractArguments(_selectedBatPath, binPath, listsPath);

            if (string.IsNullOrEmpty(arguments))
            {
                var dialog = new ContentDialog
                {
                    Title = "Ошибка",
                    Content = "Не удалось прочитать стратегию из файла.",
                    CloseButtonText = "OK",
                    XamlRoot = this.XamlRoot
                };
                await dialog.ShowAsync();
                return;
            }

            _service.Stop();
            await _service.StartAsync(arguments);

            AppState.CurrentStrategy = StrategyListView.SelectedItem as string ?? "";

            StatusInfoBar.IsOpen = true;
            StatusInfoBar.Severity = InfoBarSeverity.Success;
            StatusInfoBar.Message = "Эта стратегия сейчас активна";
        }
    }
}