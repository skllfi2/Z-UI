// ═══════════════════════════════════════════════════════════════
// ZUI.Proxy / Profile / ProxyProfile.cs
// Профиль проксификатора: правила + цепочки + настройки
// Сохранение/загрузка в JSON
// ═══════════════════════════════════════════════════════════════

using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ZUI.Core;
using ZUI.Proxy.Chain;
using ZUI.Proxy.Rules;

namespace ZUI.Proxy.Profile;

/// <summary>
/// Профиль проксификатора — полный набор настроек:
/// правила маршрутизации, цепочки прокси, политики отказоустойчивости.
/// Сохраняется/загружается из JSON.
/// </summary>
public sealed class ProxyProfile
{
    /// <summary>Уникальный идентификатор профиля.</summary>
    public string Id { get; init; } = Guid.NewGuid().ToString("N")[..8];

    /// <summary>Название профиля (для UI).</summary>
    public string Name { get; set; } = "Default";

    /// <summary>Дата последнего изменения.</summary>
    public DateTime LastModified { get; set; } = DateTime.UtcNow;

    /// <summary>Правила маршрутизации.</summary>
    public List<ProxyRule> Rules { get; set; } = [];

    /// <summary>Прокси-серверы.</summary>
    public List<ProxyTarget> Servers { get; set; } = [];

    /// <summary>Цепочки прокси (по имени, для ссылки из правил).</summary>
    public List<ProxyChain> Chains { get; set; } = [];

    /// <summary>Политика отказоустойчивости по умолчанию.</summary>
    public FailoverPolicy DefaultFailoverPolicy { get; set; } = FailoverPolicy.NextOnError;

    /// <summary>Включён ли проксификатор при старте?</summary>
    public bool AutoStart { get; set; } = false;

    /// <summary>
    /// Найти цепочку по имени.
    /// </summary>
    public ProxyChain? FindChain(string chainName)
    {
        return Chains.FirstOrDefault(c =>
            c.Name.Equals(chainName, StringComparison.OrdinalIgnoreCase));
    }

    public override string ToString() =>
        $"Profile[{Name}] ({Rules.Count} rules, {Chains.Count} chains, {Servers.Count} servers)";
}

/// <summary>
/// Загрузчик/сохранитель профилей проксификатора (JSON).
/// </summary>
public sealed class ProxyProfileManager
{
    private readonly ILogger _logger;

    /// <summary>Source Generator контекст (AOT-compatible).</summary>
    private static readonly ProxyJsonContext JsonCtx = ProxyJsonContext.Default;

    public ProxyProfileManager(ILogger<ProxyProfileManager>? logger = null)
    {
        _logger = logger ?? NullLoggerFactory.Instance.CreateLogger<ProxyProfileManager>();
    }

    /// <summary>
    /// Загрузить профиль из JSON файла.
    /// </summary>
    public async Task<Result<ProxyProfile>> LoadAsync(
        string filePath, CancellationToken ct = default)
    {
        if (!File.Exists(filePath))
            return Result<ProxyProfile>.Failed($"Profile file not found: {filePath}");

        try
        {
            var json = await File.ReadAllTextAsync(filePath, ct).ConfigureAwait(false);
            var profile = JsonSerializer.Deserialize(json, JsonCtx.ProxyProfile);

            if (profile is null)
                return Result<ProxyProfile>.Failed("Failed to deserialize profile (null result)");

            _logger.LogInformation("Loaded profile: {Profile} from {Path}", profile, filePath);
            return Result<ProxyProfile>.Success(profile);
        }
        catch (JsonException ex)
        {
            return Result<ProxyProfile>.Failed($"Invalid profile JSON: {ex.Message}");
        }
        catch (IOException ex)
        {
            return Result<ProxyProfile>.Failed($"Failed to load profile: {ex.Message}");
        }
        catch (UnauthorizedAccessException ex)
        {
            return Result<ProxyProfile>.Failed($"Failed to load profile: {ex.Message}");
        }
    }

