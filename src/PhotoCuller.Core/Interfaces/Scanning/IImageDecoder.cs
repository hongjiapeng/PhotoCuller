namespace PhotoCuller.Core.Interfaces.Scanning;

/// <summary>Decodes image files into in-memory pixel data for use by the scoring and hashing modules.</summary>
public interface IImageDecoder
{
    /// <summary>Decodes an image to a grayscale byte array (used for pHash / scoring).</summary>
    /// <returns>(width, height, grayPixels) on success; null on failure.</returns>
    Task<(int Width, int Height, byte[] GrayPixels)?> DecodeGrayAsync(
        string filePath,
        CancellationToken cancellationToken = default);

    /// <summary>Decodes an image to an RGB byte array (used for color scoring).</summary>
    Task<(int Width, int Height, byte[] RgbPixels)?> DecodeRgbAsync(
        string filePath,
        CancellationToken cancellationToken = default);
}
