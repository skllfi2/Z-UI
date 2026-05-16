// GeneratorViewModel.cs - Strategy generator page (Tab 1: ready strategies, Tab 2: create strategy)
// GeneratorViewModel.cs - Uses IAdaptiveEngine (IPC to Worker) for strategy testing
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Dispatching;
using ZUI.Services;
using ZUI.Models;

namespace ZUI.ViewModels;

/// <summary>
/// View model for strategy generator page (both tabs)
/// </summary>
public partial class GeneratorViewModel : ObservableObject
{
    private readonly IStrategyGeneratorService _generatorService;
    private readonly IStrategyManager _strategyManager;
    private readonly IAdaptiveEngine _adaptiveEngine;
    private DispatcherQueue? _dispatcherQueue;

    private StrategyParamsConfig? _paramsConfig;
    private IspProfilesConfig? _ispProfiles;
    private IspProfile? _detectedProfile;
    private GeneratedStrategy? _generatedStrategy;
    private bool _isInitialized = false;

    // === Tab 1: Ready Strategies ===

    [ObservableProperty]
    private string _currentStrategyName = LocalizationService.Get("NotSelectedF");

    [ObservableProperty]
    private ObservableCollection<StrategyInfo> _availableStrategies = new();

    [ObservableProperty]
    private StrategyInfo? _selectedStrategy;

    [ObservableProperty]
    private bool _isTesting;

    [ObservableProperty]
    private string _testResult = "";

    // === Tab 2: Create Strategy ===

    [ObservableProperty]
    private IReadOnlyList<ServiceConfig> _availableServices = Array.Empty<ServiceConfig>();

    [ObservableProperty]
    private IList<object> _selectedServices = new ObservableCollection<object>();

    [ObservableProperty]
    private string _detectedProviderName = "";

    [ObservableProperty]
    private string _detectedProviderInfo = "";

    [ObservableProperty]
    private bool _isDetectingProvider;

    [ObservableProperty]
    private int _selectedTestMode = 0; // 0=Quick, 1=Full, 2=None

    [ObservableProperty]
    private bool _hasTestResults;

    [ObservableProperty]
    private string _generatedStrategyName = "";

    [ObservableProperty]
    private IReadOnlyList<ServiceTestResultDisplay> _testResults = Array.Empty<ServiceTestResultDisplay>();

    [ObservableProperty]
    private bool _isRunningTest;

    [ObservableProperty]
    private bool _isApplying;

    /// <summary>
    /// Winws command-line arguments for the generated strategy
    /// </summary>
    [ObservableProperty]
    private string _generatedWinwsArgs = "";

    /// <summary>
    /// Whether the Change Provider dialog is open
    /// </summary>
    [ObservableProperty]
    private bool _isChangeProviderDialogOpen;

    /// <summary>
    /// Available ISP profiles for Change Provider dialog
    /// </summary>
    [ObservableProperty]
    private ObservableCollection<IspProfile> _availableProfiles = new();

    /// <summary>
    /// Selected ISP profile in Change Provider dialog
    /// </summary>
    [ObservableProperty]
    private IspProfile? _dialogSelectedProfile;

    /// <summary>
    /// Custom domains added by user
    /// </summary>
    [ObservableProperty]
    private ObservableCollection<string> _customDomains = new();

    // === Tab Navigation ===

    [ObservableProperty]
    private int _selectedTab; // 0 = Ready Strategies, 1 = Create Strategy

    // === DPI Method Selection ===

    [ObservableProperty]
    private ObservableCollection<DpiMethod> _availableDpiMethods = new();

    [ObservableProperty]
    private DpiMethod? _selectedDpiMethod;

    // === Method Parameters (bound to UI controls) ===

    [ObservableProperty]
    private string _selectedFooling = "badseq"; // fooling mode for methods that support it

    [ObservableProperty]
    private int _fakeRepeats = 11;

    [ObservableProperty]
    private string _splitPos = "2"; // string because options can be "1,midsld"

    [ObservableProperty]
    private int _splitSeqovl = 652;

    [ObservableProperty]
    private string _fakedsplitPattern = "0x00";

    [ObservableProperty]
    private string _hostfakesplitMod = "host=www.google.com";

    [ObservableProperty]
    private bool _combineMultidisorder;

