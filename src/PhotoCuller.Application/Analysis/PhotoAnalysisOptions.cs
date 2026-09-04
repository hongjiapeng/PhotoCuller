using PhotoCuller.Core.Models;
using PhotoCuller.Core.Scoring;

namespace PhotoCuller.Application.Analysis;

/// <summary>Per-run settings owned by the caller rather than a UI framework.</summary>
public sealed record PhotoAnalysisOptions
{
    public ScoringWeights ScoringWeights { get; init; } = ScoringWeights.Default;

    public bool GenerateThumbnails { get; init; } = true;

    public int ThumbnailMaxEdgePixels { get; init; } = 256;

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
