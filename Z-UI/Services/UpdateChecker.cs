using System;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using ZUI.Services;

namespace ZUI.Services
{
    /// <summary>
    /// Статический сервис проверки обновлений. Хранит результат и уведомляет подписчиков.
    /// </summary>
    public static class UpdateChecker
    {
        private const string VersionUrl =
            "https://raw.githubusercontent.com/Flowseal/zapret-discord-youtube/main/.service/version.txt";

        private static readonly HttpClient _http = new()
        {
            Timeout = TimeSpan.FromSeconds(8),
            DefaultRequestHeaders = { { "User-Agent", "ZUI" } }
        };

        public static string? LatestVersion { get; private set; }
        public static bool UpdateAvailable { get; private set; }

        /// <summary>Срабатывает когда найдено обновление. Передаёт номер новой версии.</summary>
        public static event Action<string>? UpdateFound;

        /// <summary>Проверяет версию в фоне. Вызывать при старте и периодически.</summary>
        public static async Task CheckAsync()
        {
            try
            {
                var latest = (await _http.GetStringAsync(VersionUrl)).Trim();
                LatestVersion = latest;

                var local = ZapretPaths.LocalVersion;
                UpdateAvailable = !string.IsNullOrEmpty(latest) && latest != local && local != "неизвестно";

                if (UpdateAvailable)
                    UpdateFound?.Invoke(latest);
            }
            catch
            {
                // нет сети — не страшно
            }
        }
    }
}
