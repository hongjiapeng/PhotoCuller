using Microsoft.Extensions.Options;
using PhotoCuller.Core.Interfaces.Hashing;
using PhotoCuller.Core.Models;

namespace PhotoCuller.Infrastructure.Hashing;

/// <summary>
/// Determines similarity between two photos using their pre-computed hashes.
/// Thresholds are read from AppSettings so users can tune them in the Settings page.
/// </summary>
public sealed class SimilarityService : ISimilarityService
{
    private readonly AppSettings _settings;
    private readonly IPerceptualHashService _pHash;

    public SimilarityService(
        IOptions<AppSettings> settings,
        IPerceptualHashService pHash)
    {
        _settings = settings.Value;
        _pHash = pHash;
    }

    /// <summary>
    /// Returns true when both photos have a PerceptualHash and their
    /// Hamming distance is ≤ SimilarityThresholdHigh (default 8).
    /// </summary>
    public bool IsHighlySimilar(PhotoItem a, PhotoItem b)
    {
        if (a.PerceptualHash is null || b.PerceptualHash is null) return false;
        return _pHash.HammingDistance(a.PerceptualHash.Value, b.PerceptualHash.Value)
               <= _settings.SimilarityThresholdHigh;
    }

    /// <summary>
    /// Returns true when Hamming distance ≤ SimilarityThresholdPossible (default 15).
    /// </summary>
    public bool IsPossiblySimilar(PhotoItem a, PhotoItem b)
    {
        if (a.PerceptualHash is null || b.PerceptualHash is null) return false;
        return _pHash.HammingDistance(a.PerceptualHash.Value, b.PerceptualHash.Value)
               <= _settings.SimilarityThresholdPossible;
    }

    /// <summary>
    /// Returns true when both photos have a SHA-256 hash and they are identical.
    /// </summary>
    public bool IsExactDuplicate(PhotoItem a, PhotoItem b)
    {
        if (string.IsNullOrEmpty(a.Sha256Hash) || string.IsNullOrEmpty(b.Sha256Hash))
            return false;
        return string.Equals(a.Sha256Hash, b.Sha256Hash, StringComparison.OrdinalIgnoreCase);
    }
}
