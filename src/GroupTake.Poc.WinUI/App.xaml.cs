using Microsoft.UI.Xaml;

namespace GroupTake.Poc.WinUI;

public partial class App : Microsoft.UI.Xaml.Application
{
    private Window? _window;
    public App() => InitializeComponent();
    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _window = new MainWindow();
        _window.Activate();
    }
}
