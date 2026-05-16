// TgProxyViewModel.cs - ViewModel for Telegram proxy (SOCKS5→WS + MTProxy)
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Dispatching;
using ZUI.Services;

namespace ZUI.ViewModels;

/// <summary>
/// ViewModel for the Telegram Proxy page.
/// Controls SOCKS5→WebSocket proxy and MTProxy via ITelegramProxyService.
/// </summary>
public partial class TgProxyViewModel : ObservableObject
{
    private readonly ITelegramProxyService _tgProxyService;
    private DispatcherQueue? _dispatcherQueue;

    // ── SOCKS5→WS Proxy ─────────────────────────────────────

    [ObservableProperty]
    private bool _isSocks5Running;

    [ObservableProperty]
    private int _socks5Port = 1080;

    [ObservableProperty]
    private string _wsUrl = "wss://web.telegram.org/ws";

    [ObservableProperty]
    private string _wsSecret = "";

    [ObservableProperty]
    private bool _isSocks5Toggling;

    [ObservableProperty]
    private string _socks5Status = LocalizationService.Get("TgSocks5Off");

    // ── MTProxy ──────────────────────────────────────────────

    [ObservableProperty]
    private bool _isMtProxyRunning;

    [ObservableProperty]
    private int _mtProxyPort = 8888;

    [ObservableProperty]
    private string _mtProxySecret = "";

    [ObservableProperty]
    private bool _isMtProxyToggling;

    [ObservableProperty]
    private string _mtProxyStatus = LocalizationService.Get("TgMtProxyOff");

    // ── Common ───────────────────────────────────────────────

    [ObservableProperty]
    private int _activeConnections;

    [ObservableProperty]
    private string _proxyLink = "";

