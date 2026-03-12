using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using ZUI.Animations;
using static ZUI.Animations.NavigationIconAnimator;
using ZUI.Services;
using ZUI.Views;
using System.Threading.Tasks; 

namespace ZUI
{
    public sealed partial class MainWindow : Window
    {
        public static MainWindow? Instance { get; private set; }

        public MainWindow()
        {
            this.InitializeComponent();
            Instance = this;

            ExtendsContentIntoTitleBar = true;
            SetTitleBar(AppTitleBar);

            AppWindow.TitleBar.ExtendsContentIntoTitleBar = true;
            AppWindow.TitleBar.ButtonBackgroundColor = Microsoft.UI.Colors.Transparent;
            AppWindow.TitleBar.ButtonInactiveBackgroundColor = Microsoft.UI.Colors.Transparent;
            AppWindow.TitleBar.PreferredTheme = TitleBarTheme.UseDefaultAppMode;

            var presenter = OverlappedPresenter.Create();
            presenter.IsAlwaysOnTop = false;
            presenter.IsMaximizable = true;
            presenter.IsMinimizable = true;
            presenter.IsResizable = true;
            presenter.SetBorderAndTitleBar(true, true);
            AppWindow.SetPresenter(presenter);

            AppWindow.Resize(new Windows.Graphics.SizeInt32(1080, 750));
            var displayArea = Microsoft.UI.Windowing.DisplayArea.GetFromWindowId(
                AppWindow.Id, Microsoft.UI.Windowing.DisplayAreaFallback.Primary);
            var x = (displayArea.WorkArea.Width - 900) / 2;
            var y = (displayArea.WorkArea.Height - 600) / 2;
            AppWindow.Move(new Windows.Graphics.PointInt32(x, y));

            // Показываем мастер настройки при первом запуске или отсутствии zapret
            if (!AppSettings.SetupCompleted)
            {
                ContentFrame.Navigate(typeof(SetupWizardPage));
                SetNavMenuEnabled(false);
                PageHeader.Text = "Настройка";
            }
            else
            {
                ContentFrame.Navigate(typeof(DashboardPage));
                NavigationViewControl.SelectedItem = NavigationViewControl.MenuItems[0];
                // Фоновая проверка обновлений hosts
                _ = CheckHostsInBackground();
            }

            // Анимации иконок навигации
            NavigationIconAnimator.Enabled = AppSettings.AnimNavIcons;
            NavigationIconAnimator.Attach(
                (NavigationViewItem)NavigationViewControl.MenuItems[0], AnimationType.Pop);       // Главная
            NavigationIconAnimator.Attach(
                (NavigationViewItem)NavigationViewControl.MenuItems[1], AnimationType.Tilt);      // Стратегии
            NavigationIconAnimator.Attach(
                (NavigationViewItem)NavigationViewControl.MenuItems[2], AnimationType.Spin);      // Сервисы
            // Settings — шестерёнка крутится
            if (NavigationViewControl.SettingsItem is NavigationViewItem settingsItem)
                NavigationIconAnimator.Attach(settingsItem, AnimationType.Spin);

            // Бейдж на пункте "Сервисы" (MenuItems[2])
            UpdateChecker.UpdateFound += version =>
            {
                DispatcherQueue.TryEnqueue(() => UpdatesBadge.Visibility = Visibility.Visible);
            };

            if (UpdateChecker.UpdateAvailable)
                UpdatesBadge.Visibility = Visibility.Visible;
        }

