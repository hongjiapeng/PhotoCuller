using Microsoft.Extensions.Logging;
using OpenCvSharp;
using PhotoCuller.Core.Interfaces.Scoring;

namespace PhotoCuller.Infrastructure.ImageProcessing.Scorers;

/// <summary>
/// Sharpness scorer using Laplacian variance.
///
/// Algorithm:
///   1. Load as grayscale
///   2. Apply Laplacian filter (edge/frequency detector)
///   3. Compute variance of the result
///   4. Map variance to 0-100 via sigmoid-like curve
///
/// A sharp image has high-frequency edges → high Laplacian variance.
/// A blurry image has smooth gradients → low variance.
/// </summary>
public sealed class SharpnessAnalyzer : ISharpnessAnalyzer
{
    // Variance at which the score reaches ~50 (tune to real-world photos)
    private const double MidpointVariance = 500.0;

    private readonly ILogger<SharpnessAnalyzer> _logger;

    public SharpnessAnalyzer(ILogger<SharpnessAnalyzer> logger) => _logger = logger;

    public Task<float?> AnalyzeAsync(string filePath, CancellationToken ct = default)
        => Task.Run(() => Analyze(filePath), ct);

    private float? Analyze(string filePath)
    {
        try
        {
            using var gray = Cv2.ImRead(filePath, ImreadModes.Grayscale);
            if (gray.Empty()) return null;

            using var laplacian = new Mat();
            Cv2.Laplacian(gray, laplacian, MatType.CV_64F);

            Cv2.MeanStdDev(laplacian, out _, out var stdDev);
            double variance = stdDev.Val0 * stdDev.Val0;

            // Sigmoid mapping: score = 100 / (1 + e^(-k*(variance - midpoint)))
            // Simplified to a smooth curve capped at 100
            double score = 100.0 * variance / (variance + MidpointVariance);
            return (float)Math.Clamp(score, 0, 100);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Sharpness analysis failed for {Path}", filePath);
            return null;
        }
    }
}
