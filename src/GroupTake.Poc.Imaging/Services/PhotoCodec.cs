using OpenCvSharp;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace GroupTake.Poc.Imaging.Services;

public static class PhotoCodec
{
    public static Mat Load(string path, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var extension = Path.GetExtension(path).ToLowerInvariant();
        if (extension is not (".jpg" or ".jpeg" or ".png"))
            throw new ArgumentException("Only JPEG and PNG photographs are supported.");
        // Same AutoOrient approach as PhotoCuller ThumbnailService. No thumbnail/cache/database dependency.
        using var image = Image.Load<Rgb24>(path);
        image.Mutate(x => x.AutoOrient());
        ct.ThrowIfCancellationRequested();
        var pixels = new byte[checked(image.Width * image.Height * 3)];
        image.CopyPixelDataTo(pixels);
        using var rgb = Mat.FromPixelData(image.Height, image.Width, MatType.CV_8UC3, pixels);
        var bgr = new Mat();
        try { Cv2.CvtColor(rgb, bgr, ColorConversionCodes.RGB2BGR); return bgr; }
        catch { bgr.Dispose(); throw; }
    }

    public static byte[] Preview(Mat photo, int maxEdge = 2000)
    {
        using var preview = new Mat();
        var scale = Math.Min(1, maxEdge / (double)Math.Max(photo.Width, photo.Height));
        Cv2.Resize(photo, preview, new OpenCvSharp.Size(Math.Max(1, (int)(photo.Width * scale)),
            Math.Max(1, (int)(photo.Height * scale))), interpolation: InterpolationFlags.Area);
        return preview.ToBytes(".png");
    }

    public static void Export(Mat photo, string destination, IEnumerable<string> sourcePaths,
        int jpegQuality = 95, CancellationToken ct = default)
    {
        var full = Path.GetFullPath(destination);
        if (sourcePaths.Any(p => string.Equals(Path.GetFullPath(p), full, StringComparison.OrdinalIgnoreCase)))
            throw new IOException("Cannot overwrite an original photograph.");
        if (File.Exists(full)) throw new IOException("Destination already exists. Choose a new filename.");
        var ext = Path.GetExtension(full).ToLowerInvariant();
        if (ext is not (".jpg" or ".jpeg" or ".png")) throw new ArgumentException("Choose JPEG or PNG.");
        if (jpegQuality is < 1 or > 100) throw new ArgumentOutOfRangeException(nameof(jpegQuality));
        ct.ThrowIfCancellationRequested();
        var data = ext == ".png" ? photo.ToBytes(".png")
            : photo.ToBytes(".jpg", new ImageEncodingParam(ImwriteFlags.JpegQuality, jpegQuality));
        ct.ThrowIfCancellationRequested();
        var temporary = full + $".{Guid.NewGuid():N}.tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                stream.Write(data);
                stream.Flush(flushToDisk: true);
            }
            ct.ThrowIfCancellationRequested();
            File.Move(temporary, full, overwrite: false);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
