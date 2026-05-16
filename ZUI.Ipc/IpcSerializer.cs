// ═══════════════════════════════════════════════════════════════
// ZUI.Ipc / IpcSerializer.cs
// JSON сериализация IPC сообщений (полиморфная, AOT-compatible)
// Использует Source Generator (IpcJsonContext) вместо reflection
// ═══════════════════════════════════════════════════════════════

using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace ZUI.Ipc;

/// <summary>
/// Сериализатор IPC сообщений (AOT-compatible).
/// Поддерживает полиморфную сериализацию/десериализацию через $type.
/// Использует IpcJsonContext (Source Generator) вместо reflection.
/// </summary>
public static class IpcSerializer
{
    /// <summary>Source Generator контекст с метаданными всех IPC типов.</summary>
    internal static IpcJsonContext JsonContext { get; }

    /// <summary>AOT-compatible TypeInfo для IpcMessage (полиморфный).</summary>
    private static readonly JsonTypeInfo<IpcMessage> MessageTypeInfo;

    static IpcSerializer()
    {
        // Базовые опции без reflection-конвертеров
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            WriteIndented = false, // Компактный формат для Named Pipes
        };

        // Source Generator контекст: передаём options в конструктор
        // ВАЖНО: после new IpcJsonContext(options) — options замораживается (frozen)
        // Поэтому TypeInfoResolver задаём ДО создания контекста
        var context = new IpcJsonContext(options);
        JsonContext = context;

        // Получаем полиморфный TypeInfo для IpcMessage
        MessageTypeInfo = context.IpcMessage;
    }

    /// <summary>
    /// Сериализовать IPC сообщение в JSON байты (AOT-compatible).
    /// </summary>
    public static byte[] Serialize(IpcMessage message)
    {
        return JsonSerializer.SerializeToUtf8Bytes(message, MessageTypeInfo);
    }

    /// <summary>
    /// Десериализовать IPC сообщение из JSON байтов (AOT-compatible).
    /// </summary>
    public static Result<IpcMessage> Deserialize(ReadOnlySpan<byte> data)
    {
        try
        {
            var message = JsonSerializer.Deserialize(data, MessageTypeInfo);
            if (message is null)
                return Result<IpcMessage>.Failed("Deserialized message is null");

            return Result<IpcMessage>.Success(message);
        }
        catch (JsonException ex)
        {
            return Result<IpcMessage>.Failed($"Failed to deserialize IPC message: {ex.Message}");
        }
    }

    /// <summary>
    /// Десериализовать IPC сообщение из строки (AOT-compatible).
    /// </summary>
    public static Result<IpcMessage> Deserialize(string json)
    {
        try
        {
            var message = JsonSerializer.Deserialize(json, MessageTypeInfo);
            if (message is null)
                return Result<IpcMessage>.Failed("Deserialized message is null");

            return Result<IpcMessage>.Success(message);
        }
        catch (JsonException ex)
        {
            return Result<IpcMessage>.Failed($"Failed to deserialize IPC message: {ex.Message}");
        }
    }

    // ── Результат (дублируем Result из ZUI.Core для независимости) ──

    /// <summary>Результат операции без исключений.</summary>
    public readonly struct Result
    {
        public bool IsSuccess { get; init; }
        public string? Error { get; init; }

        public static Result Success() => new() { IsSuccess = true };
        public static Result Failed(string error) => new() { IsSuccess = false, Error = error };
    }

    /// <summary>Результат операции со значением.</summary>
    public readonly struct Result<T>
    {
        public bool IsSuccess { get; init; }
        public T? Value { get; init; }
        public string? Error { get; init; }

        public static Result<T> Success(T value) => new() { IsSuccess = true, Value = value };
        public static Result<T> Failed(string error) => new() { IsSuccess = false, Error = error };
    }
}
