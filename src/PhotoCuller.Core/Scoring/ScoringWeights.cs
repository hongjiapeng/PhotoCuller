namespace PhotoCuller.Core.Scoring;

/// <summary>
/// Photo composite scoring weight configuration.
/// The sum of all dimension weights must equal 100.
/// Uses readonly record struct: value semantics, stack-allocated, ideal for frequent comparisons.
/// </summary>
public readonly record struct ScoringWeights
{
    public int SharpnessPercent { get; init; }
    public int ExposurePercent { get; init; }
    public int ContrastPercent { get; init; }
    public int NoisePercent { get; init; }
    public int ResolutionPercent { get; init; }
    public int CompositionPercent { get; init; }
    public int ColorPercent { get; init; }

    /// <summary>Sum of all weights.</summary>
    public int Total =>
        SharpnessPercent + ExposurePercent + ContrastPercent
        + NoisePercent + ResolutionPercent + CompositionPercent
        + ColorPercent;

    /// <summary>
    /// Default weights: Sharpness(35) + Exposure(20) + Contrast(15)
    ///                  + Noise(10) + Resolution(10) + Composition(10) = 100
    /// </summary>
    public static ScoringWeights Default => new()
    {
        SharpnessPercent    = 35,
        ExposurePercent     = 20,
        ContrastPercent     = 15,
        NoisePercent        = 10,
        ResolutionPercent   = 10,
        CompositionPercent  = 10,
        ColorPercent        = 0,
    };

    /// <summary>Returns whether the total weight equals 100.</summary>
    public bool IsValid() => Total == 100;

    public override string ToString() =>
        $"Sharpness={SharpnessPercent} Exposure={ExposurePercent} " +
        $"Contrast={ContrastPercent} Noise={NoisePercent} " +
        $"Resolution={ResolutionPercent} Composition={CompositionPercent} " +
        $"Color={ColorPercent} (Total={Total})";
}
