// AboutPage.xaml.cs - Thin code-behind for About page
using Microsoft.UI.Xaml.Navigation;
using ZUI.ViewModels;

namespace ZUI.Views;

/// <summary>
/// About page — version info, license, links.
/// </summary>
public sealed partial class AboutPage : BasePage
{
    public AboutViewModel ViewModel { get; }

    public AboutPage()
    {
        InitializeComponent();
        ViewModel = App.Services.GetRequiredService<AboutViewModel>();
        DataContext = ViewModel;
    }
}
