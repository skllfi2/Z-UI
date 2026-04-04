using System.Reflection;
using CommunityToolkit.Mvvm.ComponentModel;
using ZUI.Services;

namespace ZUI.ViewModels;

public partial class AboutViewModel : ViewModelBase
{
[ObservableProperty]
private string _appVersion = GetAppVersion();

[ObservableProperty]
private string _zapretVersion = ZapretPaths.LocalVersion;

private static string GetAppVersion()
{
try
{
var version = Assembly.GetExecutingAssembly().GetName().Version;
return version != null ? $"{version.Major}.{version.Minor}.{version.Build}" : "1.0.0";
}
catch
{
return "1.0.0";
}
}
}
