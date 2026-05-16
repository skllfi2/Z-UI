// ProxifierViewModel.cs - ViewModel for per-app proxy routing (Proxifier)
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Dispatching;
using System.Collections.ObjectModel;
using ZUI.Models;
using ZUI.Services;

namespace ZUI.ViewModels;

/// <summary>
/// Simple display record for proxy chains.
/// </summary>
public sealed record ProxyChainDisplayModel(string Name, int NodeCount, string[] ServerIds);

/// <summary>
/// ViewModel for the Proxifier page — per-app proxy routing.
/// Controls the Worker's ProxifierEngine via IProxifierService.
/// </summary>
public partial class ProxifierViewModel : ObservableObject
{
    private readonly IProxifierService _proxifierService;
    private DispatcherQueue? _dispatcherQueue;

    // ── Status ──────────────────────────────────────────────

    [ObservableProperty]
    private bool _isRunning;

    [ObservableProperty]
    private bool _isToggling;

    [ObservableProperty]
    private string _statusText = LocalizationService.Get("ProxifierOff");

    [ObservableProperty]
    private string _toggleButtonText = LocalizationService.Get("ProxifierStart");

    // ── Stats ───────────────────────────────────────────────

    [ObservableProperty]
    private int _activeRules;

    [ObservableProperty]
    private int _activeConnections;

    [ObservableProperty]
    private string _trafficSent = "0 B";

    [ObservableProperty]
    private string _trafficReceived = "0 B";

    // ── Collections ───────────────────────────────────────────

    [ObservableProperty]
    private ObservableCollection<ProxyServerDisplayModel> _servers = new();

    [ObservableProperty]
    private ObservableCollection<ProxyRuleDisplayModel> _rules = new();

    [ObservableProperty]
    private ObservableCollection<ProxyChainDisplayModel> _chains = new();

    [ObservableProperty]
    private ProxyServerDisplayModel? _selectedServer;

    [ObservableProperty]
    private ProxyRuleDisplayModel? _selectedRule;

    public ProxifierViewModel(IProxifierService proxifierService)
    {
        _proxifierService = proxifierService ?? throw new ArgumentNullException(nameof(proxifierService));

        try
        {
            _dispatcherQueue = DispatcherQueue.GetForCurrentThread();
        }
        catch (InvalidOperationException) { /* Not on UI thread */ }
    }

    /// <summary>
    /// Set the DispatcherQueue for UI thread marshalling.
    /// Called from NetworkPage.OnNavigatedTo.
    /// </summary>
    public void SetDispatcherQueue(Microsoft.UI.Dispatching.DispatcherQueue queue)
    {
        if (_dispatcherQueue != null) return;
        _dispatcherQueue = queue;
    }

    /// <summary>
    /// Called on page navigation — refreshes status and loads data in the background.
    /// </summary>
    public async Task InitializeAsync()
    {
        try { await RefreshStatusAsync().ConfigureAwait(false); }
        catch (IOException) { /* Worker is unavailable — do not crash the UI */ }
        catch (TimeoutException) { /* Worker is unavailable — do not crash the UI */ }
        catch (ObjectDisposedException) { /* Worker is unavailable — do not crash the UI */ }

        try { await RefreshServersAsync().ConfigureAwait(false); }
        catch (IOException) { /* Worker is unavailable — do not crash the UI */ }
        catch (TimeoutException) { /* Worker is unavailable — do not crash the UI */ }
        catch (ObjectDisposedException) { /* Worker is unavailable — do not crash the UI */ }

        try { await RefreshRulesAsync().ConfigureAwait(false); }
        catch (IOException) { /* Worker is unavailable — do not crash the UI */ }
        catch (TimeoutException) { /* Worker is unavailable — do not crash the UI */ }
        catch (ObjectDisposedException) { /* Worker is unavailable — do not crash the UI */ }
    }

