using System.IO;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Windows.Foundation;
using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI;
using ZUI.Animations;
using ZUI.Services;

namespace ZUI.Views
{
    public sealed partial class DashboardPage : Page
    {
        private WinwsService _service => AppState.WinwsService;

        public DashboardPage()
        {
            this.InitializeComponent();

            this.Loaded += (s, e) =>
            {
                // Анимации кнопок
                ButtonAnimator.Attach(ToggleButton);

                // Анимации карточек сервисов (pulse при нажатии)
                ButtonAnimator.AttachToIcon(DiscordIndicator, "pulse");
                ButtonAnimator.AttachToIcon(YoutubeIndicator, "pulse");
                ButtonAnimator.AttachToIcon(GoogleIndicator,  "pulse");

                SetupRequiredBar.IsOpen = !System.IO.File.Exists(ZapretPaths.WinwsExe);
                UpdateStatus();
                LoadFilters();
                if (UpdateChecker.UpdateAvailable && UpdateChecker.LatestVersion != null)
                    ShowUpdateBanner(UpdateChecker.LatestVersion);
            };

            _service.StatusChanged += _ =>
            {
                DispatcherQueue.TryEnqueue(UpdateStatus);
            };

            UpdateChecker.UpdateFound += version =>
            {
                DispatcherQueue.TryEnqueue(() => ShowUpdateBanner(version));
            };
        }

        private async void ToggleButton_Click(object sender, RoutedEventArgs e)
        {
            // Проверяем наличие файлов zapret
            if (!System.IO.File.Exists(ZapretPaths.WinwsExe))
            {
                var dialog = new ContentDialog
                {
                    Title           = "zapret не установлен",
                    Content         = "Файлы zapret не найдены. Запустите мастер настройки чтобы скачать и установить zapret.",
                    PrimaryButtonText = "Открыть мастер настройки",
                    CloseButtonText = "Отмена",
                    XamlRoot        = this.XamlRoot
                };

                var result = await Task.Run(() => dialog.ShowAsync().GetResults());
                if (result == ContentDialogResult.Primary)
                {
                    AppSettings.SetupCompleted = false;
                    AppSettings.Save();
                    MainWindow.Instance?.NavigateTo("setup");
                }
                return;
            }

            if (_service.IsRunning)
            {
                _service.Stop();
            }
            else
            {
                var strategyName = AppState.CurrentStrategy;
                var batFile = Path.Combine(ZapretPaths.StrategiesDir, strategyName + ".bat");

                if (File.Exists(batFile))
                {
                    var arguments = BatStrategyParser.ParseStrategy(batFile);
                    if (arguments != null)
                    {
                        await _service.StartAsync(arguments);
                        return;
                    }
                }

                // fallback
                var listsP = ZapretPaths.ListsDir + "\\";
                var binP = ZapretPaths.WinwsDir + "\\";
                var args =
                    $"--wf-tcp=80,443,2053,2083,2087,2096,8443 --wf-udp=443,19294-19344,50000-50100 " +
                    $"--filter-udp=443 --hostlist=\"{listsP}list-general.txt\" --hostlist-exclude=\"{listsP}list-exclude.txt\" --ipset-exclude=\"{listsP}ipset-exclude.txt\" --dpi-desync=fake --dpi-desync-repeats=6 --dpi-desync-fake-quic=\"{binP}quic_initial_www_google_com.bin\" --new " +
                    $"--filter-udp=19294-19344,50000-50100 --filter-l7=discord,stun --dpi-desync=fake --dpi-desync-repeats=6 --new " +
                    $"--filter-tcp=80,443 --hostlist=\"{listsP}list-general.txt\" --hostlist-exclude=\"{listsP}list-exclude.txt\" --dpi-desync=multisplit --dpi-desync-split-seqovl=568 --dpi-desync-split-pos=1 --dpi-desync-split-seqovl-pattern=\"{binP}tls_clienthello_4pda_to.bin\"";

                await _service.StartAsync(args);
            }
        }

        private void UpdateStatus()
        {
            var running = _service.IsRunning;

            // Индикатор статуса
            StatusIndicator.Fill = new SolidColorBrush(
                running ? Color.FromArgb(255, 0, 200, 0)
                        : Color.FromArgb(255, 200, 0, 0));
            StatusText.Text = running ? "Запущено" : "Остановлено";
            StrategyText.Text = AppState.CurrentStrategy;

            // Кнопка
            if (running)
            {
                ToggleButton.Content = "⏹  Остановить";
                ToggleButton.Style = (Style)Application.Current.Resources["DefaultButtonStyle"];
            }
            else
            {
                ToggleButton.Content = "▶  Запустить";
                ToggleButton.Style = (Style)Application.Current.Resources["AccentButtonStyle"];
            }

            // Карточки сервисов
            var serviceText = running ? "Обход DPI активен" : "Защита выключена";
            DiscordStatus.Text = serviceText;
            YoutubeStatus.Text = serviceText;
            GoogleStatus.Text = serviceText;
        }

        private void ShowUpdateBanner(string version)
        {
            UpdateInfoBar.Message = $"Доступна новая версия zapret: {version}";
            UpdateInfoBar.IsOpen = true;
        }

        private void OpenWizard_Click(object sender, RoutedEventArgs e) =>
            MainWindow.Instance?.NavigateTo("setup");

        private void UpdateBanner_Click(object sender, RoutedEventArgs e) =>
            MainWindow.Instance?.NavigateTo("updates");

        private void ChangeStrategy_Click(object sender, RoutedEventArgs e) =>
            MainWindow.Instance?.NavigateTo("strategies");

        // ── Фильтры ────────────────────────────────────────────────────

        private bool _filtersLoading = true;

        private void LoadFilters()
        {
            _filtersLoading = true;

            // Game Filter
            GameFilterComboBox.SelectedIndex = AppSettings.GameFilter switch
            {
                "all" => 1,
                "tcp" => 2,
                "udp" => 3,
                _     => 0
            };

            // IPSet Filter — берём реальное состояние из bat-файла
            var actualIpset = BatStrategyParser.GetCurrentIpsetMode();
            IpsetFilterComboBox.SelectedIndex = actualIpset switch
            {
                "loaded" => 1,
                "none"   => 2,
                _        => 0
            };
            AppSettings.IpsetFilter = actualIpset;

            // Статус ipset файла
            var ipsetFile = System.IO.Path.Combine(ZapretPaths.ListsDir, "ipset-all.txt");
            IpsetStatusText.Text = actualIpset switch
            {
                "loaded" => System.IO.File.Exists(ipsetFile)
                                ? $"Загружен список: {System.IO.File.ReadAllLines(ipsetFile).Length} записей"
                                : "Загружен список IP",
                "none"   => "Фильтрация по IP отключена",
                _        => "Любой IP адрес (any)"
            };

            _filtersLoading = false;
        }

        private void GameFilterComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_filtersLoading) return;
            var tag = (GameFilterComboBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "disabled";
            AppSettings.GameFilter = tag;
            AppSettings.Save();
        }

        private void IpsetFilterComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_filtersLoading) return;
            var tag = (IpsetFilterComboBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "any";
            try { BatStrategyParser.ApplyIpsetFilter(tag); } catch { }
            AppSettings.IpsetFilter = tag;
            AppSettings.Save();
            LoadFilters(); // обновить статус-текст
        }
    }
}
