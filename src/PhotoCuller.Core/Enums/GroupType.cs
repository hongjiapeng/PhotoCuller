namespace PhotoCuller.Core.Enums;

/// <summary>Photo group type.</summary>
public enum GroupType
{
    /// <summary>Exact duplicate (identical SHA-256).</summary>
    ExactDuplicate = 0,

    /// <summary>Visually similar (pHash distance ≤ 15).</summary>
    Similar = 1,

    /// <summary>Burst shot (time-window + pHash dual check).</summary>
    Burst = 2,

    /// <summary>Low-quality photo group.</summary>
    LowQuality = 3,
}
