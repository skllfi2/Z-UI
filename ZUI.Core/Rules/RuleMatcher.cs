// ═══════════════════════════════════════════════════════════════
// ZUI.Core / Rules / RuleMatcher.cs
// Сопоставление перехваченного пакета с правилами FilterRule
// Порядок: Protocol → Port → L7 → Hostlist → Ipset → Cutoff
// ═══════════════════════════════════════════════════════════════

using System.Net;
using ZUI.Core.Engine;
using ZUI.Core.Intercept;
using ZUI.Core.WinDivert;

namespace ZUI.Core.Rules;

/// <summary>
/// Результат матчинга пакета к правилу.
/// </summary>
public sealed class RuleMatch
{
    /// <summary>Правило, с которым совпал пакет. null = нет совпадения.</summary>
    public FilterRule? Rule { get; init; }

    /// <summary>Извлечённый SNI/Host из пакета (для hostlist проверки).</summary>
    public string? Hostname { get; init; }

    /// <summary>Обнаруженный L7-протокол.</summary>
    public L7Protocol L7Protocol { get; init; }

    /// <summary>Совпало ли правило.</summary>
    public bool IsMatch => Rule is not null;
}

/// <summary>
/// Сопоставление перехваченного пакета с правилами фильтрации.
/// Проверяет: Protocol → Port → L7 → Hostlist → Ipset → Cutoff.
/// </summary>
public sealed class RuleMatcher
{
    private readonly DomainListLoader _domainLoader;

    public RuleMatcher(DomainListLoader domainLoader)
    {
        _domainLoader = domainLoader;
    }

    /// <summary>
    /// Найти первое подходящее правило для пакета.
    /// Правила проверяются в порядке Order (ascending).
    /// </summary>
    public RuleMatch Match(
        ParsedPacket packet,
        StrategyConfig strategy,
        Engine.ConnectionTracker? connectionTracker = null)
    {
        var rules = strategy.Rules;
        if (rules is null || rules.Length == 0)
            return NoMatch();

        // Определяем L7 протокол и hostname один раз
        var payload = packet.Payload;
        var l7 = L7ProtocolDetector.Detect(payload, packet.DstPort);
        string? hostname = null;

        if (l7 == L7Protocol.Tls)
            hostname = SniParser.ExtractSni(payload);
        else if (l7 == L7Protocol.Http)
            hostname = SniParser.ExtractHostFromHttp(payload);

        // Проверяем правила по порядку
        foreach (var rule in rules)
        {
            if (TryMatchRule(packet, rule, l7, hostname))
            {
                return new RuleMatch
                {
                    Rule = rule,
                    Hostname = hostname,
                    L7Protocol = l7,
                };
            }
        }

        return new RuleMatch
        {
            Rule = null,
            Hostname = hostname,
            L7Protocol = l7,
        };
    }

    /// <summary>
    /// Проверить все правила и вернуть все совпадения.
    /// Нужно когда к одному пакету применяются несколько режимов десинхронизации.
    /// </summary>
    public List<RuleMatch> MatchAll(
        ParsedPacket packet,
        StrategyConfig strategy)
    {
        var results = new List<RuleMatch>();
        var rules = strategy.Rules;
        if (rules is null || rules.Length == 0)
            return results;

        var payload = packet.Payload;
        var l7 = L7ProtocolDetector.Detect(payload, packet.DstPort);
        string? hostname = null;

        if (l7 == L7Protocol.Tls)
            hostname = SniParser.ExtractSni(payload);
        else if (l7 == L7Protocol.Http)
            hostname = SniParser.ExtractHostFromHttp(payload);

        foreach (var rule in rules)
        {
            if (TryMatchRule(packet, rule, l7, hostname))
            {
                results.Add(new RuleMatch
                {
                    Rule = rule,
                    Hostname = hostname,
                    L7Protocol = l7,
                });
            }
        }

        return results;
    }

    // ── Внутренняя логика матчинга ───────────────────────────

