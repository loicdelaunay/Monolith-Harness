using Microsoft.UI.Xaml;
namespace MonolithHarnessGui.Portable;
public partial class App : Application
{
    Window? window;
    public static MainPage? Page { get; private set; }
    public App()
    {
        InitializeComponent();
        Resources.MergedDictionaries.Add(new Uno.Material.MaterialTheme(MaterialColors.Create(),null));
    }
    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        Page=new MainPage(); window=new Window {Content=Page}; window.Activate();
    }
}
