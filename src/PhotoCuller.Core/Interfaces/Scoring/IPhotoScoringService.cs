using PhotoCuller.Core.Models;
using PhotoCuller.Core.Scoring;

namespace PhotoCuller.Core.Interfaces.Scoring;

/// <summary>Photo composite scoring coordinator; aggregates dimension scores by weight.</summary>
public interface IPhotoScoringService
{
    /// <summary>
    /// Computes scores for all dimensions of a single photo and writes them back to the PhotoItem.
    /// If some dimensions fail, the remaining dimension scores are still returned; failed dimensions are null.
    /// </summary>
    Task ScoreAsync(
        PhotoItem photo,
        ScoringWeights weights,
        CancellationToken ct = default);

    /// <summary>Batch scoring (parallel, thread-safe).</summary>
    Task ScoreRangeAsync(
        IReadOnlyList<PhotoItem> photos,
        ScoringWeights weights,
        IProgress<int>? progress = null,
        CancellationToken ct = default);
}
