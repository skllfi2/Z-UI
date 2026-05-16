// StickyHeaderHelper.cs - Attached property for ScrollViewer → PageHeader sticky behavior
// When VerticalOffset > 0, sets IsSticky=true on the referenced PageHeader
// PageHeader stays pinned at top (Grid.Row=0), but its visual state changes on scroll.
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using ZUI.Controls;

namespace ZUI.Helpers;

/// <summary>
/// Attached property that connects a ScrollViewer's scroll position
/// to a pinned PageHeader's visual state.
///
/// When the ScrollViewer is scrolled (VerticalOffset > 0), the PageHeader
/// transitions to "sticky" visual state (layer background + divider).
/// When scrolled back to top, it returns to "normal" (transparent background).
///
/// Usage in XAML:
/// &lt;Grid&gt;
/// &lt;Grid.RowDefinitions&gt;
/// &lt;RowDefinition Height="Auto"/&gt;
/// &lt;RowDefinition Height="*"/&gt;
/// &lt;/Grid.RowDefinitions&gt;
/// &lt;controls:PageHeader x:Name="HeaderControl" Grid.Row="0" .../&gt;
/// &lt;ScrollViewer Grid.Row="1"
/// helpers:StickyHeaderHelper.StickyHeader="{x:Bind HeaderControl}"&gt;
/// &lt;!-- content --&gt;
/// &lt;/ScrollViewer&gt;
/// &lt;/Grid&gt;
/// </summary>
public static class StickyHeaderHelper
{
    public static readonly DependencyProperty StickyHeaderProperty =
        DependencyProperty.RegisterAttached(
            "StickyHeader",
            typeof(PageHeader),
            typeof(StickyHeaderHelper),
            new PropertyMetadata(null, OnStickyHeaderChanged));

    public static PageHeader? GetStickyHeader(DependencyObject obj)
        => (PageHeader?)obj.GetValue(StickyHeaderProperty);

    public static void SetStickyHeader(DependencyObject obj, PageHeader? value)
        => obj.SetValue(StickyHeaderProperty, value);

    private static void OnStickyHeaderChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not ScrollViewer scrollViewer) return;

        if (e.OldValue is PageHeader)
            scrollViewer.ViewChanged -= OnViewChanged;

        if (e.NewValue is PageHeader)
            scrollViewer.ViewChanged += OnViewChanged;
    }

    private static void OnViewChanged(object? sender, ScrollViewerViewChangedEventArgs e)
    {
        if (sender is not ScrollViewer scrollViewer) return;

        var header = GetStickyHeader(scrollViewer);
        if (header == null) return;

        var shouldStick = scrollViewer.VerticalOffset > 0;
        if (header.IsSticky != shouldStick)
        {
            header.IsSticky = shouldStick;

            // Sync with MainWindow - show/hide floating back button in TitleBar
            SyncFloatingBackButton(header, shouldStick);
        }
    }

    private static void SyncFloatingBackButton(PageHeader header, bool isScrolled)
    {
        // Only show floating back button if:
        // 1. Page is scrolled (isScrolled = true)
        // 2. Current page has a back button (CanGoBack = true)
        // 3. MainWindow instance exists
        if (MainWindow.Instance == null) return;

        var navigationService = MainWindow.Instance.NavigationService;
        var canGoBack = navigationService?.CanGoBack ?? false;

        // Show floating back button in TitleBar when:
        // - User has scrolled down
        // - Navigation can go back
        var showFloatingButton = isScrolled && canGoBack;
        MainWindow.Instance.SetFloatingBackButtonVisible(showFloatingButton, header.Title);

        // Hide back button and title in PageHeader when showing in TitleBar
        // to avoid duplication
        header.SetBackButtonVisibility(!showFloatingButton);
        header.SetTitleVisibility(!showFloatingButton);
    }
}
