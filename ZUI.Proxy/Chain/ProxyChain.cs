// ═══════════════════════════════════════════════════════════════
// ZUI.Proxy / Chain / ProxyChain.cs
// Именованная цепочка прокси-серверов для последовательного
// прохождения трафика: app → proxy1 → proxy2 → ... → target
// ═══════════════════════════════════════════════════════════════

using ZUI.Proxy.Rules;

namespace ZUI.Proxy.Chain;

/// <summary>
/// Именованная цепочка прокси: трафик проходит через все узлы
/// последовательно (proxy1 → proxy2 → ... → target).
/// Последний узел — всегда конечный прокси, через который
/// устанавливается CONNECT к целевому хосту.
/// </summary>
public sealed class ProxyChain
{
    /// <summary>Уникальное имя цепочки (для ссылки из ProxyRule.ChainName).</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>Узлы цепочки в порядке прохождения (от первого к последнему).</summary>
    public List<ProxyTarget> Nodes { get; init; } = [];

    /// <summary>Включена ли цепочка.</summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>Количество узлов в цепочке.</summary>
    public int Count => Nodes.Count;

    /// <summary>Пустая ли цепочка?</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsEmpty => Nodes.Count == 0;

    public override string ToString()
    {
        var chain = string.Join(" → ", Nodes.Select(n => n.ToString()));
        return $"Chain[{Name}]: {chain}";
    }
}
