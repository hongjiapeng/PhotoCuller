using PhotoCuller.Core.Models;

namespace PhotoCuller.Core.Interfaces.FileOps;

/// <summary>Safely moves photos to a pending-delete folder (does not permanently delete the originals).</summary>
public interface IFileMoveService
{
    /// <summary>
    /// Moves the specified photo to the pending-delete folder.
    /// The caller is responsible for obtaining confirmation before calling this method.
    /// Records an operation log entry.
    /// </summary>
    Task<FileOperationLog> MoveToTrashAsync(
        PhotoItem photo,
        string toDeleteDirectory,
        bool preserveStructure = true,
        CancellationToken ct = default);

    /// <summary>Moves multiple photos in bulk; returns an operation log entry for each photo.</summary>
    Task<IReadOnlyList<FileOperationLog>> MoveManyToTrashAsync(
        IReadOnlyList<PhotoItem> photos,
        string toDeleteDirectory,
        bool preserveStructure = true,
        IProgress<int>? progress = null,
        CancellationToken ct = default);
}
