// AboutViewModel.cs - About page ViewModel
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ZUI.Services;

namespace ZUI.ViewModels;

/// <summary>
/// ViewModel for the About page — version info, links, dependencies.
/// </summary>
public partial class AboutViewModel : ObservableObject
{
    private readonly MalwLinkUpdateService _updateService;

    // ── Localized labels ────────────────────────────────────

    public string Title => LocalizationService.Get("AboutTitle");
    public string Subtitle => LocalizationService.Get("AboutSubtitle");
    public string Description => LocalizationService.Get("AboutDescription");
    public string AboutVersionLabel => LocalizationService.Get("AboutVersion");
    public string VersionsTitle => LocalizationService.Get("AboutVersion");
    public string ZapretLabel => "zapret";
    public string WorkerLabel => LocalizationService.Get("WorkerLabel");
    public string LinksTitle => LocalizationService.Get("AboutGitHub");
    public string GitHubLabel => LocalizationService.Get("AboutGitHub");
    public string GitHubDesc => LocalizationService.Get("AboutGitHubDesc");
    public string ZapretProjectLabel => LocalizationService.Get("AboutZapret");
    public string ZapretProjectDesc => LocalizationService.Get("AboutZapretDesc");
    public string OpenLabel => LocalizationService.Get("OpenLabel");
    public string DependenciesTitle => LocalizationService.Get("AboutDependencies");
    public string DependenciesInfo => LocalizationService.Get("AboutDepsInfo");
    public string LicenseLabel => LocalizationService.Get("AboutLicense");
    public string LicenseInfo => LocalizationService.Get("AboutLicenseInfo");
    public string Copyright => LocalizationService.Get("AboutCopyright");

    [ObservableProperty]
    private string _appVersion = "";

    [ObservableProperty]
    private string _workerVersion = "";

    [ObservableProperty]
    private string _zapretVersion = ZapretPaths.LocalVersion;

    public AboutViewModel(MalwLinkUpdateService updateService)
    {
        _updateService = updateService ?? throw new System.ArgumentNullException(nameof(updateService));
        LoadVersion();
    }

    private async void LoadVersion()
    {
        try
        {
            var version = await _updateService.GetCurrentVersionAsync().ConfigureAwait(false);
            AppVersion = version ?? "1.0.0";
        }
        catch
        {
            AppVersion = "1.0.0";
        }

        // Worker version comes from the same source (zapret binaries)
        WorkerVersion = ZapretPaths.LocalVersion;
    }

    [RelayCommand]
    private void OpenGitHub()
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "https://github.com/Flowseal/zapret-discord-youtube",
                UseShellExecute = true
            });
        }
        catch { /* No default browser */ }
    }

    [RelayCommand]
    private void OpenZapret()
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "https://github.com/bol-van/zapret",
                UseShellExecute = true
            });
        }
        catch { /* No default browser */ }
    }
}
