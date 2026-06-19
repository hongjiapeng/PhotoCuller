namespace PhotoCuller.Core.Interfaces.Scoring;

/// <summary>Exposure analyzer: based on brightness histogram; penalises underexposure and overexposure; outputs 0-100.</summary>
public interface IExposureAnalyzer
{
    Task<float?> AnalyzeAsync(string filePath, CancellationToken ct = default);
}
