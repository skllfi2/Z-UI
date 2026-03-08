using System;
using System.IO;
using System.Text.RegularExpressions;

namespace ZapretGUI.Services
{
    public static class BatStrategyParser
    {
        public static string? ExtractArguments(string batFilePath, string binPath, string listsPath)
        {
            if (!File.Exists(batFilePath)) return null;

            var content = File.ReadAllText(batFilePath);

            // ищем строку запуска winws.exe
            var match = Regex.Match(content,
                @"start\s+""[^""]*""\s+/min\s+""[^""]*winws\.exe""\s+([\s\S]+?)(?=\r?\n\r?\n|\z)",
                RegexOptions.IgnoreCase);

            if (!match.Success) return null;

            var args = match.Groups[1].Value;

            // убираем переносы строк с ^
            args = Regex.Replace(args, @"\s*\^\s*\r?\n\s*", " ");

            // заменяем пути
            args = args.Replace("%BIN%", binPath)
                       .Replace("%LISTS%", listsPath);

            // убираем ,% GameFilterTCP% и ,%GameFilterUDP% (в конце списка портов)
            args = Regex.Replace(args, @",\s*%GameFilterTCP%", "");
            args = Regex.Replace(args, @",\s*%GameFilterUDP%", "");

            // убираем --filter-tcp=%GameFilterTCP% и --filter-udp=%GameFilterUDP% (отдельные фильтры)
            args = Regex.Replace(args, @"--filter-tcp=%GameFilterTCP%\s*", "");
            args = Regex.Replace(args, @"--filter-udp=%GameFilterUDP%\s*", "");

            // убираем оставшиеся %GameFilterTCP% и %GameFilterUDP%
            args = args.Replace("%GameFilterTCP%", "")
                       .Replace("%GameFilterUDP%", "");

            // убираем --new в конце если остался без фильтра
            args = Regex.Replace(args, @"--new\s*$", "");

            args = args.Trim();
            args = Regex.Replace(args, @"\s+", " ");

            return args;
        }
    }
}