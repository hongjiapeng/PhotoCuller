using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using PhotoCuller.Core.Interfaces.Scanning;
using PhotoCuller.Core.Interfaces.Storage;
using PhotoCuller.Core.Models;
using PhotoCuller.Core.Enums;
using PhotoCuller.Infrastructure.Scanning;

namespace PhotoCuller.Tests.InfrastructureTests;

public sealed class PhotoScannerTests : IDisposable
{
    private readonly string _sourceDirectory;

    public PhotoScannerTests()
    {
        _sourceDirectory = Path.Combine(
            Path.GetTempPath(), $"PhotoCullerScannerTests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_sourceDirectory);
    }

    public void Dispose()
    {
        if (Directory.Exists(_sourceDirectory))
            Directory.Delete(_sourceDirectory, recursive: true);
    }

    [Fact]
    public async Task ScanAsync_SupportedAndUnsupportedFiles_YieldsOnlySupportedFile()
    {
        var supportedPath = Path.Combine(_sourceDirectory, "photo.JPG");
        var unsupportedPath = Path.Combine(_sourceDirectory, "notes.txt");
        await File.WriteAllTextAsync(supportedPath, "not decoded by this boundary test");
        await File.WriteAllTextAsync(unsupportedPath, "not a photo");

        var metadata = Substitute.For<IPhotoMetadataReader>();
        metadata.ReadAsync(Arg.Any<PhotoItem>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        var repository = Substitute.For<IPhotoRepository>();
        repository.GetAllAsync(Arg.Any<CancellationToken>()).Returns([]);
        var progress = Substitute.For<IScanProgressService>();
        var settings = Options.Create(new AppSettings
        {
            IsRecursiveScanEnabled = false,
            SupportedExtensions = [".jpg", ".png"],
        });
        var sut = new PhotoScanner(
            metadata,
            repository,
            progress,
            settings,
            NullLogger<PhotoScanner>.Instance);
        var scanned = new List<PhotoItem>();

        await foreach (var photo in sut.ScanAsync(_sourceDirectory))
            scanned.Add(photo);

        scanned.Should().ContainSingle();
        scanned[0].FilePath.Should().Be(supportedPath);
        scanned[0].FileExtension.Should().Be(".jpg");
        await metadata.Received(1).ReadAsync(
            Arg.Is<PhotoItem>(photo => photo.FilePath == supportedPath),
            Arg.Any<CancellationToken>());
        await metadata.DidNotReceive().ReadAsync(
            Arg.Is<PhotoItem>(photo => photo.FilePath == unsupportedPath),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ScanAsync_UnchangedAndNewFiles_ReturnsFullDetachedInventoryWithoutDeletedPhoto()
    {
        var cached = await StoredPhotoAsync("cached.jpg");
        cached.RecommendationStatus = RecommendationStatus.ManualKeep;
        cached.Tags = ["family"];
        cached.GroupId = Guid.NewGuid();
        var deleted = new PhotoItem { FilePath = Path.Combine(_sourceDirectory, "deleted.jpg") };
        var freshPath = Path.Combine(_sourceDirectory, "new.jpg");
        await File.WriteAllTextAsync(freshPath, "new file");
        var metadata = Substitute.For<IPhotoMetadataReader>();
        var sut = Scanner(metadata, [cached, deleted]);

        var result = await CollectAsync(sut);

        result.Select(p => p.FileName).Should().BeEquivalentTo("cached.jpg", "new.jpg");
        var reused = result.Single(p => p.Id == cached.Id);
        reused.Sha256Hash.Should().Be("cached-sha");
        reused.ScoringSignature.Should().Be("cached-score");
        reused.RecommendationStatus.Should().Be(RecommendationStatus.ManualKeep);
        reused.GroupId.Should().BeNull();
        reused.Tags.Add("new tag");
        cached.Tags.Should().Equal("family");
        cached.GroupId.Should().NotBeNull();
        await metadata.DidNotReceive().ReadAsync(
            Arg.Is<PhotoItem>(p => p.Id == cached.Id), Arg.Any<CancellationToken>());
        await metadata.Received(1).ReadAsync(
            Arg.Is<PhotoItem>(p => p.FilePath == freshPath), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("older-timestamp")]
    [InlineData("size")]
    [InlineData("algorithm-version")]
    [InlineData("previous-error")]
    [InlineData("missing-dimensions")]
    public async Task ScanAsync_InvalidatedFile_ReReadsMetadataAndPreservesIdentityAndDecision(string change)
    {
        var stored = await StoredPhotoAsync("changed.jpg");
        stored.RecommendationStatus = RecommendationStatus.ManualDelete;
        switch (change)
        {
            case "older-timestamp": stored.ModifiedTime = stored.ModifiedTime!.Value.AddDays(1); break;
            case "size": stored.FileSize++; break;
            case "algorithm-version": stored.AnalysisVersion--; break;
            case "previous-error": stored.ErrorMessage = "previous failure"; break;
            case "missing-dimensions": stored.Width = 0; break;
        }
        var metadata = Substitute.For<IPhotoMetadataReader>();

        var result = await CollectAsync(Scanner(metadata, [stored]));

        var photo = result.Should().ContainSingle().Subject;
        photo.Id.Should().Be(stored.Id);
        photo.RecommendationStatus.Should().Be(RecommendationStatus.ManualDelete);
        photo.Sha256Hash.Should().BeNull();
        photo.ScoringSignature.Should().BeNull();
        await metadata.Received(1).ReadAsync(photo, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ScanAsync_EarlyConsumerDisposal_ReleasesProducerAndAllowsNextScan()
    {
        for (var i = 0; i < 5; i++)
            await File.WriteAllTextAsync(Path.Combine(_sourceDirectory, $"{i}.jpg"), "photo");
        var sut = Scanner(Substitute.For<IPhotoMetadataReader>(), []);
        await using (var iterator = sut.ScanAsync(_sourceDirectory).GetAsyncEnumerator())
            (await iterator.MoveNextAsync()).Should().BeTrue();

        sut.IsScanning.Should().BeFalse();
        (await CollectAsync(sut)).Should().HaveCount(5);
    }

    [Fact]
    public async Task ScanAsync_PendingDeleteSubfolder_ExcludesItsContents()
    {
        await File.WriteAllTextAsync(Path.Combine(_sourceDirectory, "active.jpg"), "photo");
        var trash = Path.Combine(_sourceDirectory, "PhotoCuller_ToDelete");
        Directory.CreateDirectory(trash);
        await File.WriteAllTextAsync(Path.Combine(trash, "removed.jpg"), "photo");

        var result = await CollectAsync(Scanner(Substitute.For<IPhotoMetadataReader>(), []));

        result.Should().ContainSingle().Which.FileName.Should().Be("active.jpg");
    }

    [Fact]
    public async Task ScanAsync_MetadataFailure_PropagatesFailureAndResetsScanningState()
    {
        await File.WriteAllTextAsync(Path.Combine(_sourceDirectory, "failure.jpg"), "photo");
        var metadata = Substitute.For<IPhotoMetadataReader>();
        metadata.ReadAsync(Arg.Any<PhotoItem>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new IOException("simulated read failure")));
        var sut = Scanner(metadata, []);

        var act = () => CollectAsync(sut);

        await act.Should().ThrowAsync<IOException>().WithMessage("simulated read failure");
        sut.IsScanning.Should().BeFalse();
    }

    private PhotoScanner Scanner(IPhotoMetadataReader metadata, IReadOnlyList<PhotoItem> stored)
    {
        var repository = Substitute.For<IPhotoRepository>();
        repository.GetAllAsync(Arg.Any<CancellationToken>()).Returns(stored);
        return new PhotoScanner(metadata, repository, Substitute.For<IScanProgressService>(),
            Options.Create(new AppSettings()), NullLogger<PhotoScanner>.Instance);
    }

    private async Task<PhotoItem> StoredPhotoAsync(string name)
    {
        var path = Path.Combine(_sourceDirectory, name);
        await File.WriteAllTextAsync(path, "stored photo");
        var file = new FileInfo(path);
        return new PhotoItem
        {
            FilePath = path, FileName = name, FileSize = file.Length,
            ModifiedTime = file.LastWriteTimeUtc, ScanTime = DateTime.UtcNow,
            AnalysisVersion = PhotoItem.CurrentAnalysisVersion,
            Width = 2000, Height = 1000,
            Sha256Hash = "cached-sha", ScoringSignature = "cached-score",
        };
    }

    private async Task<List<PhotoItem>> CollectAsync(PhotoScanner scanner)
    {
        var photos = new List<PhotoItem>();
        await foreach (var photo in scanner.ScanAsync(_sourceDirectory)) photos.Add(photo);
        return photos;
    }
}