    [RelayCommand]
    private async Task ToggleProxifierAsync()
    {
        if (IsToggling) return;

        await RunOnUIThreadAsync(() => IsToggling = true);

        try
        {
            if (IsRunning)
            {
                await RunOnUIThreadAsync(() => StatusText = LocalizationService.Get("StoppingProxy"));
                var result = await _proxifierService.StopAsync().ConfigureAwait(false);

                await RunOnUIThreadAsync(() =>
                {
                    if (result.IsSuccess)
                    {
                        StatusText = LocalizationService.Get("ProxifierStopped");
                        IsRunning = false;
                    }
                    else
                    {
                        StatusText = LocalizationService.Get("ErrorMsg", result.Error ?? "Unknown");
                    }
                });
            }
            else
            {
                await RunOnUIThreadAsync(() => StatusText = LocalizationService.Get("StartingProxy"));
                var result = await _proxifierService.StartAsync().ConfigureAwait(false);

                await RunOnUIThreadAsync(() =>
                {
                    if (result.IsSuccess)
                    {
                        StatusText = LocalizationService.Get("ProxifierActive2");
                        IsRunning = true;
                    }
                    else
                    {
                        StatusText = LocalizationService.Get("ErrorMsg", result.Error ?? "Unknown");
                    }
                });
            }

            await RunOnUIThreadAsync(() =>
            {
                ToggleButtonText = IsRunning ? LocalizationService.Get("ProxifierStop") : LocalizationService.Get("ProxifierStart");
            });
        }
        catch (InvalidOperationException ex)
        {
            await RunOnUIThreadAsync(() => StatusText = LocalizationService.Get("ErrorMsg", ex.Message));
        }
        catch (IOException ex)
        {
            await RunOnUIThreadAsync(() => StatusText = LocalizationService.Get("ErrorMsg", ex.Message));
        }
        finally
        {
            await RunOnUIThreadAsync(() => IsToggling = false);
        }
    }

    [RelayCommand]
    private async Task RefreshStatusAsync()
    {
        await _proxifierService.RefreshStatusAsync().ConfigureAwait(false);

        await RunOnUIThreadAsync(() =>
        {
            IsRunning = _proxifierService.IsRunning;
            ToggleButtonText = IsRunning ? LocalizationService.Get("ProxifierStop") : LocalizationService.Get("ProxifierStart");

            var status = _proxifierService.Status;
            if (status != null)
            {
                ActiveRules = status.ActiveRules;
                ActiveConnections = status.ActiveConnections;
                TrafficSent = FormatBytes(status.TotalBytesSent);
                TrafficReceived = FormatBytes(status.TotalBytesReceived);
            }

            StatusText = IsRunning ? LocalizationService.Get("ProxifierActive2") : LocalizationService.Get("ProxifierOff");
        });
    }

[RelayCommand]
private async Task AddServerAsync(ProxyServerDisplayModel server)
{
    if (server is null) return;

    try
    {
        var result = await _proxifierService.AddServerAsync(server).ConfigureAwait(false);
        if (result.IsSuccess)
        {
            await RefreshServersAsync().ConfigureAwait(false);
        }
        else
        {
            await RunOnUIThreadAsync(() => StatusText = LocalizationService.Get("ErrorMsg", result.Error ?? "Unknown"));
        }
    }
    catch (IOException ex) { await RunOnUIThreadAsync(() => StatusText = LocalizationService.Get("ErrorMsg", ex.Message)); }
    catch (TimeoutException ex) { await RunOnUIThreadAsync(() => StatusText = LocalizationService.Get("ErrorMsg", ex.Message)); }
    catch (ObjectDisposedException ex) { await RunOnUIThreadAsync(() => StatusText = LocalizationService.Get("ErrorMsg", ex.Message)); }
}

    [RelayCommand]
    private async Task RemoveServerAsync(ProxyServerDisplayModel? server)
    {
        if (server is null) return;

        try
        {
            var result = await _proxifierService.RemoveServerAsync(server.Name).ConfigureAwait(false);
            if (result.IsSuccess)
            {
                await RefreshServersAsync().ConfigureAwait(false);
            }
            else
            {
                await RunOnUIThreadAsync(() => StatusText = LocalizationService.Get("ErrorMsg", result.Error ?? "Unknown"));
            }
        }
        catch (IOException ex) { await RunOnUIThreadAsync(() => StatusText = LocalizationService.Get("ErrorMsg", ex.Message)); }
        catch (TimeoutException ex) { await RunOnUIThreadAsync(() => StatusText = LocalizationService.Get("ErrorMsg", ex.Message)); }
        catch (ObjectDisposedException ex) { await RunOnUIThreadAsync(() => StatusText = LocalizationService.Get("ErrorMsg", ex.Message)); }
    }

