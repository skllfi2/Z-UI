using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml;
using ZUI.Services;
using Windows.Storage.Pickers;
using Windows.Storage;
using System.IO;
using System.Text.Json;

namespace ZUI.ViewModels;

public partial class SettingsViewModel : ViewModelBase
{
	public event Action? ThemeChanged;

	private bool _initialized = false;

	[ObservableProperty]
	private bool _autoStartZapret;

	[ObservableProperty]
	private bool _autoStartWithAdmin;

	[ObservableProperty]
	private bool _minimizeToTrayOnStart;

	[ObservableProperty]
	private bool _soundEffects;

	[ObservableProperty]
	private bool _toastNotifications;

	[ObservableProperty]
	private bool _autoUpdateCheck;

	[ObservableProperty]
	private bool _autoUpdateDownload;

	[ObservableProperty]
	private int _themeIndex;

	[ObservableProperty]
	private int _languageIndex;

	[ObservableProperty]
	private string _gameFilter;

	[ObservableProperty]
	private string _ipsetFilter;

	[ObservableProperty]
	private bool _animNavIcons;

	[ObservableProperty]
	private bool _animButtons;

	[ObservableProperty]
	private bool _animCards;

	[ObservableProperty]
	private bool _hotkeysEnabled;

	[ObservableProperty]
	private bool _hostsAutoUpdate;

	[ObservableProperty]
	private bool _expandedMain = true;

	[ObservableProperty]
	private bool _expandedAppearance = true;

	[ObservableProperty]
	private bool _expandedUpdates = true;

	[ObservableProperty]
	private bool _expandedHotkeys;

	[ObservableProperty]
	private bool _expandedFilters;

	public bool AllExpanded => ExpandedMain && ExpandedAppearance && ExpandedUpdates && ExpandedHotkeys && ExpandedFilters;

	public SettingsViewModel()
	{
		_autoStartZapret = AppSettings.AutoStartZapret;
		_autoStartWithAdmin = AppSettings.AutoStartWithAdmin;
		_minimizeToTrayOnStart = AppSettings.MinimizeToTrayOnStart;
		_soundEffects = AppSettings.SoundEffects;
		_toastNotifications = AppSettings.ToastNotifications;
		_autoUpdateCheck = AppSettings.AutoUpdateCheck;
		_autoUpdateDownload = AppSettings.AutoUpdateDownload;
		_gameFilter = AppSettings.GameFilter;
		_ipsetFilter = AppSettings.IpsetFilter;
		_animNavIcons = AppSettings.AnimNavIcons;
		_animButtons = AppSettings.AnimButtons;
		_animCards = AppSettings.AnimCards;
		_hotkeysEnabled = AppSettings.HotkeysEnabled;
		_hostsAutoUpdate = AppSettings.HostsAutoUpdate;

		_themeIndex = AppSettings.Theme switch
		{
			"Light" => 1,
			"Dark" => 2,
			_ => 0
		};
		_languageIndex = AppSettings.Language == "en" ? 1 : 0;

		_initialized = true;
	}

	private void SaveAllSettings()
	{
		AppSettings.AutoStartZapret = AutoStartZapret;
		AppSettings.AutoStartWithAdmin = AutoStartWithAdmin;
		AppSettings.MinimizeToTrayOnStart = MinimizeToTrayOnStart;
		AppSettings.SoundEffects = SoundEffects;
		AppSettings.ToastNotifications = ToastNotifications;
		AppSettings.AutoUpdateCheck = AutoUpdateCheck;
		AppSettings.AutoUpdateDownload = AutoUpdateDownload;
		AppSettings.GameFilter = GameFilter;
		AppSettings.IpsetFilter = IpsetFilter;
		AppSettings.AnimNavIcons = AnimNavIcons;
		AppSettings.AnimButtons = AnimButtons;
		AppSettings.AnimCards = AnimCards;
		AppSettings.HotkeysEnabled = HotkeysEnabled;
		AppSettings.HostsAutoUpdate = HostsAutoUpdate;
		AppSettings.Save();
	}

	partial void OnAutoStartZapretChanged(bool value)
	{
		if (!_initialized) return;
		if (value) AutoStartWithAdmin = false;
		SaveAllSettings();
		AutoStartService.SetAutoStart(value);
		AutoStartService.SetAutoStartAdmin(false);
	}

