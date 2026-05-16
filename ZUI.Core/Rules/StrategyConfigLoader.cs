// ═══════════════════════════════════════════════════════════════
// ZUI.Core / Rules / StrategyConfigLoader.cs
// Загрузчик стратегий: JSON (предпочтительно) или BAT fallback
// Сканирует директории, кэширует, отслеживает изменения файлов
// ═══════════════════════════════════════════════════════════════

using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace ZUI.Core.Rules;

/// <summary>
/// Загрузчик стратегий обхода DPI.
/// Приоритет: JSON файлы > BAT конвертация.
/// JSON стратегии хранятся в strategies/ рядом с BAT файлами.
/// Поддерживает hot-reload при изменении файлов.
/// </summary>
public sealed class StrategyConfigLoader
{
    private readonly ILogger _logger;
    private readonly BatStrategyConverter _converter;

    // Кэш загруженных стратегий: Id → (Config, LastWriteTime)
    private readonly ConcurrentDictionary<string, StrategyEntry> _cache = new();

    // Путь к директории стратегий
    private string? _strategiesDir;

    /// <summary>Source Generator контекст (AOT-compatible).</summary>
    private static readonly CoreJsonContext JsonCtx = CoreJsonContext.Default;

    /// <summary>JSON сериализатор с пользовательскими конвертерами (AOT-compatible).</summary>
    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            TypeInfoResolver = JsonCtx,
        };
        options.Converters.Add(new PortRangeJsonConverter());
        options.Converters.Add(new SplitPositionJsonConverter());
        return options;
    }

    public StrategyConfigLoader(
        BatStrategyConverter converter,
        ILogger<StrategyConfigLoader>? logger = null)
    {
        _converter = converter;
        _logger = logger ?? NullLoggerFactory.Instance.CreateLogger<StrategyConfigLoader>();
    }

    // ── Загрузка всех стратегий ─────────────────────────────

    /// <summary>
    /// Загрузить все стратегии из директории.
    /// Сканирует JSON файлы, затем BAT файлы (если нет JSON-версии).
    /// </summary>
    public async Task<Result<StrategyConfig[]>> LoadAllAsync(
        string strategiesDir,
        CancellationToken ct = default)
    {
        if (!Directory.Exists(strategiesDir))
            return Result<StrategyConfig[]>.Failed($"Strategies directory not found: {strategiesDir}");

        _strategiesDir = strategiesDir;

        var strategies = new List<StrategyConfig>();
        var seenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // 1. Загрузить JSON стратегии
        var jsonFiles = Directory.GetFiles(strategiesDir, "*.json");
        foreach (var jsonFile in jsonFiles)
        {
            ct.ThrowIfCancellationRequested();

            var result = await LoadFromJsonAsync(jsonFile, ct).ConfigureAwait(false);
            if (result.IsSuccess && result.Value is not null)
            {
                if (seenIds.Add(result.Value.Id))
                    strategies.Add(result.Value);
                else
                    _logger.LogWarning("Duplicate strategy Id '{Id}' from {File}", result.Value.Id, jsonFile);
            }
            else
            {
                _logger.LogWarning("Failed to load JSON strategy from {File}: {Error}", jsonFile, result.Error);
            }
        }

        // 2. BAT fallback: конвертировать BAT файлы, для которых нет JSON
        var batFiles = Directory.GetFiles(strategiesDir, "*.bat")
            .Where(f => !Path.GetFileName(f).Equals("service.bat", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        foreach (var batFile in batFiles)
        {
            ct.ThrowIfCancellationRequested();

            var batId = Path.GetFileNameWithoutExtension(batFile)
                .ToLowerInvariant()
                .Replace(' ', '-');

            if (seenIds.Contains(batId))
                continue; // JSON версия уже загружена

            var result = _converter.Convert(batFile);
            if (result.IsSuccess && result.Value is not null)
            {
                if (seenIds.Add(result.Value.Id))
                    strategies.Add(result.Value);

                // Автосохранить как JSON для следующего запуска
                _ = SaveAsJsonAsync(result.Value, ct);
            }
            else
            {
                _logger.LogWarning("Failed to convert BAT strategy from {File}: {Error}", batFile, result.Error);
            }
        }

        _logger.LogInformation("Loaded {Count} strategies from {Dir}", strategies.Count, strategiesDir);
        return Result<StrategyConfig[]>.Success(strategies.ToArray());
    }

    // ── Загрузка одной стратегии ────────────────────────────

    /// <summary>
    /// Загрузить стратегию из JSON файла.
    /// </summary>
    public async Task<Result<StrategyConfig>> LoadFromJsonAsync(
        string jsonFilePath,
        CancellationToken ct = default)
    {
        try
        {
            var json = await File.ReadAllTextAsync(jsonFilePath, ct).ConfigureAwait(false);
            var config = JsonSerializer.Deserialize(json, JsonCtx.StrategyConfig);

            if (config is null)
                return Result<StrategyConfig>.Failed($"Failed to deserialize JSON from {jsonFilePath}");

            // Обновить кэш
            var writeTime = File.GetLastWriteTimeUtc(jsonFilePath);
            _cache[config.Id] = new StrategyEntry(config, writeTime);

            return Result<StrategyConfig>.Success(config);
        }
        catch (JsonException ex)
        {
            return Result<StrategyConfig>.Failed($"Failed to read JSON strategy from {jsonFilePath}: {ex.Message}");
        }
        catch (IOException ex)
        {
            return Result<StrategyConfig>.Failed($"Failed to read JSON strategy from {jsonFilePath}: {ex.Message}");
        }
    }

    // ── Сохранение стратегии как JSON ───────────────────────

    /// <summary>
    /// Сохранить стратегию как JSON файл в директории стратегий.
    /// Имя файла: {Id}.json
    /// </summary>
    public async Task<Result> SaveAsJsonAsync(
        StrategyConfig config,
        CancellationToken ct = default)
    {
        if (_strategiesDir is null)
            return Result.Failed("Strategies directory not set. Call LoadAllAsync first.");

        try
        {
            var jsonPath = Path.Combine(_strategiesDir, $"{config.Id}.json");
            var json = JsonSerializer.Serialize(config, JsonCtx.StrategyConfig);
            await File.WriteAllTextAsync(jsonPath, json, ct).ConfigureAwait(false);

            _logger.LogDebug("Saved strategy '{Id}' to {Path}", config.Id, jsonPath);
            return Result.Success();
        }
        catch (IOException ex)
        {
            return Result.Failed($"Failed to save strategy '{config.Id}': {ex.Message}");
        }
        catch (JsonException ex)
        {
            return Result.Failed($"Failed to save strategy '{config.Id}': {ex.Message}");
        }
        catch (UnauthorizedAccessException ex)
        {
            return Result.Failed($"Failed to save strategy '{config.Id}': {ex.Message}");
        }
    }

    /// <summary>
    /// Сохранить стратегию по указанному пути.
    /// </summary>
    public async Task<Result> SaveAsJsonAsync(
        StrategyConfig config,
        string jsonFilePath,
        CancellationToken ct = default)
    {
        try
        {
            var json = JsonSerializer.Serialize(config, JsonCtx.StrategyConfig);
            await File.WriteAllTextAsync(jsonFilePath, json, ct).ConfigureAwait(false);
            return Result.Success();
        }
        catch (IOException ex)
        {
            return Result.Failed($"Failed to save strategy to {jsonFilePath}: {ex.Message}");
        }
        catch (JsonException ex)
        {
            return Result.Failed($"Failed to save strategy to {jsonFilePath}: {ex.Message}");
        }
        catch (UnauthorizedAccessException ex)
        {
            return Result.Failed($"Failed to save strategy to {jsonFilePath}: {ex.Message}");
        }
    }

    // ── Hot-reload ──────────────────────────────────────────

    /// <summary>
    /// Проверить изменения файлов и перезагрузить устаревшие стратегии.
    /// Возвращает обновлённые стратегии (если есть изменения).
    /// </summary>
    public async Task<Result<StrategyConfig[]>> CheckForChangesAsync(
        CancellationToken ct = default)
    {
        if (_strategiesDir is null || !Directory.Exists(_strategiesDir))
            return Result<StrategyConfig[]>.Failed("Strategies directory not set.");

        var changed = new List<StrategyConfig>();

        // Проверить JSON файлы
        var jsonFiles = Directory.GetFiles(_strategiesDir, "*.json");
        foreach (var jsonFile in jsonFiles)
        {
            ct.ThrowIfCancellationRequested();

            var writeTime = File.GetLastWriteTimeUtc(jsonFile);
            var id = Path.GetFileNameWithoutExtension(jsonFile);

            if (_cache.TryGetValue(id, out var entry) && entry.LastWriteTime >= writeTime)
                continue; // Не изменился

            var result = await LoadFromJsonAsync(jsonFile, ct).ConfigureAwait(false);
            if (result.IsSuccess && result.Value is not null)
                changed.Add(result.Value);
        }

        if (changed.Count > 0)
            _logger.LogInformation("Hot-reloaded {Count} changed strategies", changed.Count);

        return Result<StrategyConfig[]>.Success(changed.ToArray());
    }

    // ── BAT → JSON конвертация всех файлов ──────────────────

    /// <summary>
    /// Конвертировать все BAT стратегии в JSON и сохранить.
    /// Вызывать один раз при первом запуске или по запросу пользователя.
    /// </summary>
    public async Task<Result<int>> ConvertAllBatToJsonAsync(
        string strategiesDir,
        CancellationToken ct = default)
    {
        if (!Directory.Exists(strategiesDir))
            return Result<int>.Failed($"Strategies directory not found: {strategiesDir}");

        var batFiles = Directory.GetFiles(strategiesDir, "*.bat")
            .Where(f => !Path.GetFileName(f).Equals("service.bat", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        int converted = 0;
        foreach (var batFile in batFiles)
        {
            ct.ThrowIfCancellationRequested();

            var result = _converter.Convert(batFile);
            if (!result.IsSuccess)
            {
                _logger.LogWarning("Skipping {File}: {Error}", batFile, result.Error);
                continue;
            }

            var config = result.Value;
            var jsonPath = Path.Combine(strategiesDir, $"{config.Id}.json");

            // Не перезаписывать существующие JSON (если пользователь вручную редактировал)
            if (File.Exists(jsonPath))
            {
                _logger.LogDebug("JSON already exists for '{Id}', skipping", config.Id);
                continue;
            }

            var saveResult = await SaveAsJsonAsync(config, jsonPath, ct).ConfigureAwait(false);
            if (saveResult.IsSuccess)
                converted++;
        }

        _logger.LogInformation("Converted {Converted}/{Total} BAT strategies to JSON", converted, batFiles.Length);
        return Result<int>.Success(converted);
    }

    // ── Получение из кэша ───────────────────────────────────

    /// <summary>
    /// Получить стратегию из кэша по Id. null если не загружена.
    /// </summary>
    public StrategyConfig? GetFromCache(string id)
    {
        return _cache.TryGetValue(id, out var entry) ? entry.Config : null;
    }

    /// <summary>
    /// Все загруженные стратегии из кэша.
    /// </summary>
    public IReadOnlyList<StrategyConfig> AllFromCache =>
        _cache.Values.Select(e => e.Config).ToList().AsReadOnly();

    /// <summary>
    /// Очистить кэш.
    /// </summary>
    public void ClearCache() => _cache.Clear();

    // ── Внутренние типы ─────────────────────────────────────

    private sealed record StrategyEntry(StrategyConfig Config, DateTime LastWriteTime);

    // ── JSON конвертеры ─────────────────────────────────────

    /// <summary>
    /// Конвертер для PortRange: сериализует как "80" или "80-443",
    /// десериализует из числа, строки или объекта.
    /// </summary>
    private sealed class PortRangeJsonConverter : JsonConverter<PortRange>
    {
        public override PortRange Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.Number)
            {
                var port = reader.GetUInt16();
                return new PortRange(port);
            }

            if (reader.TokenType == JsonTokenType.String)
            {
                var str = reader.GetString()!;
                if (str.Contains('-'))
                {
                    var parts = str.Split('-');
                    if (parts.Length == 2 && ushort.TryParse(parts[0], out var start) && ushort.TryParse(parts[1], out var end))
                        return new PortRange(start, end);
                }
                if (ushort.TryParse(str, out var single))
                    return new PortRange(single);
            }

            if (reader.TokenType == JsonTokenType.StartObject)
        {
                ushort start = 0, end = 0;
                while (reader.Read())
                {
                    if (reader.TokenType == JsonTokenType.EndObject)
                        break;
                    if (reader.TokenType == JsonTokenType.PropertyName)
                    {
                        var prop = reader.GetString()!;
                        reader.Read();
                        if (prop.Equals("start", StringComparison.OrdinalIgnoreCase) ||
                            prop.Equals("Start", StringComparison.OrdinalIgnoreCase))
                            start = reader.GetUInt16();
                        else if (prop.Equals("end", StringComparison.OrdinalIgnoreCase) ||
                                 prop.Equals("End", StringComparison.OrdinalIgnoreCase))
                            end = reader.GetUInt16();
                    }
                }
                return new PortRange(start, end);
            }

            throw new JsonException($"Cannot deserialize PortRange from {reader.TokenType}");
        }

        public override void Write(Utf8JsonWriter writer, PortRange value, JsonSerializerOptions options)
        {
            if (value.Start == value.End)
                writer.WriteNumberValue(value.Start);
            else
                writer.WriteStringValue(value.ToString());
        }
    }

    /// <summary>
    /// Конвертер для SplitPositions: int → число, string → строка.
    /// Массив может содержать смешанные типы: [1, "midsld", 3].
    /// </summary>
    private sealed class SplitPositionJsonConverter : JsonConverter<object[]>
    {
        public override object[] Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType != JsonTokenType.StartArray)
                throw new JsonException("Expected array for SplitPositions");

            var list = new List<object>();
            while (reader.Read())
            {
                if (reader.TokenType == JsonTokenType.EndArray)
                    break;

                if (reader.TokenType == JsonTokenType.Number)
                    list.Add(reader.GetInt32());
                else if (reader.TokenType == JsonTokenType.String)
                    list.Add(reader.GetString()!);
            }

            return list.ToArray();
        }

        public override void Write(Utf8JsonWriter writer, object[] value, JsonSerializerOptions options)
        {
            writer.WriteStartArray();
            foreach (var item in value)
            {
                if (item is int intVal)
                    writer.WriteNumberValue(intVal);
                else if (item is string strVal)
                    writer.WriteStringValue(strVal);
                else
                    writer.WriteStringValue(item.ToString());
            }
            writer.WriteEndArray();
        }
    }
}
