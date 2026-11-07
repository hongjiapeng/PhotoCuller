using GroupTake.Poc.Imaging.Services;
using OpenCvSharp;
using SixLabors.ImageSharp.Metadata.Profiles.Exif;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace GroupTake.Poc.Tests;

public sealed class PhotoCodecTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "GroupTake.Tests", Guid.NewGuid().ToString("N"));
    public PhotoCodecTests() => Directory.CreateDirectory(_directory);
    [Theory]
    [InlineData((ushort)1, 40, 20)] [InlineData((ushort)6, 20, 40)] [InlineData((ushort)8, 20, 40)]
    public void Load_ExifOrientationAndBgr_AreCorrectWithoutChangingOriginal(ushort orientation, int width, int height)
    {
        var path = Path.Combine(_directory, "input.jpg");
        using (var image = new SixLabors.ImageSharp.Image<Rgb24>(40, 20, new Rgb24(220, 50, 10)))
        {
            image.Metadata.ExifProfile = new ExifProfile(); image.Metadata.ExifProfile.SetValue(ExifTag.Orientation, orientation);
            SixLabors.ImageSharp.ImageExtensions.SaveAsJpeg(image, path);
        }
        var original = File.ReadAllBytes(path);
        using var loaded = PhotoCodec.Load(path);
        Assert.Equal(width, loaded.Width); Assert.Equal(height, loaded.Height);
        var color = loaded.At<Vec3b>(0, 0); Assert.InRange(color.Item2, 210, 230); Assert.InRange(color.Item0, 0, 20);
        Assert.Equal(original, File.ReadAllBytes(path));
    }
    [Theory]
    [InlineData(".png")] [InlineData(".jpg")]
    public void Export_RoundTripsNativeDimensionsAndColor_RejectsOverwriteAndOriginal(string extension)
    {
        using var image = new Mat(123, 321, MatType.CV_8UC3, new Scalar(20, 80, 220));
        var path = Path.Combine(_directory, "output" + extension);
        PhotoCodec.Export(image, path, [], 96);
        using var decoded = PhotoCodec.Load(path);
        Assert.Equal(image.Size(), decoded.Size());
        Assert.InRange(decoded.At<Vec3b>(60, 100).Item2, 215, 225);
        if (extension == ".png") Assert.Equal(0, Cv2.Norm(image, decoded, NormTypes.INF));
        var original = File.ReadAllBytes(path);
        Assert.Throws<IOException>(() => PhotoCodec.Export(image, path, []));
        var sourcePath = Path.Combine(_directory, "original.png");
        Assert.Throws<IOException>(() => PhotoCodec.Export(image, sourcePath, [sourcePath]));
        Assert.False(File.Exists(sourcePath)); Assert.Equal(original, File.ReadAllBytes(path));
        Assert.Empty(Directory.GetFiles(_directory, "*.tmp"));
    }
    [Fact]
    public void Export_InvalidParametersLeaveNoFile()
    {
        using var image = new Mat(5, 5, MatType.CV_8UC3, Scalar.All(1));
        var path = Path.Combine(_directory, "bad.bmp");
        Assert.Throws<ArgumentException>(() => PhotoCodec.Export(image, path, [])); Assert.False(File.Exists(path));
        path = Path.Combine(_directory, "bad.jpg");
        Assert.Throws<ArgumentOutOfRangeException>(() => PhotoCodec.Export(image, path, [], 101)); Assert.False(File.Exists(path));
    }
    public void Dispose() => Directory.Delete(_directory, recursive: true);
}
