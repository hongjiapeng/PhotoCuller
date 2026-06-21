namespace PhotoCuller.Core.Enums;

/// <summary>
/// Similar-image matching presets for 64-bit perceptual hashes.
/// Lower thresholds are stricter and reduce unrelated-scene false positives.
/// </summary>
public enum SimilarityPreset
{
    Strict = 0,
    Normal = 1,
    Loose = 2,
}
