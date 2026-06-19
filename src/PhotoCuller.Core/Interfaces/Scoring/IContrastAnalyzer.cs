namespace PhotoCuller.Core.Interfaces.Scoring;

/// <summary>Contrast analyzer: based on brightness standard deviation; outputs 0-100.</summary>
public interface IContrastAnalyzer
{
    Task<float?> AnalyzeAsync(string filePath, CancellationToken ct = default);
}
