using GroupTake.Poc.Imaging.ImageProcessing;
using GroupTake.Poc.Imaging.Models;
using GroupTake.Poc.Imaging.Services;
using OpenCvSharp;
using SixLabors.ImageSharp.Metadata.Profiles.Exif;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace GroupTake.Poc.Tests;

public sealed class ImagingTests
{
    private static FaceSelection Face => new(new Rect(40, 30, 160, 190),
        [new(85, 90), new(155, 90), new(120, 125), new(94, 160), new(146, 160)]);
    private static Mat Base() => new(260, 320, MatType.CV_8UC3, new Scalar(40, 80, 140));
    private static Mat Source()
    {
        var mat = new Mat(260, 320, MatType.CV_8UC3, new Scalar(100, 130, 180));
        Cv2.Circle(mat, new Point(85, 90), 6, new Scalar(210, 30, 60), -1);
        Cv2.Circle(mat, new Point(155, 90), 6, new Scalar(210, 30, 60), -1);
        Cv2.Ellipse(mat, new Point(120, 160), new Size(22, 8), 0, 0, 180, Scalar.All(20), 3);
        return mat;
    }

    [Fact]
    public void Viewport_RejectsLetterbox_AndRoundTripsZoomPan()
    {
        var fit = new ImageViewport(4000, 2000, 400, 400);
        Assert.False(fit.TryToImage(200, 50, out _, out _));
        Assert.True(fit.TryToImage(200, 200, out var x, out var y));
        Assert.Equal(2000, x, 6); Assert.Equal(1000, y, 6);
        var zoomed = fit with { Zoom = 3.2, PanX = -17, PanY = 39 };
        var view = zoomed.ToView(1123, 879);
        Assert.True(zoomed.TryToImage(view.X, view.Y, out x, out y));
        Assert.Equal(1123, x, 6); Assert.Equal(879, y, 6);
        Assert.False(fit.TryToImage(400, 200, out _, out _));
    }

