using PhotoCuller.Core.Enums;
using PhotoCuller.Core.Models;

namespace PhotoCuller.Infrastructure.Hashing;

internal static class SimilarityThresholds
{
    public const int HashSize = 8;

    public static int FromSettings(AppSettings settings)
        => settings.SimilarityPreset switch
        {
            SimilarityPreset.Strict => Math.Clamp(settings.SimilarityThresholdHigh, 1, 64),
            SimilarityPreset.Normal => Math.Clamp(settings.SimilarityThresholdPossible, 1, 64),
            SimilarityPreset.Loose  => Math.Clamp(settings.SimilarityThresholdPossible + 3, 1, 18),
            _                       => Math.Clamp(settings.SimilarityThresholdPossible, 1, 64),
        };
}
