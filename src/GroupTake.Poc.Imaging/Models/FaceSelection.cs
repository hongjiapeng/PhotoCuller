using OpenCvSharp;

namespace GroupTake.Poc.Imaging.Models;

// Coordinates always refer to the full resolution, EXIF-oriented image.
// Landmarks are ordered by IMAGE side: left eye, right eye, nose, left mouth, right mouth.
public sealed record FaceSelection(Rect Bounds, Point2f[] Landmarks)
{
    public FaceSelection Scale(double scale) => new(
        new Rect((int)Math.Round(Bounds.X * scale), (int)Math.Round(Bounds.Y * scale),
            (int)Math.Round(Bounds.Width * scale), (int)Math.Round(Bounds.Height * scale)),
        Landmarks.Select(p => new Point2f((float)(p.X * scale), (float)(p.Y * scale))).ToArray());
}

public enum BlendMethod { Alpha, NormalClone, MixedClone }
public sealed record BlendOptions(BlendMethod Method = BlendMethod.Alpha, double FeatherRadius = 16);

/// <summary>Explicit user protection stroke in EXIF-oriented original pixels. Protected pixels are never transplanted.</summary>
public sealed record MaskProtectionStroke(double Radius, float[][] Points, bool Restore = false);