	partial void OnAutoStartWithAdminChanged(bool value)
	{
		if (!_initialized) return;
		if (value) AutoStartZapret = false;
		SaveAllSettings();
		AutoStartService.SetAutoStartAdmin(value);
		AutoStartService.SetAutoStart(false);
	}

	partial void OnMinimizeToTrayOnStartChanged(bool value)
	{
		if (!_initialized) return;
		SaveAllSettings();
	}

	partial void OnSoundEffectsChanged(bool value)
	{
		if (!_initialized) return;
		SaveAllSettings();
	}

	partial void OnToastNotificationsChanged(bool value)
	{
		if (!_initialized) return;
		SaveAllSettings();
	}

	partial void OnAutoUpdateCheckChanged(bool value)
	{
		if (!_initialized) return;
		SaveAllSettings();
	}

	partial void OnAutoUpdateDownloadChanged(bool value)
	{
		if (!_initialized) return;
		SaveAllSettings();
	}

	partial void OnThemeIndexChanged(int value)
	{
		if (!_initialized) return;
		var theme = value switch
		{
			1 => "Light",
			2 => "Dark",
			_ => "Default"
		};
		AppSettings.Theme = theme;
		AppSettings.Save();
		ApplyTheme(theme);
		ThemeChanged?.Invoke();
	}

	partial void OnLanguageIndexChanged(int value)
	{
		if (!_initialized) return;
		var lang = value == 1 ? "en" : "ru";
		AppSettings.Language = lang;
		AppSettings.Save();
		
		LocalizationService.CurrentLanguage = lang;
	}

	partial void OnGameFilterChanged(string value)
	{
		if (!_initialized) return;
		SaveAllSettings();
	}

	partial void OnIpsetFilterChanged(string value)
	{
		if (!_initialized) return;
		SaveAllSettings();
		BatStrategyParser.ApplyIpsetFilter(value);
	}

	partial void OnAnimNavIconsChanged(bool value)
	{
		if (!_initialized) return;
		SaveAllSettings();
	}

	partial void OnAnimButtonsChanged(bool value)
	{
		if (!_initialized) return;
		SaveAllSettings();
	}

	partial void OnAnimCardsChanged(bool value)
	{
		if (!_initialized) return;
		SaveAllSettings();
	}

	partial void OnHotkeysEnabledChanged(bool value)
	{
		if (!_initialized) return;
		SaveAllSettings();
	}

	partial void OnHostsAutoUpdateChanged(bool value)
	{
		if (!_initialized) return;
		SaveAllSettings();
	}

	[RelayCommand]
	private void ToggleAllGroups()
	{
		var newState = !AllExpanded;
		ExpandedMain = newState;
		ExpandedAppearance = newState;
		ExpandedUpdates = newState;
		ExpandedHotkeys = newState;
		ExpandedFilters = newState;
		OnPropertyChanged(nameof(AllExpanded));
	}

	partial void OnExpandedMainChanged(bool value) => OnPropertyChanged(nameof(AllExpanded));
	partial void OnExpandedAppearanceChanged(bool value) => OnPropertyChanged(nameof(AllExpanded));
	partial void OnExpandedUpdatesChanged(bool value) => OnPropertyChanged(nameof(AllExpanded));
	partial void OnExpandedHotkeysChanged(bool value) => OnPropertyChanged(nameof(AllExpanded));
	partial void OnExpandedFiltersChanged(bool value) => OnPropertyChanged(nameof(AllExpanded));

	public static void ApplyTheme(string theme)
	{
		var window = (App.Current as App)?.MainWindow;
		if (window == null) return;

		var elementTheme = theme switch
		{
			"Light" => ElementTheme.Light,
			"Dark" => ElementTheme.Dark,
			_ => ElementTheme.Default
		};

		if (window.Content is FrameworkElement rootElement)
		{
			rootElement.RequestedTheme = elementTheme;
		}
	}

