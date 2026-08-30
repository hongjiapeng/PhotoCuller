using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using OpenCvSharp;
using PhotoCuller.Infrastructure.Hashing;

namespace PhotoCuller.Tests.CoreTests;

public sealed class OrbLocalFeatureMatcherTests : IDisposable
{
    private readonly string _tempDirectory = Path.Combine(
        Path.GetTempPath(),
        "PhotoCuller.Tests",
        Guid.NewGuid().ToString("N"));

    public OrbLocalFeatureMatcherTests()
        => Directory.CreateDirectory(_tempDirectory);

    [Fact]
    public async Task IsSameScene_SmallViewpointChange_ReturnsTrue()
    {
        var originalPath = Path.Combine(_tempDirectory, "original.png");
        var transformedPath = Path.Combine(_tempDirectory, "transformed.png");
        using var original = CreateTexturedScene(seed: 17);
        using var transformed = new Mat();
        using var transform = Cv2.GetRotationMatrix2D(
            new Point2f(original.Width / 2f, original.Height / 2f),
            angle: 2.5,
            scale: 0.98);
        transform.Set(0, 2, transform.At<double>(0, 2) + 12);
        transform.Set(1, 2, transform.At<double>(1, 2) - 8);
        Cv2.WarpAffine(
            original,
            transformed,
            transform,
            original.Size(),
            borderMode: BorderTypes.Reflect101);
        Cv2.ImWrite(originalPath, original);
        Cv2.ImWrite(transformedPath, transformed);
        using var sut = CreateMatcher();

        var forward = await sut.IsSameSceneAsync(originalPath, transformedPath);
        var reverse = await sut.IsSameSceneAsync(transformedPath, originalPath);

        forward.Should().BeTrue();
        reverse.Should().BeTrue("scene matching must not depend on database order");
    }

    [Fact]
    public async Task IsSameScene_UnrelatedTexturedImages_ReturnsFalse()
    {
        var firstPath = Path.Combine(_tempDirectory, "first.png");
        var secondPath = Path.Combine(_tempDirectory, "second.png");
        using var first = CreateTexturedScene(seed: 23);
        using var second = CreateTexturedScene(seed: 91);
        Cv2.ImWrite(firstPath, first);
        Cv2.ImWrite(secondPath, second);
        using var sut = CreateMatcher();

        var result = await sut.IsSameSceneAsync(firstPath, secondPath);

        result.Should().BeFalse();
    }

    [Fact]
    public async Task IsSameScene_UnreadableImage_ReturnsFalseWithoutThrowing()
    {
        var validPath = Path.Combine(_tempDirectory, "valid.png");
        var missingPath = Path.Combine(_tempDirectory, "missing.png");
        using var valid = CreateTexturedScene(seed: 42);
        Cv2.ImWrite(validPath, valid);
        using var sut = CreateMatcher();

        var result = await sut.IsSameSceneAsync(validPath, missingPath);

        result.Should().BeFalse();
    }

    [Fact]
    public async Task IsSameScene_CancelledBeforeExtraction_ThrowsCancellation()
    {
        using var sut = CreateMatcher();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var action = () => sut.IsSameSceneAsync(
            "first.png", "second.png", cancellation.Token);

        await action.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task IsSameScene_FileChangedBetweenScans_RefreshesCachedFeatures()
    {
        var firstPath = Path.Combine(_tempDirectory, "stable.png");
        var changingPath = Path.Combine(_tempDirectory, "changing.png");
        using var first = CreateTexturedScene(seed: 17);
        using var matching = first.Clone();
        Cv2.ImWrite(firstPath, first);
        Cv2.ImWrite(changingPath, matching);
        using var sut = CreateMatcher();
        var beforeChange = await sut.IsSameSceneAsync(firstPath, changingPath);

        using var unrelated = CreateTexturedScene(seed: 91);
        Cv2.ImWrite(changingPath, unrelated);
        var afterChange = await sut.IsSameSceneAsync(firstPath, changingPath);

        beforeChange.Should().BeTrue();
        afterChange.Should().BeFalse(
            "a rescan must not reuse descriptors from the old file contents");
    }

    private static OrbLocalFeatureMatcher CreateMatcher()
        => new(NullLogger<OrbLocalFeatureMatcher>.Instance);

    private static Mat CreateTexturedScene(int seed)
    {
        var random = new Random(seed);
        var image = new Mat(
            new Size(800, 600),
            MatType.CV_8UC3,
            new Scalar(25, 25, 25));

        for (var i = 0; i < 90; i++)
        {
            var center = new Point(
                random.Next(25, image.Width - 25),
                random.Next(25, image.Height - 25));
            var color = new Scalar(
                random.Next(40, 255),
                random.Next(40, 255),
                random.Next(40, 255));
            Cv2.Circle(image, center, random.Next(4, 18), color, thickness: 2);
            Cv2.Line(
                image,
                center,
                new Point(
                    Math.Clamp(center.X + random.Next(-35, 36), 0, image.Width - 1),
                    Math.Clamp(center.Y + random.Next(-35, 36), 0, image.Height - 1)),
                color,
                thickness: 2);
        }

        var label = new string(Enumerable.Range(0, 9)
            .Select(_ => (char)random.Next('A', 'Z' + 1))
            .ToArray());
        Cv2.PutText(
            image,
            label,
            new Point(210, 310),
            HersheyFonts.HersheyComplex,
            2.0,
            Scalar.White,
            thickness: 4);
        return image;
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
            Directory.Delete(_tempDirectory, recursive: true);
    }
}
