// DiagnosticsPage.xaml.cs - Thin code-behind for MVVM diagnostics page
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using ZUI.ViewModels;

namespace ZUI.Views;

/// <summary>
/// Diagnostics page — thin code-behind.
/// All logic is in DiagnosticsViewModel. This file only:
/// 1. Creates/assigns ViewModel from DI
/// 2. Triggers auto-diagnostics on navigation
/// </summary>
public sealed partial class DiagnosticsPage : BasePage
{
    public DiagnosticsViewModel ViewModel { get; }

    public DiagnosticsPage()
    {
        InitializeComponent();

        ViewModel = App.Services.GetRequiredService<DiagnosticsViewModel>();
        DataContext = ViewModel;
    }

    private bool _isInitialized;

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);

        // Set DispatcherQueue for VM — DI singletons are created on thread pool.
        ViewModel.SetDispatcherQueue(Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread());

        // First visit: fire-and-forget quick check — page shows instantly,
        // diagnostics results update in background.
        // Subsequent visits: page shows cached results immediately.
        if (!_isInitialized)
        {
            _isInitialized = true;
            if (ViewModel.RunQuickCheckCommand.CanExecute(null))
            {
                _ = ViewModel.RunQuickCheckCommand.ExecuteAsync(null);
            }
        }
    }
}
