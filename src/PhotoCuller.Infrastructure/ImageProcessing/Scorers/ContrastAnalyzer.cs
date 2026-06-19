using Microsoft.Extensions.Logging;
using OpenCvSharp;
using PhotoCuller.Core.Interfaces.Scoring;

namespace PhotoCuller.Infrastructure.ImageProcessing.Scorers;

/// <summary>
/// Contrast scorer based on luminance standard deviation.
///
/// Algorithm:
///   1. Convert to grayscale
///   2. Compute standard deviation of pixel values
///   3. Map stddev to 0-100:
///      - Too low (&lt;20): flat, washed-out image
///      - Ideal range [35, 75]: good contrast
///      - Very high (&gt;90): harsh, over-processed
/// </summary>
public sealed class ContrastAnalyzer : IContrastAnalyzer
{
    private readonly ILogger<ContrastAnalyzer> _logger;

    public ContrastAnalyzer(ILogger<ContrastAnalyzer> logger) => _logger = logger;

    public Task<float?> AnalyzeAsync(string filePath, CancellationToken ct = default)
        => Task.Run(() => Analyze(filePath), ct);

    private float? Analyze(string filePath)
    {
        try
        {
            using var gray = Cv2.ImRead(filePath, ImreadModes.Grayscale);
            if (gray.Empty()) return null;

            Cv2.MeanStdDev(gray, out _, out var stdDev);
            double sd = stdDev.Val0;

            double score;
            if (sd < 20)
                score = (sd / 20.0) * 60.0;         // low contrast: 0-60
            else if (sd <= 75)
                score = 60.0 + (sd - 20) / 55.0 * 40.0; // ideal: 60-100
            else if (sd <= 90)
                score = 100.0;                        // still good
            else
                score = 100.0 - (sd - 90) / 40.0 * 30.0; // harsh: drop toward 70

            return (float)Math.Clamp(score, 0, 100);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Contrast analysis failed for {Path}", filePath);
            return null;
        }
    }
}
