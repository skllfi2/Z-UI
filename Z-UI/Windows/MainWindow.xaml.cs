using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using ZUI.Services;
using ZUI.Views;
using ZUI.ViewModels;
using System;
using System.Threading.Tasks;
using System.Linq;

namespace ZUI
{
 public sealed partial class MainWindow : Window
 {
 public static MainWindow? Instance { get; private set; }
 private SettingsViewModel? _currentSettingsViewModel;

public MainWindow()
	{
		this.InitializeComponent();
		Instance = this;

		ExtendsContentIntoTitleBar = true;
		SetTitleBar(AppTitleBar);

		AppWindow.TitleBar.ButtonBackgroundColor = Microsoft.UI.Colors.Transparent;
		AppWindow.TitleBar.ButtonInactiveBackgroundColor = Microsoft.UI.Colors.Transparent;

		AppWindow.Resize(new Windows.Graphics.SizeInt32(1080, 750));

		var displayArea = Microsoft.UI.Windowing.DisplayArea.GetFromWindowId(AppWindow.Id, Microsoft.UI.Windowing.DisplayAreaFallback.Primary);
		var workArea = displayArea.WorkArea;
		AppWindow.Move(new Windows.Graphics.PointInt32(
			workArea.X + (workArea.Width - 1080) / 2,
			workArea.Y + (workArea.Height - 750) / 2
		));

		if (!AppSettings.SetupCompleted)
		{
			NavigateTo("setup");
		}
		else
		{
			NavigateTo("dashboard");
			_ = CheckHostsInBackground();
		}

		ContentFrame.Navigated += (_, _) =>
		{
			NavigationViewControl.IsBackEnabled = ContentFrame.CanGoBack;
		};

		UpdateChecker.UpdateFound += version =>
		{
			DispatcherQueue.TryEnqueue(() => UpdatesBadge.Visibility = Visibility.Visible);
		};

		if (UpdateChecker.UpdateAvailable)
			UpdatesBadge.Visibility = Visibility.Visible;
	}

 private void NavigationView_Loaded(object sender, RoutedEventArgs e)
 {
 ClearAllTooltips();
 }

 private void ClearAllTooltips()
 {
 var allItems = NavigationViewControl.MenuItems
 .OfType<NavigationViewItem>()
 .Concat(NavigationViewControl.FooterMenuItems.OfType<NavigationViewItem>());

 foreach (var item in allItems)
 {
 ToolTipService.SetToolTip(item, null);
 item.Loaded += (s, _) => ToolTipService.SetToolTip(s as DependencyObject, null);
 }
 }

 public void ReloadAllPages()
 {
 var current = NavigationViewControl.SelectedItem as NavigationViewItem;
 if (current == null) return;
 var tag = current.Tag?.ToString()
 ?? (current == NavigationViewControl.SettingsItem as NavigationViewItem ? "settings" : "");
 if (!string.IsNullOrEmpty(tag))
 {
 ContentFrame.BackStack.Clear();
 NavigateTo(tag);
 }
 }

 public void NavigateTo(string tag)
 {
 if (tag == "setup")
 {
 SetNavMenuEnabled(false);
 ContentFrame.Navigate(typeof(SetupWizardPage));
 PageHeader.Text = "Настройка";
 return;
 }

 if (tag == "about")
 {
 ContentFrame.Navigate(typeof(AboutPage));
 PageHeader.Text = "О программе";
 return;
 }

 if (tag == "settings")
 {
 NavigationViewControl.SelectedItem = SettingsNavItem;
 return;
 }

 var item = NavigationViewControl.MenuItems
 .OfType<NavigationViewItem>()
 .FirstOrDefault(i => i.Tag?.ToString() == tag);

 if (item != null)
 {
 NavigationViewControl.SelectedItem = item;
 ProcessNavigation(item);
 }
 }

 private void NavigationView_BackRequested(NavigationView sender, NavigationViewBackRequestedEventArgs args)
 {
 if (ContentFrame.CanGoBack)
 ContentFrame.GoBack();
 }

	private void NavigationView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
	{
		if (args.IsSettingsSelected)
		{
			PageHeader.Text = "Параметры";
			ContentFrame.Navigate(typeof(SettingsPage));
			return;
		}

		var item = args.SelectedItem as NavigationViewItem;
		if (item?.Tag?.ToString() == "settings")
		{
			PageHeader.Text = "Параметры";
			ContentFrame.Navigate(typeof(SettingsPage));
			return;
		}

		ProcessNavigation(item);
	}

	private void NavigationView_ItemInvoked(NavigationView sender, NavigationViewItemInvokedEventArgs args)
	{
		if (args.IsSettingsInvoked)
		{
			NavigateTo("settings");
			return;
		}

		var item = args.InvokedItemContainer as NavigationViewItem;
		var tag = item?.Tag?.ToString();
		if (!string.IsNullOrEmpty(tag))
		{
			NavigateTo(tag);
		}
	}

 private NavigationViewItem? GetPreviousSelectedItem()
 {
 return NavigationViewControl.MenuItems.OfType<NavigationViewItem>()
 .FirstOrDefault(i => i.IsSelected) ??
 NavigationViewControl.FooterMenuItems.OfType<NavigationViewItem>()
 .FirstOrDefault(i => i.IsSelected);
 }

	private bool CheckUnsavedSettings()
	{
		return true;
	}

private bool _isNavigating = false;

	private void ProcessNavigation(NavigationViewItem? item)
	{
		if (_isNavigating) return;
		_isNavigating = true;
		
		if (item == null) 
		{
			_isNavigating = false;
			return;
		}

		string tag = item.Tag?.ToString() ?? "";
		PageHeader.Text = item.Content?.ToString();

		switch (tag)
		{
			case "dashboard":
				ContentFrame.Navigate(typeof(DashboardPage));
				break;
			case "strategies":
				ContentFrame.Navigate(typeof(StrategiesPage));
				break;
			case "updates":
				ContentFrame.Navigate(typeof(ServicesPage), 0);
				UpdatesBadge.Visibility = Visibility.Collapsed;
				break;
			case "about":
				ContentFrame.Navigate(typeof(AboutPage));
				break;
		}

		_isNavigating = false;
	}

private void SetNavMenuEnabled(bool enabled)
 {
 foreach (var item in NavigationViewControl.MenuItems.OfType<NavigationViewItem>())
 item.IsEnabled = enabled;
 foreach (var item in NavigationViewControl.FooterMenuItems.OfType<NavigationViewItem>())
 item.IsEnabled = enabled;
 }

 private async Task CheckHostsInBackground()
 {
 await Task.Delay(3000);
 }

 public void CompleteSetup()
 {
 SetNavMenuEnabled(true);
 NavigateTo("dashboard");
 }

 public void SetCurrentSettingsViewModel(SettingsViewModel? viewModel)
 {
 _currentSettingsViewModel = viewModel;
 }

 public void NavigateToSettings()
 {
 NavigateTo("settings");
 }
 }
}
