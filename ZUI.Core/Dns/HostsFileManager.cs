// ═══════════════════════════════════════════════════════════════
// ZUI.Core / Dns / HostsFileManager.cs
// Управление секциями файла hosts (C:\Windows\System32\drivers\etc\hosts)
// Поддержка именованных секций для безопасного добавления/удаления записей
// В отличие от ProxyManager (где пустые catch + нет backup), здесь:
// - Result вместо исключений для ожидаемых ошибок
// - Backup перед каждым изменением
// - Блокировка параллельных изменений через SemaphoreSlim
// ═══════════════════════════════════════════════════════════════

using System.Globalization;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace ZUI.Core.Dns;

/// <summary>
/// Запись в файле hosts.
/// </summary>
public sealed class HostsEntry
{
    public string Ip { get; init; } = string.Empty;
    public string Host { get; init; } = string.Empty;
    public string? Comment { get; init; }

    public override string ToString() => string.IsNullOrEmpty(Comment)
        ? $"{Ip} {Host}"
        : $"{Ip} {Host} # {Comment}";
}

/// <summary>
/// Менеджер файла hosts с поддержкой именованных секций.
/// 
/// Формат секции:
/// # ── Z-UI:BEGIN:section_name ──
/// 127.0.0.1 blocked.example.com
/// # ── Z-UI:END:section_name ──
/// 
/// Это позволяет безопасно добавлять и удалять группы записей,
/// не затрагивая другие содержимое файла hosts.
/// </summary>
public sealed class HostsFileManager
{
    private const string BeginMarker = "# ── Z-UI:BEGIN:";
    private const string EndMarker = "# ── Z-UI:END:";
    private const string BackupExtension = ".zui.bak";

    private readonly ILogger _logger;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly string _hostsPath;

