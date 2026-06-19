using Microsoft.Extensions.Logging;
using OpenCvSharp;
using PhotoCuller.Core.Interfaces.Scoring;

namespace PhotoCuller.Infrastructure.ImageProcessing.Scorers;

/// <summary>
/// Exposure scorer based on luminance histogram.
///
/// Algorithm:
///   1. Convert to HSV, extract V (brightness) channel
///   2. Build normalised histogram (256 bins)
///   3. Compute weighted mean brightness
///   4. Penalise underexposure (mean &lt; 85) and overexposure (mean &gt; 200)
///   5. Also penalise images where &gt;15% pixels are crushed black or blown white
/// </summary>
public sealed class ExposureAnalyzer : IExposureAnalyzer
{
    private readonly ILogger<ExposureAnalyzer> _logger;

    public ExposureAnalyzer(ILogger<ExposureAnalyzer> logger) => _logger = logger;

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

            using var vChannel = new Mat();
            Cv2.ExtractChannel(hsv, vChannel, 2);

            // Histogram
            using var hist = new Mat();
            int[] histSize = [256];
            Rangef[] ranges = [new(0, 256)];
            Cv2.CalcHist([vChannel], [0], null, hist, 1, histSize, ranges);

            float total = bgr.Rows * bgr.Cols;
            float[] h = new float[256];
            for (int i = 0; i < 256; i++)
                h[i] = hist.At<float>(i);

            // Weighted mean brightness
            double weightedSum = 0;
            for (int i = 0; i < 256; i++)
                weightedSum += i * h[i];
            double mean = weightedSum / total;

            // Fraction of crushed blacks (0-15) and blown whites (240-255)
            double blackFrac = 0, whiteFrac = 0;
            for (int i = 0; i <= 15; i++)  blackFrac += h[i];
            for (int i = 240; i <= 255; i++) whiteFrac += h[i];
            blackFrac /= total;
            whiteFrac /= total;

            // Base score: penalise deviation from ideal range [90, 190]
            double score;
            if (mean < 85)
                score = 100.0 * (mean / 85.0);          // underexposed ramp
            else if (mean > 200)
                score = 100.0 * (1.0 - (mean - 200) / 55.0); // overexposed ramp
            else
                score = 100.0;                           // ideal zone

            // Additional penalty for clipping
            if (blackFrac > 0.15) score -= (blackFrac - 0.15) * 150;
            if (whiteFrac > 0.15) score -= (whiteFrac - 0.15) * 150;

            return (float)Math.Clamp(score, 0, 100);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Exposure analysis failed for {Path}", filePath);
            return null;
        }
    }
}