    /// <summary>
    /// Indicates whether there are custom domains
    /// </summary>
    public bool HasCustomDomains => CustomDomains.Count > 0;

    /// <summary>Whether fooling mode selector should be visible for current method.</summary>
    public bool ShowFoolingSelector => SelectedDpiMethod?.Id is "fake" or "fakedsplit" or "hostfakesplit" or "syndata";

    /// <summary>Whether fake repeats input should be visible for current method.</summary>
    public bool ShowFakeRepeats => SelectedDpiMethod?.Id is "fake" or "fakedsplit" or "udplen";

    /// <summary>Whether split position input should be visible for current method.</summary>
    public bool ShowSplitParams => SelectedDpiMethod?.Id is "multisplit" or "multidisorder" or "syndata";

    /// <summary>Whether fakedsplit pattern input should be visible.</summary>
    public bool ShowFakedsplitPattern => SelectedDpiMethod?.Id == "fakedsplit";

    /// <summary>Whether hostfakesplit mod input should be visible.</summary>
    public bool ShowHostfakesplitMod => SelectedDpiMethod?.Id == "hostfakesplit";

    /// <summary>Whether combineMultidisorder toggle should be visible.</summary>
    public bool ShowCombineMultidisorder => SelectedDpiMethod?.Id == "syndata";

    /// <summary>
    /// Indicates whether test can be run (Tab 2)
    /// </summary>
    public bool CanRunTest => !IsRunningTest &&
    !IsApplying &&
    (SelectedServices.Count > 0 || CustomDomains.Count > 0);

    /// <summary>
    /// Indicates whether strategy can be applied (Tab 2)
    /// </summary>
    public bool CanApply => !IsApplying &&
        !IsRunningTest &&
        (_generatedStrategy != null || (SelectedTestMode == 2 && !string.IsNullOrEmpty(GeneratedWinwsArgs)));

    /// <summary>
    /// Notify that CanRunTest/CanApply changed (called from code-behind)
    /// </summary>
    public void NotifyCanRunTestChanged()
    {
        OnPropertyChanged(nameof(CanRunTest));
        OnPropertyChanged(nameof(CanApply));
    }

    public GeneratorViewModel(
        IStrategyGeneratorService generatorService,
        IStrategyManager strategyManager,
        IAdaptiveEngine adaptiveEngine)
    {
        _generatorService = generatorService ?? throw new ArgumentNullException(nameof(generatorService));
        _strategyManager = strategyManager ?? throw new ArgumentNullException(nameof(strategyManager));
        _adaptiveEngine = adaptiveEngine ?? throw new ArgumentNullException(nameof(adaptiveEngine));

        try
        {
            _dispatcherQueue = DispatcherQueue.GetForCurrentThread();
        }
        catch (InvalidOperationException) { /* Not on UI thread - expected in tests */ }

        // Load ready strategies for Tab 1
        LoadReadyStrategies();
    }

    /// <summary>
    /// Set the DispatcherQueue for UI thread marshalling.
    /// Called from GeneratorPage.OnNavigatedTo.
    /// </summary>
    public void SetDispatcherQueue(DispatcherQueue queue)
    {
        if (_dispatcherQueue != null) return;
        _dispatcherQueue = queue;
    }

