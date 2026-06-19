using PhotoCuller.Core.Models;

namespace PhotoCuller.Core.Interfaces.Ai;

/// <summary>Generates natural-language explanations for photo recommendations (supports both algorithm-only and AI modes).</summary>
public interface IAiExplanationService
{
    /// <summary>
    /// Generates a recommendation reason for the recommended photo within a group.
    /// In algorithm-only mode, produces template text based on score differences.
    /// In AI mode, calls a cloud or local VLM to generate natural language.
    /// </summary>
    Task<string> GenerateRecommendationReasonAsync(
        PhotoItem recommended,
        IReadOnlyList<PhotoItem> groupPhotos,
        CancellationToken ct = default);
}
