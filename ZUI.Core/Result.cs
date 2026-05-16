namespace ZUI.Core;

/// <summary>
/// Результат операции без возвращаемого значения.
/// Never throw exceptions for expected failures — return Failed result.
/// </summary>
public readonly struct Result
{
    public bool IsSuccess { get; init; }
    public string? Error { get; init; }

    public static Result Success() => new() { IsSuccess = true };
    public static Result Failed(string error) => new() { IsSuccess = false, Error = error };

    public override string ToString() => IsSuccess ? "Success" : $"Failed: {Error}";
}

/// <summary>
/// Результат операции с возвращаемым значением.
/// </summary>
public readonly struct Result<T>
{
    public bool IsSuccess { get; init; }
    public string? Error { get; init; }
    public T Value { get; init; }

    public static Result<T> Success(T value) => new() { IsSuccess = true, Value = value };
    public static Result<T> Failed(string error) => new() { IsSuccess = false, Error = error };

    public static implicit operator Result(Result<T> r) =>
        r.IsSuccess ? Result.Success() : Result.Failed(r.Error!);

    public override string ToString() => IsSuccess ? $"Success: {Value}" : $"Failed: {Error}";
}
