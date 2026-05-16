// ═══════════════════════════════════════════════════════════════
// ZUI.Core / Rules / BatStrategyConverter.cs
// Конвертер BAT-стратегий zapret → StrategyConfig
// Парсит winws.exe командную строку из BAT файлов
// ═══════════════════════════════════════════════════════════════

using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace ZUI.Core.Rules;

/// <summary>
/// Конвертер BAT-стратегий zapret в StrategyConfig.
/// Парсит командную строку winws.exe из BAT файлов.
/// Поддерживает все параметры --filter-*, --dpi-desync-*.
/// </summary>
public sealed class BatStrategyConverter
{
    private readonly ILogger _logger;

    public BatStrategyConverter(ILogger<BatStrategyConverter>? logger = null)
    {
        _logger = logger ?? NullLoggerFactory.Instance.CreateLogger<BatStrategyConverter>();
    }

    /// <summary>
    /// Конвертировать BAT-файл стратегии в StrategyConfig.
    /// </summary>
    public Result<StrategyConfig> Convert(string batFilePath)
    {
        if (!File.Exists(batFilePath))
            return Result<StrategyConfig>.Failed($"BAT file not found: {batFilePath}");

        try
        {
            var content = File.ReadAllText(batFilePath, Encoding.UTF8);
            return ParseBatContent(content, batFilePath);
        }
        catch (IOException ex)
        {
            return Result<StrategyConfig>.Failed($"Failed to read BAT file: {ex.Message}");
        }
        catch (UnauthorizedAccessException ex)
        {
            return Result<StrategyConfig>.Failed($"Failed to read BAT file: {ex.Message}");
        }
    }

    /// <summary>
    /// Конвертировать содержимое BAT-файла в StrategyConfig.
    /// </summary>
    public Result<StrategyConfig> ParseBatContent(string content, string? sourceFile = null)
    {
        // 1. Извлечь командную строку winws.exe
        var cmdlineResult = ExtractWinwsCommandLine(content);
        if (!cmdlineResult.IsSuccess)
            return Result<StrategyConfig>.Failed(cmdlineResult.Error!);

        string cmdline = cmdlineResult.Value;

        // 2. Токенизировать параметры
        var tokens = Tokenize(cmdline);

        // 3. Извлечь --wf-tcp и --wf-udp (WinDivert filter)
        var wfTcpPorts = ExtractWfPorts(tokens, "--wf-tcp");
        var wfUdpPorts = ExtractWfPorts(tokens, "--wf-udp");

        // 4. Разбить на правила по --new
        var ruleTokensList = SplitByNew(tokens);

        // 5. Парсим каждое правило
        var rules = new List<FilterRule>();
        for (int i = 0; i < ruleTokensList.Count; i++)
        {
            var rule = ParseRule(ruleTokensList[i], i);
            if (rule is not null)
                rules.Add(rule);
        }

        // 6. Фильтр из wf портов (если нет явно заданного)
        string? winDivertFilter = null;
        if (wfTcpPorts.Count > 0 || wfUdpPorts.Count > 0)
        {
            winDivertFilter = FilterStringBuilder.BuildFromPorts(
                wfTcpPorts.ToArray(), wfUdpPorts.ToArray());
        }

        // 7. Определяем GameFilter
        var gameFilter = DetectGameFilter(tokens);

        var name = sourceFile is not null
            ? Path.GetFileNameWithoutExtension(sourceFile)
            : "imported-strategy";

        return Result<StrategyConfig>.Success(new StrategyConfig
        {
            Id = name.ToLowerInvariant().Replace(' ', '-'),
            Name = name,
            WinDivertFilter = winDivertFilter ?? string.Empty,
            Rules = rules.ToArray(),
            GameFilter = gameFilter,
            SourceBatFile = sourceFile,
            IsEnabled = true,
        });
    }

    // ── Извлечение командной строки winws ───────────────────

