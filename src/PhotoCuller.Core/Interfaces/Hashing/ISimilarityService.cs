using PhotoCuller.Core.Models;

namespace PhotoCuller.Core.Interfaces.Hashing;

/// <summary>Determines the degree of similarity between two photos based on their hash values.</summary>
public interface ISimilarityService
{
    /// <summary>
    /// Returns whether two photos are highly similar.
    /// Hamming distance ≤ SimilarityThresholdHigh.
    /// </summary>
    bool IsHighlySimilar(PhotoItem a, PhotoItem b);

    /// <summary>
    /// Returns whether two photos are possibly similar.
    /// Hamming distance ≤ SimilarityThresholdPossible.
    /// </summary>
    bool IsPossiblySimilar(PhotoItem a, PhotoItem b);

    /// <summary>Returns whether two photos are exact duplicates (identical SHA-256).</summary>
    bool IsExactDuplicate(PhotoItem a, PhotoItem b);
}