    /// <summary>
    /// Сохранить профиль в JSON файл.
    /// </summary>
    public async Task<Result> SaveAsync(
        ProxyProfile profile, string filePath, CancellationToken ct = default)
    {
        try
        {
            profile.LastModified = DateTime.UtcNow;

            var directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var json = JsonSerializer.Serialize(profile, JsonCtx.ProxyProfile);
            await File.WriteAllTextAsync(filePath, json, ct).ConfigureAwait(false);

            _logger.LogInformation("Saved profile: {Profile} to {Path}", profile, filePath);
            return Result.Success();
        }
        catch (IOException ex)
        {
            return Result.Failed($"Failed to save profile: {ex.Message}");
        }
        catch (UnauthorizedAccessException ex)
        {
            return Result.Failed($"Failed to save profile: {ex.Message}");
        }
    }

    /// <summary>
    /// Создать профиль по умолчанию с базовыми правилами.
    /// </summary>
    public static ProxyProfile CreateDefault()
    {
        return new ProxyProfile
        {
            Name = "Default",
            Rules =
            [
                new ProxyRule
                {
                    Id = "default",
                    Name = "Default Rule",
                    Priority = 9999,
                    Action = ProxyAction.Direct,
                },
            ],
            Chains = [],
            DefaultFailoverPolicy = FailoverPolicy.NextOnError,
            AutoStart = false,
        };
    }

    /// <summary>
    /// Загрузить все профили из директории.
    /// </summary>
    public async Task<Result<ProxyProfile[]>> LoadAllAsync(
        string directory, CancellationToken ct = default)
    {
        if (!Directory.Exists(directory))
            return Result<ProxyProfile[]>.Failed($"Profiles directory not found: {directory}");

        var profiles = new List<ProxyProfile>();
        var errors = new List<string>();

        foreach (var file in Directory.GetFiles(directory, "*.json"))
        {
            var result = await LoadAsync(file, ct).ConfigureAwait(false);
            if (result.IsSuccess)
                profiles.Add(result.Value);
            else
                errors.Add($"{Path.GetFileName(file)}: {result.Error}");
        }

        if (profiles.Count == 0 && errors.Count > 0)
            return Result<ProxyProfile[]>.Failed($"No profiles loaded. Errors: {string.Join("; ", errors)}");

        if (errors.Count > 0)
            _logger.LogWarning("Loaded {Count} profiles with {Errors} errors: {Details}",
                profiles.Count, errors.Count, string.Join("; ", errors));

        return Result<ProxyProfile[]>.Success(profiles.ToArray());
    }

    // ── Proxy Server CRUD ───────────────────────────────────

