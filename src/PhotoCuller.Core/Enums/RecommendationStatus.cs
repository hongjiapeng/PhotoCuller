namespace PhotoCuller.Core.Enums;

/// <summary>Photo recommendation status.</summary>
public enum RecommendationStatus
{
    /// <summary>Unprocessed (initial state).</summary>
    Unprocessed = 0,

    /// <summary>Recommended to keep.</summary>
    Keep = 1,

    /// <summary>Awaiting user review.</summary>
    Review = 2,

    /// <summary>Moved to the pending-delete folder.</summary>
    ToDelete = 3,

    /// <summary>Manually marked as keep by the user (highest priority).</summary>
    ManualKeep = 4,

    /// <summary>Manually marked for deletion by the user.</summary>
    ManualDelete = 5,
}
