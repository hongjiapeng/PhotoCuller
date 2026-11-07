using GroupTake.Poc.Imaging.ImageProcessing;
using GroupTake.Poc.Imaging.Models;
using OpenCvSharp;
using Xunit;

namespace GroupTake.Poc.Tests;

public sealed class BlendMaskIntegrityTests
{
    [Theory]
    [InlineData(BlendMethod.NormalClone)]
    [InlineData(BlendMethod.MixedClone)]
    public void Poisson_KeepsCallerMaskImmutableAndPreservesOriginalOutsideSupport(BlendMethod method)
    {
        using var target = new Mat(100, 120, MatType.CV_8UC3, Scalar.All(100));
        using var source = new Mat(100, 120, MatType.CV_8UC3, Scalar.All(160));
        Cv2.Circle(source, new Point(60, 50), 8, new Scalar(30, 100, 220), -1);
        using var mask = new Mat(target.Size(), MatType.CV_8UC1, Scalar.All(0));
        Cv2.Ellipse(mask, new Point(60, 50), new Size(25, 35), 0, 0, 360, Scalar.All(255), -1);
        // An excluded interior patch represents protected clothing, not just an outside bounding-box check.
        Cv2.Rectangle(mask, new Rect(39, 65, 12, 8), Scalar.All(0), -1);
        using var beforeMask = mask.Clone(); using var beforeTarget = target.Clone(); using var beforeSource = source.Clone();
        using var result = FaceBlendingService.Blend(target, source, mask, new(method, 0));
        Assert.Equal(0, Cv2.Norm(beforeMask, mask, NormTypes.INF));
        using var diff = new Mat(); Cv2.Absdiff(target, result, diff);
        using var outsideMask = new Mat(); Cv2.BitwiseNot(beforeMask, outsideMask);
        using var outside = new Mat(target.Size(), MatType.CV_8UC3, Scalar.All(0)); diff.CopyTo(outside, outsideMask);
        Assert.Equal(0, Cv2.Norm(outside, NormTypes.INF));
        Assert.True(Cv2.Norm(diff, NormTypes.INF) > 10, "A real source feature must transfer inside the permitted mask.");
        Assert.Equal(0, Cv2.Norm(beforeTarget, target, NormTypes.INF));
        Assert.Equal(0, Cv2.Norm(beforeSource, source, NormTypes.INF));
    }
}