    /// <summary>Добавить прокси-сервер в профиль.</summary>
    public void AddServer(ProxyProfile profile, ProxyTarget server)
    {
        if (profile.Servers.Any(s => s.Name.Equals(server.Name, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException($"Server with name '{server.Name}' already exists");

        profile.Servers.Add(server);
        _logger.LogInformation("Added server: {Name} ({Host}:{Port})", server.Name, server.Host, server.Port);
    }

    /// <summary>Удалить прокси-сервер из профиля по Id.</summary>
    public bool RemoveServer(ProxyProfile profile, string serverId)
    {
        var server = profile.Servers.FirstOrDefault(s => s.Name.Equals(serverId, StringComparison.OrdinalIgnoreCase));
        if (server is null)
            return false;

        profile.Servers.Remove(server);
        _logger.LogInformation("Removed server: {Name}", server.Name);
        return true;
    }

    /// <summary>Обновить существующий прокси-сервер.</summary>
    public bool UpdateServer(ProxyProfile profile, string serverId, string? name, string? host,
        int? port, string? proxyType, string? username, string? password, string? dnsPolicy)
    {
        var server = profile.Servers.FirstOrDefault(s => s.Name.Equals(serverId, StringComparison.OrdinalIgnoreCase));
        if (server is null)
            return false;

        if (name is not null) server.Name = name;
        if (host is not null) server.Host = host;
        if (port.HasValue) server.Port = port.Value;
        if (proxyType is not null && Enum.TryParse<ProxyType>(proxyType, out var pt))
            server.Type = pt;
        if (username is not null) server.Username = username;
        if (password is not null) server.Password = password;

        _logger.LogInformation("Updated server: {Name}", server.Name);
        return true;
    }

    // ── Proxy Rule CRUD ─────────────────────────────────────

    /// <summary>Добавить правило маршрутизации в профиль.</summary>
    public void AddRule(ProxyProfile profile, ProxyRule rule)
    {
        if (rule.IsDefault && profile.Rules.Any(r => r.IsDefault))
            throw new InvalidOperationException("Profile already has a default rule");

        profile.Rules.Add(rule);
        _logger.LogInformation("Added rule: {Name}", rule.Name);
    }

    /// <summary>Удалить правило маршрутизации из профиля по Id.</summary>
    public bool RemoveRule(ProxyProfile profile, string ruleId)
    {
        var rule = profile.Rules.FirstOrDefault(r => r.Id.Equals(ruleId, StringComparison.OrdinalIgnoreCase));
        if (rule is null)
            return false;

        if (rule.IsDefault)
            throw new InvalidOperationException("Cannot remove the default rule");

        profile.Rules.Remove(rule);
        _logger.LogInformation("Removed rule: {Name}", rule.Name);
        return true;
    }

    // ── Rule Import / Export ─────────────────────────────────

    /// <summary>
    /// Клонировать правило с новым ID (для импорта дубликатов).
    /// </summary>
    private static ProxyRule CloneRuleWithNewId(ProxyRule source, string newId)
    {
        return new ProxyRule
        {
            Id = newId,
            Name = source.Name,
            IsEnabled = source.IsEnabled,
            Priority = source.Priority,
            ProcessName = source.ProcessName,
            ProcessNamePattern = source.ProcessNamePattern,
            ProcessId = source.ProcessId,
            DestinationIp = source.DestinationIp,
            DestinationPort = source.DestinationPort,
            Action = source.Action,
            Proxy = source.Proxy,
            ChainName = source.ChainName,
            DnsPolicy = source.DnsPolicy,
        };
    }

    /// <summary>
    /// Экспортировать правила маршрутизации в JSON строку.
    /// Сериализует List&lt;ProxyRule&gt; с отступами (человекочитаемый формат).
    /// </summary>
    public Result<string> ExportRules(ProxyProfile profile)
    {
        try
        {
            var rules = profile.Rules.Where(r => !r.IsDefault).ToList();
            var json = JsonSerializer.Serialize(rules, JsonCtx.ListProxyRule);
            _logger.LogInformation("Exported {Count} rules from profile {Name}", rules.Count, profile.Name);
            return Result<string>.Success(json);
        }
        catch (JsonException ex)
        {
            return Result<string>.Failed($"Failed to export rules: {ex.Message}");
        }
    }

    /// <summary>
    /// Импортировать правила маршрутизации из JSON строки.
    /// Добавляет правила в профиль (не заменяет существующие).
    /// Дубликаты по Id пропускаются.
    /// </summary>
    public Result<int> ImportRules(ProxyProfile profile, string json)
    {
        try
        {
            var rules = JsonSerializer.Deserialize(json, JsonCtx.ListProxyRule);

            if (rules is null)
                return Result<int>.Failed("Failed to deserialize rules (null result)");

            var existingIds = new HashSet<string>(profile.Rules.Select(r => r.Id));
            int imported = 0;

            foreach (var rule in rules)
            {
                if (rule.IsDefault)
                    continue; // Пропускаем default rule

                if (existingIds.Contains(rule.Id))
                {
                    // Генерируем новый ID для дубликата через клонирование
                    var newRule = CloneRuleWithNewId(rule, Guid.NewGuid().ToString("N")[..8]);
                    profile.Rules.Add(newRule);
                    existingIds.Add(newRule.Id);
                    imported++;
                    continue;
                }

                profile.Rules.Add(rule);
                existingIds.Add(rule.Id);
                imported++;
            }

            _logger.LogInformation("Imported {Count} rules into profile {Name} (total rules: {Total})",
                imported, profile.Name, profile.Rules.Count);
            return Result<int>.Success(imported);
        }
        catch (JsonException ex)
        {
            return Result<int>.Failed($"Failed to import rules: invalid JSON: {ex.Message}");
        }
    }
}
