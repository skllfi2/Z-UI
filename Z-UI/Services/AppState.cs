using System.Collections.Generic;
using ZUI.Services;

namespace ZUI
{
    /// <summary>
    /// Представляет глобальное состояние приложения Z-UI.
    /// Содержит логи, текущую стратегию и другую состоятельную информацию приложения.
    /// </summary>
    public static class AppState
    {
        /// <summary>
        /// Получает или задает текущую выбранную стратегию.
        /// Стратегия определяет конфигурационные параметры для zapret процесса.
        /// </summary>
        public static string CurrentStrategy
        {
            get => AppSettings.CurrentStrategy;
            set { AppSettings.CurrentStrategy = value; AppSettings.Save(); }
        }

        /// <summary>
        /// Получает коллекцию логов приложения.
        /// Каждая запись в этой коллекции относится к событию в приложении.
        /// </summary>
        public static List<string> Logs { get; } = new();
    }
}