    private void LoadReadyStrategies()
    {
        var strategies = _strategyManager.GetAvailableStrategies();

        AvailableStrategies.Clear();
        foreach (var strategy in strategies)
        {
            AvailableStrategies.Add(strategy);
        }

        // Add "Auto" option at the beginning
        var autoStrategy = StrategyInfo.CreateJson("auto", LocalizationService.Get("AutoRecommended"), LocalizationService.Get("AutoRecommendedDesc"));
        AvailableStrategies.Insert(0, autoStrategy);

        // Select Auto by default
        SelectedStrategy = autoStrategy;
        CurrentStrategyName = LocalizationService.Get("AutoRecommended");
    }

partial void OnSelectedStrategyChanged(StrategyInfo? value)
    {
        if (value != null)
        {
            CurrentStrategyName = value.Name;
        }
    }

partial void OnSelectedDpiMethodChanged(DpiMethod? value)
    {
        if (value?.Params != null)
        {
            // Set default parameter values from the selected method's Params dictionary
		if (value.Params.TryGetValue("fooling", out var foolingParam) && foolingParam.Default != null)
				SelectedFooling = JsonElementHelper.UnwrapToString(foolingParam.Default) ?? "badseq";

			if (value.Params.TryGetValue("repeats", out var repeatsParam) && repeatsParam.Default != null)
				FakeRepeats = JsonElementHelper.UnwrapToInt(repeatsParam.Default) ?? 2;

			if (value.Params.TryGetValue("splitPos", out var splitPosParam) && splitPosParam.Default != null)
				SplitPos = JsonElementHelper.UnwrapToString(splitPosParam.Default) ?? "2";

			if (value.Params.TryGetValue("splitSeqovl", out var seqovlParam) && seqovlParam.Default != null)
				SplitSeqovl = JsonElementHelper.UnwrapToInt(seqovlParam.Default) ?? 2;

			if (value.Params.TryGetValue("fakedsplitPattern", out var fspParam) && fspParam.Default != null)
				FakedsplitPattern = JsonElementHelper.UnwrapToString(fspParam.Default) ?? "0x00";

			if (value.Params.TryGetValue("hostfakesplitMod", out var hfsParam) && hfsParam.Default != null)
				HostfakesplitMod = JsonElementHelper.UnwrapToString(hfsParam.Default) ?? "host=www.google.com";
        }

        // Notify visibility properties
        OnPropertyChanged(nameof(ShowFoolingSelector));
        OnPropertyChanged(nameof(ShowFakeRepeats));
        OnPropertyChanged(nameof(ShowSplitParams));
        OnPropertyChanged(nameof(ShowFakedsplitPattern));
        OnPropertyChanged(nameof(ShowHostfakesplitMod));
        OnPropertyChanged(nameof(ShowCombineMultidisorder));

        // Update CanApply/CanRunTest
        OnPropertyChanged(nameof(CanApply));
        OnPropertyChanged(nameof(CanRunTest));

        UpdateWinwsPreview();
    }

	/// <summary>
	/// Build a preview of the winws CLI arguments based on current SelectedDpiMethod + all parameter properties
	/// </summary>
	private void UpdateWinwsPreview()
	{
		if (SelectedDpiMethod == null)
		{
			GeneratedWinwsArgs = "";
			return;
		}

		GeneratedWinwsArgs = WinwsArgsBuilder.BuildMethodPreview(
			SelectedDpiMethod.Id,
			SelectedFooling,
			FakeRepeats,
			SplitPos,
			SplitSeqovl,
			FakedsplitPattern,
			HostfakesplitMod,
			CombineMultidisorder);
	}

    partial void OnSelectedFoolingChanged(string value) => UpdateWinwsPreview();
    partial void OnFakeRepeatsChanged(int value) => UpdateWinwsPreview();
    partial void OnSplitPosChanged(string value) => UpdateWinwsPreview();
    partial void OnSplitSeqovlChanged(int value) => UpdateWinwsPreview();
    partial void OnFakedsplitPatternChanged(string value) => UpdateWinwsPreview();
    partial void OnHostfakesplitModChanged(string value) => UpdateWinwsPreview();
    partial void OnCombineMultidisorderChanged(bool value) => UpdateWinwsPreview();

    /// <summary>
    /// Refresh strategies list (Tab 1)
    /// </summary>
    [RelayCommand]
    private async Task RefreshStrategiesAsync()
    {
        await _strategyManager.ReloadStrategiesAsync().ConfigureAwait(false);
        await RunOnUIThreadAsync(LoadReadyStrategies);
    }

