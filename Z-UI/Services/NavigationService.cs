// NavigationService.cs - Frame-based navigation service implementation
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Navigation;

namespace ZUI.Services;

/// <summary>
/// Frame-based navigation service. Wraps a WinUI 3 Frame and provides
/// MVVM-friendly navigation without direct Window/Frame coupling.
/// </summary>
public sealed class NavigationService : INavigationService
{
    private Frame? _frame;
    private DispatcherQueue? _dispatcherQueue;

    public event EventHandler<NavigatedEventArgs>? Navigated;

    /// <inheritdoc/>
    public bool CanGoBack => _frame?.CanGoBack ?? false;

    /// <inheritdoc/>
    public Type? CurrentPage => _frame?.Content?.GetType();

    /// <summary>
    /// Initialize the service with a Frame instance. Must be called once from MainWindow
    /// after InitializeComponent, before any navigation calls.
    /// </summary>
    /// <param name="frame">The Frame that will host page navigation.</param>
    public void Initialize(Frame frame)
    {
        _frame = frame ?? throw new ArgumentNullException(nameof(frame));
        _dispatcherQueue = DispatcherQueue.GetForCurrentThread();

        // Subscribe to Frame's Navigated event to forward to our consumers
        _frame.Navigated += OnFrameNavigated;
    }

    /// <inheritdoc/>
    public void NavigateTo(Type pageType, object? parameter = null)
    {
        if (_frame is null)
            throw new InvalidOperationException("NavigationService not initialized. Call Initialize() with a Frame first.");

        RunOnUIThread(() =>
        {
            // Avoid re-navigating to the same page
            if (_frame.Content?.GetType() == pageType)
                return;

            // Use EntranceNavigationTransitionInfo for smooth page transitions.
            // Page.Transitions with EntranceThemeTransition is broken in WinUI 3
            // (GitHub #9482, #8879) — causes InvalidOperationException/COMException.
            _frame.Navigate(pageType, parameter, new EntranceNavigationTransitionInfo());
        });
    }

    /// <inheritdoc/>
    public void GoBack()
    {
        if (_frame is null)
            throw new InvalidOperationException("NavigationService not initialized. Call Initialize() with a Frame first.");

        RunOnUIThread(() =>
        {
            if (_frame.CanGoBack)
            {
                _frame.GoBack();
            }
        });
    }

    private void OnFrameNavigated(object sender, NavigationEventArgs e)
    {
        var args = new NavigatedEventArgs
        {
            SourcePageType = e.SourcePageType,
            TargetPageType = e.Content?.GetType() ?? typeof(object),
            Parameter = e.Parameter
        };

        Navigated?.Invoke(this, args);
    }

    /// <summary>
    /// Ensures the action runs on the UI thread. If already on UI thread, executes directly.
    /// Otherwise, enqueues via DispatcherQueue.
    /// </summary>
    private void RunOnUIThread(Action action)
    {
        if (_dispatcherQueue?.HasThreadAccess == true)
        {
            action();
        }
        else
        {
            _dispatcherQueue?.TryEnqueue(() => action());
        }
    }
}
