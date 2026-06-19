using MetadataExtractor;
using MetadataExtractor.Formats.Exif;
using MetadataExtractor.Formats.Jpeg;
using MetadataExtractor.Formats.Png;
using Microsoft.Extensions.Logging;
using PhotoCuller.Core.Interfaces.Scanning;
using PhotoCuller.Core.Models;
using PhotoCuller.Infrastructure.Logging;
using SixLabors.ImageSharp;

namespace PhotoCuller.Infrastructure.Scanning;

/// <summary>
/// Reads image metadata (dimensions, EXIF data) using MetadataExtractor.
/// Failures are caught per-file and stored in PhotoItem.ErrorMessage — never throws.
/// </summary>
public sealed class PhotoMetadataReader : IPhotoMetadataReader
{
    private readonly ILogger<PhotoMetadataReader> _logger;

    public PhotoMetadataReader(ILogger<PhotoMetadataReader> logger) => _logger = logger;

    public async Task ReadAsync(PhotoItem photo, CancellationToken cancellationToken = default)
    {
        try
        {
            await Task.Run(() => ReadCore(photo), cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Log.MetadataFailed(_logger, ex, photo.FilePath);
            photo.ErrorMessage = $"Metadata read failed: {ex.Message}";
        }
    }

    private static void ReadCore(PhotoItem photo)
    {
        var fi = new FileInfo(photo.FilePath);
        photo.FileSize = fi.Length;
        photo.CreatedTime = fi.CreationTimeUtc;
        photo.ModifiedTime = fi.LastWriteTimeUtc;

        // Try MetadataExtractor first (supports EXIF in JPEG, PNG, HEIC, etc.)
        try
        {
            var directories = ImageMetadataReader.ReadMetadata(photo.FilePath);
            ExtractExif(photo, directories);
            ExtractDimensions(photo, directories);
        }
        catch
        {
            // Fall back to ImageSharp for dimensions-only
        }

        // If dimensions still unknown, fall back to ImageSharp
        if (photo.Width == 0 || photo.Height == 0)
            TryReadDimensionsWithImageSharp(photo);
    }

    private static void ExtractExif(PhotoItem photo, IEnumerable<MetadataExtractor.Directory> directories)
    {
        // Find the first directory that contains EXIF date/time tags
        MetadataExtractor.Directory? exif = null;
        foreach (var dir in directories)
        {
            if (dir is ExifIfd0Directory or ExifSubIfdDirectory)
            {
                exif = dir;
                break;
            }
        }

        if (exif is null) return;

        // Capture date/time (prefer DateTimeOriginal → DateTime)
        if (exif.TryGetDateTime(ExifDirectoryBase.TagDateTimeOriginal, out var taken))
            photo.ExifTakenTime = taken;
        else if (exif.TryGetDateTime(ExifDirectoryBase.TagDateTime, out var dt))
            photo.ExifTakenTime = dt;
    }

    private static void ExtractDimensions(
        PhotoItem photo, IEnumerable<MetadataExtractor.Directory> directories)
    {
        // JPEG
        var jpeg = directories.OfType<JpegDirectory>().FirstOrDefault();
        if (jpeg is not null)
        {
            photo.Width = jpeg.GetImageWidth();
            photo.Height = jpeg.GetImageHeight();
            return;
        }

        // PNG
        var png = directories.OfType<PngDirectory>().FirstOrDefault();
        if (png is not null &&
            png.TryGetInt32(PngDirectory.TagImageWidth, out var w) &&
            png.TryGetInt32(PngDirectory.TagImageHeight, out var h))
        {
            photo.Width = w;
            photo.Height = h;
            return;
        }

        // Generic Exif pixel dimensions
        var exif = directories.OfType<ExifIfd0Directory>().FirstOrDefault();
        if (exif is not null)
        {
            if (exif.TryGetInt32(ExifDirectoryBase.TagImageWidth, out var ew))
                photo.Width = ew;
            if (exif.TryGetInt32(ExifDirectoryBase.TagImageHeight, out var eh))
                photo.Height = eh;
        }
    }

    private static void TryReadDimensionsWithImageSharp(PhotoItem photo)
    {
        try
        {
            var info = Image.Identify(photo.FilePath);
            if (info is not null)
            {
                photo.Width = info.Width;
                photo.Height = info.Height;
            }
        }
        catch
        {
            // Dimensions remain 0 — not a fatal error
        }
    }
}
