namespace PhotoCuller.Core.Interfaces.Scoring;

/// <summary>
/// Noise analyzer; outputs 0-100.
/// Version 1 may use a placeholder implementation (fixed return of 70); the interface is kept for future replacement.
/// </summary>
public interface INoiseAnalyzer
{
    Task<float?> AnalyzeAsync(string filePath, CancellationToken ct = default);
}
