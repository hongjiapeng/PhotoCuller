namespace PhotoCuller.Core.Interfaces.Platform;

/// <summary>Application theme switching (dark / light / follow system); implemented by the WinUI layer.</summary>
public interface IThemeService
{
    string CurrentTheme { get; }
    void ApplyTheme(string theme);
}
