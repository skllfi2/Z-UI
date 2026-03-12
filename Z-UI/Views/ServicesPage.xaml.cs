using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.UI;
using ZUI.Animations;
using ZUI.Services;

namespace ZUI.Views
{
    public sealed partial class ServicesPage : Page
    {
        // ── Константы обновлений ──────────────────────────────────────
        private const string VersionUrl     = "https://raw.githubusercontent.com/Flowseal/zapret-discord-youtube/main/.service/version.txt";
        private const string IpsetUrl       = "https://raw.githubusercontent.com/Flowseal/zapret-discord-youtube/refs/heads/main/.service/ipset-service.txt";
        private const string HostsUrl       = "https://raw.githubusercontent.com/Flowseal/zapret-discord-youtube/refs/heads/main/.service/hosts";
        private const string MalwHostsUrl   = "https://raw.githubusercontent.com/ImMALWARE/dns.malw.link/master/hosts";
        private const string FlowsealApiUrl = "https://api.github.com/repos/Flowseal/zapret-discord-youtube/commits?path=.service/hosts&per_page=1";

        private static bool IsRunningAsAdmin()
        {
            var id = System.Security.Principal.WindowsIdentity.GetCurrent();
            return new System.Security.Principal.WindowsPrincipal(id)
                .IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
        }