    private Result<string> ExtractWinwsCommandLine(string content)
    {
        // Ищем строку с winws.exe
        // Формат: start "..." /min "%BIN%winws.exe" ... ^
        // Многострочная: продолжение с ^ в конце

        var lines = content.Split('\n');
        var winwsLines = new List<string>();
        bool inWinwsBlock = false;

        foreach (var line in lines)
        {
            var trimmed = line.Trim('\r', ' ');

            if (trimmed.Contains("winws.exe", StringComparison.OrdinalIgnoreCase))
            {
                inWinwsBlock = true;
                // Убираем "start" заголовок и путь до winws.exe
                var winwsIdx = trimmed.IndexOf("winws.exe", StringComparison.OrdinalIgnoreCase);
                var afterExe = trimmed[(winwsIdx + 9)..].TrimStart();
                // Убираем кавычки если есть
                afterExe = afterExe.TrimStart('"');
                winwsLines.Add(afterExe);
                continue;
            }

            if (inWinwsBlock)
            {
                // Строка продолжения (после ^)
                var continued = trimmed.TrimEnd('^', ' ');
                if (continued.Length > 0)
                    winwsLines.Add(continued);

                // Если нет ^ в конце — конец блока
                if (!trimmed.EndsWith('^'))
                    break;
            }
        }

        if (winwsLines.Count == 0)
            return Result<string>.Failed("No winws.exe command line found in BAT file");

        // Объединяем строки (убирая ^ продолжения строк)
        var fullCmd = string.Join(" ", winwsLines);
        // Очищаем лишние пробелы
        fullCmd = Regex.Replace(fullCmd, @"\s+", " ").Trim();

        return Result<string>.Success(fullCmd);
    }

    // ── Токенизация ─────────────────────────────────────────

	private List<string> Tokenize(string cmdline)
	{
		var tokens = new List<string>();
		var current = new StringBuilder();
		bool inQuotes = false;

		for (int i = 0; i < cmdline.Length; i++)
		{
			char c = cmdline[i];

			if (c == '"')
			{
				inQuotes = !inQuotes;
				continue; // Пропускаем кавычки
			}

			if (c == ' ' && !inQuotes)
			{
				if (current.Length > 0)
				{
					// Разделить токен по = если это --flag=value
					var token = current.ToString();
					var eqIdx = token.IndexOf('=');
					if (eqIdx > 0 && token.StartsWith("--"))
					{
						tokens.Add(token[..eqIdx]);
						tokens.Add(token[(eqIdx + 1)..]);
					}
					else
					{
						tokens.Add(token);
					}
					current.Clear();
				}
				continue;
			}

			// Пропускаем ^ (BAT escape)
			if (c == '^')
				continue;

			current.Append(c);
		}

		if (current.Length > 0)
		{
			var token = current.ToString();
			var eqIdx = token.IndexOf('=');
			if (eqIdx > 0 && token.StartsWith("--"))
			{
				tokens.Add(token[..eqIdx]);
				tokens.Add(token[(eqIdx + 1)..]);
			}
			else
			{
				tokens.Add(token);
			}
		}

		return tokens;
	}

    // ── --wf-tcp / --wf-udp ─────────────────────────────────

    private List<ushort> ExtractWfPorts(List<string> tokens, string flag)
    {
        var ports = new List<ushort>();

        for (int i = 0; i < tokens.Count - 1; i++)
        {
            if (tokens[i].Equals(flag, StringComparison.OrdinalIgnoreCase))
            {
                var portStr = tokens[i + 1].TrimEnd(',');
                foreach (var part in portStr.Split(','))
                {
                    var trimmed = part.Trim();
                    // Пропускаем переменные %GameFilterTCP% и т.д.
                    if (trimmed.StartsWith('%'))
                        continue;
                    if (ushort.TryParse(trimmed, out var port))
                        ports.Add(port);
                }
            }
        }

        return ports;
    }

    // ── Разделение по --new ──────────────────────────────────

    private List<List<string>> SplitByNew(List<string> tokens)
    {
        var result = new List<List<string>>();
        var current = new List<string>();

        foreach (var token in tokens)
        {
            if (token.Equals("--new", StringComparison.OrdinalIgnoreCase))
            {
                if (current.Count > 0)
                    result.Add(current);
                current = new List<string>();
                continue;
            }

            current.Add(token);
        }

        if (current.Count > 0)
            result.Add(current);

        return result;
    }