    private bool TryMatchRule(
        ParsedPacket packet,
        FilterRule rule,
        L7Protocol detectedL7,
        string? hostname)
    {
        // 1. Protocol check
        if (rule.Protocol == FilterProtocol.Tcp && !packet.IsTcp)
            return false;
        if (rule.Protocol == FilterProtocol.Udp && !packet.IsUdp)
            return false;

        // 2. Port check
        if (rule.Ports is not null && rule.Ports.Length > 0)
        {
            bool portMatch = false;
            for (int i = 0; i < rule.Ports.Length; i++)
            {
                if (rule.Ports[i].Contains(packet.DstPort))
                {
                    portMatch = true;
                    break;
                }
            }
            if (!portMatch)
                return false;
        }

        // 3. L7 protocol check
        if (rule.L7Protocols is not null && rule.L7Protocols.Length > 0)
        {
            bool l7Match = false;
            for (int i = 0; i < rule.L7Protocols.Length; i++)
            {
                if (string.Equals(rule.L7Protocols[i], detectedL7.ToString(),
                        StringComparison.OrdinalIgnoreCase))
                {
                    l7Match = true;
                    break;
                }
            }
            if (!l7Match)
                return false;
        }

        // 4. IP ID check
        if (rule.IpIdZero)
        {
            // Проверяем что IP ID = 0 в заголовке
            // В ParsedPacket этого нет, нужно проверить raw bytes
            // IPv4 header: bytes 4-5 = Identification (big-endian)
            var raw = packet.RawPacket;
            if (!packet.IsIPv6 && raw.Length >= 6)
            {
                ushort ipId = (ushort)((raw[4] << 8) | raw[5]);
                if (ipId != 0)
                    return false;
            }
        }

        // 5. Hostlist check (whitelist — домен ДОЛЖЕН быть в списке)
        if (rule.HostLists is not null && rule.HostLists.Length > 0)
        {
            if (string.IsNullOrEmpty(hostname))
                return false; // Нет SNI/Host, а список обязателен

            bool inList = false;
            for (int i = 0; i < rule.HostLists.Length; i++)
            {
                if (_domainLoader.IsDomainInList(rule.HostLists[i], hostname))
                {
                    inList = true;
                    break;
                }
            }
            if (!inList)
                return false;
        }

        // 6. Host domain check (конкретные домены)
        if (rule.HostDomains is not null && rule.HostDomains.Length > 0)
        {
            if (string.IsNullOrEmpty(hostname))
                return false;

            bool inDomains = false;
            for (int i = 0; i < rule.HostDomains.Length; i++)
            {
                if (SniParser.MatchSni(hostname, rule.HostDomains[i]))
                {
                    inDomains = true;
                    break;
                }
            }
            if (!inDomains)
                return false;
        }

        // 7. Hostlist exclude check (blacklist — домен НЕ ДОЛЖЕН быть в списке)
        if (rule.HostExcludeLists is not null && rule.HostExcludeLists.Length > 0)
        {
            if (!string.IsNullOrEmpty(hostname))
            {
                for (int i = 0; i < rule.HostExcludeLists.Length; i++)
                {
                    if (_domainLoader.IsDomainInList(rule.HostExcludeLists[i], hostname))
                        return false; // Домен в списке исключений
                }
            }
        }

        // 8. Ipset check (IP ДОЛЖЕН быть в наборе)
        if (rule.IpsetLists is not null && rule.IpsetLists.Length > 0)
        {
            bool inSet = false;
            for (int i = 0; i < rule.IpsetLists.Length; i++)
            {
                if (_domainLoader.IsIpInList(rule.IpsetLists[i], packet.DstIp))
                {
                    inSet = true;
                    break;
                }
            }
            if (!inSet)
                return false;
        }

        // 9. Ipset exclude check (IP НЕ ДОЛЖЕН быть в наборе)
        if (rule.IpsetExcludeLists is not null && rule.IpsetExcludeLists.Length > 0)
        {
            for (int i = 0; i < rule.IpsetExcludeLists.Length; i++)
            {
                if (_domainLoader.IsIpInList(rule.IpsetExcludeLists[i], packet.DstIp))
                    return false;
            }
        }

        return true;
    }

    private static RuleMatch NoMatch() => new() { Rule = null, L7Protocol = L7Protocol.None };
}
