namespace PhotoCuller.Core.Interfaces.Scoring;

/// <summary>Color analyzer: based on saturation distribution; outputs 0-100.</summary>
public interface IColorAnalyzer
{
    Task<float?> AnalyzeAsync(string filePath, CancellationToken ct = default);
}