    // ── Парсинг одного правила ───────────────────────────────

	private FilterRule? ParseRule(List<string> tokens, int order)
	{
		if (tokens.Count == 0)
			return null;

		var rule = new FilterRule { Order = order };

		// Сначала определяем протокол и порты (--filter-tcp / --filter-udp)
		for (int i = 0; i < tokens.Count; i++)
		{
			var token = tokens[i];

			if (token.Equals("--filter-tcp", StringComparison.OrdinalIgnoreCase) && i + 1 < tokens.Count)
			{
				rule = rule with { Protocol = FilterProtocol.Tcp, Ports = ParsePorts(tokens[i + 1]) };
				break;
			}

			if (token.Equals("--filter-udp", StringComparison.OrdinalIgnoreCase) && i + 1 < tokens.Count)
			{
				rule = rule with { Protocol = FilterProtocol.Udp, Ports = ParsePorts(tokens[i + 1]) };
				break;
			}
		}

		// Затем парсим все остальные параметры (desync, hostlists, fooling, etc.)
		return ParseRuleDetails(rule, tokens, order);
	}

    private FilterRule ParseRuleDetails(FilterRule rule, List<string> tokens, int order)
    {
        var hostLists = new List<string>();
        var hostDomains = new List<string>();
        var hostExcludeLists = new List<string>();
        var ipsetLists = new List<string>();
        var ipsetExcludeLists = new List<string>();
        var desyncModes = new List<DesyncMode>();
        var fakeTlsFiles = new List<string>();
        var fakeTlsMods = new List<string>();
        var splitPositions = new List<object>();

        string? fakeQuicFile = null;
        string? fakeHttpFile = null;
        string? fakeUnknownUdpFile = null;
        string? fakeSplitPattern = null;
        int fakeRepeats = 0;
        FoolingMode fooling = FoolingMode.None;
        int? splitSeqOvl = null;
        string? splitSeqOvlPattern = null;
        bool anyProtocol = false;
        int? cutoff = null;
        bool ipIdZero = false;
        string[]? l7Protocols = null;

        for (int i = 0; i < tokens.Count; i++)
        {
            var token = tokens[i];

            switch (token.ToLowerInvariant())
            {
                // ── Hostlists ────────────────────────────
                case "--hostlist" when i + 1 < tokens.Count:
                    hostLists.Add(NormalizePath(tokens[++i]));
                    break;

                case "--hostlist-domains" when i + 1 < tokens.Count:
                    foreach (var d in tokens[++i].Split(','))
                        hostDomains.Add(d.Trim());
                    break;

                case "--hostlist-exclude" when i + 1 < tokens.Count:
                    hostExcludeLists.Add(NormalizePath(tokens[++i]));
                    break;

                // ── Ipset ────────────────────────────────
                case "--ipset" when i + 1 < tokens.Count:
                    ipsetLists.Add(NormalizePath(tokens[++i]));
                    break;

                case "--ipset-exclude" when i + 1 < tokens.Count:
                    ipsetExcludeLists.Add(NormalizePath(tokens[++i]));
                    break;

                // ── IP ID ────────────────────────────────
                case "--ip-id" when i + 1 < tokens.Count:
                    ipIdZero = tokens[++i].Equals("zero", StringComparison.OrdinalIgnoreCase);
                    break;

                // ── L7 protocol ──────────────────────────
                case "--filter-l7" when i + 1 < tokens.Count:
                    l7Protocols = tokens[++i].Split(',').Select(s => s.Trim()).ToArray();
                    break;

                // ── Desync modes ─────────────────────────
                case "--dpi-desync" when i + 1 < tokens.Count:
                    desyncModes = ParseDesyncModes(tokens[++i]);
                    break;

                // ── Fake repeats ─────────────────────────
                case "--dpi-desync-repeats" when i + 1 < tokens.Count:
                    int.TryParse(tokens[++i], out fakeRepeats);
                    break;

                // ── Fooling ──────────────────────────────
                case "--dpi-desync-fooling" when i + 1 < tokens.Count:
                    fooling = ParseFoolingMode(tokens[++i]);
                    break;

	// ── Fake TLS ─────────────────────────────
		case "--dpi-desync-fake-tls" when i + 1 < tokens.Count:
		{
			var value = tokens[++i];
			// Специальные значения: 0x... (hex pattern), ! (auto-generate)
			// Не нормализуем как путь
			if (value.StartsWith("0x", StringComparison.OrdinalIgnoreCase) || value == "!")
				fakeTlsFiles.Add(value);
			else
				fakeTlsFiles.Add(NormalizePath(value));
			break;
		}

                // ── Fake TLS mods ────────────────────────
                case "--dpi-desync-fake-tls-mod" when i + 1 < tokens.Count:
                    foreach (var m in tokens[++i].Split(','))
                        fakeTlsMods.Add(m.Trim());
                    break;

                // ── Fake QUIC ────────────────────────────
                case "--dpi-desync-fake-quic" when i + 1 < tokens.Count:
                    fakeQuicFile = NormalizePath(tokens[++i]);
                    break;

                // ── Fake HTTP ────────────────────────────
		case "--dpi-desync-fake-http" when i + 1 < tokens.Count:
			fakeHttpFile = NormalizePath(tokens[++i]);
			break;

                // ── Fake unknown UDP ─────────────────────
                case "--dpi-desync-fake-unknown-udp" when i + 1 < tokens.Count:
                    fakeUnknownUdpFile = NormalizePath(tokens[++i]);
                    break;

                // ── Fakedsplit pattern ───────────────────
                case "--dpi-desync-fakedsplit-pattern" when i + 1 < tokens.Count:
                    fakeSplitPattern = tokens[++i];
                    break;

                // ── Split positions ──────────────────────
                case "--dpi-desync-split-pos" when i + 1 < tokens.Count:
                    foreach (var p in tokens[++i].Split(','))
                    {
                        var trimmed = p.Trim();
                        if (int.TryParse(trimmed, out var intPos))
                            splitPositions.Add(intPos);
                        else
                            splitPositions.Add(trimmed);
                    }
                    break;

                // ── Seq overlap ──────────────────────────
                case "--dpi-desync-split-seqovl" when i + 1 < tokens.Count:
                    splitSeqOvl = int.TryParse(tokens[++i], out var ovl) ? ovl : null;
                    break;

                case "--dpi-desync-split-seqovl-pattern" when i + 1 < tokens.Count:
                    splitSeqOvlPattern = NormalizePath(tokens[++i]);
                    break;

                // ── Any protocol ─────────────────────────
                case "--dpi-desync-any-protocol" when i + 1 < tokens.Count:
                    anyProtocol = tokens[++i] == "1";
                    break;

                // ── Cutoff ───────────────────────────────
                case "--dpi-desync-cutoff" when i + 1 < tokens.Count:
                    cutoff = ParseCutoff(tokens[++i]);
                    break;
            }
        }

        return rule with
        {
            Order = order,
            HostLists = hostLists.Count > 0 ? hostLists.ToArray() : null,
            HostDomains = hostDomains.Count > 0 ? hostDomains.ToArray() : null,
            HostExcludeLists = hostExcludeLists.Count > 0 ? hostExcludeLists.ToArray() : null,
            IpsetLists = ipsetLists.Count > 0 ? ipsetLists.ToArray() : null,
            IpsetExcludeLists = ipsetExcludeLists.Count > 0 ? ipsetExcludeLists.ToArray() : null,
            IpIdZero = ipIdZero,
            L7Protocols = l7Protocols,
            DesyncModes = desyncModes.ToArray(),
            FakeRepeats = fakeRepeats,
            Fooling = fooling,
		FakeTlsFiles = fakeTlsFiles.Count > 0 ? fakeTlsFiles.ToArray() : null,
            FakeTlsMods = fakeTlsMods.Count > 0 ? fakeTlsMods.ToArray() : null,
            FakeQuicFile = fakeQuicFile,
            FakeHttpFile = fakeHttpFile,
            FakeUnknownUdpFile = fakeUnknownUdpFile,
            FakeSplitPattern = fakeSplitPattern,
            SplitPositions = splitPositions.Count > 0 ? splitPositions.ToArray() : null,
            SplitSeqOvl = splitSeqOvl,
            SplitSeqOvlPattern = splitSeqOvlPattern,
            AnyProtocol = anyProtocol,
            Cutoff = cutoff,
        };
    }

