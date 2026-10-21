using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PhotoCuller.Core.Interfaces.Scanning;
using PhotoCuller.Core.Models;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Processing;

namespace PhotoCuller.Infrastructure.ThumbnailCache;

/// <summary>
/// Generates and caches photo thumbnails on disk.
///
/// Storage layout:  {cacheRoot}/{hash[0..1]}/{hash[2..3]}/{hash}.jpg
/// Example:         thumbnails/a3/f7/a3f7b2c9d1e4...jpg
///
/// Bucketing avoids slow directory enumeration when the library is large.
/// Cache identity includes file path, size, modification time and processing version.
/// Each requested edge length has its own file. Only complete JPEGs are published.
/// </summary>
public sealed class ThumbnailService : IThumbnailService, IDisposable
{
    private readonly string _cacheRoot;
    private readonly ILogger<ThumbnailService> _logger;
    private readonly SemaphoreSlim _generationBudget = new(4, 4);
    private readonly SemaphoreSlim[] _generationLocks = Enumerable.Range(0, 64)
        .Select(_ => new SemaphoreSlim(1, 1)).ToArray();

    private static readonly JpegEncoder ThumbEncoder = new() { Quality = 80 };

    public ThumbnailService(
        IOptions<AppSettings> settings,
        ILogger<ThumbnailService> logger)
    {
        _logger = logger;

        var configured = settings.Value.ThumbnailCacheDirectory;
        _cacheRoot = string.IsNullOrWhiteSpace(configured)
            ? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "PhotoCuller", "thumbnails")
            : configured;

        Directory.CreateDirectory(_cacheRoot);
    }

    /// <summary>
    /// Returns the cached thumbnail path, generating it on first call.
    /// Returns null if the source file cannot be decoded.
    /// </summary>
    public async Task<string?> GetOrCreateAsync(
        PhotoItem photo,
        int maxEdgePx = 256,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxEdgePx);
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            var thumbPath = GetThumbnailPath(photo, maxEdgePx);

            // Cache hit: thumbnail exists and is not stale
            if (File.Exists(thumbPath) && !IsStale(thumbPath, photo))
                return thumbPath;

            var gate = _generationLocks[(uint)StringComparer.Ordinal.GetHashCode(thumbPath)
                % (uint)_generationLocks.Length];
            await gate.WaitAsync(cancellationToken);
            try
            {
                if (File.Exists(thumbPath) && !IsStale(thumbPath, photo))
                    return thumbPath;
                await _generationBudget.WaitAsync(cancellationToken);
                try
                {
                    await GenerateAsync(photo.FilePath, thumbPath, maxEdgePx, cancellationToken);
                    return thumbPath;
                }
                finally { _generationBudget.Release(); }
            }
            finally { gate.Release(); }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Thumbnail generation failed for {Path}", photo.FilePath);
            return null;
        }
    }

    public Task DeleteAsync(PhotoItem photo, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var path = GetThumbnailPath(photo, 256);
        var key = ComputeCacheKey(photo);
        foreach (var cached in Directory.EnumerateFiles(Path.GetDirectoryName(path)!, $"{key}-*.jpg"))
            File.Delete(cached);
        return Task.CompletedTask;
    }

    public Task ClearAllAsync(CancellationToken cancellationToken = default)
    {
        if (Directory.Exists(_cacheRoot))
            Directory.Delete(_cacheRoot, recursive: true);
        Directory.CreateDirectory(_cacheRoot);
        _logger.LogInformation("Thumbnail cache cleared: {Root}", _cacheRoot);
        return Task.CompletedTask;
    }

    public Task<long> GetCacheSizeBytesAsync(CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(_cacheRoot))
            return Task.FromResult(0L);

        var size = new DirectoryInfo(_cacheRoot)
            .EnumerateFiles("*", SearchOption.AllDirectories)
            .Sum(f => f.Length);

        return Task.FromResult(size);
    }

    public void Dispose()
    {
        _generationBudget.Dispose();
        foreach (var gate in _generationLocks) gate.Dispose();
    }

    // ── Internals ────────────────────────────────────────────────────────────

    private string GetThumbnailPath(PhotoItem photo, int maxEdgePx)
    {
        var key = ComputeCacheKey(photo);
        var bucket1 = key[..2];
        var bucket2 = key[2..4];
        var dir = Path.Combine(_cacheRoot, bucket1, bucket2);
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, $"{key}-{maxEdgePx}.jpg");
    }

    private static string ComputeCacheKey(PhotoItem photo)
    {
        var source = new FileInfo(photo.FilePath);
        var path = Path.GetFullPath(photo.FilePath);
        if (OperatingSystem.IsWindows()) path = path.ToUpperInvariant();
        var raw = FormattableString.Invariant(
            $"thumbnail-v2|{path}|{(source.Exists ? source.Length : photo.FileSize)}|{(source.Exists ? source.LastWriteTimeUtc.Ticks : photo.ModifiedTime?.Ticks)}");
        var bytes = System.Text.Encoding.UTF8.GetBytes(raw);
        var hash = System.Security.Cryptography.SHA256.HashData(bytes);
        // First 16 bytes → 32 hex chars (plenty for uniqueness, short for filenames)
        return Convert.ToHexString(hash)[..32].ToLowerInvariant();
    }

    private static bool IsStale(string thumbPath, PhotoItem photo)
    {
        // Thumbnail is stale if the source file was modified after the thumbnail was written
        if (photo.ModifiedTime is null) return false;
        var thumbWritten = File.GetLastWriteTimeUtc(thumbPath);
        return photo.ModifiedTime.Value > thumbWritten;
    }

    private static async Task GenerateAsync(
        string sourcePath,
        string destPath,
        int maxEdgePx,
        CancellationToken ct)
    {
        await Task.Run(async () =>
        {
            var temporaryPath = destPath + $".{Guid.NewGuid():N}.tmp";
            try
            {
                using var image = await Image.LoadAsync(sourcePath, ct);
                image.Mutate(x => x.AutoOrient().Resize(new ResizeOptions
                {
                    Size = new Size(maxEdgePx, maxEdgePx),
                    Mode = ResizeMode.Max,
                    Sampler = KnownResamplers.Lanczos3,
                }));
                await image.SaveAsJpegAsync(temporaryPath, ThumbEncoder, ct);
                ct.ThrowIfCancellationRequested();
                File.Move(temporaryPath, destPath, overwrite: true);
            }
            finally
            {
                if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
            }
        }, ct);
    }
}
