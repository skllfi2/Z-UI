using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.IO;
using ZapretGUI.Services;

namespace ZapretGUI.Views
{
    public sealed partial class ServicePage : Page
    {
        public ServicePage()
        {
            this.InitializeComponent();
            this.Loaded += async (s, e) =>
            {
                LoadStrategies();
                await RefreshStatusAsync();
            };
        }

        private void LoadStrategies()
        {
            var strategiesPath = Path.Combine(AppContext.BaseDirectory, "winws", "strategies");
            if (!Directory.Exists(strategiesPath)) return;

            var current = ServiceManager.GetInstalledStrategy();
            int selectedIndex = 0, i = 0;

            foreach (var file in Directory.GetFiles(strategiesPath, "*.bat"))
            {
                var name = Path.GetFileNameWithoutExtension(file);
                StrategyComboBox.Items.Add(new ComboBoxItem { Content = name, Tag = file });
                if (name == current) selectedIndex = i;
                i++;
            }
            if (StrategyComboBox.Items.Count > 0)
                StrategyComboBox.SelectedIndex = selectedIndex;
        }

        private async System.Threading.Tasks.Task RefreshStatusAsync()
        {
            DispatcherQueue.TryEnqueue(() => ServiceStatusText.Text = "Проверяется...");
            var status = await ServiceManager.GetStatusAsync();
            DispatcherQueue.TryEnqueue(() => ServiceStatusText.Text = status);
        }

        private async void RefreshStatus_Click(object sender, RoutedEventArgs e)
        {
            await RefreshStatusAsync();
        }

        private async void InstallService_Click(object sender, RoutedEventArgs e)
        {
            var strategy = StrategyComboBox.SelectedItem?.ToString();
            if (string.IsNullOrEmpty(strategy)) return;
            await ServiceManager.InstallAsync(strategy, "", s => AppendOutput(s));
            await RefreshStatusAsync();
        }

        private async void RemoveService_Click(object sender, RoutedEventArgs e)
        {
            await ServiceManager.RemoveAsync(s => AppendOutput(s));
            await RefreshStatusAsync();
        }

        private void AppendOutput(string text)
        {
            DispatcherQueue.TryEnqueue(() => OutputText.Text += text + "\n");
        }
    }
}