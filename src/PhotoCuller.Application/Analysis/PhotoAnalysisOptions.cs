using PhotoCuller.Core.Models;
using PhotoCuller.Core.Scoring;

namespace PhotoCuller.Application.Analysis;

/// <summary>Per-run settings owned by the caller rather than a UI framework.</summary>
public sealed record PhotoAnalysisOptions
{
    public ScoringWeights ScoringWeights { get; init; } = ScoringWeights.Default;

    public bool GenerateThumbnails { get; init; } = true;

    public int ThumbnailMaxEdgePixels { get; init; } = 256;

    /// <summary>False limits full-file hashing to repeated file sizes. CLI defaults to complete hashes.</summary>
    public bool ComputeAllFileHashes { get; init; } = true;

    /// <summary>
    /// Receives a detached preview once its thumbnail is ready, before hashing,
    /// scoring and grouping finish. Awaited serially so the UI can keep up.
    /// Preview changes are provisional until AnalyzeAsync succeeds.
    /// </summary>
    public Func<PhotoItem, CancellationToken, Task>? OnPhotoReady { get; init; }

    public static PhotoAnalysisOptions FromSettings(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        return new PhotoAnalysisOptions
        {
            ScoringWeights = settings.ScoringWeights,
            GenerateThumbnails = settings.GenerateThumbnails,
        };
    }
}