    /// <summary>
    /// Test selected strategy (Tab 1) — starts bypass via IPC, waits, then stops
    /// </summary>
    [RelayCommand]
    private async Task TestStrategyAsync()
    {
        if (IsTesting || SelectedStrategy == null) return;

        await RunOnUIThreadAsync(() =>
        {
            IsTesting = true;
            TestResult = LocalizationService.Get("TestingInprogress");
        });

        try
        {
            // Stop if already running
        if (_adaptiveEngine.IsProtected)
        {
            await _adaptiveEngine.StopAsync().ConfigureAwait(false);
            await Task.Delay(1000).ConfigureAwait(false);
        }

        // Set strategy on the manager
        var strategyId = SelectedStrategy.Id;
        if (strategyId != "auto")
        {
            _strategyManager.SetStrategy(strategyId);
        }

        // Start bypass via IPC (Worker handles strategy resolution)
        var result = await _adaptiveEngine.StartWithStrategyAsync(
                _strategyManager.GetActiveStrategyId()).ConfigureAwait(false);

            await RunOnUIThreadAsync(() =>
            {
        TestResult = result.Success
            ? LocalizationService.Get("StrategyWorks", result.Strategy ?? strategyId)
            : LocalizationService.Get("StrategyError", result.Message ?? LocalizationService.Get("UnknownError"));
            });

            // Stop after test
            await Task.Delay(2000).ConfigureAwait(false);
            await _adaptiveEngine.StopAsync().ConfigureAwait(false);
    }
		catch (Exception ex) when (ex is InvalidOperationException or IOException or TimeoutException)
		{
			await RunOnUIThreadAsync(() => TestResult = LocalizationService.Get("ErrorMsg", ex.Message));
		}
    finally
    {
        await RunOnUIThreadAsync(() => IsTesting = false);
    }
}

/// <summary>
/// Initialize view model - load parameters and detect ISP (Tab 2)
/// </summary>
public async Task InitializeAsync()
{
    // Skip if already initialized
    if (_isInitialized) return;

    try
    {
        // Load parameters
        _paramsConfig = await _generatorService.LoadParametersAsync().ConfigureAwait(false);

    if (_paramsConfig != null)
    {
        await RunOnUIThreadAsync(() =>
        {
            AvailableServices = _paramsConfig.Services.Values
            .Where(s => s.Enabled)
            .ToList()
            .AsReadOnly();

            if (_paramsConfig.DpiMethods != null)
            {
                AvailableDpiMethods = new ObservableCollection<DpiMethod>(_paramsConfig.DpiMethods.Values);
                SelectedDpiMethod = AvailableDpiMethods.FirstOrDefault(m => m.Id == "fake");
            }
        });
    }

        // Load ISP profiles
        _ispProfiles = await _generatorService.LoadIspProfilesAsync().ConfigureAwait(false);

        // Detect ISP
        await DetectProviderAsync();

        _isInitialized = true;
    }
		catch (Exception ex) when (ex is InvalidOperationException or IOException or TimeoutException)
		{
			await HandleProviderErrorAsync(ex, "ProviderDetectionError", "ProviderDetectionFailed");
		}
}

    private async Task DetectProviderAsync()
    {
        await RunOnUIThreadAsync(() =>
        {
        IsDetectingProvider = true;
        DetectedProviderName = LocalizationService.Get("DetectingProvider");
        DetectedProviderInfo = "";
        });

        try
        {
            _detectedProfile = await _generatorService.DetectIspAsync().ConfigureAwait(false);

            await RunOnUIThreadAsync(() =>
            {
                if (_detectedProfile != null)
                {
                    DetectedProviderName = _detectedProfile.Name;
                    DetectedProviderInfo = BuildProviderInfo(_detectedProfile);
                }
                else
                {
            DetectedProviderName = LocalizationService.Get("ProviderNotDetected");
            DetectedProviderInfo = LocalizationService.Get("UniversalStrategyNote");
                }
            });
        }
		catch (Exception ex) when (ex is InvalidOperationException or IOException or TimeoutException)
		{
			await HandleProviderErrorAsync(ex, "ProviderDetectionError2", "ProviderDetectionFailed2");
		}
            finally
            {
                await RunOnUIThreadAsync(() => IsDetectingProvider = false);
            }
        }

    private static string BuildProviderInfo(IspProfile profile)
    {
        var info = new List<string>();

        if (!string.IsNullOrEmpty(profile.Description))
        {
            info.Add(profile.Description);
        }

        if (profile.Asn != null && profile.Asn.Count > 0)
        {
            info.Add($"ASN: {string.Join(", ", profile.Asn)}");
        }

        if (!string.IsNullOrEmpty(profile.Method))
        {
            info.Add(LocalizationService.Get("Method", profile.Method));
        }

        if (profile.Confidence > 0)
        {
            info.Add(LocalizationService.Get("Confidence", profile.Confidence));
        }

        if (!string.IsNullOrEmpty(profile.Notes))
        {
            info.Add(profile.Notes);
        }

        return string.Join("\n", info);
    }

