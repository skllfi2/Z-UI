using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace ZUI.Services
{
    public class UpdateService
    {
        private const string RepoOwner = "Flowseal";
        private const string RepoName = "zapret-discord-youtube";
        private const string VersionUrl = $"https://raw.githubusercontent.com/{RepoOwner}/{RepoName}/main/.service/version.txt";
        private const string ReleaseUrl = $"https://api.github.com/repos/{RepoOwner}/{RepoName}/releases/latest";
        private const string ChangelogUrl = $"https://raw.githubusercontent.com/{RepoOwner}/{RepoName}/main/CHANGELOG.md";

        private static readonly HttpClient _http = new()
        {
            Timeout = TimeSpan.FromSeconds(30),
        };

        static UpdateService()
        {
            _http.DefaultRequestHeaders.Add("User-Agent", "ZUI-Updater/1.0");
        }

        public event Action<int>? DownloadProgress;
        public event Action<string>? StatusChanged;
        public event Action? UpdateCompleted;
        public event Action<string>? UpdateFailed;

        public bool IsUpdating { get; private set; }
        public int Progress { get; private set; }
        public string Status { get; private set; } = "";
        public string? LatestVersion { get; private set; }
        public string? Changelog { get; private set; }

        public async Task<bool> CheckForUpdatesAsync()
        {
            try
            {
                var latest = await _http.GetStringAsync(VersionUrl);
                LatestVersion = latest.Trim();
                
                var current = ZapretPaths.LocalVersion;
                return !string.IsNullOrEmpty(LatestVersion) && 
                       LatestVersion != current && 
                       current != "неизвестно";
            }
            catch
            {
                return false;
            }
        }

        public async Task<string?> FetchChangelogAsync(string fromVersion, string toVersion)
        {
            try
            {
                var changelog = await _http.GetStringAsync(ChangelogUrl);
                return ExtractRelevantChangelog(changelog, fromVersion, toVersion);
            }
            catch
            {
                return null;
            }
        }

        private string ExtractRelevantChangelog(string fullChangelog, string fromVersion, string toVersion)
        {
            var lines = fullChangelog.Split('\n');
            var result = new System.Text.StringBuilder();
            var capturing = false;

            foreach (var line in lines)
            {
                if (line.StartsWith("## "))
                {
                    var version = line.TrimStart('#', ' ').Trim();
                    if (version == fromVersion)
                        break;
                    capturing = true;
                }

                if (capturing)
                    result.AppendLine(line);
            }

            return result.ToString();
        }

        public async Task<bool> DownloadAndInstallAsync(CancellationToken cancellationToken = default)
        {
            if (IsUpdating) return false;

            IsUpdating = true;
            Progress = 0;
            
            try
            {
                StatusChanged?.Invoke("Подключение к серверу...");
                Status = "Подключение к серверу...";

                var releaseInfo = await GetLatestReleaseInfoAsync();
                if (releaseInfo == null || string.IsNullOrEmpty(releaseInfo.DownloadUrl))
                {
                    UpdateFailed?.Invoke("Не удалось получить информацию о релизе");
                    return false;
                }

                var tempFile = Path.Combine(Path.GetTempPath(), $"zapret-{LatestVersion}.zip");
                var extractPath = Path.Combine(Path.GetTempPath(), $"zapret-extract-{Guid.NewGuid()}");

                StatusChanged?.Invoke("Скачивание обновления...");
                Status = "Скачивание обновления...";

                using (var response = await _http.GetAsync(releaseInfo.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken))
                {
                    response.EnsureSuccessStatusCode();

                    var totalBytes = response.Content.Headers.ContentLength ?? -1L;
                    using var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken);
                    using var fileStream = new FileStream(tempFile, FileMode.Create, FileAccess.Write, FileShare.None);

                    var buffer = new byte[8192];
                    var totalBytesRead = 0L;
                    int bytesRead;

                    while ((bytesRead = await contentStream.ReadAsync(buffer, cancellationToken)) > 0)
                    {
                        await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
                        totalBytesRead += bytesRead;

                        if (totalBytes > 0)
                        {
                            var progress = (int)((totalBytesRead * 100) / totalBytes);
                            if (progress != Progress)
                            {
                                Progress = progress;
                                DownloadProgress?.Invoke(progress);
                            }
                        }
                    }
                }

                StatusChanged?.Invoke("Распаковка...");
                Status = "Распаковка...";
                Progress = 0;

                Directory.CreateDirectory(extractPath);
                ZipFile.ExtractToDirectory(tempFile, extractPath);

                StatusChanged?.Invoke("Установка...");
                Status = "Установка...";

                var zapretDir = ZapretPaths.ZapretDir;
                if (Directory.Exists(zapretDir))
                {
                    var backupDir = zapretDir + ".backup";
                    if (Directory.Exists(backupDir))
                        Directory.Delete(backupDir, true);
                    Directory.Move(zapretDir, backupDir);
                }

                var extractedZapret = FindZapretFolder(extractPath);
                if (extractedZapret != null)
                {
                    Directory.Move(extractedZapret, zapretDir);
                }
                else
                {
                    throw new DirectoryNotFoundException("Не найдена папка zapret в архиве");
                }

                File.Delete(tempFile);
                if (Directory.Exists(extractPath))
                    Directory.Delete(extractPath, true);

                var backupDirFinal = zapretDir + ".backup";
                if (Directory.Exists(backupDirFinal))
                    Directory.Delete(backupDirFinal, true);

                StatusChanged?.Invoke("Обновление завершено");
                Status = "Обновление завершено";
                Progress = 100;
                UpdateCompleted?.Invoke();

                return true;
            }
            catch (OperationCanceledException)
            {
                StatusChanged?.Invoke("Обновление отменено");
                Status = "Обновление отменено";
                return false;
            }
            catch (Exception ex)
            {
                StatusChanged?.Invoke($"Ошибка: {ex.Message}");
                Status = $"Ошибка: {ex.Message}";
                UpdateFailed?.Invoke(ex.Message);
                return false;
            }
            finally
            {
                IsUpdating = false;
            }
        }

        private async Task<ReleaseInfo?> GetLatestReleaseInfoAsync()
        {
            try
            {
                var json = await _http.GetStringAsync(ReleaseUrl);
                var release = System.Text.Json.JsonSerializer.Deserialize<GithubRelease>(json);
                
                if (release?.Assets != null)
                {
                    foreach (var asset in release.Assets)
                    {
                        if (asset.Name.Contains("zapret", StringComparison.OrdinalIgnoreCase) &&
                            asset.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) &&
                            asset.BrowserDownloadUrl != null)
                        {
                            return new ReleaseInfo
                            {
                                TagName = release.TagName ?? "",
                                DownloadUrl = asset.BrowserDownloadUrl,
                                ReleaseNotes = release.Body ?? ""
                            };
                        }
                    }
                }
            }
            catch { }

            return null;
        }

        private string? FindZapretFolder(string extractPath)
        {
            var dirs = Directory.GetDirectories(extractPath);
            foreach (var dir in dirs)
            {
                if (Path.GetFileName(dir).Contains("zapret", StringComparison.OrdinalIgnoreCase))
                    return dir;

                var subdirs = Directory.GetDirectories(dir);
                foreach (var subdir in subdirs)
                {
                    if (Path.GetFileName(subdir).Contains("zapret", StringComparison.OrdinalIgnoreCase))
                        return subdir;
                }
            }
            return null;
        }

        private class ReleaseInfo
        {
            public string TagName { get; set; } = "";
            public string DownloadUrl { get; set; } = "";
            public string ReleaseNotes { get; set; } = "";
        }

        private class GithubRelease
        {
            public string? TagName { get; set; }
            public string? Body { get; set; }
            public GithubAsset[]? Assets { get; set; }
        }

        private class GithubAsset
        {
            public string? Name { get; set; }
            public string? BrowserDownloadUrl { get; set; }
        }
    }
}
