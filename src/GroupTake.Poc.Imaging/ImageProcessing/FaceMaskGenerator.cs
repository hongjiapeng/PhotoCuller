using GroupTake.Poc.Imaging.Models;
using OpenCvSharp;

namespace GroupTake.Poc.Imaging.ImageProcessing;

public static class FaceMaskGenerator
{
    public static Mat Create(Size size, FaceSelection face)
    {
        FaceAlignmentService.Validate(face, size);
        var p = face.Landmarks;
        var eyeMid = new Point2f((p[0].X + p[1].X) / 2, (p[0].Y + p[1].Y) / 2);
        var mouthMid = new Point2f((p[3].X + p[4].X) / 2, (p[3].Y + p[4].Y) / 2);
        var dx = p[1].X - p[0].X;
        var dy = p[1].Y - p[0].Y;
        var distance = Math.Sqrt(dx * dx + dy * dy);
        var angle = Math.Atan2(dy, dx);
        var ex = Math.Cos(angle); var ey = Math.Sin(angle);
        var vx = -ey; var vy = ex;
        var span = (mouthMid.X - eyeMid.X) * vx + (mouthMid.Y - eyeMid.Y) * vy;
        // Conservative inner-face ellipse: upper eyelids through below mouth, excludes hair/ears.
        var centerX = eyeMid.X + vx * span * 0.48;
        var centerY = eyeMid.Y + vy * span * 0.48;
        var radiusX = Math.Min(distance * 0.95, face.Bounds.Width * 0.44);
        var radiusY = Math.Min(span * 0.98, face.Bounds.Height * 0.43);
        if (radiusY < 4) throw new ArgumentException("人脸关键点范围过小。");
        var mask = new Mat(size, MatType.CV_8UC1, Scalar.All(0));
        Cv2.Ellipse(mask, new Point((int)Math.Round(centerX), (int)Math.Round(centerY)),
            new Size((int)radiusX, (int)radiusY), angle * 180 / Math.PI, 0, 360, Scalar.All(255), -1);
        using var boxMask = new Mat(size, MatType.CV_8UC1, Scalar.All(0));
        // Keep a one-pixel empty border, required by Poisson's boundary conditions.
        var allowed = face.Bounds & new Rect(1, 1, size.Width - 2, size.Height - 2);
        Cv2.Rectangle(boxMask, allowed, Scalar.All(255), -1);
        Cv2.BitwiseAnd(mask, boxMask, mask);
        return mask;
    }
}
