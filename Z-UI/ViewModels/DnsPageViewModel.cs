// DnsPageViewModel.cs - DNS over HTTPS and DNS Proxy settings
// Uses IEnhancedDnsManager (DNS bypass) + IIpcClientService (Worker DNS) instead of IDnsServiceAdapter
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Dispatching;
using ZUI.Services;

namespace ZUI.ViewModels;

public partial class DnsPageViewModel : ViewModelBase
{
    private readonly IDnsService _dnsService;
    private readonly IEnhancedDnsManager _dnsManager;
    private readonly IIpcClientService _ipc;
    private readonly IAppSettingsService _appSettings;
    private WorkerDnsStatus? _workerDnsStatus;

    // DNS over HTTPS properties (local Windows)
    [ObservableProperty]
    private bool _isSecureDnsEnabled;

    [ObservableProperty]
    private bool _isDohSupported;

    [ObservableProperty]
    private string _statusMessage = LocalizationService.Get("Checking");

    [ObservableProperty]
    private string? _providerName;

    [ObservableProperty]
    private string? _recommendation;

    [ObservableProperty]
    private bool _isApplying;

    [ObservableProperty]
    private int _selectedProviderIndex = 0;

    // DNS Proxy / Worker DNS properties
    [ObservableProperty]
    private bool _isDnsProxyRunning;

    [ObservableProperty]
    private string _dnsProxyStatus = LocalizationService.Get("DnsProxyNotRunning");

    [ObservableProperty]
    private bool _isDnsProxyApplying;

    [ObservableProperty]
    private bool _isFakeDnsEnabled;

    // Mode selection: 0 = DoH (Windows), 1 = DNS Proxy (Worker)
    [ObservableProperty]
    private int _selectedDnsMode = 0;

    /// <summary>DNS Proxy port (synced with IAppSettingsService).</summary>
    [ObservableProperty]
    private int _dnsPort = 5353;

    public List<string> Providers { get; } = new() { LocalizationService.Get("MalwLinkRecommended"), "Google DNS", "Cloudflare", "Quad9" };

    public List<string> DnsModes { get; } = new() { LocalizationService.Get("DnsModeDoh"), LocalizationService.Get("DnsModeProxy") };

    public DnsPageViewModel(IDnsService dnsService, IEnhancedDnsManager dnsManager, IIpcClientService ipc, IAppSettingsService appSettings)
    {
        _dnsService = dnsService ?? throw new ArgumentNullException(nameof(dnsService));
        _dnsManager = dnsManager ?? throw new ArgumentNullException(nameof(dnsManager));
        _ipc = ipc ?? throw new ArgumentNullException(nameof(ipc));
        _appSettings = appSettings ?? throw new ArgumentNullException(nameof(appSettings));

        DnsPort = _appSettings.DnsPort;
        // Do NOT call CheckDnsStatus() or UpdateDnsProxyStatus() here —
        // they launch PowerShell processes which block the UI thread for 500-1500ms.
        // Data is populated via Refresh() called from NetworkPage.OnNavigatedTo.
    }

    /// <summary>
    /// Check local Windows DoH status
    /// </summary>
    public void CheckDnsStatus()
    {
        var status = _dnsService.GetDnsStatus();
        IsSecureDnsEnabled = status.IsSecureDnsEnabled;
        IsDohSupported = status.IsDohSupported;
        StatusMessage = status.StatusMessage;
        ProviderName = status.ProviderName;
        Recommendation = status.Recommendation;
    }

