using System;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Threading.Tasks;
using ZUI.Services;

namespace ZUI.ViewModels;

public partial class UpdatesViewModel : ViewModelBase, IDisposable
{
    [ObservableProperty]
    private string _latestVersion = "—";

    [ObservableProperty]
    private bool _updateAvailable;

    [ObservableProperty]
    private string _localVersion = ZapretPaths.LocalVersion;

    [ObservableProperty]
    private bool _isChecking;

    public UpdatesViewModel()
    {
        _localVersion = ZapretPaths.LocalVersion;
        _latestVersion = UpdateChecker.LatestVersion ?? "—";
        _updateAvailable = UpdateChecker.UpdateAvailable;
        UpdateChecker.UpdateFound += OnUpdateFound;
    }

    private void OnUpdateFound(string version)
    {
        LatestVersion = version;
        UpdateAvailable = true;
    }

    [RelayCommand]
    private async Task CheckAsync()
    {
        IsChecking = true;
        await UpdateChecker.CheckAsync();
        LatestVersion = UpdateChecker.LatestVersion ?? "Не найдено";
        UpdateAvailable = UpdateChecker.UpdateAvailable;
        IsChecking = false;
    }

    public void Dispose()
    {
        UpdateChecker.UpdateFound -= OnUpdateFound;
    }
}
