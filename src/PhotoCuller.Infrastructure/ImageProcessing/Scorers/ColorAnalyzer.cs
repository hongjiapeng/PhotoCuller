using Microsoft.Extensions.Logging;
using OpenCvSharp;
using PhotoCuller.Core.Interfaces.Scoring;

namespace PhotoCuller.Infrastructure.ImageProcessing.Scorers;

/// <summary>
/// Color scorer based on HSV saturation distribution.
///
/// Algorithm:
///   1. Convert to HSV
///   2. Compute mean saturation across the image
///   3. Map mean saturation to 0-100:
///      - Very low (&lt;20): near-grayscale, low colour richness
///      - Ideal [40, 140]: vibrant but natural
///      - Very high (&gt;180): oversaturated / HDR artefact
/// </summary>
public sealed class ColorAnalyzer : IColorAnalyzer
{
    private readonly ILogger<ColorAnalyzer> _logger;

    public ColorAnalyzer(ILogger<ColorAnalyzer> logger) => _logger = logger;

    public Task<float?> AnalyzeAsync(string filePath, CancellationToken ct = default)
        => Task.Run(() => Analyze(filePath), ct);

    private float? Analyze(string filePath)
    {
        try
        {
            using var bgr = Cv2.ImRead(filePath, ImreadModes.Color);
            if (bgr.Empty()) return null;

            using var hsv = new Mat();
            Cv2.CvtColor(bgr, hsv, ColorConversionCodes.BGR2HSV);

            using var sChannel = new Mat();
            Cv2.ExtractChannel(hsv, sChannel, 1); // S channel (0-255)

            Cv2.MeanStdDev(sChannel, out var mean, out _);
            double s = mean.Val0; // 0-255

            double score;
            if (s < 20)
                score = (s / 20.0) * 50.0;           // near-grayscale
            else if (s <= 140)
                score = 50.0 + (s - 20) / 120.0 * 50.0; // ramp to 100
            else if (s <= 180)
                score = 100.0;                        // vibrant
            else
                score = 100.0 - (s - 180) / 75.0 * 30.0; // oversaturated

            return (float)Math.Clamp(score, 0, 100);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Color analysis failed for {Path}", filePath);
            return null;
        }
    }
}
