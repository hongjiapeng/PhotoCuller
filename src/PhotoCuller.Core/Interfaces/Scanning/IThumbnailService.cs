using PhotoCuller.Core.Models;

namespace PhotoCuller.Core.Interfaces.Scanning;

/// <summary>Generates and manages the photo thumbnail cache.</summary>
public interface IThumbnailService
{
    /// <summary>
    /// Generates a thumbnail for the specified photo and caches it to disk.
    /// Returns the cached path directly if the thumbnail already exists and the image is unchanged.
    /// </summary>
    /// <returns>Absolute path to the thumbnail; null on failure.</returns>
    Task<string?> GetOrCreateAsync(
        PhotoItem photo,
        int maxEdgePx = 256,
        CancellationToken cancellationToken = default);

    /// <summary>Deletes the cached thumbnail file for the specified photo.</summary>
    Task DeleteAsync(PhotoItem photo, CancellationToken cancellationToken = default);

    /// <summary>Clears the entire thumbnail cache.</summary>
    Task ClearAllAsync(CancellationToken cancellationToken = default);

    /// <summary>Returns the total size of the thumbnail cache directory in bytes.</summary>
    Task<long> GetCacheSizeBytesAsync(CancellationToken cancellationToken = default);
}
