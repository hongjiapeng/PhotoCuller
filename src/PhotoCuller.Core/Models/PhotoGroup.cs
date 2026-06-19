using PhotoCuller.Core.Enums;

namespace PhotoCuller.Core.Models;

/// <summary>
/// Similar-photo group: aggregates a set of duplicate, similar, or burst-shot photos
/// and records the recommended photo to keep.
/// </summary>
public class PhotoGroup
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public GroupType GroupType { get; set; }

    /// <summary>Display name for the group (e.g. "Burst #1").</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>ID of the recommended photo to keep within the group.</summary>
    public Guid? RecommendedPhotoId { get; set; }

    public int PhotoCount { get; set; }

    /// <summary>Highest similarity score within the group (0-1; ExactDuplicate groups are fixed at 1.0).</summary>
    public float SimilarityScore { get; set; }

    public DateTime CreatedTime { get; set; } = DateTime.UtcNow;

    // ── Navigation properties ────────────────────────────────────────
    public ICollection<PhotoItem> Photos { get; set; } = [];
}
