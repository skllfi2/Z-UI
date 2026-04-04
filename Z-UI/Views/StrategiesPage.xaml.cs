using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using ZUI.ViewModels;
using ZUI.Services;

namespace ZUI.Views;

public sealed partial class StrategiesPage : Page
{
    public StrategiesViewModel ViewModel { get; }

    public StrategiesPage()
    {
        ViewModel = App.Services.GetRequiredService<StrategiesViewModel>();
        this.InitializeComponent();
        TestResultStore.ResultsUpdated += OnResultsUpdated;
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        ViewModel.SetDispatcherQueue(this.DispatcherQueue);
        ViewModel.ReloadStrategies();
    }

    private void OnResultsUpdated()
    {
        DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Normal, () =>
        {
            ViewModel.ReloadStrategies();
        });
    }
}
