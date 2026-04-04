using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using ZUI.ViewModels;

namespace ZUI.Views;

public sealed partial class ServicesPage : Page
{
    public ServicesViewModel ViewModel { get; }

    public ServicesPage()
    {
        ViewModel = App.Services.GetRequiredService<ServicesViewModel>();
        this.InitializeComponent();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        ViewModel.SetDispatcherQueue(this.DispatcherQueue);
    }
}
