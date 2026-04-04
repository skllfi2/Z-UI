using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using ZUI.Services;

namespace ZUI.ViewModels;

public partial class SetupWizardViewModel : ViewModelBase, IDisposable
{
	private readonly WinwsService _winwsService;
	public event Action? OnCompletion;

	[ObservableProperty]
	private int _currentStep;

	[ObservableProperty]
	private string _selectedStrategy = "General";

	[ObservableProperty]
	private bool _autoStart;

	[ObservableProperty]
	private bool _autoStartWithAdmin;

	[ObservableProperty]
	private bool _filesOk = true;

	[ObservableProperty]
	private string _filesStatusText = "Все файлы найдены";

	[ObservableProperty]
	private ObservableCollection<string> _fileChecks = [];

	[ObservableProperty]
	private ObservableCollection<string> _availableStrategies = [];

	[ObservableProperty]
	private string _strategyDescription = "General подходит для большинства провайдеров.";

	[ObservableProperty]
	private Visibility _step1Visible = Visibility.Visible;

	[ObservableProperty]
	private Visibility _step2Visible = Visibility.Collapsed;

	[ObservableProperty]
	private Visibility _step3Visible = Visibility.Collapsed;

	[ObservableProperty]
	private Visibility _backButtonVisible = Visibility.Collapsed;

	[ObservableProperty]
	private Visibility _nextButtonVisible = Visibility.Visible;

	[ObservableProperty]
	private Visibility _completeButtonVisible = Visibility.Collapsed;

	public bool Step1Active => CurrentStep >= 0;
	public bool Step2Active => CurrentStep >= 1;
	public bool Step3Active => CurrentStep >= 2;

	public SetupWizardViewModel(WinwsService winwsService)
	{
		_winwsService = winwsService;
		_selectedStrategy = AppSettings.CurrentStrategy;
		_autoStart = AppSettings.AutoStartZapret;
		_autoStartWithAdmin = AppSettings.AutoStartWithAdmin;

		CheckFiles();
		LoadStrategies();
		UpdateStepVisibility();
	}

	private void CheckFiles()
	{
		FileChecks.Clear();

		var files = new[]
		{
			("winws.exe", ZapretPaths.WinwsExe),
			("WinDivert.dll", Path.Combine(ZapretPaths.WinwsDir, "WinDivert.dll")),
			("WinDivert64.sys", Path.Combine(ZapretPaths.WinwsDir, "WinDivert64.sys")),
			("cygwin1.dll", Path.Combine(ZapretPaths.WinwsDir, "cygwin1.dll")),
			("list-general.txt", Path.Combine(ZapretPaths.ListsDir, "list-general.txt")),
		};

		var allOk = true;
		foreach (var (name, path) in files)
		{
			if (File.Exists(path))
			{
				FileChecks.Add(name);
			}
			else
			{
				allOk = false;
				FilesStatusText = $"Файл не найден: {name}";
			}
		}

		FilesOk = allOk;
		if (allOk) FilesStatusText = "Все необходимые файлы найдены";
	}

	private void LoadStrategies()
	{
		AvailableStrategies.Clear();

		if (Directory.Exists(ZapretPaths.StrategiesDir))
		{
			var excludedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
			{
				"service", "install", "uninstall", "run-as-service", "run-as-admin"
			};

			foreach (var file in Directory.GetFiles(ZapretPaths.StrategiesDir, "*.bat"))
			{
				var name = Path.GetFileNameWithoutExtension(file);
				if (!excludedFiles.Contains(name))
				{
					AvailableStrategies.Add(name);
				}
			}
		}

		if (AvailableStrategies.Count == 0)
		{
			AvailableStrategies.Add("General");
		}

		if (!AvailableStrategies.Contains(SelectedStrategy))
		{
			SelectedStrategy = AvailableStrategies[0];
		}
	}

	partial void OnSelectedStrategyChanged(string value)
	{
		UpdateStrategyDescription();
	}

	private void UpdateStrategyDescription()
	{
		StrategyDescription = SelectedStrategy switch
		{
			"General" => "General подходит для большинства провайдеров. Стандартная защита.",
			"Discord" => "Оптимизировано для Discord и голосовых сервисов.",
			"YouTube" => "Оптимизировано для YouTube и видеостриминга.",
			"Russia" => "Специализировано для российских провайдеров.",
			"Gaming" => "Оптимизировано для игровых сервисов.",
			_ => "Пользовательская стратегия."
		};
	}

	partial void OnCurrentStepChanged(int value)
	{
		UpdateStepVisibility();
		OnPropertyChanged(nameof(Step1Active));
		OnPropertyChanged(nameof(Step2Active));
		OnPropertyChanged(nameof(Step3Active));
	}

	private void UpdateStepVisibility()
	{
		Step1Visible = CurrentStep == 0 ? Visibility.Visible : Visibility.Collapsed;
		Step2Visible = CurrentStep == 1 ? Visibility.Visible : Visibility.Collapsed;
		Step3Visible = CurrentStep == 2 ? Visibility.Visible : Visibility.Collapsed;

		BackButtonVisible = CurrentStep > 0 ? Visibility.Visible : Visibility.Collapsed;
		NextButtonVisible = CurrentStep < 2 ? Visibility.Visible : Visibility.Collapsed;
		CompleteButtonVisible = CurrentStep == 2 ? Visibility.Visible : Visibility.Collapsed;
	}

	[RelayCommand]
	private void NextStep()
	{
		if (CurrentStep < 2)
			CurrentStep++;
	}

	[RelayCommand]
	private void PrevStep()
	{
		if (CurrentStep > 0)
			CurrentStep--;
	}

	[RelayCommand]
	private async Task CompleteAsync()
	{
		AppSettings.CurrentStrategy = SelectedStrategy;
		AppSettings.AutoStartZapret = AutoStart;
		AppSettings.AutoStartWithAdmin = AutoStartWithAdmin;
		AppSettings.SetupCompleted = true;
		AppSettings.Save();

		if (AutoStartWithAdmin)
		{
			AutoStartService.SetAutoStartAdmin(true);
			AutoStartService.SetAutoStart(false);
		}
		else if (AutoStart)
		{
			AutoStartService.SetAutoStart(true);
			AutoStartService.SetAutoStartAdmin(false);
		}

		if (AutoStart)
		{
			var batFile = Path.Combine(ZapretPaths.StrategiesDir, SelectedStrategy + ".bat");
			var args = File.Exists(batFile)
				? BatStrategyParser.ParseStrategy(batFile) ?? GetDefaultArgs()
				: GetDefaultArgs();

			await _winwsService.StartAsync(args);
		}

		OnCompletion?.Invoke();
	}

	private string GetDefaultArgs()
	{
		var listsP = ZapretPaths.ListsDir + "\\";
		var binP = ZapretPaths.WinwsDir + "\\";
		return $"--wf-tcp=80,443,2053,2083,2087,2096,8443 --wf-udp=443,19294-19344,50000-50100 " +
			   $"--filter-udp=443 --hostlist=\"{listsP}list-general.txt\" --dpi-desync=fake --new " +
			   $"--filter-tcp=80,443 --hostlist=\"{listsP}list-general.txt\" --dpi-desync=multisplit";
	}

	public void Dispose()
	{
		OnCompletion = null;
	}
}