    /// <summary>
    /// Update Worker DNS proxy status from IEnhancedDnsManager + IIpcClientService
    /// </summary>
    private void UpdateDnsProxyStatus()
    {
        var isWorkerDnsActive = _workerDnsStatus?.DohEnabled == true || _workerDnsStatus?.FakeDnsEnabled == true;
        IsDnsProxyRunning = _dnsManager.State == DnsBypassState.Active || isWorkerDnsActive;
        IsFakeDnsEnabled = _workerDnsStatus?.FakeDnsEnabled ?? false;

        if (IsDnsProxyRunning)
        {
            var parts = new List<string> { LocalizationService.Get("DnsProxyActiveWorker") };
            if (_dnsService.IsSecureDnsEnabled())
                parts.Add(LocalizationService.Get("LocalDohOn"));
            if (_workerDnsStatus?.DohEnabled == true)
                parts.Add(LocalizationService.Get("WorkerDohOn"));
            if (_workerDnsStatus?.FakeDnsEnabled == true)
                parts.Add(LocalizationService.Get("FakeDnsOn"));
            if (_workerDnsStatus?.CachedEntries > 0)
                parts.Add(LocalizationService.Get("CacheCount", _workerDnsStatus.CachedEntries));
            if (_workerDnsStatus?.FakeDnsOverrides > 0)
                parts.Add(LocalizationService.Get("FakeDnsOverrides", _workerDnsStatus.FakeDnsOverrides));

            DnsProxyStatus = string.Join("\n", parts);
        }
        else
        {
            DnsProxyStatus = LocalizationService.Get("DnsProxyNotRunning");
        }
    }

    [RelayCommand]
    private async Task EnableDohAsync()
    {
        if (IsApplying) return;

        await RunOnUIThreadAsync(() => IsApplying = true);

        try
        {
            var providerIds = new[] { "malw", "google", "cloudflare", "quad9" };
            var success = await _dnsService.EnableSecureDnsAsync(providerIds[SelectedProviderIndex]).ConfigureAwait(false);

            await RunOnUIThreadAsync(() =>
            {
                if (success)
                {
                    StatusMessage = LocalizationService.Get("DohEnabled", Providers[SelectedProviderIndex]);
                    Recommendation = null;
                    IsSecureDnsEnabled = true;
                }
                else
                {
                    StatusMessage = LocalizationService.Get("DohEnableError");
                    Recommendation = LocalizationService.Get("RunAsAdminPrompt");
                }
            });
        }
        finally
        {
            await RunOnUIThreadAsync(() => IsApplying = false);
        }
    }

    [RelayCommand]
    private async Task DisableDohAsync()
    {
        if (IsApplying) return;

        await RunOnUIThreadAsync(() => IsApplying = true);

        try
        {
            var success = await _dnsService.DisableSecureDnsAsync().ConfigureAwait(false);

            await RunOnUIThreadAsync(() =>
            {
                if (success)
                {
                    StatusMessage = LocalizationService.Get("DohDisabled");
                    Recommendation = LocalizationService.Get("DnsResetDhcp");
                    IsSecureDnsEnabled = false;
                }
                else
                {
                    StatusMessage = LocalizationService.Get("DohDisableError");
                }
            });
        }
        finally
        {
            await RunOnUIThreadAsync(() => IsApplying = false);
        }
    }

