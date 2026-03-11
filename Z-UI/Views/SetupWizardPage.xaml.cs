using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Windows.UI;
using ZUI.Animations;
using ZUI.Services;
using System.Runtime.InteropServices.WindowsRuntime;

namespace ZUI.Views
{
    public sealed partial class SetupWizardPage : Page
    {
        private const string ApiReleasesUrl = "https://api.github.com/repos/Flowseal/zapret-discord-youtube/releases/latest";

        private static readonly HttpClient _http = new()
        {
            Timeout = TimeSpan.FromSeconds(120),
            DefaultRequestHeaders = { { "User-Agent", "ZUI" } }
        };

        private int _currentStep = 1;
        private bool _zapretDownloaded = false;
        private string? _downloadedVersion = null;

        // Шаги: панели и точки
        private StackPanel[] _panels = null!;
        private Border[]     _dots   = null!;
        private Microsoft.UI.Xaml.Shapes.Rectangle[]  _lines  = null!;

        public SetupWizardPage()
        {
            this.InitializeComponent();

            _panels = new[] { PanelStep1, PanelStep2, PanelStep3, PanelStep4, PanelStep5 };
            _dots   = new[] { Step1Dot, Step2Dot, Step3Dot, Step4Dot, Step5Dot };
            _lines  = new[] { Line12, Line23, Line34, Line45 };

            // Если zapret уже установлен — пропускаем шаг скачивания
            _zapretDownloaded = File.Exists(ZapretPaths.WinwsExe);

            WizardGameFilterComboBox.SelectedIndex = 0;
            WizardIpsetFilterComboBox.SelectedIndex = 0;

            // Анимации кнопок мастера
            this.Loaded += (s, e) =>
            {
                ButtonAnimator.Attach(WizardNextButton);
                ButtonAnimator.Attach(WizardBackButton);
                ButtonAnimator.Attach(WizardDownloadButton);
            };

            UpdateStep();
        }

        // ── Навигация ────────────────────────────────────────────────────

        private void WizardNext_Click(object sender, RoutedEventArgs e)
        {
            // Шаг 2: нельзя идти дальше без скачивания
            if (_currentStep == 2 && !_zapretDownloaded)
            {
                ShowStepError("Сначала скачайте zapret, нажав кнопку «Скачать».");
                return;
            }

            if (_currentStep < 5)
            {
                _currentStep++;
                UpdateStep();
            }
            else
            {
                FinishWizard();
            }
        }

        private void WizardBack_Click(object sender, RoutedEventArgs e)
        {
            if (_currentStep > 1)
            {
                _currentStep--;
                UpdateStep();
            }
        }

        private void UpdateStep()
        {
            for (int i = 0; i < _panels.Length; i++)
                _panels[i].Visibility = (i + 1 == _currentStep) ? Visibility.Visible : Visibility.Collapsed;

            // Точки
            for (int i = 0; i < _dots.Length; i++)
            {
                bool done    = (i + 1) < _currentStep;
                bool current = (i + 1) == _currentStep;

                if (done)
                {
                    _dots[i].Background   = Application.Current.Resources["AccentFillColorDefaultBrush"] as Brush;
                    _dots[i].BorderThickness = new Thickness(0);
                    (_dots[i].Child as TextBlock)!.Text      = "✓";
                    (_dots[i].Child as TextBlock)!.Opacity   = 1;
                    (_dots[i].Child as TextBlock)!.Foreground = new SolidColorBrush(Color.FromArgb(255, 255, 255, 255));
                }
                else if (current)
                {
                    _dots[i].Background   = Application.Current.Resources["AccentFillColorDefaultBrush"] as Brush;
                    _dots[i].BorderThickness = new Thickness(0);
                    (_dots[i].Child as TextBlock)!.Text      = (i + 1).ToString();
                    (_dots[i].Child as TextBlock)!.Opacity   = 1;
                    (_dots[i].Child as TextBlock)!.Foreground = new SolidColorBrush(Color.FromArgb(255, 255, 255, 255));
                }
                else
                {
                    _dots[i].Background   = Application.Current.Resources["CardBackgroundFillColorDefaultBrush"] as Brush;
                    _dots[i].BorderThickness = new Thickness(2);
                    (_dots[i].Child as TextBlock)!.Text      = (i + 1).ToString();
                    (_dots[i].Child as TextBlock)!.Opacity   = 0.4;
                    (_dots[i].Child as TextBlock)!.Foreground = null;
                }
            }

            // Линии — подсвечиваем пройденные
            for (int i = 0; i < _lines.Length; i++)
            {
                _lines[i].Fill = (i + 1) < _currentStep
                    ? Application.Current.Resources["AccentFillColorDefaultBrush"] as Brush
                    : Application.Current.Resources["DividerStrokeColorDefaultBrush"] as Brush;
            }

            WizardStepLabel.Text      = $"Шаг {_currentStep} из 5";
            WizardBackButton.Visibility = _currentStep > 1 ? Visibility.Visible : Visibility.Collapsed;
            WizardNextButton.Content  = _currentStep == 5 ? "Готово" : "Далее →";
            WizardSkipButton.Visibility = _currentStep == 5 ? Visibility.Collapsed : Visibility.Visible;

            // Инициализация отдельных шагов
            if (_currentStep == 3) LoadStrategies();
            if (_currentStep == 5) PrepareFinish();
        }

