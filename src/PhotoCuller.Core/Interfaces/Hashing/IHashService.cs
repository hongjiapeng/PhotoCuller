namespace PhotoCuller.Core.Interfaces.Hashing;

/// <summary>Computes the SHA-256 hash of a file for exact-duplicate detection.</summary>
public interface IHashService
{
    /// <summary>
    /// Computes the SHA-256 hash of a file and returns it as a lowercase hex string.
    /// Returns null if the file does not exist or cannot be read.
    /// </summary>
    Task<string?> ComputeSha256Async(
        string filePath,
        CancellationToken ct = default);
}