    [RelayCommand]
    private async Task StartDnsProxyAsync()
    {
        if (IsDnsProxyApplying) return;

        await RunOnUIThreadAsync(() =>
        {
            IsDnsProxyApplying = true;
            DnsProxyStatus = LocalizationService.Get("StartingDnsProxy");
        });

        try
        {
            // Enable DNS bypass via EnhancedDnsManager
            await _dnsManager.EnableDnsBypassAsync(ct: default).ConfigureAwait(false);

            // Enable Worker DNS if connected
            if (_ipc.IsConnected)
            {
                var dnsResult = await _ipc.ConfigureDnsAsync(enableDoh: true, enableFakeDns: IsFakeDnsEnabled, ct: default).ConfigureAwait(false);
                if (!dnsResult.IsSuccess)
                {
                    // Log warning but continue
                }
            }

            // Enable local DoH if supported
            try
            {
                await _dnsService.EnableSecureDnsAsync("google").ConfigureAwait(false);
            }
            catch (InvalidOperationException) { }
            catch (System.ComponentModel.Win32Exception) { }

            // Refresh cached worker status
            await RefreshWorkerDnsStatusAsync().ConfigureAwait(false);

            await RunOnUIThreadAsync(() =>
            {
                UpdateDnsProxyStatus();
                DnsProxyStatus = LocalizationService.Get("DnsProxyStarted") + "\n\n" +
                    LocalizationService.Get("DnsProxyRouting") + "\n" +
                    LocalizationService.Get("DnsProxyBlockedSites") + "\n" +
                    LocalizationService.Get("DnsProxyOtherSites") + "\n" +
                    (IsFakeDnsEnabled ? LocalizationService.Get("DnsProxyFakeDnsNote") + "\n" : "") +
                    "\n" + LocalizationService.Get("DnsProxyActivation");
            });
        }
    catch (InvalidOperationException ex)
    {
            await RunOnUIThreadAsync(() =>
            {
                DnsProxyStatus = LocalizationService.Get("DnsProxyStartError", ex.Message);
            });
        }
        catch (IOException ex)
        {
            await RunOnUIThreadAsync(() =>
            {
                DnsProxyStatus = LocalizationService.Get("DnsProxyStartError", ex.Message);
        });
    }
    finally
    {
        await RunOnUIThreadAsync(() => IsDnsProxyApplying = false);
    }
    }

    [RelayCommand]
    private async Task StopDnsProxyAsync()
    {
        if (IsDnsProxyApplying) return;

        await RunOnUIThreadAsync(() =>
        {
            IsDnsProxyApplying = true;
            DnsProxyStatus = LocalizationService.Get("StoppingDnsProxy");
        });

        try
        {
            // Disable Worker DNS
            if (_ipc.IsConnected)
            {
                try
                {
                    await _ipc.ConfigureDnsAsync(enableDoh: false, enableFakeDns: false, ct: default).ConfigureAwait(false);
                }
                catch (IOException) { }
                catch (TimeoutException) { }
                catch (InvalidOperationException) { }
            }

            // Disable DNS bypass
            await _dnsManager.DisableDnsBypassAsync().ConfigureAwait(false);

            // Disable local DoH
            try
            {
                await _dnsService.DisableSecureDnsAsync().ConfigureAwait(false);
            }
            catch (InvalidOperationException) { }
            catch (System.ComponentModel.Win32Exception) { }

            _workerDnsStatus = new WorkerDnsStatus { DohEnabled = false, FakeDnsEnabled = false };

            await RunOnUIThreadAsync(() =>
            {
                IsDnsProxyRunning = false;
                IsFakeDnsEnabled = false;
                DnsProxyStatus = LocalizationService.Get("DnsProxyStopped");
            });
        }
        finally
        {
            await RunOnUIThreadAsync(() => IsDnsProxyApplying = false);
        }
    }

    [RelayCommand]
    private async Task ToggleFakeDnsAsync()
    {
        if (IsDnsProxyApplying) return;

        await RunOnUIThreadAsync(() => IsDnsProxyApplying = true);

        try
        {
            if (!_ipc.IsConnected)
            {
                await RunOnUIThreadAsync(() => DnsProxyStatus = LocalizationService.Get("DnsProxyFakeDnsError", "Worker not connected"));
                return;
            }

            var result = await _ipc.ConfigureDnsAsync(enableDoh: true, enableFakeDns: !IsFakeDnsEnabled, ct: default).ConfigureAwait(false);

        await RunOnUIThreadAsync(() =>
        {
            if (result.IsSuccess)
            {
                IsFakeDnsEnabled = !IsFakeDnsEnabled;
                // Update status text without calling UpdateDnsProxyStatus()
                // which would reset IsFakeDnsEnabled from _workerDnsStatus (null in test / stale in runtime)
                DnsProxyStatus = IsFakeDnsEnabled
                    ? LocalizationService.Get("DnsProxyActiveWorker") + "\n" + LocalizationService.Get("FakeDnsOn")
                    : LocalizationService.Get("DnsProxyNotRunning");
            }
            else
            {
                DnsProxyStatus = LocalizationService.Get("DnsProxyFakeDnsError", result.Error ?? "Unknown error");
            }
        });
        }
        finally
        {
            await RunOnUIThreadAsync(() => IsDnsProxyApplying = false);
        }
    }

