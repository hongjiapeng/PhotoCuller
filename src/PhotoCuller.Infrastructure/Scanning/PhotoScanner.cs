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
/// Pipeline: enumerate → reuse unchanged snapshots / read changed metadata → progress report.
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

    private int _isScanning;
    public bool IsScanning => Volatile.Read(ref _isScanning) != 0;

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
        cancellationToken.ThrowIfCancellationRequested();
        directory = Path.GetFullPath(directory);
        if (!System.IO.Directory.Exists(directory))
            throw new DirectoryNotFoundException(directory);
        if (Interlocked.CompareExchange(ref _isScanning, 1, 0) != 0)
            throw new InvalidOperationException("A scan is already in progress.");

        var startedAt = DateTime.UtcNow;
        using var producerCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        // Use a Channel to bridge the try-catch boundary:
        // producer fills the channel, this method reads and yields.
        var channel = Channel.CreateBounded<PhotoItem>(
            new BoundedChannelOptions(1)
            {
                FullMode = BoundedChannelFullMode.Wait,
                SingleReader = true,
                SingleWriter = true,
            });

        var producerTask = ProduceAsync(directory, startedAt, channel.Writer, producerCts.Token);
        try
        {
            await foreach (var photo in channel.Reader.ReadAllAsync(cancellationToken))
                yield return photo;
        }
        finally
        {
            // Early consumer disposal must release a producer waiting on a full channel.
            await producerCts.CancelAsync();
            await producerTask;
            Interlocked.Exchange(ref _isScanning, 0);
        }
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

            var allFiles = await Task.Run(() => EnumerateImageFiles(directory, ct).ToList(), ct);
            int total = allFiles.Count;
            Log.FilesFound(_logger, total, directory);

            // Phase 2: Incremental filter
            _progressService.Report(new ScanProgress(ScanPhase.Enumerating, 0, total,
                Message: "Checking for changes..."));

            var existing = (await _photoRepository.GetAllAsync(ct))
                .GroupBy(p => Path.GetFullPath(p.FilePath), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

            // Phase 3: Read metadata
            _progressService.Report(new ScanProgress(ScanPhase.ReadingMetadata, 0, total,
                Message: "Reading metadata..."));

            int processed = 0;
            foreach (var filePath in allFiles)
            {
                ct.ThrowIfCancellationRequested();

                var file = new FileInfo(filePath);
                existing.TryGetValue(filePath, out var stored);
                PhotoItem photo;
                if (stored is not null
                    && stored.FileSize == file.Length
                    && stored.ModifiedTime == file.LastWriteTimeUtc
                    && stored.ScanTime.HasValue
                    && stored.Width > 0 && stored.Height > 0
                    && stored.AnalysisVersion == PhotoItem.CurrentAnalysisVersion
                    && string.IsNullOrEmpty(stored.ErrorMessage))
                {
                    photo = stored.CreateSnapshot();
                }
                else
                {
                    photo = BuildPhotoItem(filePath);
                    if (stored is not null)
                    {
                        photo.Id = stored.Id;
                        photo.RecommendationStatus = stored.RecommendationStatus;
                    }
                    await _metadataReader.ReadAsync(photo, ct);
                    photo.ScanTime = DateTime.UtcNow;
                }

                photo.GroupId = null;
                photo.IsMovedToTrash = false;
                if (photo.RecommendationStatus is not RecommendationStatus.ManualKeep
                    and not RecommendationStatus.ManualDelete)
                    photo.RecommendationStatus = RecommendationStatus.Unprocessed;

                processed++;
                _progressService.Report(new ScanProgress(
                    ScanPhase.ReadingMetadata, processed, total,
                    CurrentFile: photo.FileName, StartedAt: startedAt));

                await writer.WriteAsync(photo, ct);
            }

            _progressService.Report(ScanProgress.Completed(processed, startedAt));
            Log.ScanCompleted(_logger, processed, (DateTime.UtcNow - startedAt).TotalSeconds);
        }
        catch (OperationCanceledException ex)
        {
            _progressService.Report(ScanProgress.Cancelled(0, 0));
            Log.ScanCancelled(_logger, directory);
            writer.TryComplete(ex);
        }
        catch (Exception ex)
        {
            _progressService.Report(new ScanProgress(ScanPhase.Failed, 0, 0,
                Message: ex.Message));
            Log.ScanFailed(_logger, ex, directory);
            writer.TryComplete(ex);
        }
        finally
        {
            writer.TryComplete();
        }
    }

    private IEnumerable<string> EnumerateImageFiles(string directory, CancellationToken ct)
    {
        var opts = new EnumerationOptions
        {
            RecurseSubdirectories = false,
            IgnoreInaccessible = false,
            AttributesToSkip = BuildAttributesToSkip(),
        };

        var maxBytes = (long)_settings.MaxFileSizeMb * 1024 * 1024;
        var pending = new Stack<string>();
        pending.Push(directory);
        while (pending.TryPop(out var current))
        {
            ct.ThrowIfCancellationRequested();
            foreach (var file in System.IO.Directory.EnumerateFiles(current, "*", opts))
            {
                ct.ThrowIfCancellationRequested();
                if (!IsExtensionSupported(Path.GetExtension(file))) continue;
                if (maxBytes > 0 && new FileInfo(file).Length > maxBytes) continue;
                yield return file;
            }
            if (!_settings.IsRecursiveScanEnabled) continue;
            foreach (var child in System.IO.Directory.EnumerateDirectories(current, "*", opts))
            {
                if (!string.Equals(Path.GetFileName(child), _settings.ToDeleteFolderName,
                        StringComparison.OrdinalIgnoreCase))
                    pending.Push(child);
            }
        }
    }

    private FileAttributes BuildAttributesToSkip()
    {
        var skip = FileAttributes.ReparsePoint;
        if (_settings.SkipSystemFolders)
            skip |= FileAttributes.System;
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
