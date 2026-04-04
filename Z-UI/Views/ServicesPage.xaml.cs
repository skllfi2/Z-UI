using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using ZUI.ViewModels;
using ZUI.Services;

namespace ZUI.Views;

public sealed partial class ServicesPage : Page
{
    public ServicesViewModel ViewModel { get; }

    public ServicesPage()
    {
        ViewModel = App.Services.GetRequiredService<ServicesViewModel>();
        this.InitializeComponent();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        ViewModel.SetDispatcherQueue(this.DispatcherQueue);
        
        ViewModel.TestCompleted -= OnTestCompleted;
        ViewModel.TestCompleted += OnTestCompleted;
    }

    protected override void OnNavigatingFrom(NavigatingCancelEventArgs e)
    {
        base.OnNavigatingFrom(e);
        ViewModel.TestCompleted -= OnTestCompleted;
    }

    private void OnTestCompleted(string bestStrategy)
    {
        DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Normal, async () =>
        {
            var app = Application.Current as App;
            var mainWindow = app?.MainWindow as MainWindow;
            if (mainWindow == null) return;

            var stackPanel = new StackPanel { Spacing = 16 };

            var iconBorder = new Border
            {
                Width = 56,
                Height = 56,
                CornerRadius = new Microsoft.UI.Xaml.CornerRadius(28),
                Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 76, 195, 125)),
                HorizontalAlignment = Microsoft.UI.Xaml.HorizontalAlignment.Center
            };
            
            var accentBrush = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 76, 195, 125));
            iconBorder.Background = accentBrush;

            var icon = new FontIcon
            {
                Glyph = "\uE73E",
                FontSize = 24,
                Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 255, 255, 255)),
                HorizontalAlignment = Microsoft.UI.Xaml.HorizontalAlignment.Center,
                VerticalAlignment = Microsoft.UI.Xaml.VerticalAlignment.Center
            };
            iconBorder.Child = icon;
            
            stackPanel.Children.Add(iconBorder);

            var titleBlock = new TextBlock
            {
                Text = LocalizationService.Get("TestingComplete"),
                Style = (Microsoft.UI.Xaml.Style)Application.Current.Resources["SubtitleTextBlockStyle"],
                HorizontalAlignment = Microsoft.UI.Xaml.HorizontalAlignment.Center
            };
            stackPanel.Children.Add(titleBlock);

            var strategyBlock = new TextBlock
            {
                Text = bestStrategy,
                Style = (Microsoft.UI.Xaml.Style)Application.Current.Resources["BodyStrongTextBlockStyle"],
                HorizontalAlignment = Microsoft.UI.Xaml.HorizontalAlignment.Center,
                TextWrapping = Microsoft.UI.Xaml.TextWrapping.Wrap,
                HorizontalTextAlignment = Microsoft.UI.Xaml.TextAlignment.Center
            };
            stackPanel.Children.Add(strategyBlock);

            var hintBlock = new TextBlock
            {
                Text = LocalizationService.Get("BestStrategyHint"),
                Style = (Microsoft.UI.Xaml.Style)Application.Current.Resources["CaptionTextBlockStyle"],
                HorizontalAlignment = Microsoft.UI.Xaml.HorizontalAlignment.Center,
                Opacity = 0.6,
                TextWrapping = Microsoft.UI.Xaml.TextWrapping.Wrap,
                HorizontalTextAlignment = Microsoft.UI.Xaml.TextAlignment.Center
            };
            stackPanel.Children.Add(hintBlock);

            var dialog = new ContentDialog
            {
                Content = stackPanel,
                PrimaryButtonText = LocalizationService.Get("Apply"),
                CloseButtonText = LocalizationService.Get("Skip"),
                XamlRoot = mainWindow.Content.XamlRoot,
                DefaultButton = ContentDialogButton.Primary
            };

            var result = await dialog.ShowAsync();
            if (result == ContentDialogResult.Primary)
            {
                AppSettings.SetCurrentStrategy(bestStrategy);
            }
        });
    }
}
