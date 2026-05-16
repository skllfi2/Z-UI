// ═══════════════════════════════════════════════════════════════
// ZUI.Proxy / Chain / FailoverPolicy.cs
// Политики отказоустойчивости для прокси-цепочек и правил
// NextOnError, RoundRobin
// ═══════════════════════════════════════════════════════════════

using System.Text.Json.Serialization;
using ZUI.Proxy.Rules;

namespace ZUI.Proxy.Chain;

/// <summary>
/// Политика отказоустойчивости при ошибке подключения к прокси.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<FailoverPolicy>))]
public enum FailoverPolicy
{
    /// <summary>
    /// При ошибке — попробовать следующий прокси в списке.
    /// Если все прокси не доступны — вернуть ошибку.
    /// </summary>
    NextOnError,

    /// <summary>
    /// Round-robin: равномерное распределение между прокси.
    /// При ошибке — следующий по кругу.
    /// </summary>
    RoundRobin,

    /// <summary>
    /// Нет отказоустойчивости: первая ошибка = провал.
    /// </summary>
    None,
}

/// <summary>
/// Селектор прокси с поддержкой отказоустойчивости.
/// Управляет выбором прокси из списка альтернатив по политике.
/// </summary>
public sealed class FailoverSelector
{
    private readonly FailoverPolicy _policy;
    private readonly ProxyTarget[] _targets;
    private int _currentIndex;
    private int _errorCount;

    public FailoverSelector(
        ProxyTarget[] targets,
        FailoverPolicy policy = FailoverPolicy.NextOnError)
    {
        _targets = targets;
        _policy = policy;
        _currentIndex = 0;
        _errorCount = 0;
    }

    /// <summary>Количество доступных прокси.</summary>
    public int TargetCount => _targets.Length;

    /// <summary>Количество ошибок подряд.</summary>
    public int ConsecutiveErrors => _errorCount;

    /// <summary>Все прокси исчерпаны?</summary>
    public bool IsExhausted => _errorCount >= _targets.Length;

    /// <summary>
    /// Получить текущий целевой прокси.
    /// </summary>
    public ProxyTarget Current => _targets.Length > 0
        ? _targets[_currentIndex % _targets.Length]
        : throw new InvalidOperationException("No proxy targets available");

    /// <summary>
    /// Получить следующий прокси (для RoundRobin без ошибки).
    /// </summary>
    public ProxyTarget GetNext()
    {
        if (_targets.Length == 0)
            throw new InvalidOperationException("No proxy targets available");

        if (_policy == FailoverPolicy.RoundRobin)
        {
            _currentIndex = (_currentIndex + 1) % _targets.Length;
        }

        _errorCount = 0;
        return _targets[_currentIndex];
    }

    /// <summary>
    /// Сообщить об ошибке подключения и получить следующий прокси.
    /// Возвращает null, если все прокси исчерпаны.
    /// </summary>
    public ProxyTarget? OnErrorAndGetNext()
    {
        _errorCount++;

        switch (_policy)
        {
            case FailoverPolicy.NextOnError:
            case FailoverPolicy.RoundRobin:
                if (_errorCount >= _targets.Length)
                    return null; // Все прокси не доступны

                _currentIndex = (_currentIndex + 1) % _targets.Length;
                return _targets[_currentIndex];

            case FailoverPolicy.None:
            default:
                return null; // Без отказоустойчивости
        }
    }

    /// <summary>
    /// Сбросить состояние (например, при успешном подключении).
    /// </summary>
    public void Reset()
    {
        _errorCount = 0;
    }
}
