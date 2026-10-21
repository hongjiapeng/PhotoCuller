using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PhotoCuller.Core.Models;
using PhotoCuller.Infrastructure.ThumbnailCache;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace PhotoCuller.Tests.InfrastructureTests;

public sealed class ThumbnailServiceTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"PhotoCullerThumbs_{Guid.NewGuid():N}");
    private readonly string _cache;
    private readonly ThumbnailService _sut;

    public ThumbnailServiceTests()
    {
        _cache = Path.Combine(_directory, "cache");
        _sut = new ThumbnailService(Options.Create(new AppSettings { ThumbnailCacheDirectory = _cache }),
            NullLogger<ThumbnailService>.Instance);
    }

    public void Dispose()
    {
        _sut.Dispose();
        Directory.Delete(_directory, recursive: true);
    }

    [Fact]
    public async Task GetOrCreateAsync_DifferentSizes_ProducesIndependentCorrectlySizedImages()
    {
        var photo = await PhotoAsync();

        var small = await _sut.GetOrCreateAsync(photo, 128);
        var large = await _sut.GetOrCreateAsync(photo, 512);
        var repeated = await _sut.GetOrCreateAsync(photo, 128);

        small.Should().NotBe(large);
        repeated.Should().Be(small);
        using var first = await Image.LoadAsync(small!);
        using var second = await Image.LoadAsync(large!);
        first.Size.Should().Be(new Size(128, 64));
        second.Size.Should().Be(new Size(512, 256));
    }

    [Fact]
    public async Task GetOrCreateAsync_SizeChangesWithSameTimestamp_InvalidatesCachedPixels()
    {
        var photo = await PhotoAsync();
        var timestamp = File.GetLastWriteTimeUtc(photo.FilePath);
        var old = await _sut.GetOrCreateAsync(photo, 128);
        using (var replacement = new Image<Rgb24>(300, 600, new Rgb24(0, 200, 0)))
            await replacement.SaveAsPngAsync(photo.FilePath);
        File.SetLastWriteTimeUtc(photo.FilePath, timestamp);

        var current = await _sut.GetOrCreateAsync(photo, 128);

        current.Should().NotBe(old);
        using var image = await Image.LoadAsync(current!);
        image.Size.Should().Be(new Size(64, 128));
    }

    [Fact]
    public async Task GetOrCreateAsync_ConcurrentSameRequest_PublishesOneCompleteJpegWithoutTemporaryFiles()
    {
        var photo = await PhotoAsync();

        var paths = await Task.WhenAll(Enumerable.Range(0, 12).Select(_ => _sut.GetOrCreateAsync(photo, 256)));

        paths.Should().OnlyContain(p => p != null && p == paths[0]);
        Directory.GetFiles(_cache, "*", SearchOption.AllDirectories).Should().ContainSingle();
        using var image = await Image.LoadAsync(paths[0]!);
        image.Size.Should().Be(new Size(256, 128));
    }

    [Fact]
    public async Task GetOrCreateAsync_PreCancelled_DoesNotPublishThumbnail()
    {
        var photo = await PhotoAsync();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var act = () => _sut.GetOrCreateAsync(photo, 128, cancellation.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        Directory.GetFiles(_cache, "*", SearchOption.AllDirectories).Should().BeEmpty();
    }

    [Fact]
    public async Task GetOrCreateAsync_InvalidImage_DoesNotLeavePartialCacheFile()
    {
        var photo = await PhotoAsync();
        await File.WriteAllTextAsync(photo.FilePath, "invalid image");

        (await _sut.GetOrCreateAsync(photo)).Should().BeNull();

        Directory.GetFiles(_cache, "*", SearchOption.AllDirectories).Should().BeEmpty();
    }

    [Fact]
    public async Task DeleteAsync_RemovesAllCurrentSizes()
    {
        var photo = await PhotoAsync();
        await _sut.GetOrCreateAsync(photo, 128);
        await _sut.GetOrCreateAsync(photo, 512);

        await _sut.DeleteAsync(photo);

        Directory.GetFiles(_cache, "*", SearchOption.AllDirectories).Should().BeEmpty();
    }

    private async Task<PhotoItem> PhotoAsync()
    {
        var path = Path.Combine(_directory, "photo.png");
        using var image = new Image<Rgb24>(800, 400, new Rgb24(200, 0, 0));
        await image.SaveAsPngAsync(path);
        var file = new FileInfo(path);
        return new PhotoItem { FilePath = path, FileSize = file.Length, ModifiedTime = file.LastWriteTimeUtc };
    }
}
