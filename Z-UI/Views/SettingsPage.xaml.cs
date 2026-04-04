using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using ZUI.ViewModels;

namespace ZUI.Views;

public sealed partial class SettingsPage : Page
{
	public SettingsViewModel ViewModel { get; }

	public SettingsPage()
	{
		ViewModel = App.Services.GetRequiredService<SettingsViewModel>();
		this.InitializeComponent();
	}

	protected override void OnNavigatedTo(NavigationEventArgs e)
	{
		base.OnNavigatedTo(e);
		ViewModel.SetDispatcherQueue(this.DispatcherQueue);
		ViewModel.ThemeChanged -= OnThemeChanged;
		ViewModel.ThemeChanged += OnThemeChanged;

		if (MainWindow.Instance != null)
		{
			MainWindow.Instance.SetCurrentSettingsViewModel(null);
		}
	}

	protected override void OnNavigatedFrom(NavigationEventArgs e)
	{
		base.OnNavigatedFrom(e);
		ViewModel.ThemeChanged -= OnThemeChanged;
	}

	private void OnThemeChanged()
	{
		if (MainWindow.Instance != null)
		{
			MainWindow.Instance.ReloadAllPages();
		}
	}
}