        private static void RestartAsAdmin()
        {
            var exe = System.Diagnostics.Process.GetCurrentProcess().MainModule!.FileName;
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = exe, UseShellExecute = true, Verb = "runas"
            });
            Application.Current.Exit();
        }

        private static string ComputeSHA256(string text)
        {
            var bytes = System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes(text));
            return Convert.ToHexString(bytes);
        }

        private static async Task<string> BuildMergedHostsBlock(HttpClient http)
        {
            var flowsealContent = await http.GetStringAsync(
                "https://raw.githubusercontent.com/Flowseal/zapret-discord-youtube/refs/heads/main/.service/hosts");
            var malwContent = await http.GetStringAsync(
                "https://raw.githubusercontent.com/ImMALWARE/dns.malw.link/master/hosts");

            var flowsealDomains = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var line in flowsealContent.Split('\n'))
            {
                var t = line.Trim();
                if (t.StartsWith('#') || string.IsNullOrWhiteSpace(t)) continue;
                var parts = t.Split(new char[]{' ','\t'}, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 2) flowsealDomains.Add(parts[1]);
            }

            var extraLines = new List<string>();
            foreach (var line in malwContent.Split('\n'))
            {
                var t = line.Trim();
                if (t.StartsWith('#') || string.IsNullOrWhiteSpace(t)) continue;
                var parts = t.Split(new char[]{' ','\t'}, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 2 && !flowsealDomains.Contains(parts[1]))
                    extraLines.Add(t);
            }

            var sb = new System.Text.StringBuilder();
            sb.AppendLine("# ===== zapret hosts (Flowseal) =====");
            sb.Append(flowsealContent.TrimEnd());
            if (extraLines.Count > 0)
            {
                sb.AppendLine(); sb.AppendLine();
                sb.AppendLine("# ===== дополнительные домены (ImMALWARE/dns.malw.link) =====");
                foreach (var l in extraLines) sb.AppendLine(l);
            }
            return sb.ToString();
        }

        public async Task CheckHostsUpdateAvailable()
        {
            try
            {
                // Лёгкий запрос к GitHub API — только дата последнего коммита
                var req = new System.Net.Http.HttpRequestMessage(
                    System.Net.Http.HttpMethod.Get, FlowsealApiUrl);
                req.Headers.Add("User-Agent", "Z-UI");
                var resp = await _http.SendAsync(req);
                if (!resp.IsSuccessStatusCode) return;

                var json = await resp.Content.ReadAsStringAsync();
                using var doc = System.Text.Json.JsonDocument.Parse(json);
                var dateStr = doc.RootElement[0]
                    .GetProperty("commit").GetProperty("author").GetProperty("date").GetString();
                if (DateTime.TryParse(dateStr, out var commitDate) && commitDate > AppSettings.HostsLastCheck)
                {
                    DispatcherQueue.TryEnqueue(() =>
                    {
                        HostsUpdateBadge.Visibility = Visibility.Visible;
                        HostsStatusText.Text = "Доступно обновление hosts";
                    });

                    // Автообновление если включено и есть права
                    if (AppSettings.HostsAutoUpdate && IsRunningAsAdmin())
                        await ApplyHostsAsync(silent: true);
                }
                else
                {
                    DispatcherQueue.TryEnqueue(() =>
                        HostsLastCheckText.Text = $"Проверено: {AppSettings.HostsLastCheck:dd.MM.yy HH:mm}");
                }
                AppSettings.HostsLastCheck = DateTime.UtcNow;
                AppSettings.Save();
            }
            catch { }
        }

        private async Task ApplyHostsAsync(bool silent = false)
        {
            var hostsFile = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.System),
                "drivers", "etc", "hosts");
            try
            {
                if (!silent)
                    DispatcherQueue.TryEnqueue(() =>
                    {
                        HostsProgressPanel.Visibility = Visibility.Visible;
                        HostsProgressBar.IsIndeterminate = true;
                        HostsProgressLabel.Text = "↓ Скачиваю Flowseal hosts...";
                        HostsStatusText.Text = "Загружается...";
                        UpdateHostsButton.IsEnabled = false;
                    });

                var block = await BuildMergedHostsBlock(_http);
                var hash  = ComputeSHA256(block);

                if (!silent)
                    DispatcherQueue.TryEnqueue(() => HostsProgressLabel.Text = "Проверяю актуальность...");

                if (hash == AppSettings.HostsHash)
                {
                    DispatcherQueue.TryEnqueue(() =>
                    {
                        HostsStatusText.Text = "Hosts актуален";
                        HostsLastCheckText.Text = $"Обновлён: {AppSettings.HostsLastCheck:dd.MM.yy HH:mm}";
                        HostsUpdateBadge.Visibility = Visibility.Collapsed;
                        HostsProgressPanel.Visibility = Visibility.Collapsed;
                        UpdateHostsButton.IsEnabled = true;
                    });
                    if (!silent) AppendUpdateLog("✓ Файл hosts актуален");
                    return;
                }

                if (!silent)
                    DispatcherQueue.TryEnqueue(() => HostsProgressLabel.Text = "Применяю изменения...");

                var localContent = await File.ReadAllTextAsync(hostsFile);
                var backupFile   = hostsFile + ".backup_zui";
                if (!File.Exists(backupFile)) File.Copy(hostsFile, backupFile);

                var clean = localContent;
                var idx   = clean.IndexOf("# ===== zapret hosts");
                if (idx >= 0) clean = clean[..idx].TrimEnd();

                await File.WriteAllTextAsync(hostsFile, clean + Environment.NewLine + Environment.NewLine + block);

                AppSettings.HostsHash        = hash;
                AppSettings.HostsLastCheck   = DateTime.UtcNow;
                AppSettings.Save();

                var count = block.Split('\n').Length;
                DispatcherQueue.TryEnqueue(() =>
                {
                    HostsStatusText.Text = "Hosts обновлён ✓";
                    HostsLastCheckText.Text = $"Обновлён: {DateTime.Now:dd.MM.yy HH:mm}";
                    HostsUpdateBadge.Visibility = Visibility.Collapsed;
                    HostsProgressPanel.Visibility = Visibility.Collapsed;
                    UpdateHostsButton.IsEnabled = true;
                });
                if (!silent) AppendUpdateLog($"✓ Hosts применён ({count} строк). Бэкап: {backupFile}");
            }
            catch (UnauthorizedAccessException)
            {
                DispatcherQueue.TryEnqueue(() =>
                {
                    HostsStatusText.Text = "Нет прав администратора";
                    HostsProgressPanel.Visibility = Visibility.Collapsed;
                    UpdateHostsButton.IsEnabled = true;
                });
                if (!silent) AppendUpdateLog("✗ Нет прав на запись hosts");
            }
            catch (Exception ex)
            {
                DispatcherQueue.TryEnqueue(() =>
                {
                    HostsStatusText.Text = "Ошибка";
                    HostsProgressPanel.Visibility = Visibility.Collapsed;
                    UpdateHostsButton.IsEnabled = true;
                });
                if (!silent) AppendUpdateLog($"✗ Ошибка: {ex.Message}");
            }
        }
        private const string ApiReleasesUrl = "https://api.github.com/repos/Flowseal/zapret-discord-youtube/releases/latest";

        private static readonly HttpClient _http = new()
        {
            Timeout = TimeSpan.FromSeconds(60),
            DefaultRequestHeaders = { { "User-Agent", "ZUI" } }
        };

        // ── Тестирование ──────────────────────────────────────────────
        private static readonly (string Name, string Url)[] TestTargets =
        [
            ("Discord", "https://discord.com"),
            ("YouTube", "https://youtube.com"),
            ("Google",  "https://google.com"),
        ];

        private Process?                    _testProcess;
        private StreamWriter?               _testStdin;
        private TaskCompletionSource<string>? _pendingInput;
        private CancellationTokenSource?    _testCts;

        // ─────────────────────────────────────────────────────────────

        public ServicesPage()
        {
            this.InitializeComponent();

            // Анимации кнопок
            this.Loaded += (s, e) =>
            {
                ButtonAnimator.Attach(CheckVersionButton);
                ButtonAnimator.Attach(UpdateIpsetButton);
                ButtonAnimator.Attach(UpdateHostsButton);
                ButtonAnimator.Attach(RunTestButton);
            };

            foreach (var line in AppState.Logs)
                LogsTextBox.Text += line + "\n";
            AppState.WinwsService.LogReceived += OnLogReceived;

            // Диагностика запускается при переключении на вкладку 2 (не изменилась)
            // Обновления инициализируются при переключении на вкладку 0
            MainPivot.SelectionChanged += async (s, e) =>
            {
                if (MainPivot.SelectedIndex == 2) _ = RunDiagnosticsAsync();
                if (MainPivot.SelectedIndex == 0) await CheckVersionSilentAsync();
            };

            // Бейдж на вкладке Обновления
            if (UpdateChecker.UpdateAvailable)
                UpdatesTabBadge.Visibility = Microsoft.UI.Xaml.Visibility.Visible;

            UpdateChecker.UpdateFound += _ =>
            {
                DispatcherQueue.TryEnqueue(() =>
                    UpdatesTabBadge.Visibility = Microsoft.UI.Xaml.Visibility.Visible);
            };
        }

        // При навигации сюда с параметром — переключаем вкладку
        // NavigateTo("updates") передаёт параметр 0
        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            if (e.Parameter is int tabIndex)
                DispatcherQueue.TryEnqueue(() => MainPivot.SelectedIndex = tabIndex);
        }

        // ══════════════════════════════════════════════════════════════
        // ЛОГИ
        // ══════════════════════════════════════════════════════════════

        private void OnLogReceived(string message)
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                LogsTextBox.Text += message + "\n";
                LogsScrollViewer.ChangeView(null, LogsScrollViewer.ScrollableHeight, null);
            });
        }

        private void ClearLogs_Click(object sender, RoutedEventArgs e)
        {
            AppState.Logs.Clear();
            LogsTextBox.Text = string.Empty;
        }

        private async void SaveLogs_Click(object sender, RoutedEventArgs e) =>
            await SaveTextToFile(LogsTextBox.Text, $"zapret_logs_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}");

        // ══════════════════════════════════════════════════════════════
        // ТЕСТИРОВАНИЕ
        // ══════════════════════════════════════════════════════════════

        private void ClearTest_Click(object sender, RoutedEventArgs e) => TestOutputBox.Text = string.Empty;
        private async void SaveTest_Click(object sender, RoutedEventArgs e) =>
            await SaveTextToFile(TestOutputBox.Text, $"zapret_test_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}");

        private void StopTest_Click(object sender, RoutedEventArgs e)
        {
            _testCts?.Cancel();
            try { _testProcess?.Kill(entireProcessTree: true); } catch { }
            AppendTest("[ТЕСТ] Остановлено пользователем");
            SetTestIdle();
        }

        private async void RunTest_Click(object sender, RoutedEventArgs e)
        {
            SetTestRunning();
            TestOutputBox.Text = string.Empty;
            AppendTest("─────────────────────────────────");
            AppendTest($"[ТЕСТ] Запуск ({DateTime.Now:HH:mm:ss})");
            AppendTest($"[ТЕСТ] Защита: {(AppState.WinwsService.IsRunning ? "АКТИВНА" : "ОСТАНОВЛЕНА")}");
            AppendTest("─────────────────────────────────");

            bool serviceWasInstalled = ServiceManager.IsInstalled();
            string? serviceStrategy = null;
            if (serviceWasInstalled)
            {
                serviceStrategy = ServiceManager.GetInstalledStrategy();
                AppendTest($"[ТЕСТ] Служба установлена ({serviceStrategy}), временно удаляю...");
                await ServiceManager.RemoveAsync(l => AppendTest("[SVC] " + l));
            }

            var psScript  = Path.Combine(ZapretPaths.UtilsDir, "test_zapret.ps1");
            var psScript2 = Path.Combine(ZapretPaths.UtilsDir, "test zapret.ps1");

            if (File.Exists(psScript) || File.Exists(psScript2))
            {
                var script = File.Exists(psScript) ? psScript : psScript2;
                AppendTest($"[ТЕСТ] Скрипт: {Path.GetFileName(script)}");
                await RunInteractivePsAsync(script);
            }
            else
            {
                AppendTest("[ТЕСТ] Скрипт не найден — встроенная проверка");
                await RunBuiltinTestAsync();
            }

            if (serviceWasInstalled)
            {
                AppendTest("[ТЕСТ] Восстанавливаю службу...");
                var strategy = serviceStrategy ?? AppState.CurrentStrategy;
                var batFile = Path.Combine(ZapretPaths.StrategiesDir, strategy + ".bat");
                var args = BatStrategyParser.ParseStrategy(batFile);
                if (args != null)
                {
                    await ServiceManager.InstallAsync(strategy, args, l => AppendTest("[SVC] " + l));
                    AppendTest("[ТЕСТ] Служба восстановлена");
                }
            }

            AppendTest("─────────────────────────────────");
            SetTestIdle();
        }

        private async Task RunInteractivePsAsync(string scriptPath)
        {
            _testCts = new CancellationTokenSource();
            var ct = _testCts.Token;
            try
            {
                var scriptDir   = Path.GetDirectoryName(scriptPath)!;
                var escapedPath = scriptPath.Replace("'", "''");
                var wrapperLines = new[]
                {
                    "[Console]::OutputEncoding = [System.Text.Encoding]::UTF8",
                    "[Console]::InputEncoding = [System.Text.Encoding]::UTF8",
                    $"& '{escapedPath}'",
                };
                var tmpWrapper = Path.Combine(Path.GetTempPath(), "zapret_test_wrapper.ps1");
                File.WriteAllLines(tmpWrapper, wrapperLines, Encoding.UTF8);

                var psi = new ProcessStartInfo
                {
                    FileName               = "powershell.exe",
                    Arguments              = $"-ExecutionPolicy Bypass -File \"{tmpWrapper}\"",
                    WorkingDirectory       = scriptDir,
                    UseShellExecute        = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError  = true,
                    RedirectStandardInput  = true,
                    CreateNoWindow         = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding  = Encoding.UTF8,
                };

                _testProcess = Process.Start(psi);
                if (_testProcess == null) { AppendTest("[ТЕСТ] Не удалось запустить PowerShell"); return; }
                _testStdin = _testProcess.StandardInput;

                _ = Task.Run(async () =>
                {
                    string? line;
                    while ((line = await _testProcess.StandardOutput.ReadLineAsync()) != null)
                    {
                        if (ct.IsCancellationRequested) break;
                        AppendTest(line);
                    }
                }, ct);

                _ = Task.Run(async () =>
                {
                    string? line;
                    while ((line = await _testProcess.StandardError.ReadLineAsync()) != null)
                    {
                        if (ct.IsCancellationRequested) break;
                        if (!line.Contains("Read-Host") && !line.Contains("InvalidOperation"))
                            AppendTest("[ERR] " + line);
                    }
                }, ct);

                await MonitorForPromptsAsync(_testProcess, ct);
                if (!_testProcess.HasExited) _testProcess.WaitForExit(5000);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { AppendTest("[ТЕСТ] Ошибка: " + ex.Message); }
        }

        private async Task MonitorForPromptsAsync(Process proc, CancellationToken ct)
        {
            var reader     = proc.StandardOutput;
            var lineBuffer = new StringBuilder();
            var promptBuffer = new StringBuilder();
            var inPrompt = false;

            while (!proc.HasExited && !ct.IsCancellationRequested)
            {
                if (reader.Peek() < 0) { await Task.Delay(50, ct).ConfigureAwait(false); continue; }
                int ch = reader.Read();
                if (ch < 0) break;

                if (ch == '\n')
                {
                    var line = lineBuffer.ToString().TrimEnd('\r');
                    lineBuffer.Clear();

                    if (inPrompt)
                    {
                        promptBuffer.AppendLine(line);
                    }
                    else
                    {
                        AppendTest(line);
                    }
                }
                else if (ch == ':')
                {
                    var next = reader.Peek();
                    if (next == ' ' || next == -1)
                    {
                        var promptText = lineBuffer.ToString().Trim();
                        lineBuffer.Clear();
                        if (next == ' ') reader.Read();

                        // Проверяем, является ли это запросом
                        if (IsPromptRequest(promptText))
                        {
                            inPrompt = true;
                            promptBuffer.Clear();
                            promptBuffer.Append(promptText).AppendLine(":");
                        }
                        else
                        {
                            AppendTest(promptText + ":");
                        }
                    }
                    else
                    {
                        lineBuffer.Append((char)ch);
                    }
                }
                else if (inPrompt && ch == '\r')
                {
                    // Игнорируем возврат каретки внутри запроса
                }
                else
                {
                    lineBuffer.Append((char)ch);
                }

                // Если мы в режиме запроса и получили пустую строку, завершаем запрос
                if (inPrompt && lineBuffer.Length == 0 && promptBuffer.Length > 0)
                {
                    var fullPrompt = promptBuffer.ToString().Trim();
                    promptBuffer.Clear();
                    inPrompt = false;

                    AppendTest(fullPrompt + ":");
                    var answer = await ShowPromptAsync(fullPrompt, ct);
                    if (!ct.IsCancellationRequested && _testStdin != null)
                    {
                        AppendTest($"> {answer}");
                        await _testStdin.WriteLineAsync(answer);
                        await _testStdin.FlushAsync();
                    }
                }
            }

            if (lineBuffer.Length > 0) AppendTest(lineBuffer.ToString());
            if (promptBuffer.Length > 0) AppendTest(promptBuffer.ToString());
        }

        private bool IsPromptRequest(string text)
        {
            // Распознаем типичные запросы из PowerShell скрипта
            var lower = text.ToLower();

            // Запросы из Read-TestType
            if (lower.Contains("select test type") && lower.Contains("enter 1 or 2")) return true;

            // Запросы из Read-ModeSelection
            if (lower.Contains("select test run mode") && lower.Contains("enter 1 or 2")) return true;

            // Запросы из Read-ConfigSelection
            if (lower.Contains("available configs") && lower.Contains("enter numbers")) return true;
            if (lower.Contains("configs") && (lower.Contains("enter") || lower.Contains("numbers"))) return true;

            // Простые числовые запросы
            if (lower.Contains("enter") && (lower.Contains("1") || lower.Contains("2"))) return true;

            return false;
        }

        private Task<string> ShowPromptAsync(string prompt, CancellationToken ct)
        {
            _pendingInput = new TaskCompletionSource<string>();
            DispatcherQueue.TryEnqueue(() =>
            {
                InputPromptText.Text = prompt;
                InputButtonsList.Items.Clear();
                FreeInputPanel.Visibility = Visibility.Collapsed;

                // Определяем тип запроса
                var isTestType = prompt.Contains("test type") && prompt.Contains("Enter 1 or 2");
                var isMode = prompt.Contains("mode") && prompt.Contains("Enter 1 or 2");
                var isSimpleChoice = prompt.Contains("Enter 1 or 2");
                var isConfigSelection = prompt.Contains("configs") || prompt.Contains("numbers") || prompt.Contains("ranges");

                if (isTestType)
                {
                    AddInputButton("1", "Standard (HTTP/ping)");
                    AddInputButton("2", "DPI checkers (TCP 16-20)");
                }
                else if (isMode)
                {
                    AddInputButton("1", "Все стратегии");
                    AddInputButton("2", "Выбрать стратегии");
                }
                else if (isSimpleChoice)
                {
                    AddInputButton("1", "1");
                    AddInputButton("2", "2");
                }
                else if (isConfigSelection)
                {
                    // Показываем примеры ввода
                    FreeInputPanel.Visibility = Visibility.Visible;
                    FreeInputBox.Text = "1,3,5-7,10";
                    FreeInputBox.Focus(FocusState.Programmatic);

                    // Добавляем подсказку
                    var helpText = new TextBlock
                    {
                        Text = "Примеры: 1,3,5-7,10 или 2-5 или 1 3 5",
                        FontSize = 12,
                        Foreground = new SolidColorBrush(Microsoft.UI.Colors.Gray),
                        Margin = new Thickness(0, 4, 0, 0)
                    };
                    InputButtonsList.Items.Add(helpText);
                }
                else
                {
                    // Для других запросов показываем свободный ввод
                    FreeInputPanel.Visibility = Visibility.Visible;
                    FreeInputBox.Text = "";
                    FreeInputBox.Focus(FocusState.Programmatic);
                }

                InputPanel.Visibility = Visibility.Visible;
            });
            ct.Register(() => _pendingInput?.TrySetResult("1"));
            return _pendingInput.Task;
        }

        private void AddInputButton(string value, string label)
        {
            var btn = new Button
            {
                Content = $"[{value}] {label}",
                Tag = value,
                Margin = new Thickness(4),
                Padding = new Thickness(8, 4, 8, 4),
                MinWidth = 120
            };
            btn.Click += (s, e) =>
            {
                HideInputPanel();
                _pendingInput?.TrySetResult((s as Button)?.Tag?.ToString() ?? "1");
            };
            InputButtonsList.Items.Add(btn);
        }

        private void FreeInputBox_KeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (e.Key == Windows.System.VirtualKey.Enter) SendFreeInput();
        }
        private void FreeInputSend_Click(object sender, RoutedEventArgs e) => SendFreeInput();
        private void SendFreeInput()
        {
            var val = FreeInputBox.Text.Trim();
            if (string.IsNullOrEmpty(val))
            {
                FreeInputBox.Text = "0";
                return;
            }

            if (val.Contains(",") || val.Contains("-") || val.Contains(" "))
            {
                // Это может быть диапазон или список
                var parsed = ParseConfigSelection(val);
                if (parsed != null)
                {
                    HideInputPanel();
                    _pendingInput?.TrySetResult(string.Join(",", parsed));
                }
                else
                {
                    FreeInputBox.Text = "0";
                }
            }
            else
            {
                HideInputPanel();
                _pendingInput?.TrySetResult(val);
            }
        }

        private List<int>? ParseConfigSelection(string input)
        {
            var parts = input.Split(new[] { ',', ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            var result = new List<int>();
            var hasErrors = false;

            foreach (var part in parts)
            {
                if (string.IsNullOrWhiteSpace(part)) continue;

                if (part.Contains("-") && int.TryParse(part.Split('-')[0], out var start) && int.TryParse(part.Split('-')[1], out var end))
                {
                    if (start > end)
                    {
                        // Показываем предупреждение
                        DispatcherQueue.TryEnqueue(() => FreeInputBox.Text = $"Ошибка: {part} (начало > конца)");
                        hasErrors = true;
                        continue;
                    }

                    for (int i = start; i <= end; i++) result.Add(i);
                }
                else if (int.TryParse(part, out var num))
                {
                    result.Add(num);
                }
                else
                {
                    // Показываем предупреждение
                    DispatcherQueue.TryEnqueue(() => FreeInputBox.Text = $"Ошибка: {part} (не число)");
                    hasErrors = true;
                }
            }

            return hasErrors ? null : result;
        }
        private void HandleInputResult(string val)
        {
            if (string.IsNullOrEmpty(val)) val = "0";
            HideInputPanel();
            _pendingInput?.TrySetResult(val);
        }
        private static async Task SaveTextToFile(string text, string defaultFileName)
        {
            var picker = new FileSavePicker
            {
                SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
                SuggestedFileName = defaultFileName
            };
            picker.FileTypeChoices.Add("Текстовый файл", new List<string> { ".txt" });

            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(Window.Current);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

            StorageFile file = await picker.PickSaveFileAsync();
            if (file != null)
                await FileIO.WriteTextAsync(file, text);
        }

        private void HideInputPanel()
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                InputPanel.Visibility = Visibility.Collapsed;
                InputButtonsList.Items.Clear();
                FreeInputPanel.Visibility = Visibility.Collapsed;
            });
        }

        private async Task RunBuiltinTestAsync()
        {
            var results = new List<(string Name, bool Ok, long Ms)>();
            foreach (var (name, url) in TestTargets)
            {
                AppendTest($"[ТЕСТ] Проверка {name}...");
                var (ok, ms) = await CheckUrlAsync(url);
                results.Add((name, ok, ms));
                AppendTest($"[ТЕСТ] {name}: {(ok ? $"✓ OK ({ms} мс)" : "✗ НЕДОСТУПЕН")}");
            }
            int passed = 0;
            AppendTest("\n[ТЕСТ] Итог:");
            foreach (var (name, ok, ms) in results) { AppendTest($"  {(ok ? "✓" : "✗")} {name}"); if (ok) passed++; }
            AppendTest($"[ТЕСТ] Прошло: {passed}/{results.Count}");
            DispatcherQueue.TryEnqueue(() => TestStatusText.Text = $"Прошло: {passed}/{results.Count}");
        }

        private static async Task<(bool Ok, long Ms)> CheckUrlAsync(string url)
        {
            try
            {
                var sw   = Stopwatch.StartNew();
                var resp = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
                sw.Stop();
                return (resp.IsSuccessStatusCode || (int)resp.StatusCode < 500, sw.ElapsedMilliseconds);
            }
            catch { return (false, 0); }
        }

        private void SetTestRunning()
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                RunTestButton.IsEnabled = false;
                StopTestButton.Visibility = Visibility.Visible;
                TestProgressRing.IsActive = true;
                TestStatusText.Text = "Тестирование...";
            });
        }

        private void SetTestIdle()
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                RunTestButton.IsEnabled = true;
                StopTestButton.Visibility = Visibility.Collapsed;
                TestProgressRing.IsActive = false;
                TestStatusText.Text = "Готово";
                HideInputPanel();
                TestScrollViewer.ChangeView(null, TestScrollViewer.ScrollableHeight, null);
            });
        }

        private void AppendTest(string text) =>
            DispatcherQueue.TryEnqueue(() => TestOutputBox.Text += text + "\n");

        // ══════════════════════════════════════════════════════════════
        // ДИАГНОСТИКА
        // ══════════════════════════════════════════════════════════════

        private record CheckResult(string Name, bool Ok, string Detail);

        private void RunDiagnostics_Click(object sender, RoutedEventArgs e) => _ = RunDiagnosticsAsync();

        private async Task RunDiagnosticsAsync()
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                ChecksList.Children.Clear();
                SummaryBar.IsOpen = false;
                ChecksList.Children.Add(new ProgressRing { IsActive = true, Width = 32, Height = 32, Margin = new Thickness(16) });
            });

            var results = await Task.Run(RunAllChecks);

            DispatcherQueue.TryEnqueue(() =>
            {
                ChecksList.Children.Clear();
                int passed = 0;
                foreach (var r in results) { if (r.Ok) passed++; ChecksList.Children.Add(BuildCheckRow(r)); }
                SummaryBar.IsOpen = true;
                if (passed == results.Count)
                {
                    SummaryBar.Severity = InfoBarSeverity.Success;
                    SummaryBar.Title = $"Все проверки пройдены ({passed}/{results.Count})";
                }
                else
                {
                    SummaryBar.Severity = InfoBarSeverity.Warning;
                    SummaryBar.Title = $"Пройдено {passed} из {results.Count} проверок";
                }
            });
        }

        private static List<CheckResult> RunAllChecks()
        {
            var r = new List<CheckResult>();
            FileCheck(r, "winws.exe",       ZapretPaths.WinwsExe);
            FileCheck(r, "WinDivert.dll",   Path.Combine(ZapretPaths.WinwsDir, "WinDivert.dll"));
            FileCheck(r, "WinDivert64.sys", Path.Combine(ZapretPaths.WinwsDir, "WinDivert64.sys"));
            FileCheck(r, "cygwin1.dll",     Path.Combine(ZapretPaths.WinwsDir, "cygwin1.dll"));

            var lg = Path.Combine(ZapretPaths.ListsDir, "list-general.txt");
            r.Add(new CheckResult("list-general.txt", File.Exists(lg),
                File.Exists(lg) ? $"{CountLines(lg)} доменов" : $"Не найден: {lg}"));

            var ip = Path.Combine(ZapretPaths.ListsDir, "ipset-all.txt");
            r.Add(new CheckResult("ipset-all.txt", File.Exists(ip),
                File.Exists(ip) ? $"{CountLines(ip)} записей" : $"Не найден: {ip}"));

            FileCheck(r, "list-exclude.txt", Path.Combine(ZapretPaths.ListsDir, "list-exclude.txt"));

            var bats = Directory.Exists(ZapretPaths.StrategiesDir)
                ? Directory.GetFiles(ZapretPaths.StrategiesDir, "*.bat")
                : Array.Empty<string>();
            r.Add(new CheckResult("Стратегии (.bat)", bats.Length > 0,
                bats.Length > 0 ? $"{bats.Length} стратегий найдено" : $"Не найдены: {ZapretPaths.StrategiesDir}"));

            var vOk = File.Exists(ZapretPaths.VersionFile);
            r.Add(new CheckResult("version.txt", vOk,
                vOk ? $"Версия zapret: {File.ReadAllText(ZapretPaths.VersionFile).Trim()}" : $"Не найден: {ZapretPaths.VersionFile}"));

            bool isAdmin = false;
            try
            {
                var id = System.Security.Principal.WindowsIdentity.GetCurrent();
                isAdmin = new System.Security.Principal.WindowsPrincipal(id)
                    .IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
            }
            catch { }
            r.Add(new CheckResult("Права администратора", isAdmin,
                isAdmin ? "Запущено с правами администратора" : "Требуются права администратора"));

            return r;
        }

        private static void FileCheck(List<CheckResult> list, string name, string path) =>
            list.Add(new CheckResult(name, File.Exists(path),
                File.Exists(path) ? path : $"Не найден: {path}"));

        private static int CountLines(string path)
        {
            try { int c = 0; foreach (var _ in File.ReadLines(path)) c++; return c; }
            catch { return 0; }
        }

        private UIElement BuildCheckRow(CheckResult result)
        {
            var border = new Border
            {
                CornerRadius = new CornerRadius(8),
                Padding      = new Thickness(16, 12, 16, 12),
                Background   = new SolidColorBrush(result.Ok
                    ? Color.FromArgb(15, 0, 200, 0)
                    : Color.FromArgb(15, 200, 0, 0))
            };
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var icon = new FontIcon
            {
                Glyph = result.Ok ? "\uE73E" : "\uE783", FontSize = 16,
                Foreground = new SolidColorBrush(result.Ok
                    ? Color.FromArgb(255, 0, 180, 0)
                    : Color.FromArgb(255, 200, 50, 50)),
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(icon, 0);

            var name = new TextBlock
            {
                Text = result.Name,
                Style = (Style)Application.Current.Resources["BodyStrongTextBlockStyle"],
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(name, 1);

            var detail = new TextBlock
            {
                Text = result.Detail, Opacity = 0.6,
                Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"],
                VerticalAlignment = VerticalAlignment.Center,
                TextAlignment = TextAlignment.Right,
                MaxWidth = 380, TextTrimming = TextTrimming.CharacterEllipsis
            };
            Grid.SetColumn(detail, 2);

            grid.Children.Add(icon); grid.Children.Add(name); grid.Children.Add(detail);
            border.Child = grid;
            ToolTipService.SetToolTip(border, result.Detail);
            return border;
        }

        // ══════════════════════════════════════════════════════════════
        // ОБНОВЛЕНИЯ
        // ══════════════════════════════════════════════════════════════

        private void AppendUpdateLog(string text)
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                UpdateLogText.Text += text + "\n";
                UpdateLogScrollViewer.ScrollToVerticalOffset(double.MaxValue);
            });
        }

        private void SetUpdateButtonsEnabled(bool enabled)
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                CheckVersionButton.IsEnabled  = enabled;
                UpdateIpsetButton.IsEnabled   = enabled;
                UpdateHostsButton.IsEnabled   = enabled;
            });
        }

        private async Task CheckVersionSilentAsync()
        {
            try
            {
                var latest = (await _http.GetStringAsync(VersionUrl)).Trim();
                var local  = ZapretPaths.LocalVersion;
                DispatcherQueue.TryEnqueue(() =>
                    VersionStatusText.Text = local == latest
                        ? $"Установлена актуальная версия: {local}"
                        : $"Доступна новая версия: {latest} (установлена: {local})");
            }
            catch
            {
                DispatcherQueue.TryEnqueue(() =>
                    VersionStatusText.Text = $"Версия: {ZapretPaths.LocalVersion} (нет подключения)");
            }
        }

        private async void CheckVersion_Click(object sender, RoutedEventArgs e)
        {
            SetUpdateButtonsEnabled(false);
            DispatcherQueue.TryEnqueue(() => VersionStatusText.Text = "Проверяется...");
            AppendUpdateLog("Проверяю версию...");

            try
            {
                var latest = (await _http.GetStringAsync(VersionUrl)).Trim();
                var local  = ZapretPaths.LocalVersion;

                if (local == latest)
                {
                    DispatcherQueue.TryEnqueue(() => VersionStatusText.Text = $"Установлена актуальная версия: {local}");
                    AppendUpdateLog($"✓ Версия актуальна: {local}");
                }
                else
                {
                    DispatcherQueue.TryEnqueue(() =>
                        VersionStatusText.Text = $"Доступна новая версия: {latest} (установлена: {local})");
                    AppendUpdateLog($"↑ Доступна новая версия: {latest}");

                    DispatcherQueue.TryEnqueue(async () =>
                    {
                        var dialog = new ContentDialog
                        {
                            Title             = "Доступно обновление",
                            Content           = $"Новая версия: {latest}\nУстановленная: {local}\n\nСкачать и установить?",
                            PrimaryButtonText = "Обновить",
                            CloseButtonText   = "Позже",
                            XamlRoot          = this.XamlRoot
                        };
                        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
                            await DownloadAndInstallUpdateAsync(latest);
                    });
                }
            }
            catch (Exception ex)
            {
                DispatcherQueue.TryEnqueue(() => VersionStatusText.Text = "Ошибка проверки версии");
                AppendUpdateLog($"✗ Ошибка: {ex.Message}");
            }

            SetUpdateButtonsEnabled(true);
        }

        private async Task DownloadAndInstallUpdateAsync(string version)
        {
            SetUpdateButtonsEnabled(false);
            AppendUpdateLog($"\n--- Обновление до версии {version} ---");

            try
            {
                AppendUpdateLog("Получаю информацию о релизе...");
                var json = await _http.GetStringAsync(ApiReleasesUrl);
                using var doc = JsonDocument.Parse(json);
                var assets = doc.RootElement.GetProperty("assets");

                string? downloadUrl = null, assetName = null;
                foreach (var asset in assets.EnumerateArray())
                {
                    var n = asset.GetProperty("name").GetString() ?? "";
                    if (n.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                    {
                        downloadUrl = asset.GetProperty("browser_download_url").GetString();
                        assetName = n;
                        break;
                    }
                }

                if (downloadUrl == null) { AppendUpdateLog("✗ Архив не найден в релизе"); SetUpdateButtonsEnabled(true); return; }
                AppendUpdateLog($"Архив: {assetName}");

                var tempZip = Path.Combine(Path.GetTempPath(), $"zapret_{version}.zip");
                AppendUpdateLog("Скачиваю...");

                DispatcherQueue.TryEnqueue(() =>
                {
                    UpdateProgressCard.Visibility  = Visibility.Visible;
                    UpdateProgressBar.Value        = 0;
                    UpdateProgressLabel.Text       = "Скачивание обновления...";
                    UpdateProgressPct.Text         = "0%";
                    UpdateProgressDetail.Text      = "";
                });

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
                                var mb      = downloaded / 1024.0 / 1024.0;
                                var totalMb = total / 1024.0 / 1024.0;
                                DispatcherQueue.TryEnqueue(() =>
                                {
                                    UpdateProgressBar.Value   = pct;
                                    UpdateProgressPct.Text    = $"{pct}%";
                                    UpdateProgressDetail.Text = $"{mb:F1} МБ / {totalMb:F1} МБ";
                                    VersionStatusText.Text    = $"Скачивание: {pct}%";
                                });
                            }
                        }
                    }
                }

                AppendUpdateLog("✓ Архив скачан");
                AppendUpdateLog("Распаковываю...");
                DispatcherQueue.TryEnqueue(() =>
                {
                    UpdateProgressLabel.Text  = "Распаковка и установка...";
                    UpdateProgressBar.Value   = 100;
                    UpdateProgressDetail.Text = "Пожалуйста, подождите...";
                });
                var tempExtract = Path.Combine(Path.GetTempPath(), $"zapret_update_{version}");
                if (Directory.Exists(tempExtract)) Directory.Delete(tempExtract, true);
                ZipFile.ExtractToDirectory(tempZip, tempExtract);
                AppendUpdateLog("✓ Распаковано");

                var extractedDirs = Directory.GetDirectories(tempExtract);
                var sourceDir = extractedDirs.Length == 1 ? extractedDirs[0] : tempExtract;

                AppState.WinwsService.Stop();
                await Task.Delay(800);

                AppendUpdateLog("Устанавливаю файлы...");
                await CopyUpdateFilesAsync(sourceDir);
                await File.WriteAllTextAsync(ZapretPaths.VersionFile, version);

                File.Delete(tempZip);
                Directory.Delete(tempExtract, true);

                DispatcherQueue.TryEnqueue(() =>
                {
                    VersionStatusText.Text    = $"✓ Обновлено до версии {version}";
                    UpdateProgressLabel.Text  = $"✓ Обновлено до версии {version}";
                    UpdateProgressDetail.Text = "Установка завершена";
                });
                AppendUpdateLog($"\n✓ Zapret успешно обновлён до версии {version}");
            }
            catch (Exception ex)
            {
                AppendUpdateLog($"✗ Ошибка обновления: {ex.Message}");
                DispatcherQueue.TryEnqueue(() =>
                {
                    VersionStatusText.Text    = "Ошибка обновления";
                    UpdateProgressLabel.Text  = "✗ Ошибка обновления";
                    UpdateProgressDetail.Text = ex.Message;
                });
            }

            SetUpdateButtonsEnabled(true);
        }

        private async Task CopyUpdateFilesAsync(string sourceDir)
        {
            var userFiles = new[] { "ipset-exclude-user.txt", "list-general-user.txt", "list-exclude-user.txt" };
            await Task.Run(() =>
            {
                var srcBin = Path.Combine(sourceDir, "bin");
                if (Directory.Exists(srcBin))
                {
                    Directory.CreateDirectory(ZapretPaths.WinwsDir);
                    foreach (var f in Directory.GetFiles(srcBin))
                        File.Copy(f, Path.Combine(ZapretPaths.WinwsDir, Path.GetFileName(f)), true);
                    AppendUpdateLog("  winws бинарники обновлены");
                }
                Directory.CreateDirectory(ZapretPaths.StrategiesDir);
                foreach (var f in Directory.GetFiles(sourceDir, "*.bat"))
                {
                    if (Path.GetFileName(f).StartsWith("service", StringComparison.OrdinalIgnoreCase)) continue;
                    File.Copy(f, Path.Combine(ZapretPaths.StrategiesDir, Path.GetFileName(f)), true);
                }
                AppendUpdateLog("  стратегии обновлены");

                var srcLists = Path.Combine(sourceDir, "lists");
                if (Directory.Exists(srcLists))
                {
                    Directory.CreateDirectory(ZapretPaths.ListsDir);
                    foreach (var f in Directory.GetFiles(srcLists))
                    {
                        var fname = Path.GetFileName(f);
                        if (Array.Exists(userFiles, u => u.Equals(fname, StringComparison.OrdinalIgnoreCase)))
                        { AppendUpdateLog($"  пропущен: {fname} (пользовательский)"); continue; }
                        File.Copy(f, Path.Combine(ZapretPaths.ListsDir, fname), true);
                    }
                    AppendUpdateLog("  списки обновлены");
                }

                var srcUtils = Path.Combine(sourceDir, "utils");
                if (Directory.Exists(srcUtils))
                {
                    Directory.CreateDirectory(ZapretPaths.UtilsDir);
                    foreach (var f in Directory.GetFiles(srcUtils))
                        File.Copy(f, Path.Combine(ZapretPaths.UtilsDir, Path.GetFileName(f)), true);
                    AppendUpdateLog("  utils обновлены");
                }
            });
        }

        private async void UpdateIpset_Click(object sender, RoutedEventArgs e)
        {
            SetUpdateButtonsEnabled(false);
            DispatcherQueue.TryEnqueue(() => IpsetStatusText.Text = "Загружается...");
            AppendUpdateLog("\nОбновляю ipset-all.txt...");
            try
            {
                var content  = await _http.GetStringAsync(IpsetUrl);
                var listFile = Path.Combine(ZapretPaths.ListsDir, "ipset-all.txt");
                Directory.CreateDirectory(ZapretPaths.ListsDir);
                await File.WriteAllTextAsync(listFile, content);
                var lines = content.Split('\n').Length;
                DispatcherQueue.TryEnqueue(() => IpsetStatusText.Text = $"Обновлено: {lines} записей");
                AppendUpdateLog($"✓ ipset-all.txt обновлён ({lines} записей)");
            }
            catch (Exception ex)
            {
                DispatcherQueue.TryEnqueue(() => IpsetStatusText.Text = "Ошибка обновления");
                AppendUpdateLog($"✗ Ошибка: {ex.Message}");
            }
            SetUpdateButtonsEnabled(true);
        }

        private async void UpdateHosts_Click(object sender, RoutedEventArgs e)
        {
            if (!IsRunningAsAdmin())
            {
                var dialog = new ContentDialog
                {
                    Title             = "Требуются права администратора",
                    Content           = "Запись в hosts требует прав администратора.\nПерезапустить Z-UI от имени администратора?",
                    PrimaryButtonText = "Перезапустить",
                    CloseButtonText   = "Отмена",
                    XamlRoot          = this.XamlRoot
                };
                if (await dialog.ShowAsync() == ContentDialogResult.Primary)
                    RestartAsAdmin();
                return;
            }

            SetUpdateButtonsEnabled(false);
            AppendUpdateLog("\nОбновление hosts...");
            await ApplyHostsAsync(silent: false);
            SetUpdateButtonsEnabled(true);
        }
    }
}