    [Fact]
    public void Alignment_RecoversKnownScaleRotationTranslation()
    {
        const double angle = 0.06; const double scale = 1.2;
        var a = scale * Math.Cos(angle); var b = scale * Math.Sin(angle);
        var target = new FaceSelection(new Rect(40, 30, 240, 230), Face.Landmarks
            .Select(p => new Point2f((float)(a * p.X - b * p.Y + 15), (float)(b * p.X + a * p.Y - 8))).ToArray());
        var result = FaceAlignmentService.Estimate(target, Face, new Size(400, 350), new Size(320, 260));
        Assert.NotNull(result.RmsError); Assert.InRange(result.RmsError.Value, 0, 0.001);
        Assert.Equal(scale, result.Scale, 4); Assert.Equal(15, result.Matrix[2], 3); Assert.Equal(-8, result.Matrix[5], 3);
        Assert.Equal(angle * 180 / Math.PI, result.RotationDegrees, 3);
    }
    [Fact]
    public void Alignment_RejectsMissingReversedAndNonFiniteLandmarks()
    {
        var size = new Size(320, 260);
        Assert.Throws<ArgumentException>(() => FaceAlignmentService.Estimate(Face, Face with { Landmarks = Face.Landmarks[..4] }, size, size));
        var reversed = Face.Landmarks.ToArray(); (reversed[0], reversed[1]) = (reversed[1], reversed[0]);
        Assert.Throws<ArgumentException>(() => FaceAlignmentService.Validate(Face with { Landmarks = reversed }, size));
        var invalid = Face.Landmarks.ToArray(); invalid[2] = new Point2f(float.NaN, 125);
        Assert.Throws<ArgumentException>(() => FaceAlignmentService.Validate(Face with { Landmarks = invalid }, size));
    }
    [Fact]
    public void Alignment_RejectsLargeSingleLandmarkOutlier()
    {
        var wrong = Face.Landmarks.ToArray(); wrong[2] = new Point2f(181, 125);
        Assert.Throws<InvalidOperationException>(() => FaceAlignmentService.Estimate(Face,
            Face with { Landmarks = wrong }, new Size(320, 260), new Size(320, 260)));
    }
    [Fact]
    public void Mask_ContainsFeatures_ExcludesHairAndOutsideFace()
    {
        using var mask = FaceMaskGenerator.Create(new Size(320, 260), Face);
        foreach (var p in Face.Landmarks) Assert.Equal(255, mask.At<byte>((int)p.Y, (int)p.X));
        Assert.Equal(0, mask.At<byte>(30, 120)); Assert.Equal(0, mask.At<byte>(125, 39));
        Assert.Equal(0, mask.At<byte>(0, 0));
    }
    [Theory]
    [InlineData(BlendMethod.Alpha)] [InlineData(BlendMethod.NormalClone)] [InlineData(BlendMethod.MixedClone)]
    public void Compose_PreservesOutsideMaskAndInputs_ChangesRealSourceFeatures(BlendMethod method)
    {
        using var target = Base(); using var source = Source();
        using var beforeA = target.Clone(); using var beforeB = source.Clone();
        using var result = CompositionService.Compose(target, source, Face, Face, new BlendOptions(method, 8));
        Assert.Equal(target.Size(), result.Composite.Size());
        using var diff = new Mat(); Cv2.Absdiff(target, result.Composite, diff);
        using var inverse = new Mat(); Cv2.BitwiseNot(result.Mask, inverse);
        using var outside = new Mat(target.Size(), MatType.CV_8UC3, Scalar.All(0)); diff.CopyTo(outside, inverse);
        Assert.Equal(0, Cv2.Norm(outside, NormTypes.INF));
        Assert.True(Cv2.Norm(diff, NormTypes.INF) > 5, "Composition must actually change the selected face.");
        Assert.Equal(0, Cv2.Norm(target, beforeA, NormTypes.INF)); Assert.Equal(0, Cv2.Norm(source, beforeB, NormTypes.INF));
        if (method == BlendMethod.Alpha) Assert.Equal(source.At<Vec3b>(90, 85), result.Composite.At<Vec3b>(90, 85));
    }
    [Fact]
    public void Feather_IsInward_AndRadiusChangesBoundaryWeight()
    {
        using var a = Base(); using var b = Source(); using var mask = FaceMaskGenerator.Create(a.Size(), Face);
        using var hard = FaceBlendingService.Blend(a, b, mask, new BlendOptions(BlendMethod.Alpha, 0));
        using var soft = FaceBlendingService.Blend(a, b, mask, new BlendOptions(BlendMethod.Alpha, 30));
        Assert.True(Cv2.Norm(hard, soft, NormTypes.INF) > 10);
        Assert.Equal(a.At<Vec3b>(30, 120), soft.At<Vec3b>(30, 120));
    }
    [Fact]
    public void Compose_24MegapixelsAndDifferentSourceResolution_UsesNativeBaseSize()
    {
        using var a = new Mat(4000, 6000, MatType.CV_8UC3, new Scalar(20, 40, 80));
        using var b = new Mat(2000, 3000, MatType.CV_8UC3, new Scalar(80, 100, 120));
        var face = Face.Scale(8);
        using var result = CompositionService.Compose(a, b, face.Scale(2), face, new BlendOptions());
        Assert.Equal(new Size(6000, 4000), result.Composite.Size());
        Assert.Equal(b.At<Vec3b>(1000, 960), result.Composite.At<Vec3b>(2000, 1920));
        Assert.Equal(a.At<Vec3b>(3999, 5999), result.Composite.At<Vec3b>(3999, 5999));
    }
    [Fact]
    public void Compose_RejectsEmptyImagesInvalidOptionsAndUncoveredSource()
    {
        using var empty = new Mat(); using var a = Base(); using var b = Source();
        Assert.Throws<ArgumentException>(() => CompositionService.Compose(empty, b, Face, Face, new BlendOptions()));
        Assert.Throws<ArgumentException>(() => CompositionService.Compose(a, b, Face, Face, new BlendOptions((BlendMethod)99)));
        Assert.Throws<ArgumentException>(() => CompositionService.Compose(a, b, Face, Face, new BlendOptions(FeatherRadius: double.NaN)));
        var tight = Face with { Bounds = new Rect(80, 85, 80, 80) };
        Assert.Throws<InvalidOperationException>(() => CompositionService.Compose(a, b, Face, tight, new BlendOptions()));
    }
    [Fact]
    public void CancelledOperations_DoNotComposeOrCreateExport()
    {
        using var a = Base(); using var b = Source(); using var cts = new CancellationTokenSource(); cts.Cancel();
        Assert.Throws<OperationCanceledException>(() => CompositionService.Compose(a, b, Face, Face, new BlendOptions(), cts.Token));
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.png");
        Assert.Throws<OperationCanceledException>(() => PhotoCodec.Export(a, path, [], ct: cts.Token));
        Assert.False(File.Exists(path)); Assert.Throws<OperationCanceledException>(() => PhotoCodec.Load("missing.jpg", cts.Token));
    }
}
