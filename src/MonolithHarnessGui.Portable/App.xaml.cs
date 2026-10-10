using Microsoft.UI.Xaml;
namespace MonolithHarnessGui.Portable;
public partial class App : Application
{
    Window? window;
    public static MainPage? Page { get; private set; }
    public App() { InitializeComponent(); RequestedTheme=ApplicationTheme.Dark; }
    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        Page=new MainPage(); window=new Window {Content=Page}; window.Activate();
    }
}
