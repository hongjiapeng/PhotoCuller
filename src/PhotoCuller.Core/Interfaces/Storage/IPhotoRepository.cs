using PhotoCuller.Core.Models;

namespace PhotoCuller.Core.Interfaces.Storage;

/// <summary>Photo data persistence repository (EF Core primary path + raw SQL escape hatch).</summary>
public interface IPhotoRepository
{
    Task<PhotoItem?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<PhotoItem?> GetByPathAsync(string filePath, CancellationToken ct = default);

    /// <summary>Returns a paged list of photos (used for virtualized scrolling).</summary>
    Task<IReadOnlyList<PhotoItem>> GetPagedAsync(
        int skip, int take,
        CancellationToken ct = default);

    Task<IReadOnlyList<PhotoItem>> GetByGroupAsync(Guid groupId, CancellationToken ct = default);
    Task<int> GetTotalCountAsync(CancellationToken ct = default);

    Task AddAsync(PhotoItem photo, CancellationToken ct = default);

    /// <summary>Bulk insert (used during scanning; commits in batches of 50).</summary>
    Task AddRangeAsync(IEnumerable<PhotoItem> photos, CancellationToken ct = default);

    Task UpdateAsync(PhotoItem photo, CancellationToken ct = default);
    Task DeleteAsync(Guid id, CancellationToken ct = default);

    /// <summary>Deletes ALL photos from the table. Used before a full re-scan.</summary>
    Task DeleteAllAsync(CancellationToken ct = default);

    /// <summary>Raw SQL escape hatch: returns paths that need to be re-scanned based on file path and modified time.</summary>
    Task<IReadOnlyList<string>> GetPathsNeedingRescanAsync(
        IEnumerable<(string Path, DateTime ModifiedTime)> candidates,
        CancellationToken ct = default);
}
