// ProxifierPage.xaml.cs - Code-behind for proxifier page
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using ZUI.Models;
using ZUI.ViewModels;

namespace ZUI.Views;

public sealed partial class ProxifierPage : BasePage
{
    public ProxifierViewModel ViewModel { get; }

    public ProxifierPage()
    {
        InitializeComponent();

        ViewModel = App.Services.GetRequiredService<ProxifierViewModel>();
        DataContext = ViewModel;
    }

    private bool _isInitialized;

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);

        if (!_isInitialized)
        {
            _isInitialized = true;
            try
            {
                await ViewModel.InitializeAsync();
            }
            catch (InvalidOperationException) { }
            catch (IOException) { }
            catch (TimeoutException) { }
        }

        UpdateStatusSubtext();
        ViewModel.PropertyChanged += OnViewModelPropertyChanged;
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        ViewModel.PropertyChanged -= OnViewModelPropertyChanged;
    }

    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ViewModel.Servers) or nameof(ViewModel.ActiveRules)
            or nameof(ViewModel.ActiveConnections))
        {
            UpdateStatusSubtext();
        }
    }

    private void UpdateStatusSubtext()
    {
        var subtext = FindName("StatusSubtext") as TextBlock;
        if (subtext != null)
        {
            subtext.Text = $"Servers: {ViewModel.Servers.Count} | Rules: {ViewModel.ActiveRules} | Connections: {ViewModel.ActiveConnections}";
        }
    }

    // ── Add button handlers (show dialogs) ────────────────────

    private async void ShowAddServerDialog_Click(object sender, RoutedEventArgs e)
    {
        var result = await AddServerDialog.ShowAsync();
        if (result == ContentDialogResult.Primary)
        {
            // Dialog handled in PrimaryButtonClick
        }
    }

    private async void ShowAddRuleDialog_Click(object sender, RoutedEventArgs e)
    {
        var result = await AddRuleDialog.ShowAsync();
        if (result == ContentDialogResult.Primary)
        {
            // Dialog handled in PrimaryButtonClick
        }
    }

    // ── Server list button handlers ──────────────────────────

    private async void CheckServerButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string serverName)
        {
            var server = ViewModel.Servers.FirstOrDefault(s => s.Name == serverName);
            if (server is not null)
                await ViewModel.CheckServerCommand.ExecuteAsync(server);
        }
    }

    private async void RemoveServerButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string serverName)
        {
            var server = ViewModel.Servers.FirstOrDefault(s => s.Name == serverName);
            if (server is not null)
                await ViewModel.RemoveServerCommand.ExecuteAsync(server);
        }
    }

    // ── Rule list button handlers ────────────────────────────

    private async void RemoveRuleButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string ruleId)
        {
            var rule = ViewModel.Rules.FirstOrDefault(r => r.Id == ruleId);
            if (rule is not null)
                await ViewModel.RemoveRuleCommand.ExecuteAsync(rule);
        }
    }

    // ── ContentDialog handlers ───────────────────────────────

    private void AddServerDialog_PrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        var server = new ProxyServerDisplayModel
        {
            Name = ServerNameTextBox.Text,
            Host = ServerHostTextBox.Text,
            Port = (int)ServerPortNumberBox.Value,
            ProxyType = (ServerTypeComboBox.SelectedItem as string) ?? "Socks5",
            AuthenticationEnabled = ServerAuthCheckBox.IsChecked ?? false,
            Username = ServerAuthCheckBox.IsChecked == true ? ServerUsernameTextBox.Text : null,
            Password = ServerAuthCheckBox.IsChecked == true ? ServerPasswordBox.Password : null,
            DnsPolicy = (DnsPolicyComboBox.SelectedItem as string) ?? "Local"
        };

        _ = ViewModel.AddServerCommand.ExecuteAsync(server);
    }

    private void AddRuleDialog_PrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        var selectedProxy = RuleProxyServerComboBox.SelectedItem as ProxyServerDisplayModel;

        var rule = new ProxyRuleDisplayModel
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = RuleNameTextBox.Text,
            IsEnabled = RuleEnabledCheckBox.IsChecked ?? true,
            Priority = (int)RulePriorityNumberBox.Value,
            ProcessName = string.IsNullOrWhiteSpace(RuleProcessNameTextBox.Text) ? null : RuleProcessNameTextBox.Text,
            DestinationDomain = string.IsNullOrWhiteSpace(RuleDomainTextBox.Text) ? null : RuleDomainTextBox.Text,
            Action = (RuleActionComboBox.SelectedItem as string) ?? "Direct",
            ProxyServerId = selectedProxy?.Name,
            DnsPolicy = (RuleDnsPolicyComboBox.SelectedItem as string) ?? "Local"
        };

        _ = ViewModel.AddRuleCommand.ExecuteAsync(rule);
    }

    private void ServerAuthCheckBox_Checked(object sender, RoutedEventArgs e)
    {
        ServerAuthPanel.Visibility = Visibility.Visible;
    }

    private void ServerAuthCheckBox_Unchecked(object sender, RoutedEventArgs e)
    {
        ServerAuthPanel.Visibility = Visibility.Collapsed;
    }
}
