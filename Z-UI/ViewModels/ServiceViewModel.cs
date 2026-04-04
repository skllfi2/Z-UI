using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using ZUI.Services;

namespace ZUI.ViewModels;

public partial class ServiceViewModel : ViewModelBase
{
    [ObservableProperty]
    private string _status = "Не установлена";

    [ObservableProperty]
    private ObservableCollection<string> _logLines = [];

    public ServiceViewModel()
    {
        _ = RefreshStatusAsync();
    }

    [RelayCommand]
    private async Task RefreshStatusAsync()
    {
        Status = await ServiceManager.GetStatusAsync();
    }

    [RelayCommand]
    private async Task InstallServiceAsync(string strategyName)
    {
        LogLines.Clear();
        await ServiceManager.InstallAsync(strategyName, $"--discord-youtube={strategyName}", LogLines.Add);
        Status = await ServiceManager.GetStatusAsync();
    }

    [RelayCommand]
    private async Task RemoveServiceAsync()
    {
        LogLines.Clear();
        await ServiceManager.RemoveAsync(LogLines.Add);
        Status = await ServiceManager.GetStatusAsync();
    }
}
