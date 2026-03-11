using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace ZUI.Views
{
    public sealed partial class UpdatesPage : Page
    {
        private const string VersionUrl = "https://raw.githubusercontent.com/Flowseal/zapret-discord-youtube/main/.service/version.txt";
        private const string IpsetUrl = "https://raw.githubusercontent.com/Flowseal/zapret-discord-youtube/refs/heads/main/.service/ipset-service.txt";
        private const string HostsUrl = "https://raw.githubusercontent.com/Flowseal/zapret-discord-youtube/refs/heads/main/.service/hosts";
        private const string ApiReleasesUrl = "https://api.github.com/repos/Flowseal/zapret-discord-youtube/releases/latest";

        private static readonly HttpClient _http = new()
        {
            Timeout = TimeSpan.FromSeconds(60),
            DefaultRequestHeaders = { { "User-Agent", "ZUI" } }
        };

        public UpdatesPage()
        {
            this.InitializeComponent();
            _ = CheckVersionSilentAsync();
        }

        private void AppendLog(string text)
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                LogText.Text += text + "\n";
                LogScrollViewer.ScrollToVerticalOffset(double.MaxValue);
            });
        }

        private void SetButtonsEnabled(bool enabled)
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                CheckVersionButton.IsEnabled = enabled;
                UpdateIpsetButton.IsEnabled = enabled;
                UpdateHostsButton.IsEnabled = enabled;
            });
        }

        private async Task CheckVersionSilentAsync()
        {
            try
            {
                var latest = (await _http.GetStringAsync(VersionUrl)).Trim();
                var local = ZapretPaths.LocalVersion;
                DispatcherQueue.TryEnqueue(() =>
                {
                    VersionStatusText.Text = local == latest
                        ? $"Установлена актуальная версия: {local}"
                        : $"Доступна новая версия: {latest} (установлена: {local})";
                });
            }
            catch
            {
                DispatcherQueue.TryEnqueue(() => VersionStatusText.Text = $"Версия: {ZapretPaths.LocalVersion} (нет подключения)");
            }
        }

        private async void CheckVersion_Click(object sender, RoutedEventArgs e)
        {
            SetButtonsEnabled(false);
            DispatcherQueue.TryEnqueue(() => VersionStatusText.Text = "Проверяется...");
            AppendLog("Проверяю версию...");

            try
            {
                var latest = (await _http.GetStringAsync(VersionUrl)).Trim();
                var local = ZapretPaths.LocalVersion;

                if (local == latest)
                {
                    DispatcherQueue.TryEnqueue(() => VersionStatusText.Text = $"Установлена актуальная версия: {local}");
                    AppendLog($"✓ Версия актуальна: {local}");
                }
                else
                {
                    DispatcherQueue.TryEnqueue(() =>
                        VersionStatusText.Text = $"Доступна новая версия: {latest} (установлена: {local})");
                    AppendLog($"↑ Доступна новая версия: {latest}");

                    DispatcherQueue.TryEnqueue(async () =>
                    {
                        var dialog = new ContentDialog
                        {
                            Title = "Доступно обновление",
                            Content = $"Новая версия: {latest}\nУстановленная: {local}\n\nСкачать и установить?",
                            PrimaryButtonText = "Обновить",
                            CloseButtonText = "Позже",
                            XamlRoot = this.XamlRoot
                        };
                        var result = await dialog.ShowAsync();
                        if (result == ContentDialogResult.Primary)
                            await DownloadAndInstallUpdateAsync(latest);
                    });
                }
            }
            catch (Exception ex)
            {
                DispatcherQueue.TryEnqueue(() => VersionStatusText.Text = "Ошибка проверки версии");
                AppendLog($"✗ Ошибка: {ex.Message}");
            }

            SetButtonsEnabled(true);
        }

        private async Task DownloadAndInstallUpdateAsync(string version)
        {
            SetButtonsEnabled(false);
            AppendLog($"\n--- Обновление до версии {version} ---");

            try
            {
                // Получаем URL архива через GitHub API
                AppendLog("Получаю информацию о релизе...");
                var json = await _http.GetStringAsync(ApiReleasesUrl);
                using var doc = JsonDocument.Parse(json);
                var assets = doc.RootElement.GetProperty("assets");

                string? downloadUrl = null;
                string? assetName = null;
                foreach (var asset in assets.EnumerateArray())
                {
                    var name = asset.GetProperty("name").GetString() ?? "";
                    if (name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                    {
                        downloadUrl = asset.GetProperty("browser_download_url").GetString();
                        assetName = name;
                        break;
                    }
                }

                if (downloadUrl == null)
                {
                    AppendLog("✗ Архив не найден в релизе");
                    SetButtonsEnabled(true);
                    return;
                }

                AppendLog($"Архив: {assetName}");

                // Скачиваем с прогрессом
                var tempZip = Path.Combine(Path.GetTempPath(), $"zapret_{version}.zip");
                AppendLog("Скачиваю...");

                using (var response = await _http.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead))
                {
                    var total = response.Content.Headers.ContentLength ?? 0;
                    using var stream = await response.Content.ReadAsStreamAsync();
                    using var file = File.Create(tempZip);

                    var buffer = new byte[65536];
                    long downloaded = 0;
                    int read;
                    int lastPct = 0;

                    while ((read = await stream.ReadAsync(buffer)) > 0)
                    {
                        await file.WriteAsync(buffer.AsMemory(0, read));
                        downloaded += read;

                        if (total > 0)
                        {
                            var pct = (int)(downloaded * 100 / total);
                            if (pct != lastPct && pct % 10 == 0)
                            {
                                lastPct = pct;
                                AppendLog($"  {pct}% ({downloaded / 1024}KB / {total / 1024}KB)");
                                DispatcherQueue.TryEnqueue(() =>
                                    VersionStatusText.Text = $"Скачивание: {pct}%");
                            }
                        }
                    }
                }

                AppendLog("✓ Архив скачан");
                AppendLog("Распаковываю...");

                var tempExtract = Path.Combine(Path.GetTempPath(), $"zapret_update_{version}");
                if (Directory.Exists(tempExtract)) Directory.Delete(tempExtract, true);
                ZipFile.ExtractToDirectory(tempZip, tempExtract);
                AppendLog("✓ Распаковано");

                // Находим корень архива
                var extractedDirs = Directory.GetDirectories(tempExtract);
                var sourceDir = extractedDirs.Length == 1 ? extractedDirs[0] : tempExtract;

                // Останавливаем winws
                AppState.WinwsService.Stop();
                await Task.Delay(800);

                // Копируем файлы
                AppendLog("Устанавливаю файлы...");
                await CopyUpdateFilesAsync(sourceDir);

                // Записываем версию
                await File.WriteAllTextAsync(ZapretPaths.VersionFile, version);

                // Чистим temp
                File.Delete(tempZip);
                Directory.Delete(tempExtract, true);

                DispatcherQueue.TryEnqueue(() => VersionStatusText.Text = $"✓ Обновлено до версии {version}");
                AppendLog($"\n✓ Запрет успешно обновлён до версии {version}");
            }
            catch (Exception ex)
            {
                AppendLog($"✗ Ошибка обновления: {ex.Message}");
                DispatcherQueue.TryEnqueue(() => VersionStatusText.Text = "Ошибка обновления");
            }

            SetButtonsEnabled(true);
        }

        private async Task CopyUpdateFilesAsync(string sourceDir)
        {
            // Пользовательские файлы — не перезаписываем
            var userFiles = new[]
            {
                "ipset-exclude-user.txt",
                "list-general-user.txt",
                "list-exclude-user.txt"
            };

            await Task.Run(() =>
            {
                // bin → zapret/winws/
                var srcBin = Path.Combine(sourceDir, "bin");
                if (Directory.Exists(srcBin))
                {
                    Directory.CreateDirectory(ZapretPaths.WinwsDir);
                    foreach (var file in Directory.GetFiles(srcBin))
                    {
                        var dest = Path.Combine(ZapretPaths.WinwsDir, Path.GetFileName(file));
                        File.Copy(file, dest, overwrite: true);
                    }
                    AppendLog($"  winws бинарники обновлены");
                }

                // *.bat стратегии → zapret/strategies/
                Directory.CreateDirectory(ZapretPaths.StrategiesDir);
                foreach (var file in Directory.GetFiles(sourceDir, "*.bat"))
                {
                    if (Path.GetFileName(file).StartsWith("service", StringComparison.OrdinalIgnoreCase)) continue;
                    var dest = Path.Combine(ZapretPaths.StrategiesDir, Path.GetFileName(file));
                    File.Copy(file, dest, overwrite: true);
                }
                AppendLog("  стратегии обновлены");

                // lists → zapret/lists/ (кроме user файлов)
                var srcLists = Path.Combine(sourceDir, "lists");
                if (Directory.Exists(srcLists))
                {
                    Directory.CreateDirectory(ZapretPaths.ListsDir);
                    foreach (var file in Directory.GetFiles(srcLists))
                    {
                        var fname = Path.GetFileName(file);
                        if (Array.Exists(userFiles, u => u.Equals(fname, StringComparison.OrdinalIgnoreCase)))
                        {
                            AppendLog($"  пропущен: {fname} (пользовательский)");
                            continue;
                        }
                        File.Copy(file, Path.Combine(ZapretPaths.ListsDir, fname), overwrite: true);
                    }
                    AppendLog("  списки обновлены");
                }

                // utils
                var srcUtils = Path.Combine(sourceDir, "utils");
                if (Directory.Exists(srcUtils))
                {
                    Directory.CreateDirectory(ZapretPaths.UtilsDir);
                    foreach (var file in Directory.GetFiles(srcUtils))
                    {
                        File.Copy(file, Path.Combine(ZapretPaths.UtilsDir, Path.GetFileName(file)), overwrite: true);
                    }
                    AppendLog("  utils обновлены");
                }
            });
        }

        private async void UpdateIpset_Click(object sender, RoutedEventArgs e)
        {
            SetButtonsEnabled(false);
            DispatcherQueue.TryEnqueue(() => IpsetStatusText.Text = "Загружается...");
            AppendLog("\nОбновляю ipset-all.txt...");

            try
            {
                var content = await _http.GetStringAsync(IpsetUrl);
                var listFile = Path.Combine(ZapretPaths.ListsDir, "ipset-all.txt");
                Directory.CreateDirectory(ZapretPaths.ListsDir);
                await File.WriteAllTextAsync(listFile, content);

                var lines = content.Split('\n').Length;
                DispatcherQueue.TryEnqueue(() => IpsetStatusText.Text = $"Обновлено: {lines} записей");
                AppendLog($"✓ ipset-all.txt обновлён ({lines} записей)");
            }
            catch (Exception ex)
            {
                DispatcherQueue.TryEnqueue(() => IpsetStatusText.Text = "Ошибка обновления");
                AppendLog($"✗ Ошибка: {ex.Message}");
            }

            SetButtonsEnabled(true);
        }

        private async void UpdateHosts_Click(object sender, RoutedEventArgs e)
        {
            SetButtonsEnabled(false);
            DispatcherQueue.TryEnqueue(() => HostsStatusText.Text = "Загружается...");
            AppendLog("\nПроверяю файл hosts...");

            try
            {
                var remoteContent = await _http.GetStringAsync(HostsUrl);
                var hostsFile = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.System),
                    "drivers", "etc", "hosts");

                var localContent = await File.ReadAllTextAsync(hostsFile);
                var remoteLines = remoteContent.Trim().Split('\n');
                var firstLine = remoteLines[0].Trim();
                var lastLine = remoteLines[^1].Trim();

                if (localContent.Contains(firstLine) && localContent.Contains(lastLine))
                {
                    DispatcherQueue.TryEnqueue(() => HostsStatusText.Text = "Файл hosts актуален");
                    AppendLog("✓ Файл hosts актуален");
                }
                else
                {
                    var tempFile = Path.Combine(Path.GetTempPath(), "zapret_hosts.txt");
                    await File.WriteAllTextAsync(tempFile, remoteContent);

                    DispatcherQueue.TryEnqueue(() =>
                    {
                        HostsStatusText.Text = "Требуется обновление";
                        AppendLog("↑ Hosts файл устарел");
                        AppendLog($"  Новый hosts: {tempFile}");
                        AppendLog("  Скопируйте содержимое в системный hosts файл");
                        System.Diagnostics.Process.Start("notepad.exe", tempFile);
                        System.Diagnostics.Process.Start("notepad.exe", hostsFile);
                    });
                }
            }
            catch (UnauthorizedAccessException)
            {
                DispatcherQueue.TryEnqueue(() => HostsStatusText.Text = "Нет доступа к hosts");
                AppendLog("✗ Нет прав на чтение hosts");
            }
            catch (Exception ex)
            {
                DispatcherQueue.TryEnqueue(() => HostsStatusText.Text = "Ошибка");
                AppendLog($"✗ Ошибка: {ex.Message}");
            }

            SetButtonsEnabled(true);
        }
    }
}