    public TgProxyViewModel(ITelegramProxyService tgProxyService)
    {
        _tgProxyService = tgProxyService ?? throw new ArgumentNullException(nameof(tgProxyService));

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
    public void SetDispatcherQueue(DispatcherQueue queue)
    {
        if (_dispatcherQueue != null) return;
        _dispatcherQueue = queue;
    }

    /// <summary>
    /// Called when page is navigated to — refresh status.
    /// </summary>
    public async Task InitializeAsync()
    {
        await RefreshStatusAsync().ConfigureAwait(false);
    }

    [RelayCommand]
    private async Task ToggleSocks5Async()
    {
        if (IsSocks5Toggling) return;

        await RunOnUIThreadAsync(() => IsSocks5Toggling = true);

        try
        {
            if (IsSocks5Running)
            {
                var result = await _tgProxyService.StopSocks5Async().ConfigureAwait(false);
                await RunOnUIThreadAsync(() =>
                {
                    Socks5Status = result.IsSuccess ? LocalizationService.Get("TgSocks5Stopped") : $"{LocalizationService.Get("ErrorF")}: {result.Error}";
                    IsSocks5Running = false;
                });
            }
            else
            {
                if (string.IsNullOrWhiteSpace(WsUrl))
                {
                    await RunOnUIThreadAsync(() => Socks5Status = LocalizationService.Get("TgSpecifyWsUrl"));
                    return;
                }

                var result = await _tgProxyService.StartSocks5Async(
                    Socks5Port, WsUrl, WsSecret).ConfigureAwait(false);

                await RunOnUIThreadAsync(() =>
                {
                    if (result.IsSuccess)
                    {
                        IsSocks5Running = true;
                        Socks5Status = string.Format(LocalizationService.Get("TgSocks5Active"), Socks5Port);
                        ProxyLink = _tgProxyService.GenerateProxyLink(Socks5Port, WsSecret, isMtProxy: false);
                    }
                    else
                    {
                        Socks5Status = $"{LocalizationService.Get("ErrorF")}: {result.Error}";
                    }
                });
            }
    }
    catch (InvalidOperationException ex)
    {
        await RunOnUIThreadAsync(() => Socks5Status = $"{LocalizationService.Get("ErrorF")}: {ex.Message}");
    }
    catch (IOException ex)
    {
        await RunOnUIThreadAsync(() => Socks5Status = $"{LocalizationService.Get("ErrorF")}: {ex.Message}");
    }
    finally
        {
            await RunOnUIThreadAsync(() => IsSocks5Toggling = false);
        }
    }

    [RelayCommand]
    private async Task ToggleMtProxyAsync()
    {
        if (IsMtProxyToggling) return;

        await RunOnUIThreadAsync(() => IsMtProxyToggling = true);

        try
        {
            if (IsMtProxyRunning)
            {
                var result = await _tgProxyService.StopMtProxyAsync().ConfigureAwait(false);
                await RunOnUIThreadAsync(() =>
                {
                    MtProxyStatus = result.IsSuccess ? LocalizationService.Get("TgMtProxyStopped") : $"{LocalizationService.Get("ErrorF")}: {result.Error}";
                    IsMtProxyRunning = false;
                });
            }
            else
            {
                if (string.IsNullOrWhiteSpace(MtProxySecret))
                {
                    await RunOnUIThreadAsync(() => MtProxyStatus = LocalizationService.Get("TgSpecifyMtSecret"));
                    return;
                }

                var result = await _tgProxyService.StartMtProxyAsync(
                    MtProxyPort, MtProxySecret).ConfigureAwait(false);

                await RunOnUIThreadAsync(() =>
                {
                    if (result.IsSuccess)
                    {
                        IsMtProxyRunning = true;
                        MtProxyStatus = string.Format(LocalizationService.Get("TgMtProxyActive"), MtProxyPort);
                        ProxyLink = _tgProxyService.GenerateProxyLink(MtProxyPort, MtProxySecret, isMtProxy: true);
                    }
                    else
                    {
                        MtProxyStatus = $"{LocalizationService.Get("ErrorF")}: {result.Error}";
                    }
                });
            }
    }
    catch (InvalidOperationException ex)
    {
        await RunOnUIThreadAsync(() => MtProxyStatus = $"{LocalizationService.Get("ErrorF")}: {ex.Message}");
    }
    catch (IOException ex)
    {
        await RunOnUIThreadAsync(() => MtProxyStatus = $"{LocalizationService.Get("ErrorF")}: {ex.Message}");
    }
    finally
        {
            await RunOnUIThreadAsync(() => IsMtProxyToggling = false);
        }
    }

    [RelayCommand]
    private async Task StopAllAsync()
    {
        if (IsSocks5Toggling || IsMtProxyToggling) return;

        await RunOnUIThreadAsync(() =>
        {
            IsSocks5Toggling = true;
            IsMtProxyToggling = true;
        });

        try
        {
            var result = await _tgProxyService.StopAllAsync().ConfigureAwait(false);

            await RunOnUIThreadAsync(() =>
            {
                IsSocks5Running = false;
                IsMtProxyRunning = false;
                Socks5Status = LocalizationService.Get("TgSocks5Stopped");
                MtProxyStatus = LocalizationService.Get("TgMtProxyStopped");
                ProxyLink = "";

                if (!result.IsSuccess)
                {
                    MtProxyStatus = $"{LocalizationService.Get("TgPartialError")}: {result.Error}";
                }
            });
        }
        finally
        {
            await RunOnUIThreadAsync(() =>
            {
                IsSocks5Toggling = false;
                IsMtProxyToggling = false;
            });
        }
    }

    [RelayCommand]
    private async Task RefreshStatusAsync()
    {
        await _tgProxyService.RefreshStatusAsync().ConfigureAwait(false);

        await RunOnUIThreadAsync(() =>
        {
            var status = _tgProxyService.Status;
            if (status != null)
            {
                IsSocks5Running = status.Socks5Running;
                IsMtProxyRunning = status.MtProxyRunning;
                Socks5Port = status.Socks5Port > 0 ? status.Socks5Port : Socks5Port;
                MtProxyPort = status.MtProxyPort > 0 ? status.MtProxyPort : MtProxyPort;
                ActiveConnections = status.ActiveConnections;

                Socks5Status = status.Socks5Running
                    ? string.Format(LocalizationService.Get("TgSocks5Active"), status.Socks5Port)
                    : LocalizationService.Get("TgSocks5Off");
                MtProxyStatus = status.MtProxyRunning
                    ? string.Format(LocalizationService.Get("TgMtProxyActive"), status.MtProxyPort)
                    : LocalizationService.Get("TgMtProxyOff");
            }
        });
    }

    [RelayCommand]
    private void CopyProxyLink()
    {
        if (!string.IsNullOrEmpty(ProxyLink))
        {
            try
            {
                Windows.ApplicationModel.DataTransfer.DataPackage dp = new();
                dp.SetText(ProxyLink);
                Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(dp);
            }
            catch (InvalidOperationException) { /* Clipboard may not be available */ }
    catch (System.Runtime.InteropServices.COMException) { /* Clipboard may not be available */ }
        }
    }

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
