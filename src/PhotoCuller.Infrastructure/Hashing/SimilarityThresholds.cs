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
            var sequence = Math.Clamp(settings.SimilarityThresholdPossible, global, 32);
            return new SimilarityProfile(
                global,
                Math.Clamp(global + 4, global, 24),
                sequence,
                Math.Clamp(sequence - 2, global, 30),
                Math.Clamp(settings.BurstTimeWindowSeconds, 1, 300),
                settings.UseCaptureTimeForGrouping,
                MaxSequenceNumberGap: 1,
                BurstMaxIntervalSeconds: 2);
        }

        return settings.SimilarityPreset switch
        {
            SimilarityPreset.Strict => new SimilarityProfile(8, 12, 20, 18, 30,
                settings.UseCaptureTimeForGrouping, 1, 2),
            SimilarityPreset.Loose => new SimilarityProfile(16, 20, 32, 30, 300,
                settings.UseCaptureTimeForGrouping, 1, 2),
            _ => new SimilarityProfile(12, 16, 30, 26, 120,
                settings.UseCaptureTimeForGrouping, 1, 2),
        };
    }
}

internal readonly record struct SimilarityProfile(
    int GlobalThreshold,
    int GlobalDifferenceThreshold,
    int SequenceThreshold,
    int SequenceDifferenceThreshold,
    int SequenceWindowSeconds,
    bool UseCaptureSequence,
    int MaxSequenceNumberGap,
    int BurstMaxIntervalSeconds)
{
    public int SearchThreshold => Math.Max(GlobalThreshold, SequenceThreshold);
}
