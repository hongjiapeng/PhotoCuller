using System.Diagnostics;
using GroupTake.Poc.Imaging.ImageProcessing;
using GroupTake.Poc.Imaging.Models;
using OpenCvSharp;

namespace GroupTake.Poc.Imaging.Services;

public sealed class CompositionResult(Mat composite, Mat aligned, Mat mask, Alignment alignment,
    BlendOptions options, double elapsedMilliseconds) : IDisposable
{
    public Mat Composite { get; } = composite;
    public Mat Aligned { get; } = aligned;
    public Mat Mask { get; } = mask;
    public Alignment Alignment { get; } = alignment;
    public BlendOptions Options { get; } = options;
    public double ElapsedMilliseconds { get; } = elapsedMilliseconds;
    public void Dispose() { Composite.Dispose(); Aligned.Dispose(); Mask.Dispose(); }
}

public static class CompositionService
{
    public static CompositionResult Compose(Mat target, Mat source, FaceSelection targetFace, FaceSelection sourceFace,
        BlendOptions options, CancellationToken ct = default)
    {
        var timer = Stopwatch.StartNew();
        ct.ThrowIfCancellationRequested();
        if (target.Empty() || source.Empty() || target.Type() != MatType.CV_8UC3 || source.Type() != MatType.CV_8UC3)
            throw new ArgumentException("请载入有效 BGR8 照片。");
        var alignment = FaceAlignmentService.Estimate(targetFace, sourceFace, target.Size(), source.Size(), ct);
        var aligned = FaceAlignmentService.Warp(source, target.Size(), alignment);
        Mat? mask = null;
        Mat? composite = null;
        try
        {
            ct.ThrowIfCancellationRequested();
            mask = FaceMaskGenerator.Create(target.Size(), targetFace);
            // Warp source face support as well: never sample background or missing pixels outside the selected source face.
            using var sourceSupport = new Mat(source.Size(), MatType.CV_8UC1, Scalar.All(0));
            Cv2.Rectangle(sourceSupport, sourceFace.Bounds, Scalar.All(255), -1);
            using var coverage = FaceAlignmentService.Warp(sourceSupport, target.Size(), alignment, InterpolationFlags.Nearest);
            using var kernel = Cv2.GetStructuringElement(MorphShapes.Rect, new Size(3, 3));
            Cv2.Erode(coverage, coverage, kernel);
            using var missing = new Mat();
            Cv2.BitwiseNot(coverage, missing);
            Cv2.BitwiseAnd(mask, missing, missing);
            if (Cv2.CountNonZero(missing) > 0)
                throw new InvalidOperationException("来源人脸框未完整覆盖目标 Mask，请扩大来源框或修正对应点。");
            composite = FaceBlendingService.Blend(target, aligned, mask, options, ct);
            return new CompositionResult(composite, aligned, mask, alignment, options, timer.Elapsed.TotalMilliseconds);
        }
        catch { aligned.Dispose(); mask?.Dispose(); composite?.Dispose(); throw; }
    }
}
