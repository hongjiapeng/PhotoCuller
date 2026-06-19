namespace PhotoCuller.Core.Interfaces.Platform;

/// <summary>File / folder picker dialog (platform implementation provided by the WinUI layer).</summary>
public interface IFileDialogService
{
    /// <summary>Shows a folder picker dialog; returns the selected path, or null if cancelled.</summary>
    Task<string?> PickFolderAsync();

    /// <summary>Shows a file picker dialog; returns the list of selected file paths.</summary>
    Task<IReadOnlyList<string>> PickFilesAsync(
        string? filter = null,
        bool multiSelect = false);
}
