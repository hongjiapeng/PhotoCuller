namespace PhotoCuller.Core.Interfaces.Scoring;

/// <summary>Sharpness analyzer: based on Laplacian variance; outputs 0-100.</summary>
public interface ISharpnessAnalyzer
{
    /// <returns>Sharpness score in the range 0-100; null if analysis is not possible.</returns>
    Task<float?> AnalyzeAsync(string filePath, CancellationToken ct = default);
}
