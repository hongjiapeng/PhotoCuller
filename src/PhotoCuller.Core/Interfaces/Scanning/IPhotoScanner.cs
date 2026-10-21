using PhotoCuller.Core.Models;

namespace PhotoCuller.Core.Interfaces.Scanning;

/// <summary>
/// Photo directory scanner: traverses a directory and coordinates the metadata-reading,
/// hashing, and scoring pipeline.
/// Streams progress via a Channel without blocking the UI.
/// </summary>
public interface IPhotoScanner
{
    /// <summary>
    /// Scans the specified directory.
    /// Returns the full current inventory, including detached cached photos for unchanged files.
    /// </summary>
    IAsyncEnumerable<PhotoItem> ScanAsync(
        string directory,
        CancellationToken cancellationToken = default);

    /// <summary>Whether a scan is currently in progress.</summary>
    bool IsScanning { get; }
}
