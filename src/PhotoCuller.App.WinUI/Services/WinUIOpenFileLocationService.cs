using PhotoCuller.Core.Interfaces.Platform;
using System.Diagnostics;

namespace PhotoCuller.App.WinUI.Services;

public sealed class WinUIOpenFileLocationService : IOpenFileLocationService
{
    public void OpenInExplorer(string filePath)
    {
        try
        {
            Process.Start("explorer.exe", $"/select,\"{filePath}\"");
        }
        catch
        {
            // Silently ignore if Explorer fails to launch
        }
    }
}
