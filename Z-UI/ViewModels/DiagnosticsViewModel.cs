using System;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ZUI.Services;


namespace ZUI.ViewModels;

public partial class DiagnosticsViewModel : ViewModelBase, IDisposable
{
    private const int MaxLogLines = 500;
    private readonly WinwsService _winwsService;

    [ObservableProperty]
    private ObservableCollection<string> _logLines = [];

    public DiagnosticsViewModel(WinwsService winwsService)
    {
        _winwsService = winwsService;
        _winwsService.LogReceived += OnLogReceived;
    }

    private void OnLogReceived(string line)
    {
        LogLines.Add(line);
        while (LogLines.Count > MaxLogLines)
            LogLines.RemoveAt(0);
    }

    [RelayCommand]
    private void ClearLogs()
    {
        LogLines.Clear();
    }

    public void Dispose()
    {
        _winwsService.LogReceived -= OnLogReceived;
    }
}
