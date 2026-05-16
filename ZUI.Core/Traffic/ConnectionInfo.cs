// ═══════════════════════════════════════════════════════════════
// ZUI.Proxy / Traffic / ConnectionInfo.cs
/// Модель активного/завершённого соединения для IPC и UI
///.Immutable record для ring buffer в ProxifierEngine
// ═══════════════════════════════════════════════════════════════

using System.Net;

namespace ZUI.Core.Traffic;

/// <summary>
/// Информация о соединении проксификатора (для live events).
/// Используется в ring buffer и передаётся через IPC в UI.
/// </summary>
public sealed record ConnectionInfo
{
    /// <summary>Уникальный ID соединения (8 символов hex).</summary>
    public required string ConnectionId { get; init; }

    /// <summary>PID процесса.</summary>
    public required int Pid { get; init; }

    /// <summary>Имя процесса.</summary>
    public required string ProcessName { get; init; }

    /// <summary>Целевой хост (домен если доступен, иначе IP).</summary>
    public required string TargetHost { get; init; }

    /// <summary>Целевой порт.</summary>
    public required int TargetPort { get; init; }

    /// <summary>Целевой IP (оригинальный, из WinDivert).</summary>
    public required string TargetIp { get; init; }

    /// <summary>Действие (Proxy/Chain/Direct/Block).</summary>
    public required string Action { get; init; }

    /// <summary>Имя прокси-сервера или цепочки (если применимо).</summary>
    public string? ProxyName { get; init; }

    /// <summary>Политика DNS (Local/ThroughProxy).</summary>
    public required string DnsPolicy { get; init; }

    /// <summary>Время установки соединения (UTC).</summary>
    public required DateTime StartedAt { get; init; }

    /// <summary>Время закрытия (null = активное).</summary>
    public DateTime? EndedAt { get; init; }

    /// <summary>Байт отправлено (upstream).</summary>
    public long BytesSent { get; init; }

    /// <summary>Байт получено (downstream).</summary>
    public long BytesReceived { get; init; }

    /// <summary>Статус соединения.</summary>
    public required ConnectionStatus Status { get; init; }
}

/// <summary>
/// Статус соединения проксификатора.
/// </summary>
public enum ConnectionStatus
{
    /// <summary>Подключение через прокси.</summary>
    Connecting,
    /// <summary>Активно релеится данные.</summary>
    Active,
    /// <summary>Соединение закрыто нормально.</summary>
    Closed,
    /// <summary>Соединение закрыто с ошибкой.</summary>
    Failed,
}
