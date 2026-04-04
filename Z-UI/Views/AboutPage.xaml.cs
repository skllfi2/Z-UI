using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;
using ZUI.ViewModels;

namespace ZUI.Views;

public sealed partial class AboutPage : Page
{
    public AboutViewModel ViewModel { get; }

    public AboutPage()
    {
        ViewModel = App.Services.GetRequiredService<AboutViewModel>();
        this.InitializeComponent();
    }
}
