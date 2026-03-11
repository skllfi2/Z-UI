using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Win32;
using System;
using System.IO;
using System.Threading.Tasks;
using ZUI.Animations;
using ZUI.Services;

namespace ZUI.Views
{
    public sealed partial class SettingsPage : Page
    {
        private const string RegistryKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
        private const string AppName = "ZUI";
        private bool _isLoading = true;

        public SettingsPage()
        {
            this.InitializeComponent();
            LoadSettings();
            _isLoading = false;
            _ = Task.Run(LoadServiceStatus);
        }

        private void LoadSettings()
        {
            // Анимации
            bool anyAnim = AppSettings.AnimNavIcons || AppSettings.AnimButtons || AppSettings.AnimCards;
            AnimMasterToggle.IsOn    = anyAnim;
            AnimNavCheck.IsChecked     = AppSettings.AnimNavIcons;
            AnimButtonsCheck.IsChecked = AppSettings.AnimButtons;
            AnimCardsCheck.IsChecked   = AppSettings.AnimCards;
            UpdateAnimSummary();

            using var key = Registry.CurrentUser.OpenSubKey(RegistryKey);
            AutostartToggle.IsOn = key?.GetValue(AppName) != null;

            AutoStartZapretToggle.IsOn   = AppSettings.AutoStartZapret;
            MinimizeToTrayToggle.IsOn    = AppSettings.MinimizeToTrayOnStart;
            SoundEffectsToggle.IsOn      = AppSettings.SoundEffects;
            ToastNotificationsToggle.IsOn = AppSettings.ToastNotifications;
            AutoUpdateCheckToggle.IsOn   = AppSettings.AutoUpdateCheck;

            ThemeComboBox.SelectedIndex = AppSettings.Theme switch
            {
                "Light" => 1,
                "Dark"  => 2,
                _       => 0
            };

            LanguageComboBox.SelectedIndex = AppSettings.Language == "en" ? 1 : 0;

        }


        private void AutostartToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            using var key = Registry.CurrentUser.OpenSubKey(RegistryKey, true);
            if (AutostartToggle.IsOn)
            {
                var exePath = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName;
                key?.SetValue(AppName, $"\"{exePath}\"");
            }
            else
            {
                key?.DeleteValue(AppName, false);
            }
        }

