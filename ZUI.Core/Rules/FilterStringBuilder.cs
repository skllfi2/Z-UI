// ═══════════════════════════════════════════════════════════════
// ZUI.Core / Rules / FilterStringBuilder.cs
// Генерация WinDivert filter string из StrategyConfig
// Комбинирует порты из всех правил в один фильтр:
// "outbound and (tcp.DstPort == 443 or udp.DstPort == 443 ...)"
// ═══════════════════════════════════════════════════════════════

using System.Net;
using System.Text;

namespace ZUI.Core.Rules;

/// <summary>
/// Построитель WinDivert filter string из StrategyConfig.
/// WinDivert фильтр определяет КАКИЕ пакеты перехватывать.
/// Детальная фильтрация (hostlist, ipset, L7) — внутри DpiBypassEngine.
/// </summary>
public static class FilterStringBuilder
{
    /// <summary>
    /// Построить WinDivert filter string из стратегии.
    /// Объединяет порты из всех правил (TCP + UDP).
    /// Формат: "outbound and (tcp.DstPort == 80 or tcp.DstPort == 443 or udp.DstPort == 443)"
    /// </summary>
    public static Result<string> Build(StrategyConfig strategy)
    {
        if (strategy.Rules is null || strategy.Rules.Length == 0)
            return Result<string>.Failed("Strategy has no rules");

        // Если задан явный фильтр — используем его
        if (!string.IsNullOrWhiteSpace(strategy.WinDivertFilter))
            return Result<string>.Success(strategy.WinDivertFilter);

        // Собираем TCP и UDP порты из всех правил
        var tcpPorts = new HashSet<ushort>();
        var udpPorts = new HashSet<ushort>();

        foreach (var rule in strategy.Rules)
        {
            if (rule.Ports is null || rule.Ports.Length == 0)
            {
                // Нет портов = перехватывать весь трафик этого протокола
                if (rule.Protocol == FilterProtocol.Tcp)
                    tcpPorts.Add(0); // Сигнал: весь TCP
                else
                    udpPorts.Add(0); // Сигнал: весь UDP
                continue;
            }

            foreach (var range in rule.Ports)
            {
                var targetSet = rule.Protocol == FilterProtocol.Tcp ? tcpPorts : udpPorts;
                for (ushort p = range.Start; p <= range.End; p++)
                    targetSet.Add(p);
            }
        }

        if (tcpPorts.Count == 0 && udpPorts.Count == 0)
            return Result<string>.Failed("No ports defined in strategy rules");

        var sb = new StringBuilder();
        sb.Append("outbound");

        var conditions = new List<string>();

        // TCP условия
        if (tcpPorts.Contains(0))
        {
            // Весь TCP
            conditions.Add("tcp");
        }
        else if (tcpPorts.Count > 0)
        {
            var portConditions = BuildPortConditions("tcp", tcpPorts);
            if (portConditions.Length > 0)
                conditions.Add(portConditions);
        }

        // UDP условия
        if (udpPorts.Contains(0))
        {
            // Весь UDP
            conditions.Add("udp");
        }
        else if (udpPorts.Count > 0)
        {
            var portConditions = BuildPortConditions("udp", udpPorts);
            if (portConditions.Length > 0)
                conditions.Add(portConditions);
        }

        if (conditions.Count == 0)
            return Result<string>.Failed("No valid port conditions");

        sb.Append(" and (");
        sb.AppendJoin(" or ", conditions);
        sb.Append(')');

        var filter = sb.ToString();

        // Валидация фильтра
        if (!WinDivert.WinDivertInterceptor.ValidateFilter(filter))
            return Result<string>.Failed($"Generated filter is invalid: {filter}");

        return Result<string>.Success(filter);
    }

    /// <summary>
    /// Построить портовые условия для протокола.
    /// Оптимизация: диапазоны портов вместо перечисления.
    /// </summary>
    public static string BuildPortConditions(string proto, HashSet<ushort> ports)
    {
        if (ports.Count == 0)
            return string.Empty;

        // Сортируем порты и группируем в диапазоны
        var sorted = ports.Where(p => p > 0).OrderBy(p => p).ToList();
        if (sorted.Count == 0)
            return string.Empty;

        var ranges = GroupIntoRanges(sorted);
        var parts = new List<string>();

        foreach (var range in ranges)
        {
            if (range.Start == range.End)
                parts.Add($"{proto}.DstPort == {range.Start}");
            else if (range.End - range.Start <= 3)
            {
                // Небольшой диапазон — перечисляем
                for (ushort p = range.Start; p <= range.End; p++)
                    parts.Add($"{proto}.DstPort == {p}");
            }
            else
            {
                // Большой диапазон — используем >= и <=
                parts.Add($"({proto}.DstPort >= {range.Start} and {proto}.DstPort <= {range.End})");
            }
        }

        return string.Join(" or ", parts);
    }

    /// <summary>
    /// Группировать отсортированные порты в непрерывные диапазоны.
    /// </summary>
    internal static List<(ushort Start, ushort End)> GroupIntoRanges(List<ushort> sortedPorts)
    {
        var ranges = new List<(ushort Start, ushort End)>();
        if (sortedPorts.Count == 0)
            return ranges;

        ushort rangeStart = sortedPorts[0];
        ushort rangeEnd = sortedPorts[0];

        for (int i = 1; i < sortedPorts.Count; i++)
        {
            if (sortedPorts[i] == rangeEnd + 1)
            {
                rangeEnd = sortedPorts[i];
            }
            else
            {
                ranges.Add((rangeStart, rangeEnd));
                rangeStart = sortedPorts[i];
                rangeEnd = sortedPorts[i];
            }
        }

        ranges.Add((rangeStart, rangeEnd));
        return ranges;
    }

    /// <summary>
    /// Построить фильтр из явных TCP и UDP портов.
    /// Удобный метод для создания стратегий на лету.
    /// </summary>
    public static string BuildFromPorts(ushort[]? tcpPorts, ushort[]? udpPorts)
    {
        var sb = new StringBuilder();
        sb.Append("outbound");

        var conditions = new List<string>();

        if (tcpPorts is not null && tcpPorts.Length > 0)
        {
            var tcpSet = new HashSet<ushort>(tcpPorts);
            var cond = BuildPortConditions("tcp", tcpSet);
            if (cond.Length > 0)
                conditions.Add(cond);
        }

        if (udpPorts is not null && udpPorts.Length > 0)
        {
            var udpSet = new HashSet<ushort>(udpPorts);
            var cond = BuildPortConditions("udp", udpSet);
            if (cond.Length > 0)
                conditions.Add(cond);
        }

        if (conditions.Count > 0)
        {
            sb.Append(" and (");
            sb.AppendJoin(" or ", conditions);
            sb.Append(')');
        }

        return sb.ToString();
    }
}
