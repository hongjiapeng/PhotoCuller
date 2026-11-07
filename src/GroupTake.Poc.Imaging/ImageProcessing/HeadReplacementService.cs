using System.Diagnostics;
using GroupTake.Poc.Imaging.Models;
using GroupTake.Poc.Imaging.Services;
using OpenCvSharp;

namespace GroupTake.Poc.Imaging.ImageProcessing;

public sealed record HeadReplacementOptions(double FeatherRadius = 8, double RotationDegrees = 0,
    double ScaleAdjustment = 1, double OffsetX = 0, double OffsetY = 0,
    MaskProtectionStroke[]? TargetProtection = null, MaskProtectionStroke[]? SourceProtection = null,
    HeadMaskPatch? TargetMask = null, HeadMaskPatch? SourceMask = null);

/// <summary>
/// Phase-one experiment: transplant the complete head as one rigid image patch. No landmark fitting,
/// expression deformation, generative pixels, or gradient mixing. Boxes include hair, ears and chin.
/// </summary>
public static class HeadReplacementService
{
    public static CompositionResult Compose(Mat target, Mat source, Rect targetHead, Rect sourceHead,
        HeadReplacementOptions options, CancellationToken ct = default)
    {
        var timer = Stopwatch.StartNew();
        ct.ThrowIfCancellationRequested();
        Validate(target, targetHead); Validate(source, sourceHead);
        if (!double.IsFinite(options.RotationDegrees) || Math.Abs(options.RotationDegrees) > 25
            || !double.IsFinite(options.ScaleAdjustment) || options.ScaleAdjustment is < 0.75 or > 1.25
            || !double.IsFinite(options.OffsetX) || !double.IsFinite(options.OffsetY)
            || Math.Abs(options.OffsetX) > targetHead.Width || Math.Abs(options.OffsetY) > targetHead.Height)
            throw new ArgumentException("整头微调参数无效。旋转限 ±25°，缩放微调为 0.75–1.25。");
        var shapeRatio = (targetHead.Width / (double)targetHead.Height) / (sourceHead.Width / (double)sourceHead.Height);
        if (shapeRatio is < 0.65 or > 1.55)
            throw new ArgumentException("两个人头框的比例差异过大。请完整框住同一人的头发、耳朵和下巴，并选择相近姿态。");
        var scale = targetHead.Height / (double)sourceHead.Height * options.ScaleAdjustment;
        var angle = options.RotationDegrees * Math.PI / 180;
        var a = scale * Math.Cos(angle); var b = scale * Math.Sin(angle);
        var sourceX = sourceHead.X + sourceHead.Width / 2d; var sourceY = sourceHead.Y + sourceHead.Height / 2d;
        var targetX = targetHead.X + targetHead.Width / 2d + options.OffsetX;
        var targetY = targetHead.Y + targetHead.Height / 2d + options.OffsetY;
        var alignment = new Alignment([a, -b, targetX - a * sourceX + b * sourceY,
            b, a, targetY - b * sourceX - a * sourceY], null, null, scale, options.RotationDegrees, "ManualHeadBoxes");
        var aligned = FaceAlignmentService.Warp(source, target.Size(), alignment);
        Mat? mask = null;
        Mat? composite = null;
        try
        {
            ct.ThrowIfCancellationRequested();
            mask = options.TargetMask is { } targetPatch ? HeadMaskEditor.Materialize(targetPatch, target.Size())
                : new Mat(target.Size(), MatType.CV_8UC1, Scalar.All(0));
            if (options.TargetMask is null)
            {
                Cv2.Ellipse(mask, new Point(targetHead.X + targetHead.Width / 2, targetHead.Y + targetHead.Height / 2),
                    new Size(targetHead.Width / 2, targetHead.Height / 2), 0, 0, 360, Scalar.All(255), -1);
            }
            // The selected head bounds also constrain externally supplied semantic patches.
            using var limit = new Mat(target.Size(), MatType.CV_8UC1, Scalar.All(0));
            Cv2.Rectangle(limit, targetHead, Scalar.All(255), -1); Cv2.BitwiseAnd(mask, limit, mask);
            HeadMaskEditor.Apply(mask, options.TargetProtection, targetHead, ct);
            using var support = new Mat(source.Size(), MatType.CV_8UC1, Scalar.All(255));
            using var coverage = FaceAlignmentService.Warp(support, target.Size(), alignment, InterpolationFlags.Nearest);
            using var missing = new Mat(); Cv2.BitwiseNot(coverage, missing); Cv2.BitwiseAnd(mask, missing, missing);
            if (Cv2.CountNonZero(missing) > 0) throw new InvalidOperationException("对齐后的来源图未覆盖头部区域。请重新框选或减小微调范围。");
            // A rectangle/ellipse is NOT semantic head segmentation: collars can lie inside it.
            // Respect explicit target protection and warp source protection through the SAME transform as its pixels.
            if (options.SourceMask is not null || options.SourceProtection is { Length: > 0 })
            {
                using var allowed = options.SourceMask is { } sourcePatch ? HeadMaskEditor.Materialize(sourcePatch, source.Size())
                    : new Mat(source.Size(), MatType.CV_8UC1, Scalar.All(255));
                HeadMaskEditor.Apply(allowed, options.SourceProtection, sourceHead, ct);
                // Linear RGB sampling can touch neighboring protected source pixels. Expand protection one pixel.
                using var kernel = Cv2.GetStructuringElement(MorphShapes.Rect, new Size(3, 3));
                Cv2.Erode(allowed, allowed, kernel);
                using var warpedAllowed = FaceAlignmentService.Warp(allowed, target.Size(), alignment, InterpolationFlags.Nearest);
                Cv2.BitwiseAnd(mask, warpedAllowed, mask);
            }
            var blend = new BlendOptions(BlendMethod.Alpha, options.FeatherRadius);
            composite = FaceBlendingService.Blend(target, aligned, mask, blend, ct);
            return new CompositionResult(composite, aligned, mask, alignment, blend, timer.Elapsed.TotalMilliseconds);
        }
        catch { aligned.Dispose(); mask?.Dispose(); composite?.Dispose(); throw; }
    }

    private static void Validate(Mat image, Rect box)
    {
        if (image.Empty() || image.Type() != MatType.CV_8UC3 || box.Width < 16 || box.Height < 16
            || box.X < 0 || box.Y < 0 || box.Right > image.Width || box.Bottom > image.Height)
            throw new ArgumentException("请在有效照片内完整框选头部，范围至少 16×16 像素。");
    }

}
