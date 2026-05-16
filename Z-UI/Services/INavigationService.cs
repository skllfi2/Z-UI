// INavigationService.cs - Frame-based MVVM navigation abstraction
using Microsoft.UI.Xaml.Controls;

namespace ZUI.Services;

/// <summary>
/// Abstraction over Frame-based navigation for MVVM compatibility.
/// ViewModels and Views depend on this interface, not on MainWindow or Frame directly.
/// </summary>
public interface INavigationService
{
    /// <summary>
    /// Navigate to the specified page type.
    /// </summary>
    /// <param name="pageType">The type of the page to navigate to (e.g., typeof(DashboardPage)).</param>
    /// <param name="parameter">Optional navigation parameter passed to the target page's OnNavigatedTo.</param>
    void NavigateTo(Type pageType, object? parameter = null);

    /// <summary>
    /// Navigate back to the previous page in the back stack.
    /// </summary>
    void GoBack();

    /// <summary>
    /// Whether back navigation is possible (back stack is not empty).
    /// </summary>
    bool CanGoBack { get; }

    /// <summary>
    /// The type of the currently displayed page, or null if no page is loaded.
    /// </summary>
    Type? CurrentPage { get; }

    /// <summary>
    /// Raised after a navigation completes (both forward and back).
    /// </summary>
    event EventHandler<NavigatedEventArgs>? Navigated;
}
