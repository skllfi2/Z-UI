// SystemBackdropElement.cs - Container for WinUI 3 system backdrop elements (MicaBackdrop, etc.)
// MicaBackdrop is a SystemBackdrop (DependencyObject), NOT a UIElement.
// This control accepts it as a DP so XAML compiles; the visual Mica effect
// is provided by the parent window's SystemBackdrop showing through this transparent container.
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace ZUI.Controls;

/// <summary>
/// Transparent container that accepts a SystemBackdrop (e.g. MicaBackdrop)
/// as a dependency property. The actual backdrop effect comes from the
/// parent window's Mica showing through this transparent Grid.
/// </summary>
public class SystemBackdropElement : Grid
{
    public static readonly DependencyProperty SystemBackdropProperty =
        DependencyProperty.Register(
            nameof(SystemBackdrop),
            typeof(SystemBackdrop),
            typeof(SystemBackdropElement),
            new PropertyMetadata(null));

    /// <summary>
    /// The system backdrop element (e.g. MicaBackdrop, DesktopAcrylicBackdrop).
    /// Stored for XAML compatibility; visual effect is from the window-level backdrop.
    /// </summary>
    public SystemBackdrop? SystemBackdrop
    {
        get => (SystemBackdrop?)GetValue(SystemBackdropProperty);
        set => SetValue(SystemBackdropProperty, value);
    }
}
