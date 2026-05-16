// ═══════════════════════════════════════════════════════════════
// ZUI.Proxy / Rules / RuleEvaluator.cs
// Сопоставление: процесс → правило → действие
// Первое совпавшее правило по приоритету; fallback = Default
// ═══════════════════════════════════════════════════════════════

using System.Net;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ZUI.Core.Intercept;

namespace ZUI.Proxy.Rules;

/// <summary>
/// Результат оценки правила.
/// </summary>
public sealed class RuleEvalResult
{
    /// <summary>Совпавшее правило (null если = Direct по умолчанию).</summary>
    public ProxyRule? MatchedRule { get; init; }

    /// <summary>Действие.</summary>
    public ProxyAction Action { get; init; } = ProxyAction.Direct;

    /// <summary>Целевой прокси (если Action = Proxy).</summary>
    public ProxyTarget? Proxy { get; init; }

    /// <summary>Имя цепочки (если Action = Chain).</summary>
    public string? ChainName { get; init; }

    /// <summary>Политика DNS.</summary>
    public DnsPolicy DnsPolicy { get; init; } = DnsPolicy.Local;

    public static RuleEvalResult Default() => new()
    {
        Action = ProxyAction.Direct,
        DnsPolicy = DnsPolicy.Local,
    };

    public static RuleEvalResult FromRule(ProxyRule rule) => new()
    {
        MatchedRule = rule,
        Action = rule.IsEnabled ? rule.Action : ProxyAction.Direct,
        Proxy = rule.Proxy,
        ChainName = rule.ChainName,
        DnsPolicy = rule.DnsPolicy,
    };
}

/// <summary>
/// Оценщик правил: процесс → действие.
/// Правила сортированы по Priority (меньше = выше).
/// Поддержка: точное имя процесса, wildcard, PID, IP/CIDR, порт/диапазон.
/// </summary>
public sealed class RuleEvaluator
{
    private readonly ILogger _logger;
    private readonly PidMapper _pidMapper;
    private ProxyRule[] _rules = [];
    private ProxyRule? _defaultRule;

    public RuleEvaluator(
        PidMapper pidMapper,
        ILogger<RuleEvaluator>? logger = null)
    {
        _pidMapper = pidMapper;
        _logger = logger ?? NullLoggerFactory.Instance.CreateLogger<RuleEvaluator>();
    }

    /// <summary>Количество активных правил.</summary>
    public int RuleCount => _rules.Length;

    /// <summary>
    /// Загрузить правила (заменяют предыдущие).
    /// Автоматически сортирует по Priority и выделяет Default.
    /// </summary>
    public void LoadRules(ProxyRule[] rules)
    {
        _defaultRule = null;
        var sorted = rules
            .Where(r => !r.IsDefault)
            .OrderBy(r => r.Priority)
            .ThenBy(r => r.Name)
            .ToArray();

        // Найти default правило
        var defaultRule = rules.FirstOrDefault(r => r.IsDefault);
        if (defaultRule is not null)
            _defaultRule = defaultRule;

        _rules = sorted;
        _logger.LogInformation("Loaded {Count} rules + {Default} default", _rules.Length, _defaultRule is not null ? 1 : 0);
    }

    /// <summary>
    /// Оценить соединение: определить действие по процессу и адресу назначения.
    /// </summary>
    public RuleEvalResult Evaluate(
        string processName,
        IPAddress destinationIp,
        int destinationPort,
        string? domainName = null)
    {
        foreach (var rule in _rules)
        {
            if (!rule.IsEnabled)
                continue;

            if (MatchesRule(rule, processName, destinationIp, destinationPort, domainName))
            {
                _logger.LogDebug("Rule matched: {Rule} for {Process} → {DstIp}:{DstPort}" +
                    (!string.IsNullOrEmpty(domainName) ? $" ({domainName})" : ""),
                    rule.Name, processName, destinationIp, destinationPort);
                return RuleEvalResult.FromRule(rule);
            }
        }

        // Default правило
        if (_defaultRule is not null && _defaultRule.IsEnabled)
        {
            _logger.LogDebug("Default rule matched for {Process} → {DstIp}:{DstPort}" +
                (!string.IsNullOrEmpty(domainName) ? $" ({domainName})" : ""),
                processName, destinationIp, destinationPort);
            return RuleEvalResult.FromRule(_defaultRule);
        }

        return RuleEvalResult.Default();
    }

    /// <summary>
    /// Оценить соединение по PID (получить имя процесса через PidMapper).
    /// </summary>
    public RuleEvalResult EvaluateByPid(
        int pid,
        IPAddress destinationIp,
        int destinationPort,
        string? domainName = null)
    {
        var processName = _pidMapper.GetProcessName((uint)pid);
        if (string.IsNullOrEmpty(processName))
            processName = $"PID:{pid}";
        return Evaluate(processName, destinationIp, destinationPort, domainName);
    }

    // ── Сопоставление правила ─────────────────────────────

