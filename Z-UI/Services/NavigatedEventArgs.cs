// NavigatedEventArgs.cs - Navigation event payload
namespace ZUI.Services;

/// <summary>
/// Event arguments raised after a navigation completes.
/// </summary>
public sealed class NavigatedEventArgs : EventArgs
{
    /// <summary>
    /// The page type that was navigated away from, or null if this is the first navigation.
    /// </summary>
    public Type? SourcePageType { get; init; }

    /// <summary>
    /// The page type that was navigated to.
    /// </summary>
    public Type TargetPageType { get; init; } = null!;

    /// <summary>
    /// The optional parameter passed during navigation.
    /// </summary>
    public object? Parameter { get; init; }
}
