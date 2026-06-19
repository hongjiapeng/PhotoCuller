using PhotoCuller.Core.Models;

namespace PhotoCuller.Core.Interfaces.Hashing;

/// <summary>
/// Groups a list of photos into duplicate, similar, burst, and other group types.
/// This is the core coordination step that runs after scoring and before UI display.
/// </summary>
public interface IDuplicateGroupingService
{
    /// <summary>
    /// Builds groups from the full photo set and returns all photo groups.
    /// Progress is reported via the IProgress callback.
    /// </summary>
    Task<IReadOnlyList<PhotoGroup>> BuildGroupsAsync(
        IReadOnlyList<PhotoItem> photos,
        IProgress<string>? progress = null,
        CancellationToken ct = default);
}
