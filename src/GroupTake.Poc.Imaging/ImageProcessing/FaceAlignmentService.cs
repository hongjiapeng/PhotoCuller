using GroupTake.Poc.Imaging.Models;
using OpenCvSharp;

namespace GroupTake.Poc.Imaging.ImageProcessing;

public sealed record Alignment(double[] Matrix, double? RmsError, double? MaxError, double Scale,
    double RotationDegrees, string Basis = "FiveLandmarks");

public static class FaceAlignmentService
{
    public static Alignment Estimate(FaceSelection target, FaceSelection source, Size targetSize, Size sourceSize,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        Validate(target, targetSize);
        Validate(source, sourceSize);
        using var from = InputArray.Create(source.Landmarks);
        using var to = InputArray.Create(target.Landmarks);
        using var inliers = new Mat();
        using var matrix = Cv2.EstimateAffinePartial2D(from, to, inliers, RobustEstimationAlgorithms.RANSAC,
            Math.Max(1.5, target.Bounds.Width * 0.035), 2000, 0.99, 10);
        ct.ThrowIfCancellationRequested();
        if (matrix is null || matrix.Empty()) throw new InvalidOperationException("无法估计仿射变换，请检查对应关键点。");
        var m = new double[6];
        for (var i = 0; i < 6; i++) m[i] = matrix.At<double>(i / 3, i % 3);
        var scale = Math.Sqrt(m[0] * m[0] + m[3] * m[3]);
        var angle = Math.Atan2(m[3], m[0]) * 180 / Math.PI;
        var expectedScale = target.Bounds.Width / (double)source.Bounds.Width;
        var errors = source.Landmarks.Select((p, i) =>
        {
            var dx = m[0] * p.X + m[1] * p.Y + m[2] - target.Landmarks[i].X;
            var dy = m[3] * p.X + m[4] * p.Y + m[5] - target.Landmarks[i].Y;
            return Math.Sqrt(dx * dx + dy * dy);
        }).ToArray();
        var rms = Math.Sqrt(errors.Average(e => e * e));
        // Evaluate ALL landmarks, including RANSAC outliers. Do not silently drop a mismatched mouth/eye.
        if (m.Any(v => !double.IsFinite(v)) || scale / expectedScale is < 0.55 or > 1.8
            || Math.Abs(angle) > 25 || rms > target.Bounds.Width * 0.065
            || errors.Max() > target.Bounds.Width * 0.12 || Cv2.CountNonZero(inliers) < 4)
            throw new InvalidOperationException($"对齐不可信：RMS={rms:F2}px，旋转={angle:F1}°。请修正关键点；明显转头或遮挡不适合此实验。");
        return new Alignment(m, rms, errors.Max(), scale, angle);
    }

    public static void Validate(FaceSelection face, Size size)
    {
        var b = face.Bounds;
        if (b.Width < 16 || b.Height < 16 || b.X < 0 || b.Y < 0 || b.Right > size.Width || b.Bottom > size.Height)
            throw new ArgumentException("人脸框必须在图像内，且至少为 16×16 像素。");
        if (face.Landmarks.Length != 5) throw new ArgumentException("请标记全部五个点：画面左眼、右眼、鼻尖、左嘴角、右嘴角。");
        foreach (var p in face.Landmarks)
            if (!float.IsFinite(p.X) || !float.IsFinite(p.Y) || p.X < b.X || p.X >= b.Right || p.Y < b.Y || p.Y >= b.Bottom)
                throw new ArgumentException("关键点必须在对应人脸框内。");
        var points = face.Landmarks;
        var eyeDistance = Math.Sqrt(Math.Pow(points[1].X - points[0].X, 2) + Math.Pow(points[1].Y - points[0].Y, 2));
        if (points[0].X >= points[1].X || points[3].X >= points[4].X || eyeDistance < b.Width * 0.15
            || (points[3].Y + points[4].Y) / 2 <= (points[0].Y + points[1].Y) / 2 + eyeDistance * 0.15
            || points[2].Y <= Math.Min(points[0].Y, points[1].Y) || points[2].Y >= Math.Max(points[3].Y, points[4].Y))
            throw new ArgumentException("关键点顺序或几何无效。左右按画面方向标记；鼻尖应位于双眼与嘴巴之间。");
    }

    public static Mat Warp(Mat source, Size targetSize, Alignment alignment, InterpolationFlags interpolation = InterpolationFlags.Linear)
    {
        using var matrix = new Mat(2, 3, MatType.CV_64FC1);
        for (var i = 0; i < 6; i++) matrix.Set(i / 3, i % 3, alignment.Matrix[i]);
        var output = new Mat();
        try { Cv2.WarpAffine(source, output, matrix, targetSize, interpolation, BorderTypes.Constant, Scalar.All(0)); return output; }
        catch { output.Dispose(); throw; }
    }
}
