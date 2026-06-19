using Microsoft.Extensions.Logging;
using OpenCvSharp;
using PhotoCuller.Core.Interfaces.Scanning;

namespace PhotoCuller.Infrastructure.Scanning;

/// <summary>
/// Decodes image files to raw pixel arrays for scoring and hashing.
/// Uses OpenCvSharp for efficient grayscale/RGB decoding.
/// Limits decoded image size to avoid OOM on very large files.
/// </summary>
public sealed class ImageDecoder : IImageDecoder
{
    /// <summary>Max edge length before downscaling for analysis (not for display).</summary>
    private const int MaxAnalysisEdge = 1024;

    private readonly ILogger<ImageDecoder> _logger;

    public ImageDecoder(ILogger<ImageDecoder> logger) => _logger = logger;

    public Task<(int Width, int Height, byte[] GrayPixels)?> DecodeGrayAsync(
        string filePath, CancellationToken cancellationToken = default)
        => Task.Run(() => DecodeGray(filePath), cancellationToken);

    public Task<(int Width, int Height, byte[] RgbPixels)?> DecodeRgbAsync(
        string filePath, CancellationToken cancellationToken = default)
        => Task.Run(() => DecodeRgb(filePath), cancellationToken);

    private (int Width, int Height, byte[] GrayPixels)? DecodeGray(string filePath)
    {
        try
        {
            using var mat = Cv2.ImRead(filePath, ImreadModes.Grayscale);
            if (mat.Empty()) return null;

            using var resized = MaybeResize(mat);
            var pixels = new byte[resized.Rows * resized.Cols];
            System.Runtime.InteropServices.Marshal.Copy(resized.Data, pixels, 0, pixels.Length);
            return (resized.Cols, resized.Rows, pixels);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Gray decode failed for {Path}", filePath);
            return null;
        }
    }

    private (int Width, int Height, byte[] RgbPixels)? DecodeRgb(string filePath)
    {
        try
        {
            using var mat = Cv2.ImRead(filePath, ImreadModes.Color);
            if (mat.Empty()) return null;

            using var rgb = new Mat();
            Cv2.CvtColor(mat, rgb, ColorConversionCodes.BGR2RGB);

            using var resized = MaybeResize(rgb);
            int bytes = resized.Rows * resized.Cols * 3;
            var pixels = new byte[bytes];
            System.Runtime.InteropServices.Marshal.Copy(resized.Data, pixels, 0, bytes);
            return (resized.Cols, resized.Rows, pixels);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "RGB decode failed for {Path}", filePath);
            return null;
        }
    }

    private static Mat MaybeResize(Mat src)
    {
        int maxEdge = Math.Max(src.Cols, src.Rows);
        if (maxEdge <= MaxAnalysisEdge)
            return src.Clone();

        double scale = (double)MaxAnalysisEdge / maxEdge;
        var dst = new Mat();
        Cv2.Resize(src, dst, new Size(0, 0), scale, scale, InterpolationFlags.Area);
        return dst;
    }
}