    [RelayCommand]
    private async Task CheckServerAsync(ProxyServerDisplayModel? server)
    {
        if (server is null) return;

        try
        {
            var response = await _proxifierService.CheckServerAsync(server).ConfigureAwait(false);
            await RunOnUIThreadAsync(() =>
            {
                StatusText = response is not null
                    ? $"Server check: {response}"
                    : LocalizationService.Get("ErrorMsg", "Check failed");
            });
        }
        catch (IOException ex) { await RunOnUIThreadAsync(() => StatusText = LocalizationService.Get("ErrorMsg", ex.Message)); }
        catch (TimeoutException ex) { await RunOnUIThreadAsync(() => StatusText = LocalizationService.Get("ErrorMsg", ex.Message)); }
        catch (ObjectDisposedException ex) { await RunOnUIThreadAsync(() => StatusText = LocalizationService.Get("ErrorMsg", ex.Message)); }
    }

[RelayCommand]
private async Task AddRuleAsync(ProxyRuleDisplayModel rule)
{
    if (rule is null) return;

    try
    {
        var result = await _proxifierService.AddRuleAsync(rule).ConfigureAwait(false);
        if (result.IsSuccess)
        {
            await RefreshRulesAsync().ConfigureAwait(false);
        }
        else
        {
            await RunOnUIThreadAsync(() => StatusText = LocalizationService.Get("ErrorMsg", result.Error ?? "Unknown"));
        }
    }
    catch (IOException ex) { await RunOnUIThreadAsync(() => StatusText = LocalizationService.Get("ErrorMsg", ex.Message)); }
    catch (TimeoutException ex) { await RunOnUIThreadAsync(() => StatusText = LocalizationService.Get("ErrorMsg", ex.Message)); }
    catch (ObjectDisposedException ex) { await RunOnUIThreadAsync(() => StatusText = LocalizationService.Get("ErrorMsg", ex.Message)); }
}

    [RelayCommand]
    private async Task RemoveRuleAsync(ProxyRuleDisplayModel? rule)
    {
        if (rule is null) return;

        try
        {
            var result = await _proxifierService.RemoveRuleAsync(rule.Id).ConfigureAwait(false);
            if (result.IsSuccess)
            {
                await RefreshRulesAsync().ConfigureAwait(false);
            }
            else
            {
                await RunOnUIThreadAsync(() => StatusText = LocalizationService.Get("ErrorMsg", result.Error ?? "Unknown"));
            }
        }
        catch (IOException ex) { await RunOnUIThreadAsync(() => StatusText = LocalizationService.Get("ErrorMsg", ex.Message)); }
        catch (TimeoutException ex) { await RunOnUIThreadAsync(() => StatusText = LocalizationService.Get("ErrorMsg", ex.Message)); }
        catch (ObjectDisposedException ex) { await RunOnUIThreadAsync(() => StatusText = LocalizationService.Get("ErrorMsg", ex.Message)); }
    }

    [RelayCommand]
    private async Task RefreshServersAsync()
    {
        try
        {
            var servers = await _proxifierService.GetServersAsync().ConfigureAwait(false);
            await RunOnUIThreadAsync(() =>
            {
                Servers = new ObservableCollection<ProxyServerDisplayModel>(servers ?? new List<ProxyServerDisplayModel>());
            });
        }
        catch (IOException ex) { await RunOnUIThreadAsync(() => StatusText = LocalizationService.Get("ErrorMsg", ex.Message)); }
        catch (TimeoutException ex) { await RunOnUIThreadAsync(() => StatusText = LocalizationService.Get("ErrorMsg", ex.Message)); }
        catch (ObjectDisposedException ex) { await RunOnUIThreadAsync(() => StatusText = LocalizationService.Get("ErrorMsg", ex.Message)); }
    }

    [RelayCommand]
    private async Task RefreshRulesAsync()
    {
        try
        {
            var rules = await _proxifierService.GetRulesAsync().ConfigureAwait(false);
            await RunOnUIThreadAsync(() =>
            {
                Rules = new ObservableCollection<ProxyRuleDisplayModel>(rules ?? new List<ProxyRuleDisplayModel>());
            });
        }
        catch (IOException ex) { await RunOnUIThreadAsync(() => StatusText = LocalizationService.Get("ErrorMsg", ex.Message)); }
        catch (TimeoutException ex) { await RunOnUIThreadAsync(() => StatusText = LocalizationService.Get("ErrorMsg", ex.Message)); }
        catch (ObjectDisposedException ex) { await RunOnUIThreadAsync(() => StatusText = LocalizationService.Get("ErrorMsg", ex.Message)); }
    }

    private static string FormatBytes(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => $"{bytes / 1024.0:F1} KB",
        < 1024 * 1024 * 1024 => $"{bytes / (1024.0 * 1024):F1} MB",
        _ => $"{bytes / (1024.0 * 1024 * 1024):F1} GB"
    };

    private Task RunOnUIThreadAsync(Action action)
    {
        if (_dispatcherQueue == null || _dispatcherQueue.HasThreadAccess)
        {
            action();
            return Task.CompletedTask;
        }

        var tcs = new TaskCompletionSource<bool>();
        _dispatcherQueue.TryEnqueue(() =>
        {
            try
            {
                action();
                tcs.SetResult(true);
            }
            catch (ObjectDisposedException ex)
            {
                tcs.SetException(ex);
            }
            catch (InvalidOperationException ex)
            {
                tcs.SetException(ex);
            }
        });
        return tcs.Task;
    }
}