    /// <summary>
    /// Show dialog to select provider manually.
    /// Populates AvailableProfiles and sets IsChangeProviderDialogOpen = true.
    /// The view shows a ContentDialog; when user confirms, calls ConfirmProviderChange().
    /// </summary>
    [RelayCommand]
    private async Task ChangeProviderAsync()
    {
        if (_ispProfiles == null || _ispProfiles.Profiles.Count == 0)
            return;

        var profiles = _ispProfiles.Profiles.Values.ToList();

        await RunOnUIThreadAsync(() =>
        {
            AvailableProfiles.Clear();
            foreach (var p in profiles)
                AvailableProfiles.Add(p);

            DialogSelectedProfile = _detectedProfile ?? profiles.FirstOrDefault();
            IsChangeProviderDialogOpen = true;
        });
    }

    /// <summary>
    /// Called from code-behind when user confirms provider selection in ContentDialog.
    /// </summary>
    public void ConfirmProviderChange()
    {
        if (DialogSelectedProfile == null) return;

        _detectedProfile = DialogSelectedProfile;
        DetectedProviderName = _detectedProfile.Name;
        DetectedProviderInfo = BuildProviderInfo(_detectedProfile);
        IsChangeProviderDialogOpen = false;
    }

    /// <summary>
    /// Called from code-behind when user cancels the ContentDialog.
    /// </summary>
    public void CancelProviderChange()
    {
        IsChangeProviderDialogOpen = false;
    }

    /// <summary>
    /// Generate strategy and run tests
    /// </summary>
    [RelayCommand]
    private async Task RunTestAsync()
    {
        if (IsRunningTest || SelectedServices.Count == 0) return;

        await RunOnUIThreadAsync(() =>
        {
            IsRunningTest = true;
            HasTestResults = false;
            TestResults = Array.Empty<ServiceTestResultDisplay>();
        });

        try
        {
            // Get selected service IDs
            var selectedIds = SelectedServices
            .OfType<ServiceConfig>()
            .Select(s => s.Id);

            // Get custom domains
            var customDomains = CustomDomains.ToList();

            // Generate strategy
            _generatedStrategy = await _generatorService.GenerateAsync(
                selectedIds,
                _detectedProfile,
                customDomains.Count > 0 ? customDomains : null).ConfigureAwait(false);

            if (_generatedStrategy == null)
            {
                await RunOnUIThreadAsync(() =>
                {
                    GeneratedStrategyName = LocalizationService.Get("GenerationError");
                });
                return;
            }

        await RunOnUIThreadAsync(() =>
        {
            GeneratedStrategyName = _generatedStrategy.Name;
            GeneratedWinwsArgs = _generatedStrategy.WinwsArgs;
            OnPropertyChanged(nameof(CanApply));
        });

            // Determine test level
            var testLevel = SelectedTestMode switch
            {
                0 => TestLevel.Quick,
                1 => TestLevel.Full,
                _ => TestLevel.Quick
            };

            // Skip test if mode is "None"
            if (SelectedTestMode == 2)
            {
                await RunOnUIThreadAsync(() =>
                {
                    HasTestResults = false;
                    TestResults = Array.Empty<ServiceTestResultDisplay>();
                });
                return;
            }

            // Run test
            var results = await _generatorService.TestStrategyAsync(_generatedStrategy, testLevel).ConfigureAwait(false);

            // Convert to display models
            var displayResults = results.ServiceResults.Values
            .Select(r => new ServiceTestResultDisplay(
                r.ServiceId,
                r.Passed,
                r.LatencyMs))
            .ToList()
            .AsReadOnly();

            await RunOnUIThreadAsync(() =>
            {
                TestResults = displayResults;
                HasTestResults = true;
            });
        }
		catch (Exception ex) when (ex is InvalidOperationException or IOException)
		{
			await RunOnUIThreadAsync(() =>
			{
				GeneratedStrategyName = LocalizationService.Get("ErrorMsg", ex.Message);
				HasTestResults = false;
			});
		}
            finally
            {
                await RunOnUIThreadAsync(() => IsRunningTest = false);
            }
        }