        // ── Шаг 2: Скачивание ────────────────────────────────────────────

        private async void WizardDownload_Click(object sender, RoutedEventArgs e)
        {
            WizardDownloadButton.IsEnabled = false;
            WizardDownloadProgress.Visibility = Visibility.Visible;
            WizardLogBorder.Visibility = Visibility.Visible;

            try
            {
                AppendLog("Получаю информацию о релизе...");
                var json = await _http.GetStringAsync(ApiReleasesUrl);
                using var doc = JsonDocument.Parse(json);
                var tag     = doc.RootElement.GetProperty("tag_name").GetString() ?? "";
                var assets  = doc.RootElement.GetProperty("assets");

                _downloadedVersion = tag;
                DispatcherQueue.TryEnqueue(() => WizardVersionText.Text = $"Версия: {tag}");

                string? downloadUrl = null, assetName = null;
                foreach (var asset in assets.EnumerateArray())
                {
                    var n = asset.GetProperty("name").GetString() ?? "";
                    if (n.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                    {
                        downloadUrl = asset.GetProperty("browser_download_url").GetString();
                        assetName   = n;
                        break;
                    }
                }

                if (downloadUrl == null) { AppendLog("✗ Архив не найден в релизе"); return; }
                AppendLog($"Скачиваю: {assetName}");

                var tempZip = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"zapret_setup_{tag}.zip");

                using (var response = await _http.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead))
                {
                    var total = response.Content.Headers.ContentLength ?? 0;
                    using var stream = await response.Content.ReadAsStreamAsync();
                    using var file   = File.Create(tempZip);
                    var buffer = new byte[65536];
                    long downloaded = 0; int lastPct = -1, read;

                    while ((read = await stream.ReadAsync(buffer)) > 0)
                    {
                        await file.WriteAsync(buffer.AsMemory(0, read));
                        downloaded += read;
                        if (total > 0)
                        {
                            var pct = (int)(downloaded * 100 / total);
                            if (pct != lastPct)
                            {
                                lastPct = pct;
                                var mb = downloaded / 1024.0 / 1024.0;
                                var totalMb = total / 1024.0 / 1024.0;
                                DispatcherQueue.TryEnqueue(() =>
                                {
                                    WizardProgressBar.Value  = pct;
                                    WizardProgressPct.Text   = $"{pct}%";
                                    WizardProgressText.Text  = $"{mb:F1} МБ / {totalMb:F1} МБ";
                                });
                            }
                        }
                    }
                }

                AppendLog("✓ Скачано. Распаковываю...");
                DispatcherQueue.TryEnqueue(() => WizardProgressText.Text = "Распаковка...");

                var tempExtract = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"zapret_setup_extract");
                if (Directory.Exists(tempExtract)) Directory.Delete(tempExtract, true);
                await Task.Run(() => ZipFile.ExtractToDirectory(tempZip, tempExtract));

                var extractedDirs = Directory.GetDirectories(tempExtract);
                var sourceDir     = extractedDirs.Length == 1 ? extractedDirs[0] : tempExtract;

                AppendLog("Устанавливаю файлы...");
                await InstallZapretAsync(sourceDir, tag);

                File.Delete(tempZip);
                Directory.Delete(tempExtract, true);

