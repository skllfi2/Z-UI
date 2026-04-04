using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;
using ZUI.ViewModels;

namespace ZUI.Views;

public sealed partial class DiagnosticsPage : Page
{
    public DiagnosticsViewModel ViewModel { get; }

    public DiagnosticsPage()
    {
        ViewModel = App.Services.GetRequiredService<DiagnosticsViewModel>();
        this.InitializeComponent();
    }
}
