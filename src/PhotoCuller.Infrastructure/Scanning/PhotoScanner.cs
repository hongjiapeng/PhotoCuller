using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PhotoCuller.Core.Enums;
using PhotoCuller.Core.Interfaces.Scanning;
using PhotoCuller.Core.Interfaces.Storage;
using PhotoCuller.Core.Models;
using PhotoCuller.Infrastructure.Logging;

namespace PhotoCuller.Infrastructure.Scanning;

/// <summary>
/// Scans a directory tree and yields PhotoItem instances as they are processed.
/// Pipeline: enumerate → incremental filter → metadata read → progress report.
/// Hash computation and scoring happen in later stages (Stages 6 and 7).
/// </summary>
public sealed class PhotoScanner : IPhotoScanner
{
    private static readonly HashSet<string> DefaultExtensions =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ".jpg", ".jpeg", ".png", ".heic", ".heif",
            ".webp", ".bmp", ".tiff", ".tif",
        };

    private readonly IPhotoMetadataReader _metadataReader;
    private readonly IPhotoRepository _photoRepository;
    private readonly IScanProgressService _progressService;
    private readonly ILogger<PhotoScanner> _logger;
    private readonly AppSettings _settings;

    private volatile bool _isScanning;
    public bool IsScanning => _isScanning;

    public PhotoScanner(
        IPhotoMetadataReader metadataReader,
        IPhotoRepository photoRepository,
        IScanProgressService progressService,
        IOptions<AppSettings> settings,
        ILogger<PhotoScanner> logger)
    {
        _metadataReader = metadataReader;
        _photoRepository = photoRepository;
        _progressService = progressService;
        _settings = settings.Value;
        _logger = logger;
    }

    public async IAsyncEnumerable<PhotoItem> ScanAsync(
        string directory,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (_isScanning)
            throw new InvalidOperationException("A scan is already in progress.");

        _isScanning = true;
        var startedAt = DateTime.UtcNow;

        // Use a Channel to bridge the try-catch boundary:
        // producer fills the channel, this method reads and yields.
        var channel = Channel.CreateUnbounded<PhotoItem>(
            new UnboundedChannelOptions { SingleReader = true, SingleWriter = true });

        var producerTask = ProduceAsync(directory, startedAt, channel.Writer, cancellationToken);

        await foreach (var photo in channel.Reader.ReadAllAsync(cancellationToken))
            yield return photo;

        // Propagate any producer exception
        await producerTask;
        _isScanning = false;
    }

    private async Task ProduceAsync(
        string directory,
        DateTime startedAt,
        ChannelWriter<PhotoItem> writer,
        CancellationToken ct)
    {
        try
        {
            Log.ScanStarted(_logger, directory);

            // Phase 1: Enumerate files
            _progressService.Report(new ScanProgress(ScanPhase.Enumerating, 0, 0,
                Message: "Enumerating files..."));

            var allFiles = EnumerateImageFiles(directory, ct).ToList();
            int total = allFiles.Count;
            Log.FilesFound(_logger, total, directory);

            // Phase 2: Incremental filter
            _progressService.Report(new ScanProgress(ScanPhase.Enumerating, 0, total,
                Message: "Checking for changes..."));

            var candidates = allFiles.Select(f =>
            {
                var fi = new FileInfo(f);
                return (Path: f, ModifiedTime: fi.LastWriteTimeUtc);
            });

            var pathsToScan = await _photoRepository
                .GetPathsNeedingRescanAsync(candidates, ct);

            int newTotal = pathsToScan.Count;
            Log.FilesToScan(_logger, newTotal, total - newTotal);

            // Phase 3: Read metadata
            _progressService.Report(new ScanProgress(ScanPhase.ReadingMetadata, 0, newTotal,
                Message: "Reading metadata..."));

            int processed = 0;
            foreach (var filePath in pathsToScan)
            {
                ct.ThrowIfCancellationRequested();

                var photo = BuildPhotoItem(filePath);
                await _metadataReader.ReadAsync(photo, ct);
                photo.ScanTime = DateTime.UtcNow;

                processed++;
                _progressService.Report(new ScanProgress(
                    ScanPhase.ReadingMetadata, processed, newTotal,
                    CurrentFile: photo.FileName, StartedAt: startedAt));

                await writer.WriteAsync(photo, ct);
            }

            _progressService.Report(ScanProgress.Completed(processed, startedAt));
            Log.ScanCompleted(_logger, processed, (DateTime.UtcNow - startedAt).TotalSeconds);
        }
        catch (OperationCanceledException)
        {
            _progressService.Report(ScanProgress.Cancelled(0, 0));
            Log.ScanCancelled(_logger, directory);
            throw;
        }
        catch (Exception ex)
        {
            _progressService.Report(new ScanProgress(ScanPhase.Failed, 0, 0,
                Message: ex.Message));
            Log.ScanFailed(_logger, ex, directory);
            throw;
        }
        finally
        {
            _isScanning = false;
            writer.TryComplete();
        }
    }

    private IEnumerable<string> EnumerateImageFiles(string directory, CancellationToken ct)
    {
        var opts = new EnumerationOptions
        {
            RecurseSubdirectories = _settings.IsRecursiveScanEnabled,
            IgnoreInaccessible = true,
            AttributesToSkip = BuildAttributesToSkip(),
        };

        var maxBytes = (long)_settings.MaxFileSizeMb * 1024 * 1024;
        var result = new List<string>();

        foreach (var file in System.IO.Directory.EnumerateFiles(directory, "*", opts))
        {
            ct.ThrowIfCancellationRequested();

            var ext = Path.GetExtension(file);
            if (!IsExtensionSupported(ext)) continue;

            long length;
            try { length = new FileInfo(file).Length; }
            catch { continue; } // Skip inaccessible files

            if (maxBytes > 0 && length > maxBytes) continue;

            result.Add(file);
        }

        return result;
    }

    private FileAttributes BuildAttributesToSkip()
    {
        var skip = FileAttributes.System;
        if (_settings.SkipHiddenFolders)
            skip |= FileAttributes.Hidden;
        return skip;
    }

    private bool IsExtensionSupported(string extension)
    {
        if (_settings.SupportedExtensions?.Count > 0)
            return _settings.SupportedExtensions.Contains(
                extension, StringComparer.OrdinalIgnoreCase);
        return DefaultExtensions.Contains(extension);
    }

    private static PhotoItem BuildPhotoItem(string filePath) => new()
    {
        FilePath = filePath,
        FileName = Path.GetFileName(filePath),
        FileExtension = Path.GetExtension(filePath).ToLowerInvariant(),
        RecommendationStatus = RecommendationStatus.Unprocessed,
    };
}