        private void AutoStartZapretToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            AppSettings.AutoStartZapret = AutoStartZapretToggle.IsOn;
            AppSettings.Save();
        }

        private void MinimizeToTrayToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            AppSettings.MinimizeToTrayOnStart = MinimizeToTrayToggle.IsOn;
            AppSettings.Save();
        }

        private void SoundEffectsToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            AppSettings.SoundEffects = SoundEffectsToggle.IsOn;
            AppSettings.Save();
        }

        private void ToastNotificationsToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            AppSettings.ToastNotifications = ToastNotificationsToggle.IsOn;
            AppSettings.Save();
        }

        private void AutoUpdateCheckToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            AppSettings.AutoUpdateCheck = AutoUpdateCheckToggle.IsOn;
            AppSettings.Save();
        }

        private void ThemeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isLoading) return;
            var tag = (ThemeComboBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "Default";
            AppSettings.Theme = tag;
            AppSettings.Save();
            ApplyTheme(tag);
        }

        private void LanguageComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isLoading) return;
            var tag = (LanguageComboBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "ru";
            if (tag == AppSettings.Language) return;
            AppSettings.Language = tag;
            AppSettings.Save();
            MainWindow.Instance?.ReloadAllPages();
        }



        private static void ApplyTheme(string theme)
        {
            if (MainWindow.Instance?.Content is FrameworkElement root)
            {
                root.RequestedTheme = theme switch
                {
                    "Light" => ElementTheme.Light,
                    "Dark"  => ElementTheme.Dark,
                    _       => ElementTheme.Default
                };
            }
        }

        private async void LoadServiceStatus()
        {
            var status = await ServiceManager.GetStatusAsync();
            DispatcherQueue.TryEnqueue(() => ServiceStatusText.Text = status);
        }

        private void AppendServiceLog(string text)
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                ServiceLogText.Text += text + "\n";
                ServiceLogScrollViewer.ScrollToVerticalOffset(double.MaxValue);
            });
        }

        private async void InstallService_Click(object sender, RoutedEventArgs e)
        {
            var strategy = AppState.CurrentStrategy;
            if (string.IsNullOrEmpty(strategy))
            {
                AppendServiceLog("Ошибка: сначала выберите стратегию на странице Стратегии");
                return;
            }

            var batFile   = Path.Combine(ZapretPaths.StrategiesDir, strategy + ".bat");
            var arguments = BatStrategyParser.ParseStrategy(batFile);

            if (arguments == null)
            {
                AppendServiceLog("Ошибка: не удалось распарсить стратегию");
                return;
            }

            InstallServiceButton.IsEnabled = false;
            AppendServiceLog($"Устанавливаю службу со стратегией: {strategy}...");
            await ServiceManager.InstallAsync(strategy, arguments, AppendServiceLog);
            InstallServiceButton.IsEnabled = true;
            _ = Task.Run(LoadServiceStatus);
        }

        private async void RemoveService_Click(object sender, RoutedEventArgs e)
        {
            AppendServiceLog("Удаляю службы...");
            await ServiceManager.RemoveAsync(AppendServiceLog);
            AppendServiceLog("Готово.");
            _ = Task.Run(LoadServiceStatus);
        }

        // ── Анимации ─────────────────────────────────────────────────────────

        private bool _animExpanded = false;

        private void AnimHeader_PointerPressed(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
        {
            _animExpanded = !_animExpanded;
            AnimDetailPanel.Visibility = _animExpanded ? Visibility.Visible : Visibility.Collapsed;
            AnimExpandIcon.Glyph = _animExpanded ? "" : "";
        }

        private void AnimMasterToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            bool on = AnimMasterToggle.IsOn;
            AppSettings.AnimNavIcons = on;
            AppSettings.AnimButtons  = on;
            AppSettings.AnimCards    = on;

            _isLoading = true;
            AnimNavCheck.IsChecked     = on;
            AnimButtonsCheck.IsChecked = on;
            AnimCardsCheck.IsChecked   = on;
            _isLoading = false;

            ApplyAndSave();
        }

        private void AnimNav_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            AppSettings.AnimNavIcons = AnimNavCheck.IsChecked == true;
            ApplyAndSave();
        }

        private void AnimButtons_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            AppSettings.AnimButtons = AnimButtonsCheck.IsChecked == true;
            ApplyAndSave();
        }

        private void AnimCards_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            AppSettings.AnimCards = AnimCardsCheck.IsChecked == true;
            ApplyAndSave();
        }

        private void ApplyAndSave()
        {
            MainWindow.Instance?.ApplyAnimationSettings();
            UpdateAnimSummary();
            AppSettings.Save();
        }

        private void UpdateAnimSummary()
        {
            var enabled = new System.Collections.Generic.List<string>();
            if (AppSettings.AnimNavIcons) enabled.Add("навигация");
            if (AppSettings.AnimButtons)  enabled.Add("кнопки");
            if (AppSettings.AnimCards)    enabled.Add("карточки");

            _isLoading = true;
            AnimMasterToggle.IsOn = enabled.Count > 0;
            _isLoading = false;

            AnimSummaryText.Text = enabled.Count == 0
                ? "Все анимации отключены"
                : enabled.Count == 3
                    ? "Все категории включены"
                    : $"Включено: {string.Join(", ", enabled)}";
        }

        private void AboutButton_Click(object sender, RoutedEventArgs e) =>
            MainWindow.Instance?.NavigateTo("about");
    }
}
