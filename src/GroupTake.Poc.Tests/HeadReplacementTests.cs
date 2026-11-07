using System.Text.Json;
using GroupTake.Poc.Imaging.ImageProcessing;
using GroupTake.Poc.Imaging.Services;
using OpenCvSharp;
using Xunit;

namespace GroupTake.Poc.Tests;

public sealed class HeadReplacementTests
{
    [Fact]
    public void WholeHead_TargetProtectionKeepsClothesInsideEllipseUnchanged()
    {
        using var a = new Mat(240, 320, MatType.CV_8UC3, new Scalar(90, 100, 110));
        using var b = new Mat(240, 320, MatType.CV_8UC3, new Scalar(150, 160, 170));
        var garment = new Rect(66, 135, 48, 11); // Deliberately INSIDE the old ellipse, not a trivial outside-mask assertion.
        Cv2.Rectangle(a, garment, new Scalar(220, 40, 20), -1);
        Cv2.Rectangle(b, new Rect(86, 155, 48, 11), new Scalar(20, 40, 230), -1);
        using var bad = HeadReplacementService.Compose(a, b, new(40, 30, 100, 130), new(60, 50, 100, 130), new(0));
        using var targetGarment = new Mat(a, garment); using var badGarment = new Mat(bad.Composite, garment);
        Assert.True(Cv2.Norm(targetGarment, badGarment, NormTypes.INF) > 100, "The original ellipse really imports source clothing here.");
        using var corrected = HeadReplacementService.Compose(a, b, new(40, 30, 100, 130), new(60, 50, 100, 130),
            new(0, TargetProtection: [new(9, [[66, 140], [113, 140]])]));
        using var protectedGarment = new Mat(corrected.Composite, garment);
        Assert.Equal(0, Cv2.Norm(targetGarment, protectedGarment, NormTypes.INF));
        Assert.Equal(b.At<Vec3b>(100, 110), corrected.Composite.At<Vec3b>(80, 90));
    }

    [Fact]
    public void WholeHead_SourceProtectionPreventsClothesMovingIntoTarget()
    {
        using var a = new Mat(240, 320, MatType.CV_8UC3, Scalar.All(80));
        using var b = new Mat(240, 320, MatType.CV_8UC3, Scalar.All(140));
        Cv2.Circle(b, new Point(110, 155), 5, new Scalar(0, 0, 255), -1);
        using var unprotected = HeadReplacementService.Compose(a, b, new(40, 30, 100, 130), new(60, 50, 100, 130), new(0));
        Assert.Equal(new Vec3b(0, 0, 255), unprotected.Composite.At<Vec3b>(135, 90));
        using var corrected = HeadReplacementService.Compose(a, b, new(40, 30, 100, 130), new(60, 50, 100, 130),
            new(0, SourceProtection: [new(8, [[110, 155]])]));
        Assert.Equal(a.At<Vec3b>(135, 90), corrected.Composite.At<Vec3b>(135, 90));
        Assert.Equal(0, corrected.Mask.At<byte>(135, 90));
        Assert.Equal(b.At<Vec3b>(100, 110), corrected.Composite.At<Vec3b>(80, 90));
    }

    [Fact]
    public void WholeHead_RejectsInvalidProtectionCoordinatesAndRadius()
    {
        using var a = new Mat(240, 320, MatType.CV_8UC3, Scalar.All(80));
        Assert.Throws<ArgumentException>(() => HeadReplacementService.Compose(a, a, new(40, 30, 100, 130), new(40, 30, 100, 130),
            new(SourceProtection: [new(double.NaN, [[80, 80]])])));
        Assert.Throws<ArgumentException>(() => HeadReplacementService.Compose(a, a, new(40, 30, 100, 130), new(40, 30, 100, 130),
            new(TargetProtection: [new(5, [[-1, 80]])])));
    }

