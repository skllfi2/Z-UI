// ═══════════════════════════════════════════════════════════════
// ZUI.Proxy / Client / ProxyAuth.cs
// Аутентификация прокси: None, UserPass
// ═══════════════════════════════════════════════════════════════

namespace ZUI.Proxy.Client;

/// <summary>
/// Метод аутентификации прокси.
/// </summary>
public abstract class ProxyAuth
{
    /// <summary>Без аутентификации.</summary>
    public static ProxyAuth None { get; } = new NoAuth();

    /// <summary>Имя пользователя + пароль.</summary>
    public static ProxyAuth UserPass(string username, string password) => new UserPassAuth(username, password);

    /// <summary>Требуется ли аутентификация?</summary>
    public bool RequiresAuth { get; init; }

    /// <summary>Имя пользователя.</summary>
    public string? Username { get; init; }

    /// <summary>Пароль.</summary>
    public string? Password { get; init; }
}

/// <summary>
/// Без аутентификации.
/// </summary>
internal sealed class NoAuth : ProxyAuth
{
    public NoAuth() { RequiresAuth = false; }
}

/// <summary>
/// Аутентификация по имени пользователя и паролю (RFC 1929).
/// </summary>
internal sealed class UserPassAuth : ProxyAuth
{
    public UserPassAuth(string username, string password)
    {
        RequiresAuth = true;
        Username = username;
        Password = password;
    }
}