    [RelayCommand]
    public async Task Refresh()
    {
        // CheckDnsStatus() calls GetDnsStatus() which launches PowerShell processes (500-1500ms).
        // Run on thread pool to avoid blocking UI.
        var status = await Task.Run(() => _dnsService.GetDnsStatus()).ConfigureAwait(false);

        // Property setters trigger PropertyChanged → WinRT COM marshaling.
        // Must run on UI thread to avoid RPC_E_WRONG_THREAD (0x8001010E).
        await RunOnUIThreadAsync(() =>
        {
            IsSecureDnsEnabled = status.IsSecureDnsEnabled;
            IsDohSupported = status.IsDohSupported;
            StatusMessage = status.StatusMessage;
            ProviderName = status.ProviderName;
            Recommendation = status.Recommendation;
        });

        // Async part — IPC to Worker
        try
        {
            await RefreshWorkerDnsStatusAsync().ConfigureAwait(false);

            // UpdateDnsProxyStatus() also calls _dnsService.IsSecureDnsEnabled() (PowerShell).
            // Pre-fetch on thread pool, then build status on UI thread.
            var isSecureDnsEnabled = await Task.Run(() => _dnsService.IsSecureDnsEnabled()).ConfigureAwait(false);
            await RunOnUIThreadAsync(() => UpdateDnsProxyStatusWithCache(isSecureDnsEnabled));
        }
        catch
        {
            // Worker unavailable — don't crash UI
        }
    }

    /// <summary>
    /// Same as UpdateDnsProxyStatus but uses pre-fetched IsSecureDnsEnabled
    /// to avoid launching PowerShell on the UI thread.
    /// </summary>
    private void UpdateDnsProxyStatusWithCache(bool isSecureDnsEnabled)
    {
        var isWorkerDnsActive = _workerDnsStatus?.DohEnabled == true || _workerDnsStatus?.FakeDnsEnabled == true;
        IsDnsProxyRunning = _dnsManager.State == DnsBypassState.Active || isWorkerDnsActive;
        IsFakeDnsEnabled = _workerDnsStatus?.FakeDnsEnabled ?? false;

        if (IsDnsProxyRunning)
        {
            var parts = new List<string> { LocalizationService.Get("DnsProxyActiveWorker") };
            if (isSecureDnsEnabled)
                parts.Add(LocalizationService.Get("LocalDohOn"));
            if (_workerDnsStatus?.DohEnabled == true)
                parts.Add(LocalizationService.Get("WorkerDohOn"));
            if (_workerDnsStatus?.FakeDnsEnabled == true)
                parts.Add(LocalizationService.Get("FakeDnsOn"));
            if (_workerDnsStatus?.CachedEntries > 0)
                parts.Add(LocalizationService.Get("CacheCount", _workerDnsStatus.CachedEntries));
            if (_workerDnsStatus?.FakeDnsOverrides > 0)
                parts.Add(LocalizationService.Get("FakeDnsOverrides", _workerDnsStatus.FakeDnsOverrides));

            DnsProxyStatus = string.Join("\n", parts);
        }
        else
        {
            DnsProxyStatus = LocalizationService.Get("DnsProxyNotRunning");
        }
    }

    partial void OnDnsPortChanged(int value)
    {
        if (value >= 1024 && value <= 65535)
        _appSettings.DnsPort = value;
    }

    private async Task RefreshWorkerDnsStatusAsync(CancellationToken ct = default)
    {
        if (_ipc.IsConnected)
        {
            try
            {
                var result = await _ipc.GetDnsStatusAsync(ct).ConfigureAwait(false);
                if (result.IsSuccess && result.Value != null)
                    _workerDnsStatus = result.Value;
            }
            catch (IOException) { }
    catch (TimeoutException) { }
    catch (InvalidOperationException) { }
    }
    }
}
