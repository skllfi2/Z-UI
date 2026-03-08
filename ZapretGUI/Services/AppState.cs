using System.Collections.Generic;
using ZapretGUI.Services;

namespace ZapretGUI
{
    public static class AppState
    {
        public static WinwsService WinwsService { get; } = new();
        public static string CurrentStrategy { get; set; } = "General";
        public static List<string> Logs { get; } = new();

        static AppState()
        {
            WinwsService.LogReceived += line =>
            {
                Logs.Add(line);
            };
        }
    }
}