	[RelayCommand]
	private async Task ExportSettingsAsync()
	{
		var picker = new FileSavePicker
		{
			SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
			SuggestedFileName = "zui-settings"
		};
		picker.FileTypeChoices.Add("JSON", new[] { ".json" });

		var hwnd = WinRT.Interop.WindowNative.GetWindowHandle((App.Current as App)?.MainWindow ?? null!);
		WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

		var file = await picker.PickSaveFileAsync();
		if (file == null) return;

		try
		{
			var data = new ExportSettingsData
			{
				AutoStartZapret = AutoStartZapret,
				AutoStartWithAdmin = AutoStartWithAdmin,
				MinimizeToTrayOnStart = MinimizeToTrayOnStart,
				SoundEffects = SoundEffects,
				ToastNotifications = ToastNotifications,
				AutoUpdateCheck = AutoUpdateCheck,
				Theme = AppSettings.Theme,
				Language = AppSettings.Language,
				GameFilter = GameFilter,
				IpsetFilter = IpsetFilter,
				SetupCompleted = AppSettings.SetupCompleted,
				CurrentStrategy = AppSettings.CurrentStrategy,
				AnimNavIcons = AnimNavIcons,
				AnimButtons = AnimButtons,
				AnimCards = AnimCards,
				HotkeysEnabled = HotkeysEnabled,
				HostsAutoUpdate = HostsAutoUpdate
			};

			var json = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
			await FileIO.WriteTextAsync(file, json);
		}
		catch { }
	}

	[RelayCommand]
	private async Task ImportSettingsAsync()
	{
		var picker = new FileOpenPicker
		{
			SuggestedStartLocation = PickerLocationId.DocumentsLibrary
		};
		picker.FileTypeFilter.Add(".json");

		var hwnd = WinRT.Interop.WindowNative.GetWindowHandle((App.Current as App)?.MainWindow ?? null!);
		WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

		var file = await picker.PickSingleFileAsync();
		if (file == null) return;

		try
		{
			var json = await FileIO.ReadTextAsync(file);
			var data = JsonSerializer.Deserialize<ExportSettingsData>(json);
			if (data == null) return;

			_initialized = false;
			AutoStartZapret = data.AutoStartZapret;
			AutoStartWithAdmin = data.AutoStartWithAdmin;
			MinimizeToTrayOnStart = data.MinimizeToTrayOnStart;
			SoundEffects = data.SoundEffects;
			ToastNotifications = data.ToastNotifications;
			AutoUpdateCheck = data.AutoUpdateCheck;
			AutoUpdateDownload = data.AutoUpdateDownload;
			GameFilter = data.GameFilter ?? "disabled";
			IpsetFilter = data.IpsetFilter ?? "any";
			AnimNavIcons = data.AnimNavIcons;
			AnimButtons = data.AnimButtons;
			AnimCards = data.AnimCards;
			HotkeysEnabled = data.HotkeysEnabled;
			HostsAutoUpdate = data.HostsAutoUpdate;

			var theme = data.Theme ?? "Default";
			var lang = data.Language ?? "ru";
			_themeIndex = theme switch { "Light" => 1, "Dark" => 2, _ => 0 };
			_languageIndex = lang == "en" ? 1 : 0;

			AppSettings.Theme = theme;
			AppSettings.Language = lang;
			SaveAllSettings();
			ApplyTheme(theme);
			ThemeChanged?.Invoke();

			_initialized = true;
			OnPropertyChanged(nameof(ThemeIndex));
			OnPropertyChanged(nameof(LanguageIndex));
		}
		catch { }
	}
}

public class ExportSettingsData
{
	public bool AutoStartZapret { get; set; }
	public bool AutoStartWithAdmin { get; set; }
	public bool MinimizeToTrayOnStart { get; set; }
	public bool SoundEffects { get; set; }
	public bool ToastNotifications { get; set; }
	public bool AutoUpdateCheck { get; set; }
	public bool AutoUpdateDownload { get; set; }
	public string? Theme { get; set; }
	public string? Language { get; set; }
	public string? GameFilter { get; set; }
	public string? IpsetFilter { get; set; }
	public bool SetupCompleted { get; set; }
	public string? CurrentStrategy { get; set; }
	public bool AnimNavIcons { get; set; }
	public bool AnimButtons { get; set; }
	public bool AnimCards { get; set; }
	public bool HotkeysEnabled { get; set; }
	public bool HostsAutoUpdate { get; set; }
}