                _zapretDownloaded = true;
                AppendLog($"✓ zapret {tag} успешно установлен");
                DispatcherQueue.TryEnqueue(() =>
                {
                    WizardProgressBar.Value = 100;
                    WizardProgressText.Text = "Установка завершена";
                    WizardProgressPct.Text  = "100%";
                    WizardDownloadButton.Content   = "✓ Установлено";
                    WizardDownloadButton.IsEnabled = false;
                });
            }
            catch (Exception ex)
            {
                AppendLog($"✗ Ошибка: {ex.Message}");
                WizardDownloadButton.IsEnabled = true;
            }
        }

        private async Task InstallZapretAsync(string sourceDir, string version)
        {
            await Task.Run(() =>
            {
                void CopyDir(string src, string dst)
                {
                    Directory.CreateDirectory(dst);
                    foreach (var f in Directory.GetFiles(src))
                        File.Copy(f, System.IO.Path.Combine(dst, System.IO.Path.GetFileName(f)), true);
                }

                // bin → winws
                var srcBin = System.IO.Path.Combine(sourceDir, "bin");
                if (Directory.Exists(srcBin)) CopyDir(srcBin, ZapretPaths.WinwsDir);

                // *.bat → strategies (кроме service*.bat)
                Directory.CreateDirectory(ZapretPaths.StrategiesDir);
                foreach (var f in Directory.GetFiles(sourceDir, "*.bat"))
                {
                    if (!System.IO.Path.GetFileName(f).StartsWith("service", StringComparison.OrdinalIgnoreCase))
                        File.Copy(f, System.IO.Path.Combine(ZapretPaths.StrategiesDir, System.IO.Path.GetFileName(f)), true);
                }

                // lists
                var srcLists = System.IO.Path.Combine(sourceDir, "lists");
                if (Directory.Exists(srcLists)) CopyDir(srcLists, ZapretPaths.ListsDir);

                // utils
                var srcUtils = System.IO.Path.Combine(sourceDir, "utils");
                if (Directory.Exists(srcUtils)) CopyDir(srcUtils, ZapretPaths.UtilsDir);
            });

            await File.WriteAllTextAsync(ZapretPaths.VersionFile, version);
            AppendLog("  файлы скопированы");
        }

        private void AppendLog(string text)
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                WizardLogText.Text += text + "\n";
                WizardLogScroll.ScrollToVerticalOffset(double.MaxValue);
            });
        }

        // ── Шаг 3: Стратегии ─────────────────────────────────────────────

        private void LoadStrategies()
        {
            WizardStrategyComboBox.Items.Clear();

            if (!Directory.Exists(ZapretPaths.StrategiesDir))
            {
                WizardStrategyComboBox.Items.Add(new ComboBoxItem { Content = "General", Tag = "General" });
                WizardStrategyComboBox.SelectedIndex = 0;
                return;
            }

            var bats = Directory.GetFiles(ZapretPaths.StrategiesDir, "*.bat")
                                 .Select(System.IO.Path.GetFileNameWithoutExtension)
                                 .Where(n => n != null)
                                 .OrderBy(n => n)
                                 .ToList();

            foreach (var name in bats)
                WizardStrategyComboBox.Items.Add(new ComboBoxItem { Content = name, Tag = name });

            // Выбираем General по умолчанию
            var generalIdx = bats.IndexOf("General");
            WizardStrategyComboBox.SelectedIndex = generalIdx >= 0 ? generalIdx : 0;

            WizardStrategyComboBox.SelectionChanged += (s, e) => UpdateStrategyInfo();
            UpdateStrategyInfo();
        }

        private void UpdateStrategyInfo()
        {
            var name = (WizardStrategyComboBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "";
            StrategyInfoBar.Title   = name;
            StrategyInfoBar.Message = name switch
            {
                "General"  => "Универсальная стратегия для Discord, YouTube и Google. Подходит большинству пользователей.",
                "Discord"  => "Оптимизирована для обхода блокировок Discord.",
                "YouTube"  => "Оптимизирована для обхода блокировок YouTube.",
                _          => "Пользовательская стратегия."
            };
        }

        // ── Шаг 5: Завершение ────────────────────────────────────────────

        private void PrepareFinish()
        {
            var strategy = (WizardStrategyComboBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "General";
            DoneStrategyRun.Text = $"Стратегия «{strategy}» выбрана.";
        }

        private void FinishWizard()
        {
            // Сохраняем стратегию
            var strategy = (WizardStrategyComboBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "General";
            AppState.CurrentStrategy = strategy;

            // Сохраняем фильтры
            var gameFilter  = (WizardGameFilterComboBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "disabled";
            var ipsetFilter = (WizardIpsetFilterComboBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "any";
            AppSettings.GameFilter  = gameFilter;
            AppSettings.IpsetFilter = ipsetFilter;
            AppSettings.SetupCompleted = true;
            AppSettings.Save();

            // Переходим на главную
            MainWindow.Instance?.CompleteSetup();
        }

        // ── Вспомогательное ──────────────────────────────────────────────

        private async void WizardSkip_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new ContentDialog
            {
                Title             = "Пропустить настройку?",
                Content           = "Вы сможете запустить мастер настройки позже с главной страницы.\nБез установки zapret защита не будет работать.",
                PrimaryButtonText = "Пропустить",
                CloseButtonText   = "Отмена",
                XamlRoot          = this.XamlRoot
            };
            if (await dialog.ShowAsync().AsTask() == ContentDialogResult.Primary)
            {
                AppSettings.SetupCompleted = true;
                AppSettings.Save();
                MainWindow.Instance?.CompleteSetup();
            }
        }

        private async void ShowStepError(string message)
        {
            var dialog = new ContentDialog
            {
                Title           = "Требуется действие",
                Content         = message,
                CloseButtonText = "OK",
                XamlRoot        = this.XamlRoot
            };
            await dialog.ShowAsync();
        }
    }
}
