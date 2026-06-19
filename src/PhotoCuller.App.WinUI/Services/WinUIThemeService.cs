using Microsoft.UI.Xaml;
using PhotoCuller.App.WinUI.Views;
using PhotoCuller.Core.Interfaces.Platform;

namespace PhotoCuller.App.WinUI.Services;

public sealed class WinUIThemeService : IThemeService
{
    private readonly Func<MainWindow> _getWindow;

    public WinUIThemeService(Func<MainWindow> getWindow) => _getWindow = getWindow;

    public string CurrentTheme
    {
        get
        {
            if (_getWindow().Content is FrameworkElement fe)
                return fe.RequestedTheme == ElementTheme.Light ? "Light" : "Dark";
            return "Dark";
        }
    }

    public void ApplyTheme(string theme)
    {
        if (_getWindow().Content is not FrameworkElement fe) return;
        fe.RequestedTheme = theme switch
        {
            "Light"  => ElementTheme.Light,
            "Dark"   => ElementTheme.Dark,
            _        => ElementTheme.Default,
        };
    }
}
