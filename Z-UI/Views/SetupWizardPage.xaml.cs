using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using ZUI.ViewModels;

namespace ZUI.Views;

public sealed partial class SetupWizardPage : Page
{
	public SetupWizardViewModel ViewModel { get; }

	public SetupWizardPage()
	{
		ViewModel = App.Services.GetRequiredService<SetupWizardViewModel>();
		this.InitializeComponent();
	}

	protected override void OnNavigatedTo(NavigationEventArgs e)
	{
		base.OnNavigatedTo(e);
		ViewModel.OnCompletion -= OnSetupComplete;
		ViewModel.OnCompletion += OnSetupComplete;
	}

	protected override void OnNavigatedFrom(NavigationEventArgs e)
	{
		base.OnNavigatedFrom(e);
		ViewModel.OnCompletion -= OnSetupComplete;
	}

	private void OnSetupComplete()
	{
		if (MainWindow.Instance != null)
		{
			MainWindow.Instance.CompleteSetup();
		}
	}
}
