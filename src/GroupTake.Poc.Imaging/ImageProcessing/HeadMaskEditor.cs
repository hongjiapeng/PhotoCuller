using GroupTake.Poc.Imaging.Models;
using OpenCvSharp;
using System.Runtime.InteropServices;

namespace GroupTake.Poc.Imaging.ImageProcessing;

public static class HeadMaskEditor
{
    public static Mat Materialize(HeadMaskPatch patch, Size size)
    {
        if (patch.ImageWidth != size.Width || patch.ImageHeight != size.Height || patch.Width <= 0 || patch.Height <= 0
            || patch.X < 0 || patch.Y < 0 || (long)patch.X + patch.Width > size.Width || (long)patch.Y + patch.Height > size.Height
            || patch.Pixels is null || patch.Pixels.LongLength != (long)patch.Width * patch.Height
            || patch.Pixels.Any(p => p != 0 && p != 255)) throw new ArgumentException("蒙版尺寸或二值像素无效，请重新分割。");
        var result = new Mat(size, MatType.CV_8UC1, Scalar.All(0));
        using var crop = new Mat(patch.Height, patch.Width, MatType.CV_8UC1);
        Marshal.Copy(patch.Pixels, 0, crop.Data, patch.Pixels.Length);
        using var roi = new Mat(result, new Rect(patch.X, patch.Y, patch.Width, patch.Height)); crop.CopyTo(roi);
        return result;
    }

    public static void Apply(Mat mask, MaskProtectionStroke[]? strokes, Rect restoreLimit, CancellationToken ct = default)
    {
        if (strokes is null) return;
        foreach (var stroke in strokes)
        {
            ct.ThrowIfCancellationRequested();
            if (stroke is null || !double.IsFinite(stroke.Radius) || stroke.Radius is < 1 or > 500
                || stroke.Points is not { Length: > 0 }) throw new ArgumentException("笔刷需要有效半径与坐标。");
            // A restore stroke is clipped to the selected head, so accidental painting cannot import another person.
            using var paint = new Mat(mask.Size(), MatType.CV_8UC1, Scalar.All(0));
            Point? previous = null; var radius = (int)Math.Ceiling(stroke.Radius);
            foreach (var p in stroke.Points)
            {
                if (p is not { Length: 2 } || !float.IsFinite(p[0]) || !float.IsFinite(p[1])
                    || p[0] < 0 || p[1] < 0 || p[0] >= mask.Width || p[1] >= mask.Height)
                    throw new ArgumentException("笔刷坐标必须位于对应原图内。");
                var point = new Point((int)Math.Round(p[0]), (int)Math.Round(p[1]));
                Cv2.Circle(paint, point, radius, Scalar.All(255), -1);
                if (previous is { } start) Cv2.Line(paint, start, point, Scalar.All(255), radius * 2 + 1);
                previous = point;
            }
            if (stroke.Restore)
            {
                using var clip = new Mat(mask.Size(), MatType.CV_8UC1, Scalar.All(0));
                Cv2.Rectangle(clip, restoreLimit, Scalar.All(255), -1); Cv2.BitwiseAnd(paint, clip, paint);
            }
            mask.SetTo(Scalar.All(stroke.Restore ? 255 : 0), paint);
        }
    }
}
