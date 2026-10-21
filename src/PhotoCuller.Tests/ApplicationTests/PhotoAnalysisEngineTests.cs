using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using PhotoCuller.Application.Analysis;
using PhotoCuller.Application.Contracts;
using PhotoCuller.Core.Enums;
using PhotoCuller.Core.Interfaces.Hashing;
using PhotoCuller.Core.Interfaces.Scanning;
using PhotoCuller.Core.Interfaces.Scoring;
using PhotoCuller.Core.Models;

namespace PhotoCuller.Tests.ApplicationTests;

public sealed class PhotoAnalysisEngineTests : IDisposable
{
    private readonly string _sourceDirectory;
    private readonly IPhotoScanner _scanner = Substitute.For<IPhotoScanner>();
    private readonly IThumbnailService _thumbnails = Substitute.For<IThumbnailService>();
    private readonly IHashService _hash = Substitute.For<IHashService>();
    private readonly IPerceptualHashService _perceptualHash =
        Substitute.For<IPerceptualHashService>();
    private readonly IPhotoScoringService _scoring =
        Substitute.For<IPhotoScoringService>();
    private readonly IDuplicateGroupingService _grouping =
        Substitute.For<IDuplicateGroupingService>();
    private readonly IScanProgressService _scannerProgress =
        Substitute.For<IScanProgressService>();