    /// <summary>
    /// Save generated strategy and set it as active in StrategyManager.
    /// When SelectedTestMode==2 (Skip), builds a GeneratedStrategy from the
    /// current DPI method + winws preview args instead of requiring a test run.
    /// </summary>
    [RelayCommand]
    private async Task ApplyStrategyAsync()
    {
        if (IsApplying) return;

        // In Skip mode, create strategy from current DPI method params
        if (_generatedStrategy == null)
        {
            if (SelectedTestMode == 2 && SelectedDpiMethod != null && !string.IsNullOrEmpty(GeneratedWinwsArgs))
            {
                _generatedStrategy = new GeneratedStrategy
                {
                    Name = $"custom-{SelectedDpiMethod.Id}",
                    WinwsArgs = GeneratedWinwsArgs,
                    IncludedServices = SelectedServices.OfType<ServiceConfig>().Select(s => s.Id).ToList(),
                    CustomDomains = CustomDomains.ToList(),
                };
            }
            else
            {
                return;
            }
        }

        await RunOnUIThreadAsync(() => IsApplying = true);

        try
        {
            // 1. Save strategy to user config
            var userConfig = await _generatorService.LoadUserServicesAsync().ConfigureAwait(false);
            userConfig = userConfig with
            {
                GeneratedStrategy = _generatedStrategy,
                SelectedServices = SelectedServices.OfType<ServiceConfig>().Select(s => s.Id).ToList(),
                CustomDomains = CustomDomains.ToList()
            };
            await _generatorService.SaveUserServicesAsync(userConfig).ConfigureAwait(false);

            // 2. Set custom strategy in StrategyManager (replaces old SetCustomStrategyArgs)
            var selectedServiceIds = SelectedServices.OfType<ServiceConfig>().Select(s => s.Id).ToList();
            _strategyManager.SetCustomStrategy(
                strategyId: $"generated-{_generatedStrategy.Name}",
                method: _generatedStrategy.Name,
                services: selectedServiceIds);

            // 3. Success - show message
        await RunOnUIThreadAsync(() =>
        {
            HasTestResults = true;
            GeneratedStrategyName = LocalizationService.Get("StrategySaved", _generatedStrategy!.Name);
        });
    }
    catch (Exception ex) when (ex is InvalidOperationException or IOException)
    {
        await RunOnUIThreadAsync(() => GeneratedStrategyName = LocalizationService.Get("ErrorMsg", ex.Message));
    }
        finally
        {
            await RunOnUIThreadAsync(() => IsApplying = false);
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

    partial void OnSelectedServicesChanged(IList<object> value)
    {
        OnPropertyChanged(nameof(CanRunTest));
        OnPropertyChanged(nameof(CanApply));
    }

    partial void OnIsRunningTestChanged(bool value)
    {
        OnPropertyChanged(nameof(CanRunTest));
        OnPropertyChanged(nameof(CanApply));
    }

    partial void OnIsApplyingChanged(bool value)
    {
        OnPropertyChanged(nameof(CanRunTest));
        OnPropertyChanged(nameof(CanApply));
    }

    partial void OnCustomDomainsChanged(ObservableCollection<string> value)
    {
        OnPropertyChanged(nameof(HasCustomDomains));
        OnPropertyChanged(nameof(CanRunTest));
        OnPropertyChanged(nameof(CanApply));
    }

    public void AddCustomDomain(string domain)
    {
        if (string.IsNullOrWhiteSpace(domain))
            return;

        domain = domain.Trim().ToLowerInvariant();

        if (!CustomDomains.Contains(domain))
        {
            CustomDomains.Add(domain);
            OnPropertyChanged(nameof(HasCustomDomains));
            OnPropertyChanged(nameof(CanRunTest));
        }
    }

    public void RemoveCustomDomain(string domain)
    {
        if (CustomDomains.Contains(domain))
        {
            CustomDomains.Remove(domain);
            OnPropertyChanged(nameof(HasCustomDomains));
            OnPropertyChanged(nameof(CanRunTest));
        }
    }

	/// <summary>
	/// Handle provider/IO errors consistently across async operations.
	/// Catches InvalidOperationException, IOException, TimeoutException.
	/// </summary>
	private async Task HandleProviderErrorAsync(Exception ex, string nameKey, string infoKey)
	{
		await RunOnUIThreadAsync(() =>
		{
			DetectedProviderName = LocalizationService.Get(nameKey);
			DetectedProviderInfo = LocalizationService.Get(infoKey, ex.Message);
		});
	}
}
