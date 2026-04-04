using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;
using ZUI.ViewModels;

namespace ZUI.Views;

public sealed partial class UpdatesPage : Page
{
    public UpdatesViewModel ViewModel { get; }

    public UpdatesPage()
    {
        ViewModel = App.Services.GetRequiredService<UpdatesViewModel>();
        this.InitializeComponent();
    }
}