    // ── Парсинг портов ──────────────────────────────────────

    private static PortRange[] ParsePorts(string portStr)
    {
        var ports = new List<PortRange>();
        foreach (var part in portStr.TrimEnd(',').Split(','))
        {
            var trimmed = part.Trim();
            if (trimmed.StartsWith('%'))
                continue; // Переменные %GameFilterTCP%

            if (trimmed.Contains('-'))
            {
                var range = trimmed.Split('-');
                if (range.Length == 2 &&
                    ushort.TryParse(range[0], out var start) &&
                    ushort.TryParse(range[1], out var end))
                {
                    ports.Add(new PortRange(start, end));
                }
            }
            else if (ushort.TryParse(trimmed, out var singlePort))
            {
                ports.Add(new PortRange(singlePort));
            }
        }
        return ports.ToArray();
    }

    // ── Парсинг режимов десинхронизации ─────────────────────

    private static List<DesyncMode> ParseDesyncModes(string modesStr)
    {
        var result = new List<DesyncMode>();
        foreach (var mode in modesStr.Split(','))
        {
            var trimmed = mode.Trim().ToLowerInvariant();
            switch (trimmed)
            {
                case "fake":
                    result.Add(DesyncMode.Fake);
                    break;
                case "multisplit":
                    result.Add(DesyncMode.MultiSplit);
                    break;
                case "fakedsplit":
                    result.Add(DesyncMode.FakeSplit);
                    break;
                case "multidisorder":
                    result.Add(DesyncMode.MultiDisorder);
                    break;
            }
        }
        return result;
    }

