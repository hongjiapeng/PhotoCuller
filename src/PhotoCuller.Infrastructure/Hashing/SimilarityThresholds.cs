using PhotoCuller.Core.Enums;
using PhotoCuller.Core.Models;

namespace PhotoCuller.Infrastructure.Hashing;

internal static class SimilarityThresholds
{
    public const int HashSize = 8;

    public static SimilarityProfile FromSettings(AppSettings settings)
    {
        if (settings.UseAdvancedSimilaritySettings)
        {
            var global = Math.Clamp(settings.SimilarityThresholdHigh, 1, 24);
            var sequence = Math.Clamp(settings.SimilarityThresholdPossible, global, 40);
            return new SimilarityProfile(
                global,
                sequence,
                Math.Clamp(settings.BurstTimeWindowSeconds, 1, 300),
                settings.UseCaptureTimeForGrouping,
                MaxSequenceNumberGap: 5);
        }

        return settings.SimilarityPreset switch
        {
            SimilarityPreset.Strict => new SimilarityProfile(8, 20, 30,
                settings.UseCaptureTimeForGrouping, 2),
            SimilarityPreset.Loose => new SimilarityProfile(16, 38, 300,
                settings.UseCaptureTimeForGrouping, 10),
            _ => new SimilarityProfile(12, 32, 120,
                settings.UseCaptureTimeForGrouping, 5),
        };
    }
}

internal readonly record struct SimilarityProfile(
    int GlobalThreshold,
    int SequenceThreshold,
    int SequenceWindowSeconds,
    bool UseCaptureSequence,
    int MaxSequenceNumberGap)
{
    public int SearchThreshold => Math.Max(GlobalThreshold, SequenceThreshold);
}
