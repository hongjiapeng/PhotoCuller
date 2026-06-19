using PhotoCuller.Core.Models;

namespace PhotoCuller.Core.Interfaces.Scanning;

/// <summary>Reads metadata from an image file (dimensions, format, EXIF, etc.).</summary>
public interface IPhotoMetadataReader
{
    /// <summary>
    /// Reads basic metadata for a single photo and populates the PhotoItem.
    /// Does not throw on failure; writes the error message to PhotoItem.ErrorMessage instead.
    /// </summary>
    Task ReadAsync(PhotoItem photo, CancellationToken cancellationToken = default);
}
