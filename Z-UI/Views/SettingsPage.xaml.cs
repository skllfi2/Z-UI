// SettingsPage.xaml.cs - Thin code-behind for MVVM settings page
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using ZUI.ViewModels;

namespace ZUI.Views;

/// <summary>
/// Settings page — thin code-behind.
/// All logic is in SettingsViewModel. This file only:
/// 1. Creates/assigns ViewModel from DI
/// 2. Wires theme change requests to MainWindow
/// 3. Shows ContentDialogs on ViewModel request
/// </summary>
public sealed partial class SettingsPage : BasePage
{
    public SettingsViewModel ViewModel { get; }

    public SettingsPage()
    {
        InitializeComponent();

        ViewModel = App.Services.GetRequiredService<SettingsViewModel>();
        DataContext = ViewModel;

        // Wire theme change request from ViewModel → MainWindow
        ViewModel.ThemeChangeRequested += OnThemeChangeRequested;

        // Wire dialog request from ViewModel → view
        ViewModel.DialogRequested += OnDialogRequested;

        // Apply sound setting
        ElementSoundPlayer.State = ViewModel.SoundEffects
            ? ElementSoundPlayerState.On
            : ElementSoundPlayerState.Off;
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        ViewModel.SetDispatcherQueue(DispatcherQueue.GetForCurrentThread());
    }

    private void OnThemeChangeRequested(ElementTheme theme)
    {
        if ((App.Current as App)?.MainWindow?.Content is FrameworkElement fe)
        {
            fe.RequestedTheme = theme;
        }
    }

    private async Task<bool> OnDialogRequested(string title, string message, string primaryText, string closeText)
    {
        var dialog = new ContentDialog
        {
            Title = title,
            Content = message,
            PrimaryButtonText = primaryText,
            CloseButtonText = closeText,
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = this.XamlRoot
        };

        var result = await dialog.ShowAsync();
        return result == ContentDialogResult.Primary;
    }
}
