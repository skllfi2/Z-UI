using Microsoft.Windows.ApplicationModel.DynamicDependency;

namespace ZUI;

internal class Program
{
    [System.STAThread]
    static void Main(string[] args)
    {
        Bootstrap.Initialize(0x00020000, "experimental6");
        Microsoft.UI.Xaml.Application.Start((p) => { var app = new App(); });
    }
}
