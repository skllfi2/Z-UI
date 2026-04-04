using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;
using ZUI.ViewModels;

namespace ZUI.Views;

public sealed partial class ServicePage : Page
{
    public ServiceViewModel ViewModel { get; }

    public ServicePage()
    {
        ViewModel = App.Services.GetRequiredService<ServiceViewModel>();
        this.InitializeComponent();
    }
}