    // ── Парсинг fooling ─────────────────────────────────────

    private static FoolingMode ParseFoolingMode(string str)
    {
        return str.Trim().ToLowerInvariant() switch
        {
            "ts" => FoolingMode.Ts,
            "badseq" => FoolingMode.BadSeq,
            _ => FoolingMode.None,
        };
    }

    // ── Парсинг cutoff ──────────────────────────────────────

    private static int? ParseCutoff(string str)
    {
        // Формат: n3, n4, n2 → извлечь число
        var trimmed = str.Trim();
        if (trimmed.StartsWith('n') && int.TryParse(trimmed[1..], out var n))
            return n;
        if (int.TryParse(trimmed, out var num))
            return num;
        return null;
    }

    // ── Нормализация путей ──────────────────────────────────

    private static string NormalizePath(string path)
    {
        // Заменяем %BIN% и %LISTS% на относительные пути
        // %BIN% → zapret/ (bin/ inside strategies dir)
        // %LISTS% → lists/
        var normalized = path
            .Replace("%BIN%", "bin/")
            .Replace("%LISTS%", "lists/")
            .Replace('\\', '/');

        // Убираем ведущий bin/ если есть (файлы .bin лежат в корне zapret)
        if (normalized.StartsWith("bin/"))
            normalized = normalized[4..];

        return normalized;
    }

	// ── Game Filter ─────────────────────────────────────────

    private static GameFilterMode DetectGameFilter(List<string> tokens)
    {
        // Если есть %GameFilterTCP% или %GameFilterUDP%
        foreach (var token in tokens)
        {
            if (token.Contains("%GameFilterTCP%", StringComparison.OrdinalIgnoreCase) ||
                token.Contains("%GameFilterUDP%", StringComparison.OrdinalIgnoreCase))
            {
                return GameFilterMode.General;
            }
        }
        return GameFilterMode.None;
    }
}
