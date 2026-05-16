// BatStrategyParser.cs - Parse .bat strategy files and extract winws.exe arguments
using System.IO;
using System.Text.RegularExpressions;
using ZUI;

namespace ZUI.Services;

/// <summary>
/// Parses zapret .bat strategy files to extract winws.exe command-line arguments.
/// Replaces %BIN% and %LISTS% variables with actual paths from ZapretPaths.
/// </summary>
public static class BatStrategyParser
{
    /// <summary>
    /// Parse a .bat strategy file and extract the winws.exe arguments.
    /// Replaces %BIN% with the winws directory and %LISTS% with the lists directory.
    /// </summary>
    /// <param name="batFilePath">Full path to the .bat strategy file.</param>
    /// <returns>The winws.exe arguments string, or null if parsing failed.</returns>
    public static string? ParseStrategy(string batFilePath)
    {
        try
        {
            if (!File.Exists(batFilePath)) return null;

            var content = File.ReadAllText(batFilePath);

            // Find the line containing winws.exe
            string? winwsLine = null;
            foreach (var line in content.Split('\n'))
            {
                var trimmed = line.Trim();
                if (trimmed.Contains("winws.exe", StringComparison.OrdinalIgnoreCase) ||
                    trimmed.Contains("%BIN%", StringComparison.OrdinalIgnoreCase))
                {
                    winwsLine = trimmed;
                    break;
                }
            }

            if (winwsLine == null) return null;

            // Replace variables with actual paths
            var binPath = ZapretPaths.WinwsDir + "\\";
            var listsPath = ZapretPaths.ListsDir + "\\";

            winwsLine = winwsLine
                .Replace("%BIN%", binPath)
                .Replace("%LISTS%", listsPath);

            // Extract just the arguments (everything after winws.exe)
            var exeMatch = Regex.Match(winwsLine, @"winws\.exe\s+(.*)", RegexOptions.IgnoreCase);
            return exeMatch.Success ? exeMatch.Groups[1].Value.Trim() : winwsLine;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Z-UI] BatStrategyParser.ParseStrategy failed: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Get the current ipset mode from the active strategy.
    /// </summary>
    /// <returns>"any", "loaded", or "none"</returns>
    public static string GetCurrentIpsetMode()
    {
        try
        {
            var strategy = AppSettings.CurrentStrategy;
            var batFile = Path.Combine(ZapretPaths.StrategiesDir, strategy + ".bat");

            if (!File.Exists(batFile)) return "any";

            var content = File.ReadAllText(batFile);

            if (content.Contains("--ipset=", StringComparison.OrdinalIgnoreCase))
                return "loaded";
            if (content.Contains("any", StringComparison.OrdinalIgnoreCase))
                return "any";

            return "any";
        }
        catch
        {
            return "any";
        }
    }

    /// <summary>
    /// Apply an ipset filter mode to the active strategy .bat file.
    /// Mode "any" disables the filter; other modes (russia, ukraine, custom) set a specific list file.
    /// </summary>
    public static void ApplyIpsetFilter(string mode)
    {
        try
        {
            var strategy = AppSettings.CurrentStrategy;
            var batFile = Path.Combine(ZapretPaths.StrategiesDir, strategy + ".bat");

            if (!File.Exists(batFile))
            {
                System.Diagnostics.Debug.WriteLine($"[Z-UI] BatStrategyParser.ApplyIpsetFilter: Strategy file not found: {batFile}");
                return;
            }

            // Read the .bat file content
            // Use UTF-8 with BOM encoding for reading (standard for .bat files in this project)
            var content = File.ReadAllText(batFile);
            var lines = content.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
            var modified = false;

            for (var i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                var trimmed = line.Trim();

                // Look for lines containing --ipset or --ipset= in the winws.exe command
                if (trimmed.Contains("--ipset", StringComparison.OrdinalIgnoreCase) &&
                    (trimmed.Contains("winws", StringComparison.OrdinalIgnoreCase) ||
                     trimmed.Contains("%BIN%", StringComparison.OrdinalIgnoreCase)))
                {
                    if (string.Equals(mode, "any", StringComparison.OrdinalIgnoreCase))
                    {
                        // Remove the --ipset argument entirely
                        // Handle both --ipset=list-file and --ipset list-file forms
                        lines[i] = System.Text.RegularExpressions.Regex.Replace(
                            line,
                            @"\s*--ipset=\S+",
                            "",
                            System.Text.RegularExpressions.RegexOptions.IgnoreCase);

                        lines[i] = System.Text.RegularExpressions.Regex.Replace(
                            lines[i],
                            @"\s*--ipset\s+\S+",
                            "",
                            System.Text.RegularExpressions.RegexOptions.IgnoreCase);

                        modified = true;
                    }
                    else
                    {
                        // Determine the list file based on mode
                        var listFile = mode.ToLowerInvariant() switch
                        {
                            "russia" => "list-russia.txt",
                            "ukraine" => "list-ukraine.txt",
                            "custom" => "list-custom.txt",
                            _ => $"list-{mode}.txt"
                        };

                        var listPath = $"%LISTS%\\{listFile}";

                        // Replace --ipset=<old> with --ipset=<new>
                        if (System.Text.RegularExpressions.Regex.IsMatch(lines[i], @"--ipset=\S+", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                        {
                            lines[i] = System.Text.RegularExpressions.Regex.Replace(
                                lines[i],
                                @"--ipset=\S+",
                                $"--ipset={listPath}",
                                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                        }
                        // Replace --ipset <old> with --ipset <new>
                        else if (System.Text.RegularExpressions.Regex.IsMatch(lines[i], @"--ipset\s+\S+", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                        {
                            lines[i] = System.Text.RegularExpressions.Regex.Replace(
                                lines[i],
                                @"(--ipset)\s+\S+",
                                $"$1 {listPath}",
                                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                        }

                        modified = true;
                    }
                }
            }

            if (modified)
            {
                // Write back with UTF-8 BOM encoding (CRITICAL per project conventions)
                var newContent = string.Join(Environment.NewLine, lines);
                var bom = new byte[] { 0xEF, 0xBB, 0xBF };
                var utf8Bytes = System.Text.Encoding.UTF8.GetBytes(newContent);
                var fullPath = Path.GetFullPath(batFile);
                File.WriteAllBytes(fullPath, [.. bom, .. utf8Bytes]);

                System.Diagnostics.Debug.WriteLine($"[Z-UI] BatStrategyParser.ApplyIpsetFilter: Applied mode='{mode}' to {strategy}.bat");
            }
            else
            {
                System.Diagnostics.Debug.WriteLine($"[Z-UI] BatStrategyParser.ApplyIpsetFilter: No --ipset found in {strategy}.bat, mode={mode}");
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            System.Diagnostics.Debug.WriteLine($"[Z-UI] BatStrategyParser.ApplyIpsetFilter failed: {ex.Message}");
        }
    }
}
