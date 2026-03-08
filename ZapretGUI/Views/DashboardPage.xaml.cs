using System;
using System.IO;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI;
using ZapretGUI.Services;

namespace ZapretGUI.Views
{
    public sealed partial class DashboardPage : Page
    {
        private WinwsService _service => AppState.WinwsService;

        public DashboardPage()
        {
            this.InitializeComponent();
            this.Loaded += (s, e) => UpdateStatus();

            // подписываемся на изменения статуса
            _service.StatusChanged += isRunning =>
            {
                DispatcherQueue.TryEnqueue(() => UpdateStatus());
            };
        }

        private async void StartButton_Click(object sender, RoutedEventArgs e)
        {
            var strategyName = AppState.CurrentStrategy;
            var strategiesPath = Path.Combine(AppContext.BaseDirectory, "winws", "strategies");
            var batFile = Path.Combine(strategiesPath, strategyName + ".bat");

            // если есть .bat файл — парсим его
            if (File.Exists(batFile))
            {
                var binPath = Path.Combine(AppContext.BaseDirectory, "winws") + "\\";
                var listsPath = Path.Combine(AppContext.BaseDirectory, "winws", "lists") + "\\";
                var arguments = Services.BatStrategyParser.ExtractArguments(batFile, binPath, listsPath);
                if (arguments != null)
                {
                    await _service.StartAsync(arguments);
                    return;
                }
            }

            // fallback — хардкод general стратегии
            var binP = Path.Combine(AppContext.BaseDirectory, "winws") + "\\";
            var listsP = Path.Combine(AppContext.BaseDirectory, "winws", "lists") + "\\";
            var args =
                $"--wf-tcp=80,443,2053,2083,2087,2096,8443 --wf-udp=443,19294-19344,50000-50100 " +
                $"--filter-udp=443 --hostlist=\"{listsP}list-general.txt\" --hostlist-exclude=\"{listsP}list-exclude.txt\" --ipset-exclude=\"{listsP}ipset-exclude.txt\" --dpi-desync=fake --dpi-desync-repeats=6 --dpi-desync-fake-quic=\"{binP}quic_initial_www_google_com.bin\" --new " +
                $"--filter-udp=19294-19344,50000-50100 --filter-l7=discord,stun --dpi-desync=fake --dpi-desync-repeats=6 --new " +
                $"--filter-tcp=80,443 --hostlist=\"{listsP}list-general.txt\" --hostlist-exclude=\"{listsP}list-exclude.txt\" --dpi-desync=multisplit --dpi-desync-split-seqovl=568 --dpi-desync-split-pos=1 --dpi-desync-split-seqovl-pattern=\"{binP}tls_clienthello_4pda_to.bin\"";

            await _service.StartAsync(args);
        }

        private void StopButton_Click(object sender, RoutedEventArgs e)
        {
            _service.Stop();
        }

        private void UpdateStatus()
        {
            if (_service.IsRunning)
            {
                StatusIndicator.Fill = new SolidColorBrush(Color.FromArgb(255, 0, 200, 0));
                StatusText.Text = "Запущено";
            }
            else
            {
                StatusIndicator.Fill = new SolidColorBrush(Color.FromArgb(255, 200, 0, 0));
                StatusText.Text = "Остановлено";
            }
            StrategyText.Text = AppState.CurrentStrategy;
        }

        private void ChangeStrategy_Click(object sender, RoutedEventArgs e)
        {
            MainWindow.Instance?.NavigateTo("strategies");
        }
    }
}