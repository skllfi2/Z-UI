// MainWindow.xaml.cs - Frame-based navigation with INavigationService
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using ZUI.Services;
using ZUI.Views;

namespace ZUI;

public sealed partial class MainWindow : Window
{
    public static MainWindow? Instance { get; private set; }
    public INavigationService? NavigationService { get; private set; }

    // Tracks current visibility state to avoid redundant storyboard triggers (prevents flash)
    private bool _isFloatingBackButtonVisible;

    public MainWindow()
    {
        InitializeComponent();

        Instance = this;

        try
        {
            ExtendsContentIntoTitleBar = true;
            SetTitleBar(AppTitleBar);

            AppWindow.TitleBar.ButtonBackgroundColor = Microsoft.UI.Colors.Transparent;
            AppWindow.TitleBar.ButtonInactiveBackgroundColor = Microsoft.UI.Colors.Transparent;

            AppWindow.Resize(new Windows.Graphics.SizeInt32(1080, 750));

            var displayArea = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary);
            var workArea = displayArea.WorkArea;
            AppWindow.Move(new Windows.Graphics.PointInt32(
                workArea.X + (workArea.Width - 1080) / 2,
                workArea.Y + (workArea.Height - 750) / 2
            ));
        }
        catch (System.Runtime.InteropServices.COMException ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Z-UI] MainWindow: TitleBar/sizing FAILED: {ex}");
        }
        catch (InvalidOperationException ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Z-UI] MainWindow: TitleBar/sizing FAILED: {ex}");
        }

        try
        {
            // Инициализация Frame-навигации
            var navigationService = App.Services.GetService(typeof(INavigationService)) as NavigationService;
            if (navigationService != null)
            {
                navigationService.Initialize(RootFrame);
                NavigationService = navigationService;

                // Reset floating back button when navigating to a new page.
                // Use immediate (no animation) hide to prevent flash —
                // the From=1 in HideBackButtonStoryboard would briefly show
                // the button even if already at Opacity=0.
                RootFrame.Navigated += (s, e) =>
                {
                    // Restore PageHeader back button/title on the page we're leaving
                    // (cached pages may have hidden these via StickyHeaderHelper on scroll)
                    if (e.Content is Page newPage)
                    {
                        var header = FindChildOfType<ZUI.Controls.PageHeader>(newPage);
                        if (header != null)
                        {
                            header.SetBackButtonVisibility(true);
                            header.SetTitleVisibility(true);
                            header.IsSticky = false;
                        }
                    }

                    // Immediately hide floating back button in TitleBar without animation.
                    // StickyHeaderHelper will re-show it on scroll if appropriate.
                    HideFloatingBackButtonImmediate();
                };

                NavigationService.NavigateTo(typeof(DashboardPage));
            }
        }
        catch (InvalidOperationException ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Z-UI] MainWindow: Navigation FAILED: {ex}");
        }
        catch (System.Runtime.InteropServices.COMException ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Z-UI] MainWindow: Navigation FAILED: {ex}");
        }
    }

    // ─── Floating Back Button Logic ───

    /// <summary>
    /// Shows or hides the floating back button in the TitleBar with smooth animation.
    /// Also shows/hides the page title alongside the back button.
    /// Skips animation if the button is already in the target state (prevents flash on navigation).
    /// </summary>
    public void SetFloatingBackButtonVisible(bool visible, string? pageTitle = null)
    {
        if (TitleBarBackButton == null || PageTitleText == null) return;

        // Skip if already in target state — prevents storyboard re-trigger causing flash
        if (_isFloatingBackButtonVisible == visible) return;
        _isFloatingBackButtonVisible = visible;

        if (visible)
        {
            // Update page title text only when showing
            if (!string.IsNullOrEmpty(pageTitle))
            {
                PageTitleText.Text = pageTitle;
            }

            // Show back button with animation
            TitleBarBackButton.IsHitTestVisible = true;
            PageTitleText.IsHitTestVisible = true;
            ShowBackButtonStoryboard?.Begin();
            HidePageTitleStoryboard?.Stop();
            ShowPageTitleStoryboard?.Begin();
        }
        else
        {
            // Hide back button with animation — do NOT update PageTitleText
            // to avoid flash when navigating from Dashboard (no PageHeader) to
            // a subpage (has PageHeader). Title is set only on scroll show.
            TitleBarBackButton.IsHitTestVisible = false;
            PageTitleText.IsHitTestVisible = false;
            HideBackButtonStoryboard?.Begin();
            HidePageTitleStoryboard?.Begin();
        }
    }

    /// <summary>
    /// Immediately hides the floating back button without animation.
    /// Called on page navigation to prevent flash (From=1 in storyboard
    /// would briefly show the button even if already at Opacity=0).
    /// </summary>
    public void HideFloatingBackButtonImmediate()
    {
        if (TitleBarBackButton == null || PageTitleText == null) return;

        // Stop any running animations
        ShowBackButtonStoryboard?.Stop();
        HideBackButtonStoryboard?.Stop();
        ShowPageTitleStoryboard?.Stop();
        HidePageTitleStoryboard?.Stop();

        // Set final state directly — no animation, no flash
        TitleBarBackButton.Opacity = 0;
        TitleBarBackButton.IsHitTestVisible = false;
        PageTitleText.Opacity = 0;
        PageTitleText.IsHitTestVisible = false;

        _isFloatingBackButtonVisible = false;
    }

    /// <summary>
    /// Returns true if the current page has a PageHeader that would show a back button.
    /// Used to determine if the floating back button should be shown on scroll.
    /// </summary>
    public bool CurrentPageHasBackButton()
    {
        if (RootFrame?.Content is not Page page) return false;

        // Check if the page has a PageHeader with a back button
        var header = FindChildOfType<ZUI.Controls.PageHeader>(page);
        return header != null && NavigationService?.CanGoBack == true;
    }

    /// <summary>
    /// Gets the title of the current page from its PageHeader if available.
    /// </summary>
    public string? GetCurrentPageTitle()
    {
        if (RootFrame?.Content is not Page page) return null;

        var header = FindChildOfType<ZUI.Controls.PageHeader>(page);
        return header?.Title;
    }

    private static T? FindChildOfType<T>(DependencyObject parent) where T : DependencyObject
    {
        for (int i = 0; i < Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(parent, i);
            if (child is T result) return result;

            var descendant = FindChildOfType<T>(child);
            if (descendant != null) return descendant;
        }
        return null;
    }

    private void TitleBarBackButton_Click(object sender, RoutedEventArgs e)
    {
        if (NavigationService?.CanGoBack == true)
        {
            NavigationService.GoBack();
        }
    }
}
