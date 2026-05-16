// PageHeader.xaml.cs - Sticky page header with back button, title, and visual states
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using ZUI.Services;

namespace ZUI.Controls;

/// <summary>
/// Sticky page header with back button, title, and optional subtitle/actions.
/// When placed inside a ScrollViewer with StickyHeaderHelper, the header
/// transitions to "sticky" visual state (layer background + divider) on scroll.
/// </summary>
public sealed partial class PageHeader : UserControl, INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    public PageHeader()
    {
        this.InitializeComponent();
    }

    // ─── Dependency Properties ───

    public static readonly DependencyProperty TitleProperty =
        DependencyProperty.Register(nameof(Title), typeof(string), typeof(PageHeader),
            new PropertyMetadata(string.Empty, OnTitleChanged));

    public static readonly DependencyProperty SubtitleProperty =
        DependencyProperty.Register(nameof(Subtitle), typeof(string), typeof(PageHeader),
            new PropertyMetadata(string.Empty, OnSubtitleChanged));

    public static readonly DependencyProperty ActionsProperty =
        DependencyProperty.Register(nameof(Actions), typeof(object), typeof(PageHeader),
            new PropertyMetadata(null));

    public static readonly DependencyProperty IsStickyProperty =
        DependencyProperty.Register(nameof(IsSticky), typeof(bool), typeof(PageHeader),
            new PropertyMetadata(false, OnIsStickyChanged));

    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public string Subtitle
    {
        get => (string)GetValue(SubtitleProperty);
        set => SetValue(SubtitleProperty, value);
    }

    public object? Actions
    {
        get => GetValue(ActionsProperty);
        set => SetValue(ActionsProperty, value);
    }

    /// <summary>
    /// When true, header is in "sticky" mode — shows layer background and divider.
    /// Set by StickyHeaderHelper based on scroll position.
    /// </summary>
    public bool IsSticky
    {
        get => (bool)GetValue(IsStickyProperty);
        set => SetValue(IsStickyProperty, value);
    }

    // Computed property for subtitle visibility
    public bool HasSubtitle => !string.IsNullOrEmpty(Subtitle);

    private static void OnTitleChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        ((PageHeader)d).NotifyPropertyChanged(nameof(Title));
    }

    private static void OnSubtitleChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var header = (PageHeader)d;
        header.NotifyPropertyChanged(nameof(Subtitle));
        header.NotifyPropertyChanged(nameof(HasSubtitle));
    }

    private static void OnIsStickyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var header = (PageHeader)d;
        var isSticky = (bool)e.NewValue;
        VisualStateManager.GoToState(header, isSticky ? "StickyState" : "NormalState", true);
    }

    // ─── Back Button ───

    private void BackButton_Click(object sender, RoutedEventArgs e)
    {
        var navigationService = App.Services.GetService(typeof(INavigationService)) as INavigationService;
        if (navigationService?.CanGoBack == true)
        {
            navigationService.GoBack();
        }
    }

    /// <summary>
    /// Sets the visibility of the back button within the PageHeader.
    /// Called by StickyHeaderHelper when the floating back button in TitleBar is shown/hidden.
    /// </summary>
    public void SetBackButtonVisibility(bool visible)
    {
        if (BackButton != null)
            BackButton.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>
    /// Sets the visibility of the title and subtitle text in the PageHeader.
    /// Called by StickyHeaderHelper when the floating back button moves to TitleBar
    /// to avoid duplication of the page title.
    /// </summary>
    public void SetTitleVisibility(bool visible)
    {
        if (TitleTextBlock != null)
            TitleTextBlock.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        if (SubtitleTextBlock != null)
            SubtitleTextBlock.Visibility = visible && HasSubtitle ? Visibility.Visible : Visibility.Collapsed;
    }

    private void NotifyPropertyChanged([CallerMemberName] string propertyName = "")
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