    [Fact]
    public void WholeHead_TransfersHairEyesAndMouthTogetherWithoutLandmarks_ProtectsOutside()
    {
        using var a = new Mat(240, 320, MatType.CV_8UC3, new Scalar(90, 100, 110));
        using var b = new Mat(240, 320, MatType.CV_8UC3, new Scalar(60, 70, 80));
        var target = new Rect(40, 30, 100, 130); var source = new Rect(60, 50, 100, 130);
        // Hair, two eyes, mouth: four distinct true source features inside the complete head patch.
        var points = new[] { new Point(110, 63), new Point(90, 103), new Point(130, 103), new Point(110, 150) };
        for (var i = 0; i < points.Length; i++) Cv2.Circle(b, points[i], 4, new Scalar(10 + i * 40, 220 - i * 30, 30 + i * 50), -1);
        using var beforeA = a.Clone(); using var beforeB = b.Clone();
        using var result = HeadReplacementService.Compose(a, b, target, source, new HeadReplacementOptions(0));
        Assert.Equal("ManualHeadBoxes", result.Alignment.Basis);
        Assert.Null(result.Alignment.RmsError); Assert.Null(result.Alignment.MaxError);
        Assert.Equal(-20, result.Alignment.Matrix[2], 8); Assert.Equal(-20, result.Alignment.Matrix[5], 8);
        foreach (var p in points) Assert.Equal(b.At<Vec3b>(p.Y, p.X), result.Composite.At<Vec3b>(p.Y - 20, p.X - 20));
        using var diff = new Mat(); using var inverse = new Mat(); Cv2.Absdiff(a, result.Composite, diff); Cv2.BitwiseNot(result.Mask, inverse);
        using var outside = new Mat(a.Size(), MatType.CV_8UC3, Scalar.All(0)); diff.CopyTo(outside, inverse);
        Assert.Equal(0, Cv2.Norm(outside, NormTypes.INF)); Assert.Equal(a.Size(), result.Composite.Size());
        Assert.Equal(0, Cv2.Norm(a, beforeA, NormTypes.INF)); Assert.Equal(0, Cv2.Norm(b, beforeB, NormTypes.INF));
    }
    [Fact]
    public void WholeHead_UsesUniformScaleAndExplicitAdjustmentsWithoutShear()
    {
        using var a = new Mat(500, 600, MatType.CV_8UC3, Scalar.All(50));
        using var b = new Mat(400, 400, MatType.CV_8UC3, Scalar.All(100));
        using var result = HeadReplacementService.Compose(a, b, new Rect(100, 100, 120, 180), new Rect(90, 80, 80, 120),
            new HeadReplacementOptions(8, 5, 1.1, 3, -4));
        var m = result.Alignment.Matrix;
        Assert.Equal(m[0], m[4], 10); Assert.Equal(-m[1], m[3], 10);
        Assert.Equal(1.65, result.Alignment.Scale, 8);
        Assert.Equal(163, m[0] * 130 + m[1] * 140 + m[2], 7);
        Assert.Equal(186, m[3] * 130 + m[4] * 140 + m[5], 7);
    }
    [Fact]
    public void WholeHead_RejectsInvalidBoxShapeAndAdjustment()
    {
        using var a = new Mat(240, 320, MatType.CV_8UC3, Scalar.All(90));
        var good = new Rect(40, 30, 100, 130);
        Assert.Throws<ArgumentException>(() => HeadReplacementService.Compose(a, a, new Rect(-1, 30, 100, 130), good, new()));
        Assert.Throws<ArgumentException>(() => HeadReplacementService.Compose(a, a, good, new Rect(30, 30, 200, 30), new()));
        Assert.Throws<ArgumentException>(() => HeadReplacementService.Compose(a, a, good, good, new(RotationDegrees: 26)));
        Assert.Throws<ArgumentException>(() => HeadReplacementService.Compose(a, a, good, good, new(ScaleAdjustment: double.NaN)));
        Assert.Throws<ArgumentException>(() => HeadReplacementService.Compose(a, a, good, good, new(FeatherRadius: -1)));
    }
    [Fact]
    public void WholeHead_24MPWithDifferentSourceSize_PreservesNativeResolution()
    {
        using var a = new Mat(4000, 6000, MatType.CV_8UC3, Scalar.All(30));
        using var b = new Mat(2000, 3000, MatType.CV_8UC3, Scalar.All(190));
        using var result = HeadReplacementService.Compose(a, b, new Rect(2500, 1500, 400, 600), new Rect(1250, 750, 200, 300), new(8));
        Assert.Equal(new Size(6000, 4000), result.Composite.Size()); Assert.Equal(2, result.Alignment.Scale, 8);
        Assert.Equal(new Vec3b(190, 190, 190), result.Composite.At<Vec3b>(1800, 2700));
        Assert.Equal(a.At<Vec3b>(0, 0), result.Composite.At<Vec3b>(0, 0));
    }
    [Fact]
    public void WholeHead_CancelledBeforeProcessing_DoesNotReturnResult()
    {
        using var a = new Mat(240, 320, MatType.CV_8UC3, Scalar.All(90));
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() => HeadReplacementService.Compose(a, a, new(40, 30, 100, 130), new(40, 30, 100, 130), new(), cancellation.Token));
    }
    [Fact]
    public void WholeHead_DebugExportRecordsManualBasisAndNullReprojectionError()
    {
        var folder = Path.Combine(Path.GetTempPath(), "GroupTake.Tests", Guid.NewGuid().ToString("N"));
        try
        {
            using var a = new Mat(240, 320, MatType.CV_8UC3, Scalar.All(90));
            DebugExporter.SaveHeadCase(folder, a, a, new(40, 30, 100, 130), new(40, 30, 100, 130), new());
            using var metrics = JsonDocument.Parse(File.ReadAllText(Path.Combine(folder, "metrics.json")));
            Assert.Equal("WholeHead", metrics.RootElement.GetProperty("mode").GetString());
            Assert.Equal(JsonValueKind.Null, metrics.RootElement.GetProperty("Alignment").GetProperty("RmsError").ValueKind);
            Assert.Equal(0, metrics.RootElement.GetProperty("outsideMaskChangedChannels").GetInt32());
            using var output = PhotoCodec.Load(Path.Combine(folder, "head-result.png")); Assert.Equal(a.Size(), output.Size());
        }
        finally { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
    }
    [Fact]
    public void WholeHead_DebugFailureKeepsReasonWithoutCopyingPrivatePhotos()
    {
        var folder = Path.Combine(Path.GetTempPath(), "GroupTake.Tests", Guid.NewGuid().ToString("N"));
        try
        {
            using var a = new Mat(240, 320, MatType.CV_8UC3, Scalar.All(90));
            Assert.Throws<ArgumentException>(() => DebugExporter.SaveHeadCase(folder, a, a, new(-1, 0, 30, 30), new(40, 30, 100, 130), new()));
            using var metrics = JsonDocument.Parse(File.ReadAllText(Path.Combine(folder, "metrics.json")));
            Assert.False(metrics.RootElement.GetProperty("success").GetBoolean());
            Assert.False(string.IsNullOrWhiteSpace(metrics.RootElement.GetProperty("error").GetString()));
            Assert.Single(Directory.GetFiles(folder));
        }
        finally { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
    }
}
