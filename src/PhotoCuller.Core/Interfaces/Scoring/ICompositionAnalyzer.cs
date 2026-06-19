namespace PhotoCuller.Core.Interfaces.Scoring;

/// <summary>
/// Composition analyzer; outputs 0-100.
/// Version 1 may use a placeholder implementation (center-weight based); the interface is kept for future replacement with SAMP-Net.
/// </summary>
public interface ICompositionAnalyzer
{
    Task<float?> AnalyzeAsync(string filePath, CancellationToken ct = default);
}