    /// <summary>Путь к файлу hosts по умолчанию.</summary>
    public static string DefaultHostsPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.System),
        "drivers", "etc", "hosts");

    /// <summary>Текущий путь к файлу hosts.</summary>
    public string HostsPath => _hostsPath;

    /// <summary>Количество активных секций (последнее известное).</summary>
    private int _sectionCount;

    public int SectionCount => Volatile.Read(ref _sectionCount);

    public HostsFileManager(
        string? hostsPath = null,
        ILogger<HostsFileManager>? logger = null)
    {
        _hostsPath = hostsPath ?? DefaultHostsPath;
        _logger = logger ?? NullLoggerFactory.Instance.CreateLogger<HostsFileManager>();
    }

    // ── Чтение секций ─────────────────────────────────────

    /// <summary>
    /// Прочитать все записи из именованной секции.
    /// </summary>
    public async Task<Result<HostsEntry[]>> ReadSectionAsync(
        string sectionName,
        CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (!File.Exists(_hostsPath))
                return Result<HostsEntry[]>.Success([]);

            var lines = await File.ReadAllLinesAsync(_hostsPath, ct).ConfigureAwait(false);
            var entries = ParseSection(lines, sectionName);

            return Result<HostsEntry[]>.Success(entries.ToArray());
        }
        catch (IOException ex)
        {
            return Result<HostsEntry[]>.Failed($"Failed to read hosts section '{sectionName}': {ex.Message}");
        }
        catch (UnauthorizedAccessException ex)
        {
            return Result<HostsEntry[]>.Failed($"Failed to read hosts section '{sectionName}': {ex.Message}");
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// Прочитать все секции Z-UI и их записи.
    /// </summary>
    public async Task<Result<Dictionary<string, HostsEntry[]>>> ReadAllSectionsAsync(
        CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (!File.Exists(_hostsPath))
                return Result<Dictionary<string, HostsEntry[]>>.Success(new());

            var lines = await File.ReadAllLinesAsync(_hostsPath, ct).ConfigureAwait(false);
            var sections = ParseAllSections(lines);

            return Result<Dictionary<string, HostsEntry[]>>.Success(sections);
        }
        catch (IOException ex)
        {
            return Result<Dictionary<string, HostsEntry[]>>.Failed($"Failed to read hosts sections: {ex.Message}");
        }
        catch (UnauthorizedAccessException ex)
        {
            return Result<Dictionary<string, HostsEntry[]>>.Failed($"Failed to read hosts sections: {ex.Message}");
        }
        finally
        {
            _lock.Release();
        }
    }

    // ── Запись секций ─────────────────────────────────────

    /// <summary>
    /// Добавить или заменить секцию в файле hosts.
    /// Если секция с таким именем уже существует — она будет заменена.
    /// Создаёт backup перед изменением.
    /// </summary>
    public async Task<Result> WriteSectionAsync(
        string sectionName,
        HostsEntry[] entries,
        CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            // 1. Создать backup
            var backupResult = CreateBackup();
            if (!backupResult.IsSuccess)
                _logger.LogWarning("Failed to create hosts backup: {Error}", backupResult.Error);

            // 2. Прочитать текущее содержимое
            var existingLines = File.Exists(_hostsPath)
                ? await File.ReadAllLinesAsync(_hostsPath, ct).ConfigureAwait(false)
                : [];

            // 3. Удалить старую секцию с таким именем (если есть)
            var filteredLines = RemoveSectionLines(existingLines, sectionName);

            // 4. Добавить новую секцию в конец
            var newLines = new List<string>(filteredLines);

            // Пустая строка перед секцией (если файл не пуст и не заканчивается пустой строкой)
            if (newLines.Count > 0 && newLines[^1].Length > 0)
                newLines.Add(string.Empty);

            newLines.Add($"{BeginMarker}{sectionName} ──");

            foreach (var entry in entries)
            {
                newLines.Add(entry.ToString());
            }

            newLines.Add($"{EndMarker}{sectionName} ──");

            // 5. Записать файл
            await File.WriteAllLinesAsync(_hostsPath, newLines, ct).ConfigureAwait(false);

            Interlocked.Increment(ref _sectionCount);
            _logger.LogInformation("Hosts section '{Section}' written with {Count} entries", sectionName, entries.Length);

            return Result.Success();
        }
        catch (UnauthorizedAccessException ex)
        {
            return Result.Failed($"Access denied to hosts file (need admin rights): {ex.Message}");
        }
        catch (IOException ex)
        {
            return Result.Failed($"Failed to write hosts section '{sectionName}': {ex.Message}");
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// Добавить записи в существующую секцию (или создать новую).
    /// Не удаляет существующие записи — только добавляет новые.
    /// </summary>
    public async Task<Result> AddToSectionAsync(
        string sectionName,
        HostsEntry[] newEntries,
        CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            // 1. Прочитать текущие записи
            var existingLines = File.Exists(_hostsPath)
                ? await File.ReadAllLinesAsync(_hostsPath, ct).ConfigureAwait(false)
                : [];

            var existingEntries = ParseSection(existingLines, sectionName);

            // 2. Объединить (дедупликация по хосту)
            var merged = new Dictionary<string, HostsEntry>(StringComparer.OrdinalIgnoreCase);
            foreach (var e in existingEntries)
                merged[e.Host] = e;

            foreach (var e in newEntries)
                merged[e.Host] = e;

            // 3. Перезаписать секцию
            _lock.Release(); // Освободить, WriteSectionAsync тоже берёт лок
            return await WriteSectionAsync(sectionName, merged.Values.ToArray(), ct).ConfigureAwait(false);
        }
        catch (IOException ex)
        {
            return Result.Failed($"Failed to add entries to hosts section '{sectionName}': {ex.Message}");
        }
        catch (UnauthorizedAccessException ex)
        {
            return Result.Failed($"Failed to add entries to hosts section '{sectionName}': {ex.Message}");
        }
        finally
        {
            // Lock уже освобождён в WriteSectionAsync
        }
    }

    /// <summary>
    /// Удалить именованную секцию из файла hosts.
    /// </summary>
    public async Task<Result> RemoveSectionAsync(
        string sectionName,
        CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (!File.Exists(_hostsPath))
                return Result.Success(); // Нет файла — нечего удалять

            // 1. Backup
            var backupResult = CreateBackup();
            if (!backupResult.IsSuccess)
                _logger.LogWarning("Failed to create hosts backup: {Error}", backupResult.Error);

            // 2. Прочитать и удалить секцию
            var lines = await File.ReadAllLinesAsync(_hostsPath, ct).ConfigureAwait(false);
            var filteredLines = RemoveSectionLines(lines, sectionName);

            // Удалить лишнюю пустую строку в конце
            while (filteredLines.Count > 0 && filteredLines[^1].Length == 0)
                filteredLines.RemoveAt(filteredLines.Count - 1);

            if (filteredLines.Count > 0)
                filteredLines.Add(string.Empty); // Одна пустая строка в конце

            // 3. Записать
            await File.WriteAllLinesAsync(_hostsPath, filteredLines, ct).ConfigureAwait(false);

            Interlocked.Decrement(ref _sectionCount);
            _logger.LogInformation("Hosts section '{Section}' removed", sectionName);

            return Result.Success();
        }
        catch (UnauthorizedAccessException ex)
        {
            return Result.Failed($"Access denied to hosts file (need admin rights): {ex.Message}");
        }
        catch (IOException ex)
        {
            return Result.Failed($"Failed to remove hosts section '{sectionName}': {ex.Message}");
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// Удалить все секции Z-UI из файла hosts.
    /// </summary>
    public async Task<Result> RemoveAllSectionsAsync(CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (!File.Exists(_hostsPath))
                return Result.Success();

            // 1. Backup
            CreateBackup();

            // 2. Прочитать и удалить все Z-UI секции
            var lines = await File.ReadAllLinesAsync(_hostsPath, ct).ConfigureAwait(false);
            var filteredLines = RemoveAllSectionLines(lines);

            // Удалить лишние пустые строки в конце
            while (filteredLines.Count > 0 && filteredLines[^1].Length == 0)
                filteredLines.RemoveAt(filteredLines.Count - 1);

            if (filteredLines.Count > 0)
                filteredLines.Add(string.Empty);

            // 3. Записать
            await File.WriteAllLinesAsync(_hostsPath, filteredLines, ct).ConfigureAwait(false);

            Volatile.Write(ref _sectionCount, 0);
            _logger.LogInformation("All Z-UI hosts sections removed");

            return Result.Success();
        }
        catch (UnauthorizedAccessException ex)
        {
            return Result.Failed($"Access denied to hosts file (need admin rights): {ex.Message}");
        }
        catch (IOException ex)
        {
            return Result.Failed($"Failed to remove all hosts sections: {ex.Message}");
        }
        finally
        {
            _lock.Release();
        }
    }

    // ── Проверка наличия секции ────────────────────────────

    /// <summary>
    /// Проверить, существует ли секция с указанным именем.
    /// </summary>
    public async Task<bool> SectionExistsAsync(
        string sectionName,
        CancellationToken ct = default)
    {
        var result = await ReadSectionAsync(sectionName, ct).ConfigureAwait(false);
        return result.IsSuccess && result.Value is { Length: > 0 };
    }

    // ── Backup ────────────────────────────────────────────

    /// <summary>
    /// Создать backup файла hosts.
    /// Сохраняется как hosts.zui.bak в той же директории.
    /// Хранится только последний backup.
    /// </summary>
    public Result CreateBackup()
    {
        try
        {
            if (!File.Exists(_hostsPath))
                return Result.Success(); // Нет файла — нечего бэкапить

            var backupPath = _hostsPath + BackupExtension;
            File.Copy(_hostsPath, backupPath, overwrite: true);
            _logger.LogDebug("Hosts backup created: {Path}", backupPath);
            return Result.Success();
        }
        catch (IOException ex)
        {
            return Result.Failed($"Failed to create hosts backup: {ex.Message}");
        }
        catch (UnauthorizedAccessException ex)
        {
            return Result.Failed($"Failed to create hosts backup: {ex.Message}");
        }
    }

    /// <summary>
    /// Восстановить файл hosts из backup.
    /// </summary>
    public async Task<Result> RestoreFromBackupAsync(CancellationToken ct = default)
    {
        try
        {
            var backupPath = _hostsPath + BackupExtension;
            if (!File.Exists(backupPath))
                return Result.Failed("No backup file found");

            var content = await File.ReadAllBytesAsync(backupPath, ct).ConfigureAwait(false);
            await File.WriteAllBytesAsync(_hostsPath, content, ct).ConfigureAwait(false);

            _logger.LogInformation("Hosts restored from backup");
            return Result.Success();
        }
        catch (IOException ex)
        {
            return Result.Failed($"Failed to restore hosts from backup: {ex.Message}");
        }
        catch (UnauthorizedAccessException ex)
        {
            return Result.Failed($"Failed to restore hosts from backup: {ex.Message}");
        }
    }

    // ── Парсинг ───────────────────────────────────────────

    /// <summary>
    /// Разобрать записи из именованной секции.
    /// </summary>
    private static List<HostsEntry> ParseSection(string[] lines, string sectionName)
    {
        var entries = new List<HostsEntry>();
        var beginTag = $"{BeginMarker}{sectionName} ──";
        var endTag = $"{EndMarker}{sectionName} ──";
        bool inSection = false;

        foreach (var line in lines)
        {
            var trimmed = line.Trim();

            if (trimmed.StartsWith(BeginMarker, StringComparison.OrdinalIgnoreCase))
            {
                if (trimmed == beginTag)
                    inSection = true;
                continue;
            }

            if (trimmed.StartsWith(EndMarker, StringComparison.OrdinalIgnoreCase))
            {
                if (trimmed == endTag)
                    inSection = false;
                continue;
            }

            if (!inSection)
                continue;

            // Пропустить пустые строки и комментарии
            if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith('#'))
                continue;

            // Формат: IP hostname [# comment]
            var entry = ParseHostsLine(trimmed);
            if (entry is not null)
                entries.Add(entry);
        }

        return entries;
    }

    /// <summary>
    /// Разобрать все секции Z-UI.
    /// </summary>
    private static Dictionary<string, HostsEntry[]> ParseAllSections(string[] lines)
    {
        var sections = new Dictionary<string, List<HostsEntry>>(StringComparer.OrdinalIgnoreCase);
        string? currentSection = null;

        foreach (var line in lines)
        {
            var trimmed = line.Trim();

            if (trimmed.StartsWith(BeginMarker, StringComparison.OrdinalIgnoreCase))
            {
                // Извлечь имя секции: "# ── Z-UI:BEGIN:section_name ──"
                var nameStart = BeginMarker.Length;
                var nameEnd = trimmed.IndexOf('─', nameStart);
                if (nameEnd > nameStart)
                {
                    currentSection = trimmed[nameStart..nameEnd].Trim();
                    if (!sections.ContainsKey(currentSection))
                        sections[currentSection] = new List<HostsEntry>();
                }
                continue;
            }

            if (trimmed.StartsWith(EndMarker, StringComparison.OrdinalIgnoreCase))
            {
                currentSection = null;
                continue;
            }

            if (currentSection is null)
                continue;

            if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith('#'))
                continue;

            var entry = ParseHostsLine(trimmed);
            if (entry is not null)
                sections[currentSection].Add(entry);
        }

        return sections.ToDictionary(
            kvp => kvp.Key,
            kvp => kvp.Value.ToArray(),
            StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Разобрать одну строку файла hosts.
    /// Формат: IP hostname [# comment]
    /// </summary>
    private static HostsEntry? ParseHostsLine(string line)
    {
        // Удалить комментарий
        var commentIdx = line.IndexOf('#');
        string mainPart = commentIdx >= 0 ? line[..commentIdx].Trim() : line;
        string? comment = commentIdx >= 0 ? line[(commentIdx + 1)..].Trim() : null;

        // Разделить на IP и hostname
        var parts = mainPart.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length < 2)
            return null;

        return new HostsEntry
        {
            Ip = parts[0],
            Host = parts[1],
            Comment = comment,
        };
    }

    /// <summary>
    /// Удалить строки секции из массива строк.
    /// </summary>
    private static List<string> RemoveSectionLines(string[] lines, string sectionName)
    {
        var beginTag = $"{BeginMarker}{sectionName} ──";
        var endTag = $"{EndMarker}{sectionName} ──";
        var result = new List<string>(lines.Length);
        bool inSection = false;

        foreach (var line in lines)
        {
            var trimmed = line.Trim();

            if (trimmed.StartsWith(BeginMarker, StringComparison.OrdinalIgnoreCase))
            {
                if (trimmed == beginTag)
                    inSection = true;
                else
                    result.Add(line); // Чужая секция — пропускаем
                continue;
            }

            if (trimmed.StartsWith(EndMarker, StringComparison.OrdinalIgnoreCase))
            {
                if (trimmed == endTag)
                    inSection = false;
                else
                    result.Add(line); // Чужая секция
                continue;
            }

            if (inSection)
                continue; // Пропускаем содержимое удаляемой секции

            result.Add(line);
        }

        return result;
    }

    /// <summary>
    /// Удалить все Z-UI секции из массива строк.
    /// </summary>
    private static List<string> RemoveAllSectionLines(string[] lines)
    {
        var result = new List<string>(lines.Length);
        bool inSection = false;

        foreach (var line in lines)
        {
            var trimmed = line.Trim();

            if (trimmed.StartsWith(BeginMarker, StringComparison.OrdinalIgnoreCase))
            {
                inSection = true;
                continue;
            }

            if (trimmed.StartsWith(EndMarker, StringComparison.OrdinalIgnoreCase))
            {
                inSection = false;
                continue;
            }

            if (inSection)
                continue;

            result.Add(line);
        }

        return result;
    }
}
