using Microsoft.Extensions.Logging;
using PhotoCuller.Application.Contracts;
using PhotoCuller.Core.Enums;
using PhotoCuller.Core.Interfaces.Hashing;
using PhotoCuller.Core.Interfaces.Scanning;
using PhotoCuller.Core.Interfaces.Scoring;
using PhotoCuller.Core.Models;

namespace PhotoCuller.Application.Analysis;

/// <summary>
/// Coordinates the existing analysis components without depending on a UI,
/// persistence format, or operating-system API.
/// </summary>
public sealed class PhotoAnalysisEngine : IPhotoAnalysisEngine
{
    private readonly IPhotoScanner _scanner;
    private readonly IThumbnailService _thumbnails;
    private readonly IHashService _hash;
    private readonly IPerceptualHashService _perceptualHash;
    private readonly IPhotoScoringService _scoring;
    private readonly IDuplicateGroupingService _grouping;
    private readonly IScanProgressService _scannerProgress;
    private readonly ILogger<PhotoAnalysisEngine> _logger;

    public PhotoAnalysisEngine(
        IPhotoScanner scanner,
        IThumbnailService thumbnails,
        IHashService hash,
        IPerceptualHashService perceptualHash,
        IPhotoScoringService scoring,
        IDuplicateGroupingService grouping,
        IScanProgressService scannerProgress,
        ILogger<PhotoAnalysisEngine> logger)
    {
        _scanner = scanner;
        _thumbnails = thumbnails;
        _hash = hash;
        _perceptualHash = perceptualHash;
        _scoring = scoring;
        _grouping = grouping;
        _scannerProgress = scannerProgress;
        _logger = logger;
    }

    public async Task<PhotoAnalysisRun> AnalyzeAsync(
        string directory,
        PhotoAnalysisOptions? options = null,
        IProgress<ScanProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        cancellationToken.ThrowIfCancellationRequested();

        var sourceDirectory = Path.GetFullPath(directory);
        if (!Directory.Exists(sourceDirectory))
            throw new DirectoryNotFoundException($"Photo directory not found: {sourceDirectory}");

        options ??= new PhotoAnalysisOptions();
        if (!options.ScoringWeights.IsValid())
            throw new ArgumentException("Scoring weights must add up to 100.", nameof(options));
        if (options.ThumbnailMaxEdgePixels <= 0)
            throw new ArgumentOutOfRangeException(
                nameof(options), "Thumbnail size must be greater than zero.");

        var startedAt = DateTimeOffset.UtcNow;
        var photos = new List<PhotoItem>();

        using var scannerProgressCts = CancellationTokenSource
            .CreateLinkedTokenSource(cancellationToken);
        var scannerProgressTask = ForwardScannerProgressAsync(
            progress,
            scannerProgressCts.Token);

        try
        {
            await foreach (var photo in _scanner.ScanAsync(
                               sourceDirectory,
                               cancellationToken))
            {
                var current = photos.Count + 1;
                var total = Math.Max(_scannerProgress.Current.Total, current);

                if (options.GenerateThumbnails)
                {
                    progress?.Report(new ScanProgress(
                        ScanPhase.GeneratingThumbnails,
                        current,
                        total,
                        CurrentFile: photo.FileName));
                    photo.ThumbnailPath = await _thumbnails.GetOrCreateAsync(
                        photo,
                        options.ThumbnailMaxEdgePixels,
                        cancellationToken);
                }

                progress?.Report(new ScanProgress(
                    ScanPhase.ComputingHashes,
                    current,
                    total,
                    CurrentFile: photo.FileName));
                photo.Sha256Hash = await _hash.ComputeSha256Async(
                    photo.FilePath,
                    cancellationToken);
                photo.PerceptualHash = await _perceptualHash.ComputePHashAsync(
                    photo.FilePath,
                    cancellationToken);

                photos.Add(photo);
            }
        }
        finally
        {
            await scannerProgressCts.CancelAsync();
            await IgnoreExpectedCancellationAsync(scannerProgressTask);
        }

        cancellationToken.ThrowIfCancellationRequested();
        progress?.Report(new ScanProgress(ScanPhase.Scoring, 0, photos.Count));
        await _scoring.ScoreRangeAsync(
            photos,
            options.ScoringWeights,
            new Progress<int>(count => progress?.Report(
                new ScanProgress(ScanPhase.Scoring, count, photos.Count))),
            cancellationToken);

        cancellationToken.ThrowIfCancellationRequested();
        progress?.Report(new ScanProgress(
            ScanPhase.Grouping,
            0,
            photos.Count,
            Message: "Grouping photos..."));
        var groups = await _grouping.BuildGroupsAsync(
            photos,
            ct: cancellationToken);

        var completedAt = DateTimeOffset.UtcNow;
        progress?.Report(ScanProgress.Completed(photos.Count, startedAt.UtcDateTime));
        _logger.LogInformation(
            "Analyzed {PhotoCount} photos into {GroupCount} groups from {Directory}",
            photos.Count,
            groups.Count,
            sourceDirectory);

        return new PhotoAnalysisRun
        {
            Photos = photos,
            Groups = groups,
            Report = PhotoAnalysisReportFactory.Create(
                sourceDirectory,
                startedAt,
                completedAt,
                photos,
                groups),
        };
    }

    private async Task ForwardScannerProgressAsync(
        IProgress<ScanProgress>? progress,
        CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var value in _scannerProgress.ReadAllAsync(cancellationToken))
                progress?.Report(value);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private static async Task IgnoreExpectedCancellationAsync(Task task)
    {
        try
        {
            await task;
        }
        catch (OperationCanceledException)
        {
        }
    }
}
