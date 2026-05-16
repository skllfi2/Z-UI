// NetworkPage.xaml.cs - Unified networking page: DNS + Proxifier + Telegram Proxy
using Microsoft.UI.Xaml.Navigation;
using ZUI.ViewModels;

namespace ZUI.Views;

/// <summary>
/// Объединённая страница сети: DNS + Проксификатор + Telegram Proxy.
/// Использует 3 ViewModel из DI. Данные показываются из кэша мгновенно,
/// фоновое обновление запускается сразу без блокировки UI.
/// </summary>
public sealed partial class NetworkPage : BasePage
{
    public DnsPageViewModel? DnsViewModel { get; private set; }
    public ProxifierViewModel? ProxifierViewModel { get; private set; }
    public TgProxyViewModel? TgProxyViewModel { get; private set; }

    private bool _isInitialized;

    public NetworkPage()
    {
        InitializeComponent();

        try
        {
            DnsViewModel = App.Services.GetService(typeof(DnsPageViewModel)) as DnsPageViewModel;
            ProxifierViewModel = App.Services.GetService(typeof(ProxifierViewModel)) as ProxifierViewModel;
            TgProxyViewModel = App.Services.GetService(typeof(TgProxyViewModel)) as TgProxyViewModel;
        }
        catch (InvalidOperationException)
        {
            // ViewModels не доступны
        }
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);

        // Set DispatcherQueue for VMs so RunOnUIThreadAsync works correctly.
        // VMs are DI singletons created on thread pool — DispatcherQueue is null by default.
        var dq = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
        DnsViewModel?.SetDispatcherQueue(dq);
        ProxifierViewModel?.SetDispatcherQueue(dq);
        TgProxyViewModel?.SetDispatcherQueue(dq);

        // First visit: fire-and-forget background refresh — page shows instantly
        // with cached/default data. Subsequent visits: show cached state immediately,
        // skip refresh (user can click Refresh buttons manually).
        if (!_isInitialized)
        {
            _isInitialized = true;
            _ = RefreshViewModelsLazyAsync();
        }
    }

    /// <summary>
    /// Background refresh — does not block navigation. All IPC calls run async.
    /// </summary>
    private async Task RefreshViewModelsLazyAsync()
    {
        try
        {
            var tasks = new List<Task>();

            if (DnsViewModel != null)
                tasks.Add(DnsViewModel.Refresh());

            if (ProxifierViewModel != null)
                tasks.Add(ProxifierViewModel.InitializeAsync());

            if (TgProxyViewModel != null)
                tasks.Add(TgProxyViewModel.InitializeAsync());

            if (tasks.Count > 0)
            {
                try { await Task.WhenAll(tasks); }
                catch (InvalidOperationException) { /* IPC errors don't crash UI */ }
                catch (HttpRequestException) { /* IPC errors don't crash UI */ }
                catch (IOException) { /* IPC errors don't crash UI */ }
            }
        }
        finally
        {
            // No _isRefreshing flag needed — _isInitialized guard prevents re-entry
        }
    }
}
