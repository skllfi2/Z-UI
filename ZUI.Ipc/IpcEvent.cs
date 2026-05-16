// ═══════════════════════════════════════════════════════════════
// ZUI.Ipc / IpcEvent.cs
// События Worker → UI (unsolicited, без запроса)
// Например: статистика пакетов, ошибки, подключения
// ═══════════════════════════════════════════════════════════════

namespace ZUI.Ipc;

/// <summary>
/// Базовый тип события Worker → UI (unsolicited).
/// </summary>
public abstract record IpcEvent : IpcMessage;

/// <summary>Статистика пакетов (отправляется периодически).</summary>
public sealed record PacketStatsEvent(
    int PacketsPerSecond,
    long TotalPackets,
    long BytesPerSecond) : IpcEvent;

/// <summary>Обход DPI остановлен (аварийно или по ошибке).</summary>
public sealed record BypassStoppedEvent(string Reason) : IpcEvent;

/// <summary>Запись лога от Worker.</summary>
public sealed record LogEntryEvent(
    int LogLevel,
    string Message,
    DateTimeOffset EventTimestamp) : IpcEvent;

/// <summary>Клиент подключился к Telegram proxy.</summary>
public sealed record TgProxyClientConnectedEvent(string ClientIp) : IpcEvent;

/// <summary>Обнаружена блокировка (passive detection).</summary>
public sealed record BlockDetectedEvent(
    string Target,
    string Type,
    string Confidence,
    string Description,
    int Occurrences) : IpcEvent;
