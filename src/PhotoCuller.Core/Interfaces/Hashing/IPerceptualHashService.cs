namespace PhotoCuller.Core.Interfaces.Hashing;

/// <summary>
/// Computes perceptual hashes (pHash / dHash) for similar-photo detection.
/// Hash values are 64-bit unsigned integers; distance is measured by Hamming distance.
/// </summary>
public interface IPerceptualHashService
{
    /// <summary>
    /// Computes the DCT perceptual hash (pHash).
    /// Returns null on failure.
    /// </summary>
    Task<ulong?> ComputePHashAsync(
        string filePath,
        CancellationToken ct = default);

    /// <summary>
    /// Computes the difference hash (dHash).
    /// Returns null on failure.
    /// </summary>
    Task<ulong?> ComputeDHashAsync(
        string filePath,
        CancellationToken ct = default);

    /// <summary>Calculates the Hamming distance between two perceptual hashes (0-64).</summary>
    int HammingDistance(ulong hashA, ulong hashB);
}
