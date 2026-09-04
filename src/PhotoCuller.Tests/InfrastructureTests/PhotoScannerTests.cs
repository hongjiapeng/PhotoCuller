using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using PhotoCuller.Core.Interfaces.Scanning;
using PhotoCuller.Core.Interfaces.Storage;
using PhotoCuller.Core.Models;
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
        IReadOnlyList<(string Path, DateTime ModifiedTime)>? filteredCandidates = null;
        repository.GetPathsNeedingRescanAsync(
                Arg.Any<IEnumerable<(string Path, DateTime ModifiedTime)>>(),
                Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                filteredCandidates = call
                    .Arg<IEnumerable<(string Path, DateTime ModifiedTime)>>()
                    .ToArray();
                return Task.FromResult<IReadOnlyList<string>>(
                    filteredCandidates.Select(candidate => candidate.Path).ToArray());
            });
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

        filteredCandidates.Should().ContainSingle();
        filteredCandidates![0].Path.Should().Be(supportedPath);
        filteredCandidates.Should().NotContain(candidate => candidate.Path == unsupportedPath);
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
}
