// BasePage.cs - Base class for pages with visual effects
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Windows.Foundation;

namespace ZUI.Views;

/// <summary>
/// Base class for pages with visual effects.
/// Navigation transitions are handled by NavigationService via NavigationTransitionInfo
/// (see GitHub #9482, #8879 — EntranceThemeTransition is broken in WinUI 3).
/// </summary>
public class BasePage : Page
{
    protected bool _animationsEnabled = true;

    public BasePage()
    {
        // Кэшируем страницу чтобы не пересоздавать при GoBack.
        // Каждая страница обновляет данные в OnNavigatedTo если нужно.
        // Note: NavigationCacheMode.Required is broken in WinUI 3 (GitHub #2707).
        NavigationCacheMode = Microsoft.UI.Xaml.Navigation.NavigationCacheMode.Enabled;
    }

    /// <summary>
    /// Animate a card hover effect using lightweight scale transform
    /// Note: Avoiding Composition APIs in constructor to prevent COM exceptions
    /// </summary>
    protected void AnimateCardHover(Border card, bool isEntering)
    {
        if (card == null) return;

        // Use Storyboard-based animations (safer than Composition APIs during page construction)
        var storyboard = new Storyboard();

        // Scale animation
        var scaleXAnim = new DoubleAnimation
        {
            To = isEntering ? 1.02 : 1.0,
            Duration = TimeSpan.FromMilliseconds(150),
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
        };
        Storyboard.SetTarget(scaleXAnim, card);
        Storyboard.SetTargetProperty(scaleXAnim, "(UIElement.RenderTransform).(ScaleTransform.ScaleX)");
        storyboard.Children.Add(scaleXAnim);

        var scaleYAnim = new DoubleAnimation
        {
            To = isEntering ? 1.02 : 1.0,
            Duration = TimeSpan.FromMilliseconds(150),
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
        };
        Storyboard.SetTarget(scaleYAnim, card);
        Storyboard.SetTargetProperty(scaleYAnim, "(UIElement.RenderTransform).(ScaleTransform.ScaleY)");
        storyboard.Children.Add(scaleYAnim);

        // Ensure render transform exists
        if (card.RenderTransform == null || card.RenderTransform is MatrixTransform)
        {
            card.RenderTransform = new ScaleTransform { ScaleX = 1, ScaleY = 1 };
            card.RenderTransformOrigin = new Point(0.5, 0.5);
        }

        storyboard.Begin();
    }

    /// <summary>
    /// Animate button press effect using lightweight scale transform
    /// </summary>
    protected void AnimateButtonPress(Button button, bool isPressed)
    {
        if (button == null) return;

        // Ensure button has scale transform
        if (button.RenderTransform == null || button.RenderTransform is MatrixTransform)
        {
            button.RenderTransform = new ScaleTransform { ScaleX = 1, ScaleY = 1 };
            button.RenderTransformOrigin = new Point(0.5, 0.5);
        }

        var storyboard = new Storyboard();

        var scaleXAnim = new DoubleAnimation
        {
            To = isPressed ? 0.95 : 1.0,
            Duration = TimeSpan.FromMilliseconds(isPressed ? 50 : 100),
            EasingFunction = isPressed
                ? new QuadraticEase { EasingMode = EasingMode.EaseOut }
                : new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.3 }
        };
        Storyboard.SetTarget(scaleXAnim, button);
        Storyboard.SetTargetProperty(scaleXAnim, "(UIElement.RenderTransform).(ScaleTransform.ScaleX)");
        storyboard.Children.Add(scaleXAnim);

        var scaleYAnim = new DoubleAnimation
        {
            To = isPressed ? 0.95 : 1.0,
            Duration = TimeSpan.FromMilliseconds(isPressed ? 50 : 100),
            EasingFunction = isPressed
                ? new QuadraticEase { EasingMode = EasingMode.EaseOut }
                : new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.3 }
        };
        Storyboard.SetTarget(scaleYAnim, button);
        Storyboard.SetTargetProperty(scaleYAnim, "(UIElement.RenderTransform).(ScaleTransform.ScaleY)");
        storyboard.Children.Add(scaleYAnim);

        storyboard.Begin();
    }
}
