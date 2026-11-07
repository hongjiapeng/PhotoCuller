using GroupTake.Poc.Imaging.Models;
using OpenCvSharp;

namespace GroupTake.Poc.Imaging.ImageProcessing;

public static class FaceBlendingService
{
    public static Mat Blend(Mat target, Mat aligned, Mat mask, BlendOptions options, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (target.Type() != MatType.CV_8UC3 || aligned.Type() != MatType.CV_8UC3 || mask.Type() != MatType.CV_8UC1
            || target.Size() != aligned.Size() || mask.Size() != target.Size())
            throw new ArgumentException("融合要求同尺寸 BGR8 图像与灰度 Mask。");
        if (!Enum.IsDefined(options.Method) || !double.IsFinite(options.FeatherRadius) || options.FeatherRadius is < 0 or > 1000)
            throw new ArgumentException("融合参数无效。");
        if (Cv2.CountNonZero(mask) < 64) throw new ArgumentException("有效 Mask 太小，无法融合。");
        var roi = Cv2.BoundingRect(mask);
        var result = target.Clone();
        try
        {
            if (options.Method == BlendMethod.Alpha)
                Alpha(target, aligned, mask, result, roi, options.FeatherRadius, ct);
            else
            {
                using var clone = new Mat();
                // OpenCV positions the nonzero-mask bounding rectangle at center, not the whole source image.
                var center = new Point(roi.X + roi.Width / 2, roi.Y + roi.Height / 2);
                Cv2.SeamlessClone(aligned, target, mask, center, clone,
                    options.Method == BlendMethod.NormalClone ? SeamlessCloneMethods.NormalClone : SeamlessCloneMethods.MixedClone);
                ct.ThrowIfCancellationRequested();
                clone.CopyTo(result, mask); // Poisson may change its rectangular ROI outside the ellipse. Restore that region.
            }
            ct.ThrowIfCancellationRequested();
            return result;
        }
        catch { result.Dispose(); throw; }
    }

    private static unsafe void Alpha(Mat target, Mat source, Mat mask, Mat output, Rect roi, double feather, CancellationToken ct)
    {
        // Inward feather only: pixels outside the binary support are exactly unchanged.
        using var distance = new Mat();
        using var maskRoi = new Mat(mask, roi);
        using var padded = new Mat();
        Cv2.CopyMakeBorder(maskRoi, padded, 1, 1, 1, 1, BorderTypes.Constant, Scalar.All(0));
        Cv2.DistanceTransform(padded, distance, DistanceTypes.L2, DistanceTransformMasks.Mask3);
        for (var y = 0; y < roi.Height; y++)
        {
            ct.ThrowIfCancellationRequested();
            var a = (byte*)target.Ptr(y + roi.Y) + roi.X * 3;
            var b = (byte*)source.Ptr(y + roi.Y) + roi.X * 3;
            var dst = (byte*)output.Ptr(y + roi.Y) + roi.X * 3;
            var support = (byte*)mask.Ptr(y + roi.Y) + roi.X;
            var d = (float*)distance.Ptr(y + 1) + 1;
            for (var x = 0; x < roi.Width; x++)
            {
                if (support[x] == 0) continue;
                var alpha = feather <= 0 ? 1 : Math.Clamp(d[x] / feather, 0, 1);
                alpha = alpha * alpha * (3 - 2 * alpha);
                for (var c = 0; c < 3; c++) dst[x * 3 + c] = (byte)Math.Clamp(
                    Math.Round(a[x * 3 + c] * (1 - alpha) + b[x * 3 + c] * alpha), 0, 255);
            }
        }
    }
}
