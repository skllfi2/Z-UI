using System;
using System.IO;

namespace ZUI
{
    public static class ZapretPaths
    {
        public static string AppDir => AppContext.BaseDirectory;
        public static string RootDir => AppDir; // Совместимость с устаревшим кодом
        public static string ZapretDir => Path.Combine(AppDir, "zapret");
        public static string WinwsDir => Path.Combine(StrategiesDir, "bin");
        public static string WinwsExe => Path.Combine(WinwsDir, "winws.exe");
        public static string ListsDir => Path.Combine(StrategiesDir, "lists");
        public static string StrategiesDir => Path.Combine(ZapretDir, "strategies");
        public static string UtilsDir => Path.Combine(ZapretDir, "utils");
        public static string VersionFile => Path.Combine(ZapretDir, "version.txt");
        public static string LocalVersionFile => VersionFile;
        public static string RemoteVersionUrl => "https://raw.githubusercontent.com/bol-van/zapret-winws/refs/heads/master/version.txt";

        public static string LocalVersion
        {
            get
            {
                try
                {
                    if (File.Exists(VersionFile))
                        return File.ReadAllText(VersionFile).Trim();
                }
                catch { }
                return "неизвестно";
            }
        }
    }
}
