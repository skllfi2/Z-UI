using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using ZapretGUI.Views;

namespace ZapretGUI
{
    public sealed partial class MainWindow : Window
    {
        public static MainWindow? Instance { get; private set; }

        public MainWindow()
        {
            this.InitializeComponent();
            Instance = this;

            // Кастомный TitleBar
            ExtendsContentIntoTitleBar = true;
            SetTitleBar(AppTitleBar);

            AppWindow.TitleBar.ExtendsContentIntoTitleBar = true;
            AppWindow.TitleBar.ButtonBackgroundColor = Microsoft.UI.Colors.Transparent;
            AppWindow.TitleBar.ButtonInactiveBackgroundColor = Microsoft.UI.Colors.Transparent;

            AppWindow.TitleBar.PreferredTheme = TitleBarTheme.UseDefaultAppMode;

            // Настройки окна
            var presenter = OverlappedPresenter.Create();
            presenter.IsAlwaysOnTop = false;
            presenter.IsMaximizable = true;
            presenter.IsMinimizable = true;
            presenter.IsResizable = true;
            presenter.SetBorderAndTitleBar(true, true);
            AppWindow.SetPresenter(presenter);

            // Размер и центрирование
            AppWindow.Resize(new Windows.Graphics.SizeInt32(900, 600));
            var displayArea = Microsoft.UI.Windowing.DisplayArea.GetFromWindowId(
                AppWindow.Id, Microsoft.UI.Windowing.DisplayAreaFallback.Primary);
            var x = (displayArea.WorkArea.Width - 900) / 2;
            var y = (displayArea.WorkArea.Height - 600) / 2;
            AppWindow.Move(new Windows.Graphics.PointInt32(x, y));

            // Навигация на Dashboard по умолчанию
            ContentFrame.Navigate(typeof(DashboardPage));
            NavigationViewControl.SelectedItem = NavigationViewControl.MenuItems[0];
        }

        public void NavigateTo(string tag)
        {
            switch (tag)
            {
                case "strategies":
                    NavigationViewControl.SelectedItem = NavigationViewControl.MenuItems[1];
                    ContentFrame.Navigate(typeof(StrategiesPage)); break;
                case "logs":
                    NavigationViewControl.SelectedItem = NavigationViewControl.MenuItems[2];
                    ContentFrame.Navigate(typeof(LogsPage)); break;
                case "dashboard":
                    NavigationViewControl.SelectedItem = NavigationViewControl.MenuItems[0];
                    ContentFrame.Navigate(typeof(DashboardPage)); break;
                case "settings":
                    NavigationViewControl.SelectedItem = NavigationViewControl.MenuItems[3];
                    ContentFrame.Navigate(typeof(SettingsPage)); break;
            }
        }

        private void NavigationView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
        {
            var tag = (args.SelectedItem as NavigationViewItem)?.Tag?.ToString();
            switch (tag)
            {
                case "dashboard": ContentFrame.Navigate(typeof(DashboardPage)); break;
                case "strategies": ContentFrame.Navigate(typeof(StrategiesPage)); break;
                case "logs": ContentFrame.Navigate(typeof(LogsPage)); break;
                case "settings": ContentFrame.Navigate(typeof(SettingsPage)); break;
            }
        }
    }
}