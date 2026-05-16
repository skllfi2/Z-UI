// DashboardPage.xaml.cs - Main dashboard page with INavigationService
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using ZUI.Services;
using ZUI.ViewModels;

namespace ZUI.Views;

public sealed partial class DashboardPage : BasePage
{
    public DashboardViewModel? ViewModel { get; private set; }
    private bool _isInitialized;

    public DashboardPage()
    {
        InitializeComponent();

        try
        {
            ViewModel = App.Services.GetService(typeof(DashboardViewModel)) as DashboardViewModel;
            if (ViewModel != null)
            {
                DataContext = ViewModel;
                ViewModel.NavigateToSetup += () => GetNavigationService()?.NavigateTo(typeof(GeneratorPage));
            }
        }
        catch (InvalidOperationException)
        {
            // ViewModel binding failed — page will show with default state
        }
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);

        // Initialize only once — subsequent navigations back to Dashboard
        // show cached state instantly. DashboardViewModel refreshes status
        // via its own 5-second DispatcherTimer anyway.
        if (_isInitialized) return;
        _isInitialized = true;

        if (ViewModel != null)
        {
            try
            {
                ViewModel.SetDispatcherQueue(Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread());
                await ViewModel.InitializeAsync();
            }
            catch (InvalidOperationException ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to initialize DashboardViewModel: {ex.Message}");
            }
            catch (System.Runtime.InteropServices.COMException ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to initialize DashboardViewModel (COM): {ex.Message}");
            }
        }
    }

    private INavigationService? GetNavigationService()
    => App.Services.GetService(typeof(INavigationService)) as INavigationService;

    // Navigation handlers — 4 cards
    private void GoToGenerator(object sender, PointerRoutedEventArgs e)
    {
        GetNavigationService()?.NavigateTo(typeof(GeneratorPage));
    }

    private void GoToNetwork(object sender, PointerRoutedEventArgs e)
    {
        GetNavigationService()?.NavigateTo(typeof(NetworkPage));
    }

    private void GoToDiagnostics(object sender, PointerRoutedEventArgs e)
    {
        GetNavigationService()?.NavigateTo(typeof(DiagnosticsPage));
    }

    private void GoToSettings(object sender, PointerRoutedEventArgs e)
    {
        GetNavigationService()?.NavigateTo(typeof(SettingsPage));
    }

    private void GoToProxifier(object sender, PointerRoutedEventArgs e)
    {
        GetNavigationService()?.NavigateTo(typeof(ProxifierPage));
    }

    private void GoToAbout(object sender, PointerRoutedEventArgs e)
    {
        GetNavigationService()?.NavigateTo(typeof(AboutPage));
    }

    private void BackButton_Click(object sender, RoutedEventArgs e)
    {
        GetNavigationService()?.GoBack();
    }

    // Card hover effects with animations
    private void Card_PointerEntered(object sender, PointerRoutedEventArgs e)
    {
        if (sender is Border border)
        {
            border.Background = (Brush)Application.Current.Resources["CardBackgroundFillColorSecondaryBrush"];
            AnimateCardHover(border, true);
        }
    }

    private void Card_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        if (sender is Border border)
        {
            border.Background = new Microsoft.UI.Xaml.Media.MediaBrush();
            AnimateCardHover(border, false);
        }
    }

    // Button press animations
    private void ToggleButton_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (sender is Button button)
        {
            AnimateButtonPress(button, true);
        }
    }

    private void ToggleButton_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (sender is Button button)
        {
            AnimateButtonPress(button, false);
        }
    }
}
