using PhotoCuller.Core.Interfaces.Scoring;

namespace PhotoCuller.Infrastructure.ImageProcessing.Scorers;

/// <summary>
/// Noise analyzer — placeholder implementation for Stage 7.
/// Returns a fixed score of 70 (neutral) so other dimensions are unaffected.
/// Interface is preserved for future replacement with a real noise estimator
/// (e.g. blind/referenceless noise variance via wavelet decomposition).
/// </summary>
public sealed class NoiseAnalyzer : INoiseAnalyzer
{
    public Task<float?> AnalyzeAsync(string filePath, CancellationToken ct = default)
        => Task.FromResult<float?>(70f);
}
