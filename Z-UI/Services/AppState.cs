using System.Collections.Generic;
using ZUI.Services;

namespace ZUI
{
    /// <summary>
    /// Представляет глобальное состояние приложения Z-UI.
    /// Содержит сервисы, логи, текущую стратегию и другую состоятельную информацию приложения.
    /// </summary>
    public static class AppState
    {
        /// <summary>
        /// Получает сервис управления Winws процессом.
        /// Этот сервис отвечает за статусом работы Winws процесса и получает события о событиях.
        /// </summary>
        public static WinwsService WinwsService { get; } = new();

        /// <summary>
        /// Получает или задает текущую выбранную стратегию.
        /// Стратегия определяет конфигурационные параметры для zapret процесса.
        /// </summary>
        public static string CurrentStrategy { get; set; } = "General";

        /// <summary>
        /// Получает коллекцию логов приложения.
        /// Каждая запись в этой коллекции относится к событию в приложении.
        /// </summary>
        public static List<string> Logs { get; } = new();

        /// <summary>
        /// Статический конструктор инициализации состояния приложения.
        /// Подписывается на события сервиса логирования и инициализирует логирование событий.
        /// </summary>
        static AppState()
        {
            WinwsService.LogReceived += line =>
            {
                Logs.Add(line);
            };
        }
    }
}