using PhotoCuller.Core.Interfaces.Scoring;

namespace PhotoCuller.Infrastructure.ImageProcessing.Scorers;

/// <summary>
/// Composition analyzer — placeholder implementation for Stage 7.
/// Returns a fixed score of 65 (slightly above neutral).
/// Interface is preserved for future replacement with SAMP-Net or
/// a rule-of-thirds / saliency-based scorer.
/// </summary>
public sealed class CompositionAnalyzer : ICompositionAnalyzer
{
    public Task<float?> AnalyzeAsync(string filePath, CancellationToken ct = default)
        => Task.FromResult<float?>(65f);
}
