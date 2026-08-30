using Microsoft.Extensions.Options;
using PhotoCuller.Core.Interfaces.Hashing;
using PhotoCuller.Core.Models;

namespace PhotoCuller.Infrastructure.Hashing;

/// <summary>
/// Determines similarity between two photos using their pre-computed hashes.
/// Thresholds are resolved from the selected user-facing search profile.
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
    /// Hamming distance is within the profile's global threshold.
    /// </summary>
    public bool IsHighlySimilar(PhotoItem a, PhotoItem b)
    {
        if (a.PerceptualHash is null || b.PerceptualHash is null) return false;
        var profile = SimilarityThresholds.FromSettings(_settings);
        return _pHash.HammingDistance(a.PerceptualHash.Value, b.PerceptualHash.Value)
               <= profile.GlobalThreshold;
    }

    /// <summary>
    /// Returns true when Hamming distance is within the profile's sequence threshold.
    /// </summary>
    public bool IsPossiblySimilar(PhotoItem a, PhotoItem b)
    {
        if (a.PerceptualHash is null || b.PerceptualHash is null) return false;
        var profile = SimilarityThresholds.FromSettings(_settings);
        return _pHash.HammingDistance(a.PerceptualHash.Value, b.PerceptualHash.Value)
               <= profile.SequenceThreshold;
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
