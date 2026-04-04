using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using ZUI.ViewModels;

namespace ZUI.Views;

public sealed partial class DashboardPage : Page
{
 public DashboardViewModel ViewModel { get; }

public DashboardPage()
	{
		ViewModel = App.Services.GetRequiredService<DashboardViewModel>();
		this.InitializeComponent();
	}

protected override void OnNavigatedTo(NavigationEventArgs e)
{
	base.OnNavigatedTo(e);
	ViewModel.SetDispatcherQueue(this.DispatcherQueue);

	ViewModel.NavigateToSetup -= OnNavigateToSetup;
	ViewModel.NavigateToSetup += OnNavigateToSetup;
	ViewModel.NavigateToStrategies -= OnNavigateToStrategies;
	ViewModel.NavigateToStrategies += OnNavigateToStrategies;
	ViewModel.NavigateToUpdates -= OnNavigateToUpdates;
	ViewModel.NavigateToUpdates += OnNavigateToUpdates;
	ViewModel.NavigateToSettings -= OnNavigateToSettings;
	ViewModel.NavigateToSettings += OnNavigateToSettings;
}

protected override void OnNavigatedFrom(NavigationEventArgs e)
{
	base.OnNavigatedFrom(e);
	ViewModel.NavigateToSetup -= OnNavigateToSetup;
	ViewModel.NavigateToStrategies -= OnNavigateToStrategies;
	ViewModel.NavigateToUpdates -= OnNavigateToUpdates;
	ViewModel.NavigateToSettings -= OnNavigateToSettings;
}

private void OnNavigateToSetup() => MainWindow.Instance?.NavigateTo("setup");
private void OnNavigateToStrategies() => MainWindow.Instance?.NavigateTo("strategies");
private void OnNavigateToUpdates() => MainWindow.Instance?.NavigateTo("updates");
private void OnNavigateToSettings() => MainWindow.Instance?.NavigateToSettings();
}
