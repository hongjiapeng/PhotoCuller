using Microsoft.UI.Xaml;
using PhotoCuller.App.WinUI.Views;
using PhotoCuller.Core.Interfaces.Platform;

namespace PhotoCuller.App.WinUI.Services;

public sealed class WinUIThemeService : IThemeService
{
    private readonly Window _window;

    public WinUIThemeService(MainWindow window) => _window = window;

    public string CurrentTheme
    {
        get
        {
            if (_window.Content is FrameworkElement fe)
                return fe.RequestedTheme == ElementTheme.Light ? "Light" : "Dark";
            return "Dark";
        }
    }

    public void ApplyTheme(string theme)
    {
        if (_window.Content is not FrameworkElement fe) return;
        fe.RequestedTheme = theme switch
        {
            "Light"  => ElementTheme.Light,
            "Dark"   => ElementTheme.Dark,
            _        => ElementTheme.Default,
        };
    }
}