        public void NavigateTo(string tag)
        {
            switch (tag)
            {
                case "dashboard":
                    NavigationViewControl.SelectedItem = NavigationViewControl.MenuItems[0];
                    ContentFrame.Navigate(typeof(DashboardPage));
                    PageHeader.Text = "Главная";
                    break;
                case "strategies":
                    NavigationViewControl.SelectedItem = NavigationViewControl.MenuItems[1];
                    ContentFrame.Navigate(typeof(StrategiesPage));
                    PageHeader.Text = "Стратегии";
                    break;
                case "updates":
                    NavigationViewControl.SelectedItem = NavigationViewControl.MenuItems[2];
                    ContentFrame.Navigate(typeof(ServicesPage), 0);
                    PageHeader.Text = "Сервисы";
                    UpdatesBadge.Visibility = Visibility.Collapsed;
                    break;
                case "test":
                    NavigationViewControl.SelectedItem = NavigationViewControl.MenuItems[2];
                    ContentFrame.Navigate(typeof(ServicesPage), 1);
                    PageHeader.Text = "Сервисы";
                    break;
                case "diag":
                    NavigationViewControl.SelectedItem = NavigationViewControl.MenuItems[2];
                    ContentFrame.Navigate(typeof(ServicesPage), 2);
                    PageHeader.Text = "Сервисы";
                    break;
                case "logs":
                    NavigationViewControl.SelectedItem = NavigationViewControl.MenuItems[2];
                    ContentFrame.Navigate(typeof(ServicesPage), 3);
                    PageHeader.Text = "Сервисы";
                    break;
                case "setup":
                    SetNavMenuEnabled(false);
                    ContentFrame.Navigate(typeof(SetupWizardPage));
                    PageHeader.Text = "Настройка";
                    break;
                case "about":
                    ContentFrame.Navigate(typeof(AboutPage));
                    PageHeader.Text = "О программе";
                    break;
                case "settings":
                    NavigationViewControl.SelectedItem = NavigationViewControl.SettingsItem;
                    ContentFrame.Navigate(typeof(SettingsPage));
                    PageHeader.Text = "Настройки";
                    break;
            }
        }

        private void NavigationView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
        {
            if (args.IsSettingsSelected)
            {
                ContentFrame.Navigate(typeof(SettingsPage));
                PageHeader.Text = "Настройки";
                return;
            }

            var tag = (args.SelectedItem as NavigationViewItem)?.Tag?.ToString();
            switch (tag)
            {
                case "dashboard":
                    ContentFrame.Navigate(typeof(DashboardPage));
                    PageHeader.Text = "Главная";
                    break;
                case "strategies":
                    ContentFrame.Navigate(typeof(StrategiesPage));
                    PageHeader.Text = "Стратегии";
                    break;
                case "updates":
                    ContentFrame.Navigate(typeof(ServicesPage), 0);
                    PageHeader.Text = "Сервисы";
                    UpdatesBadge.Visibility = Visibility.Collapsed;
                    break;
            }
        }

        public void ReloadSettings() => ContentFrame.Navigate(typeof(SettingsPage));
        public void ReloadAllPages() => ContentFrame.Navigate(typeof(SettingsPage));

        private void SetNavMenuEnabled(bool enabled)
        {
            foreach (var item in NavigationViewControl.MenuItems)
                if (item is NavigationViewItem nvi) nvi.IsEnabled = enabled;
            if (NavigationViewControl.SettingsItem is NavigationViewItem s)
                s.IsEnabled = enabled;
        }

        public void ApplyAnimationSettings()
        {
            NavigationIconAnimator.Enabled = AppSettings.AnimNavIcons;
        }

        private async Task CheckHostsInBackground()
        {
            await Task.Delay(3000); // не мешать загрузке UI
            if (ContentFrame.Content is ServicesPage sp)
                await sp.CheckHostsUpdateAvailable();
            else
            {
                // Страница не открыта — просто проверяем тихо через временный экземпляр
                // Нет доступа к UI элементам, пропускаем — проверится при открытии ServicesPage
            }
        }

        public void CompleteSetup()
        {
            SetNavMenuEnabled(true);
            NavigationViewControl.SelectedItem = NavigationViewControl.MenuItems[0];
            ContentFrame.Navigate(typeof(DashboardPage));
            PageHeader.Text = "Главная";
        }
    }
}
