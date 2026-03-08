using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace ZapretGUI.Views
{
    public sealed partial class LogsPage : Page
    {
        public LogsPage()
        {
            this.InitializeComponent();

            // показываем накопленные логи
            foreach (var line in AppState.Logs)
                LogsTextBlock.Text += line + "\n";

            // подписываемся на новые
            AppState.WinwsService.LogReceived += OnLogReceived;
        }

        private void OnLogReceived(string message)
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                LogsTextBlock.Text += message + "\n";
                LogsScrollViewer.ChangeView(null, LogsScrollViewer.ScrollableHeight, null);
            });
        }

        private void ClearLogs_Click(object sender, RoutedEventArgs e)
        {
            AppState.Logs.Clear();
            LogsTextBlock.Text = string.Empty;
        }
    }
}