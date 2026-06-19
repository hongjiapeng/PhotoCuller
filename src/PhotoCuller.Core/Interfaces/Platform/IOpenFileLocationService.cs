namespace PhotoCuller.Core.Interfaces.Platform;

/// <summary>Reveals the specified file's location in the file manager.</summary>
public interface IOpenFileLocationService
{
    void OpenInExplorer(string filePath);
}