    private static bool MatchesRule(
        ProxyRule rule,
        string processName,
        IPAddress destinationIp,
        int destinationPort,
        string? domainName = null)
    {
        // 1. Проверка PID (точное совпадение)
        if (rule.ProcessId.HasValue && rule.ProcessId.Value != 0)
        {
            if (!processName.StartsWith($"PID:{rule.ProcessId.Value}", StringComparison.OrdinalIgnoreCase))
                return false;
        }

        // 2. Проверка имени процесса
        if (!string.IsNullOrEmpty(rule.ProcessName))
        {
            if (!processName.Equals(rule.ProcessName, StringComparison.OrdinalIgnoreCase) &&
                !processName.Equals($"{rule.ProcessName}.exe", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        // 3. Проверка wildcard шаблона
        if (!string.IsNullOrEmpty(rule.ProcessNamePattern))
        {
            try
            {
                var pattern = rule.ProcessNamePattern
                    .Replace(".", "\\.")
                    .Replace("*", ".*")
                    .Replace("?", ".");
                var regex = new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.Compiled);
                if (!regex.IsMatch(processName))
                    return false;
            }
            catch (RegexParseException)
            {
                return false;
            }
        }

        // 4. Проверка домена (точное совпадение)
        if (!string.IsNullOrEmpty(rule.DestinationDomain))
        {
            if (!MatchesDomain(rule.DestinationDomain, domainName))
                return false;
        }

        // 5. Проверка домена (wildcard)
        if (!string.IsNullOrEmpty(rule.DestinationDomainPattern))
        {
            if (!MatchesDomain(rule.DestinationDomainPattern, domainName))
                return false;
        }

        // 6. Проверка IP адреса / CIDR
        if (!string.IsNullOrEmpty(rule.DestinationIp))
        {
            if (!MatchesIpRange(rule.DestinationIp, destinationIp))
                return false;
        }

        // 7. Проверка порта / диапазона
        if (!string.IsNullOrEmpty(rule.DestinationPort))
        {
            if (!MatchesPortRange(rule.DestinationPort, destinationPort))
                return false;
        }

        return true;
    }

    /// <summary>
    /// Проверить, совпадает ли домен с правилом (точное или wildcard).
    /// </summary>
    private static bool MatchesDomain(string pattern, string? domainName)
    {
        if (string.IsNullOrEmpty(domainName))
            return false;

        // Точное совпадение
        if (domainName.Equals(pattern, StringComparison.OrdinalIgnoreCase))
            return true;

        // Wildcard: *.example.com → example.com, sub.example.com, a.b.example.com
        if (pattern.StartsWith("*.", StringComparison.OrdinalIgnoreCase))
        {
            var suffix = pattern[1..]; // ".example.com"
            if (domainName.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Проверить, попадает ли IP в диапазон (single IP или CIDR).
    /// </summary>
    private static bool MatchesIpRange(string ipSpec, IPAddress targetIp)
    {
        // CIDR: 192.168.0.0/16
        if (ipSpec.Contains('/'))
        {
            var parts = ipSpec.Split('/');
            if (parts.Length != 2)
                return false;

            if (!IPAddress.TryParse(parts[0], out var network))
                return false;

            if (!int.TryParse(parts[1], out var prefixLength))
                return false;

            return IsInCidr(network, prefixLength, targetIp);
        }

        // Single IP
        if (!IPAddress.TryParse(ipSpec, out var ruleIp))
            return false;

        return ruleIp.Equals(targetIp);
    }

    /// <summary>
    /// Проверить принадлежность IP к CIDR подсети.
    /// </summary>
    private static bool IsInCidr(IPAddress network, int prefixLength, IPAddress target)
    {
        if (network.AddressFamily != target.AddressFamily)
            return false;

        var networkBytes = network.GetAddressBytes();
        var targetBytes = target.GetAddressBytes();

        int fullBytes = prefixLength / 8;
        int remainingBits = prefixLength % 8;

        // Полные байты
        for (int i = 0; i < fullBytes && i < networkBytes.Length; i++)
        {
            if (networkBytes[i] != targetBytes[i])
                return false;
        }

        // Оставшиеся биты
        if (remainingBits > 0 && fullBytes < networkBytes.Length)
        {
            byte mask = (byte)(0xFF << (8 - remainingBits));
            if ((networkBytes[fullBytes] & mask) != (targetBytes[fullBytes] & mask))
                return false;
        }

        return true;
    }

    /// <summary>
    /// Проверить, попадает ли порт в диапазон (single: 443, range: 8080-8090).
    /// </summary>
    private static bool MatchesPortRange(string portSpec, int targetPort)
    {
        // Диапазон: 8080-8090
        if (portSpec.Contains('-'))
        {
            var parts = portSpec.Split('-');
            if (parts.Length != 2)
                return false;

            if (!int.TryParse(parts[0], out var low) || !int.TryParse(parts[1], out var high))
                return false;

            return targetPort >= low && targetPort <= high;
        }

        // Несколько портов через запятую: 80,443,8080
        if (portSpec.Contains(','))
        {
            var ports = portSpec.Split(',');
            foreach (var p in ports)
            {
                if (int.TryParse(p.Trim(), out var port) && port == targetPort)
                    return true;
            }
            return false;
        }

        // Single port
        if (!int.TryParse(portSpec, out var singlePort))
            return false;

        return singlePort == targetPort;
    }
}
