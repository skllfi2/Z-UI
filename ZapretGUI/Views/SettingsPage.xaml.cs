using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Win32;
using System;

namespace ZapretGUI.Views
{
    public sealed partial class SettingsPage : Page
    {
        private const string RegistryKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
        private const string AppName = "ZapretGUI";
        private bool _isLoading = true;

        public SettingsPage()
        {
            this.InitializeComponent();
            LoadSettings();
            _isLoading = false;
        }

        private void LoadSettings()
        {
            // Автозапуск с Windows
            using var key = Registry.CurrentUser.OpenSubKey(RegistryKey);
            AutostartToggle.IsOn = key?.GetValue(AppName) != null;

            // Остальные настройки из AppSettings
            AutoStartZapretToggle.IsOn = AppSettings.AutoStartZapret;
            MinimizeToTrayToggle.IsOn = AppSettings.MinimizeToTrayOnStart;
            SoundEffectsToggle.IsOn = AppSettings.SoundEffects;
            ToastNotificationsToggle.IsOn = AppSettings.ToastNotifications;

            // Тема
            ThemeComboBox.SelectedIndex = AppSettings.Theme switch
            {
                "Light" => 1,
                "Dark" => 2,
                _ => 0
            };

            // Язык
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
            AppSettings.Language = tag;
            AppSettings.Save();
        }

        private static void ApplyTheme(string theme)
        {
            if (MainWindow.Instance?.Content is FrameworkElement root)
            {
                root.RequestedTheme = theme switch
                {
                    "Light" => ElementTheme.Light,
                    "Dark" => ElementTheme.Dark,
                    _ => ElementTheme.Default
                };
            }
        }
    }
}