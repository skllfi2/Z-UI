// GeneratorPage.xaml.cs - Code-behind for generator page
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using ZUI.ViewModels;
using ZUI.Models;

namespace ZUI.Views;

/// <summary>
/// Strategy generator page.
/// Back navigation is handled by PageHeader control via INavigationService.
/// </summary>
public sealed partial class GeneratorPage : BasePage
{
    public GeneratorViewModel ViewModel { get; }
    private bool _isInitialized;

    public GeneratorPage()
    {
        InitializeComponent();

        // Get ViewModel from DI
        ViewModel = App.Services.GetService(typeof(GeneratorViewModel)) as GeneratorViewModel
            ?? throw new InvalidOperationException("GeneratorViewModel not registered in DI");

        DataContext = ViewModel;

        // Listen for ChangeProvider dialog open requests
        ViewModel.PropertyChanged += ViewModel_PropertyChanged;
    }

    private void ViewModel_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ViewModel.IsChangeProviderDialogOpen) && ViewModel.IsChangeProviderDialogOpen)
        {
            _ = ChangeProviderDialog.ShowAsync();
        }
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);

        // Set DispatcherQueue for VM — DI singletons are created on thread pool.
        ViewModel.SetDispatcherQueue(Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread());

        if (!_isInitialized)
        {
            _isInitialized = true;
            try
            {
                await ViewModel.InitializeAsync();
            }
            catch (InvalidOperationException ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to initialize GeneratorViewModel: {ex.Message}");
            }
            catch (HttpRequestException ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to initialize GeneratorViewModel: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Handle service selection changes - sync to ViewModel
    /// </summary>
    private void ServicesGridView_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ViewModel == null) return;

        // Update ViewModel's SelectedServices collection
        foreach (var item in e.AddedItems)
        {
            if (item is ServiceConfig service && !ViewModel.SelectedServices.Contains(service))
            {
                ViewModel.SelectedServices.Add(service);
            }
        }

        foreach (var item in e.RemovedItems)
        {
            if (item is ServiceConfig service)
            {
                ViewModel.SelectedServices.Remove(service);
            }
        }

        // Notify CanRunTest changed
        ViewModel.NotifyCanRunTestChanged();
    }

    /// <summary>
    /// Add custom domain from TextBox
    /// </summary>
    private void AddCustomDomain_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel == null || string.IsNullOrWhiteSpace(CustomDomainTextBox.Text))
            return;

        // Parse domains (comma-separated)
        var domains = CustomDomainTextBox.Text
            .Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(d => d.Trim())
            .Where(d => !string.IsNullOrWhiteSpace(d));

        foreach (var domain in domains)
        {
            ViewModel.AddCustomDomain(domain);
        }

        CustomDomainTextBox.Text = string.Empty;
    }

    /// <summary>
    /// Remove custom domain
    /// </summary>
    private void RemoveCustomDomain_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel == null || sender is not Button button)
            return;

        if (button.Tag is string domain)
        {
            ViewModel.RemoveCustomDomain(domain);
        }
    }

    private void ChangeProviderDialog_Opened(ContentDialog sender, ContentDialogOpenedEventArgs args)
    {
        // Dialog is now open — nothing extra needed
    }

    private void ChangeProviderDialog_PrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        ViewModel.ConfirmProviderChange();
    }
}