    public PhotoAnalysisEngineTests()
    {
        _sourceDirectory = Path.Combine(
            Path.GetTempPath(), $"PhotoCullerEngineTests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_sourceDirectory);

        _scannerProgress.Current.Returns(ScanProgress.Idle);
        _scannerProgress.ReadAllAsync(Arg.Any<CancellationToken>())
            .Returns(EmptyProgress());
    }

    public void Dispose()
    {
        if (Directory.Exists(_sourceDirectory))
            Directory.Delete(_sourceDirectory, recursive: true);
    }

    [Fact]
    public async Task AnalyzeAsync_TwoPhotos_RunsPipelineInRequiredOrderAndReturnsProcessedObjects()
    {
        var events = new List<string>();
        var first = Photo("first.jpg");
        var second = Photo("second.jpg");
        var photos = new[] { first, second };

        _scanner.ScanAsync(_sourceDirectory, Arg.Any<CancellationToken>())
            .Returns(ScanPhotos(photos, photo => events.Add($"scan:{photo.FileName}")));
        _thumbnails.GetOrCreateAsync(
                Arg.Any<PhotoItem>(), 256, Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var photo = call.Arg<PhotoItem>();
                events.Add($"thumbnail:{photo.FileName}");
                return Task.FromResult<string?>($"thumbs/{photo.FileName}");
            });
        _hash.ComputeSha256Async(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var fileName = Path.GetFileName(call.Arg<string>());
                events.Add($"sha256:{fileName}");
                return Task.FromResult<string?>($"sha-{fileName}");
            });
        _perceptualHash.ComputePHashAsync(
                Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var fileName = Path.GetFileName(call.Arg<string>());
                events.Add($"phash:{fileName}");
                return Task.FromResult<ulong?>(fileName == "first.jpg" ? 11UL : 22UL);
            });
        _scoring.ScoreRangeAsync(
                Arg.Any<IReadOnlyList<PhotoItem>>(),
                Arg.Any<PhotoCuller.Core.Scoring.ScoringWeights>(),
                Arg.Any<IProgress<int>>(),
                Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                events.Add("score");
                foreach (var photo in call.Arg<IReadOnlyList<PhotoItem>>())
                    photo.OverallScore = 80f;
                return Task.CompletedTask;
            });

        var group = new PhotoGroup { Photos = photos, GroupType = GroupType.Similar };
        _grouping.BuildGroupsAsync(
                Arg.Any<IReadOnlyList<PhotoItem>>(),
                Arg.Any<IProgress<string>?>(),
                Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                events.Add("group");
                return Task.FromResult<IReadOnlyList<PhotoGroup>>([group]);
            });

        var sut = CreateSut();

        var run = await sut.AnalyzeAsync(_sourceDirectory);

        events.Should().Equal(
            "scan:first.jpg",
            "thumbnail:first.jpg",
            "sha256:first.jpg",
            "phash:first.jpg",
            "scan:second.jpg",
            "thumbnail:second.jpg",
            "sha256:second.jpg",
            "phash:second.jpg",
            "score",
            "group");
        run.Photos.Should().Equal(first, second);
        run.Groups.Should().ContainSingle().Which.Should().BeSameAs(group);
        first.ThumbnailPath.Should().Be("thumbs/first.jpg");
        second.ThumbnailPath.Should().Be("thumbs/second.jpg");
        first.Sha256Hash.Should().Be("sha-first.jpg");
        second.PerceptualHash.Should().Be(22UL);
        run.Report.Photos.Select(photo => photo.Id).Should().Equal(first.Id, second.Id);
    }

    [Fact]
    public async Task AnalyzeAsync_EmptyScan_ScoresAndGroupsEmptyInputAndReturnsEmptyRun()
    {
        _scanner.ScanAsync(_sourceDirectory, Arg.Any<CancellationToken>())
            .Returns(ScanPhotos([]));

        IReadOnlyList<PhotoItem>? scoredPhotos = null;
        _scoring.ScoreRangeAsync(
                Arg.Do<IReadOnlyList<PhotoItem>>(value => scoredPhotos = value),
                Arg.Any<PhotoCuller.Core.Scoring.ScoringWeights>(),
                Arg.Any<IProgress<int>>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        IReadOnlyList<PhotoItem>? groupedPhotos = null;
        _grouping.BuildGroupsAsync(
                Arg.Do<IReadOnlyList<PhotoItem>>(value => groupedPhotos = value),
                Arg.Any<IProgress<string>?>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<PhotoGroup>>([]));

        var run = await CreateSut().AnalyzeAsync(_sourceDirectory);

        scoredPhotos.Should().NotBeNull().And.BeEmpty();
        groupedPhotos.Should().NotBeNull().And.BeEmpty();
        run.Photos.Should().BeEmpty();
        run.Groups.Should().BeEmpty();
        run.Report.Photos.Should().BeEmpty();
        run.Report.Groups.Should().BeEmpty();
        run.Report.Version.Should().Be(PhotoAnalysisReport.CurrentVersion);
    }

    [Fact]
    public async Task AnalyzeAsync_CorruptedPhoto_PreservesErrorInRunAndReport()
    {
        const string error = "Metadata read failed: invalid JPEG marker";
        var corrupted = Photo("corrupted.jpg");
        corrupted.ErrorMessage = error;
        _scanner.ScanAsync(_sourceDirectory, Arg.Any<CancellationToken>())
            .Returns(ScanPhotos([corrupted]));
        ConfigureNoOpPipeline();

        var run = await CreateSut().AnalyzeAsync(_sourceDirectory);

        run.Photos.Should().ContainSingle().Which.Should().BeSameAs(corrupted);
        corrupted.ErrorMessage.Should().Be(error);
        corrupted.Sha256Hash.Should().BeNull();
        corrupted.PerceptualHash.Should().BeNull();
        run.Report.Photos.Should().ContainSingle();
        run.Report.Photos[0].Error.Should().Be(error);
        run.Report.Photos[0].Quality.Overall.Should().BeNull();
    }

    [Fact]
    public async Task AnalyzeAsync_ActiveCancellationToken_PropagatesSameTokenToEveryStage()
    {
        var photo = Photo("token.jpg");
        using var cancellation = new CancellationTokenSource();
        var token = cancellation.Token;

        _scanner.ScanAsync(_sourceDirectory, token).Returns(ScanPhotos([photo]));
        _thumbnails.GetOrCreateAsync(photo, 256, token)
            .Returns(Task.FromResult<string?>(null));
        _hash.ComputeSha256Async(photo.FilePath, token)
            .Returns(Task.FromResult<string?>(null));
        _perceptualHash.ComputePHashAsync(photo.FilePath, token)
            .Returns(Task.FromResult<ulong?>(null));
        _scoring.ScoreRangeAsync(
                Arg.Any<IReadOnlyList<PhotoItem>>(),
                Arg.Any<PhotoCuller.Core.Scoring.ScoringWeights>(),
                Arg.Any<IProgress<int>>(),
                token)
            .Returns(Task.CompletedTask);
        _grouping.BuildGroupsAsync(
                Arg.Any<IReadOnlyList<PhotoItem>>(), null, token)
            .Returns(Task.FromResult<IReadOnlyList<PhotoGroup>>([]));

        await CreateSut().AnalyzeAsync(
            _sourceDirectory, cancellationToken: token);

        _scanner.Received(1).ScanAsync(_sourceDirectory, token);
        await _thumbnails.Received(1).GetOrCreateAsync(photo, 256, token);
        await _hash.Received(1).ComputeSha256Async(photo.FilePath, token);
        await _perceptualHash.Received(1).ComputePHashAsync(photo.FilePath, token);
        await _scoring.Received(1).ScoreRangeAsync(
            Arg.Is<IReadOnlyList<PhotoItem>>(items => items.Count == 1 && items[0] == photo),
            Arg.Any<PhotoCuller.Core.Scoring.ScoringWeights>(),
            Arg.Any<IProgress<int>>(),
            token);
        await _grouping.Received(1).BuildGroupsAsync(
            Arg.Is<IReadOnlyList<PhotoItem>>(items => items.Count == 1 && items[0] == photo),
            null,
            token);
    }

    [Fact]
    public async Task AnalyzeAsync_PreCancelledToken_StopsBeforeStartingPipeline()
    {
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        var act = () => CreateSut().AnalyzeAsync(
            _sourceDirectory, cancellationToken: cancellation.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        _scanner.DidNotReceive().ScanAsync(
            Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _scoring.DidNotReceive().ScoreRangeAsync(
            Arg.Any<IReadOnlyList<PhotoItem>>(),
            Arg.Any<PhotoCuller.Core.Scoring.ScoringWeights>(),
            Arg.Any<IProgress<int>>(),
            Arg.Any<CancellationToken>());
        await _grouping.DidNotReceive().BuildGroupsAsync(
            Arg.Any<IReadOnlyList<PhotoItem>>(),
            Arg.Any<IProgress<string>?>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AnalyzeAsync_CompletedRun_MapsStableReportContract()
    {
        var photoId = Guid.NewGuid();
        var groupId = Guid.NewGuid();
        var takenAt = new DateTime(2026, 8, 17, 9, 30, 45, DateTimeKind.Unspecified);
        var photo = new PhotoItem
        {
            Id = photoId,
            FilePath = Path.Combine(_sourceDirectory, "mapped.heic"),
            FileName = "mapped.heic",
            FileExtension = ".heic",
            FileSize = 12_345,
            Width = 4032,
            Height = 3024,
            ExifTakenTime = takenAt,
            OverallScore = 91f,
            SharpnessScore = 92f,
            ExposureScore = 93f,
            ContrastScore = 94f,
            NoiseScore = 95f,
            ResolutionScore = 96f,
            CompositionScore = 97f,
            ColorScore = 98f,
            AestheticScore = 99f,
            RecommendationStatus = RecommendationStatus.Keep,
            GroupId = groupId,
            ErrorMessage = "non-fatal warning",
        };
        var group = new PhotoGroup
        {
            Id = groupId,
            GroupType = GroupType.Burst,
            SimilarityScore = 0.875f,
            RecommendedPhotoId = photoId,
            Photos = [photo],
        };

        _scanner.ScanAsync(_sourceDirectory, Arg.Any<CancellationToken>())
            .Returns(ScanPhotos([photo]));
        _thumbnails.GetOrCreateAsync(
                photo, 256, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<string?>(null));
        _hash.ComputeSha256Async(
                photo.FilePath, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<string?>("stable-sha"));
        _perceptualHash.ComputePHashAsync(
                photo.FilePath, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<ulong?>(42UL));
        _scoring.ScoreRangeAsync(
                Arg.Any<IReadOnlyList<PhotoItem>>(),
                Arg.Any<PhotoCuller.Core.Scoring.ScoringWeights>(),
                Arg.Any<IProgress<int>>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        _grouping.BuildGroupsAsync(
                Arg.Any<IReadOnlyList<PhotoItem>>(),
                Arg.Any<IProgress<string>?>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<PhotoGroup>>([group]));

        var before = DateTimeOffset.UtcNow;
        var run = await CreateSut().AnalyzeAsync(_sourceDirectory);
        var after = DateTimeOffset.UtcNow;

        var report = run.Report;
        report.Version.Should().Be("1.0");
        report.Version.Should().Be(PhotoAnalysisReport.CurrentVersion);
        report.SourceDirectory.Should().Be(Path.GetFullPath(_sourceDirectory));
        report.StartedAtUtc.Should().BeOnOrAfter(before);
        report.CompletedAtUtc.Should().BeOnOrBefore(after);
        report.CompletedAtUtc.Should().BeOnOrAfter(report.StartedAtUtc);
        report.StartedAtUtc.Offset.Should().Be(TimeSpan.Zero);
        report.CompletedAtUtc.Offset.Should().Be(TimeSpan.Zero);
        report.Photos.Should().ContainSingle().Which.Should().BeEquivalentTo(
            new AnalyzedPhoto(
                photoId,
                photo.FilePath,
                "mapped.heic",
                ".heic",
                12_345,
                4032,
                3024,
                new DateTimeOffset(takenAt, TimeSpan.Zero),
                new PhotoQuality(91f, 92f, 93f, 94f, 95f, 96f, 97f, 98f, 99f),
                RecommendationStatus.Keep,
                groupId,
                "non-fatal warning"));
        report.Groups.Should().ContainSingle().Which.Should().BeEquivalentTo(
            new AnalyzedPhotoGroup(
                groupId,
                GroupType.Burst,
                0.875f,
                photoId,
                [photoId]));
    }

    [Fact]
    public async Task AnalyzeAsync_CachedHashes_ReusesHashesAndStillGroupsFullInventory()
    {
        var cached = Photo("cached.jpg");
        cached.Sha256Hash = "stored-sha";
        cached.PerceptualHash = 73;
        _scanner.ScanAsync(_sourceDirectory, Arg.Any<CancellationToken>())
            .Returns(ScanPhotos([cached]));
        ConfigureNoOpPipeline();

        var result = await CreateSut().AnalyzeAsync(_sourceDirectory);

        result.Photos.Should().ContainSingle().Which.Sha256Hash.Should().Be("stored-sha");
        cached.PerceptualHash.Should().Be(73);
        cached.AnalysisVersion.Should().Be(PhotoItem.CurrentAnalysisVersion);
        await _hash.DidNotReceive().ComputeSha256Async(Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _perceptualHash.DidNotReceive().ComputePHashAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _grouping.Received(1).BuildGroupsAsync(
            Arg.Is<IReadOnlyList<PhotoItem>>(p => p.Count == 1 && p[0].Id == cached.Id),
            null, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AnalyzeAsync_SizePrefilter_HashesOnlyRepeatedSizesAndRetainsUniquePhotos()
    {
        var first = Photo("first.jpg"); first.FileSize = 1024;
        var second = Photo("second.jpg"); second.FileSize = 1024;
        var unique = Photo("unique.jpg"); unique.FileSize = 4096;
        _scanner.ScanAsync(_sourceDirectory, Arg.Any<CancellationToken>())
            .Returns(ScanPhotos([first, second, unique]));
        ConfigureNoOpPipeline();
        _hash.ComputeSha256Async(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns("same-content");

        var result = await CreateSut().AnalyzeAsync(_sourceDirectory,
            new PhotoAnalysisOptions { ComputeAllFileHashes = false });

        result.Photos.Should().HaveCount(3);
        first.Sha256Hash.Should().Be("same-content");
        second.Sha256Hash.Should().Be("same-content");
        unique.Sha256Hash.Should().BeNull();
        await _hash.DidNotReceive().ComputeSha256Async(unique.FilePath, Arg.Any<CancellationToken>());
        await _perceptualHash.Received(1).ComputePHashAsync(unique.FilePath, Arg.Any<CancellationToken>());
        await _grouping.Received(1).BuildGroupsAsync(
            Arg.Is<IReadOnlyList<PhotoItem>>(p => p.Count == 3), null, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AnalyzeAsync_ThumbnailDisabled_DoesNotRequestThumbnail()
    {
        _scanner.ScanAsync(_sourceDirectory, Arg.Any<CancellationToken>())
            .Returns(ScanPhotos([Photo("no-thumbnail.jpg")]));
        ConfigureNoOpPipeline();

        await CreateSut().AnalyzeAsync(_sourceDirectory, new PhotoAnalysisOptions { GenerateThumbnails = false });

        await _thumbnails.DidNotReceive().GetOrCreateAsync(
            Arg.Any<PhotoItem>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    private PhotoAnalysisEngine CreateSut() => new(
        _scanner,
        _thumbnails,
        _hash,
        _perceptualHash,
        _scoring,
        _grouping,
        _scannerProgress,
        NullLogger<PhotoAnalysisEngine>.Instance);

    private void ConfigureNoOpPipeline()
    {
        _thumbnails.GetOrCreateAsync(
                Arg.Any<PhotoItem>(), 256, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<string?>(null));
        _hash.ComputeSha256Async(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<string?>(null));
        _perceptualHash.ComputePHashAsync(
                Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<ulong?>(null));
        _scoring.ScoreRangeAsync(
                Arg.Any<IReadOnlyList<PhotoItem>>(),
                Arg.Any<PhotoCuller.Core.Scoring.ScoringWeights>(),
                Arg.Any<IProgress<int>>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        _grouping.BuildGroupsAsync(
                Arg.Any<IReadOnlyList<PhotoItem>>(),
                Arg.Any<IProgress<string>?>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<PhotoGroup>>([]));
    }

    private PhotoItem Photo(string fileName) => new()
    {
        FilePath = Path.Combine(_sourceDirectory, fileName),
        FileName = fileName,
        FileExtension = Path.GetExtension(fileName),
    };

    private static async IAsyncEnumerable<PhotoItem> ScanPhotos(
        IEnumerable<PhotoItem> photos,
        Action<PhotoItem>? beforeYield = null)
    {
        foreach (var photo in photos)
        {
            beforeYield?.Invoke(photo);
            yield return photo;
        }

        await Task.CompletedTask;
    }

    private static async IAsyncEnumerable<ScanProgress> EmptyProgress()
    {
        await Task.CompletedTask;
        yield break;
    }
}
