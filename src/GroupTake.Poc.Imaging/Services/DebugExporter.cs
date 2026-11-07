using System.Text.Json;
using GroupTake.Poc.Imaging.ImageProcessing;
using GroupTake.Poc.Imaging.Models;
using OpenCvSharp;

namespace GroupTake.Poc.Imaging.Services;

public static class DebugExporter
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    public static object DescribeFace(FaceSelection face) => new
    {
        bounds = new[] { face.Bounds.X, face.Bounds.Y, face.Bounds.Width, face.Bounds.Height },
        landmarks = face.Landmarks.Select(p => new[] { p.X, p.Y }).ToArray()
    };

    public static void SaveHeadCase(string directory, Mat target, Mat source, Rect targetHead, Rect sourceHead,
        HeadReplacementOptions options, CancellationToken ct = default)
    {
        directory = Path.GetFullPath(directory);
        if (Directory.Exists(directory) || File.Exists(directory)) throw new IOException("调试目录已存在。");
        ct.ThrowIfCancellationRequested();
        var staging = directory + $".{Guid.NewGuid():N}.partial";
        Directory.CreateDirectory(staging);
        try
        {
            using var result = HeadReplacementService.Compose(target, source, targetHead, sourceHead, options, ct);
            foreach (var item in new[] { (Name: "target", Patch: options.TargetMask, Edits: options.TargetProtection, Bounds: targetHead, Size: target.Size()),
                (Name: "source", Patch: options.SourceMask, Edits: options.SourceProtection, Bounds: sourceHead, Size: source.Size()) })
            {
                if (item.Patch is null) continue;
                using var raw = HeadMaskEditor.Materialize(item.Patch, item.Size);
                PhotoCodec.Export(raw, Path.Combine(staging, item.Name + "-mask-raw.png"), [], ct: ct);
                HeadMaskEditor.Apply(raw, item.Edits, item.Bounds, ct);
                PhotoCodec.Export(raw, Path.Combine(staging, item.Name + "-mask-edited.png"), [], ct: ct);
            }
            foreach (var (name, image) in new[] { ("base.png", target), ("source.png", source), ("aligned.png", result.Aligned),
                ("mask.png", result.Mask), ("head-result.png", result.Composite) })
                PhotoCodec.Export(image, Path.Combine(staging, name), [], ct: ct);
            using var diff = new Mat(); using var inverse = new Mat();
            Cv2.Absdiff(target, result.Composite, diff); Cv2.BitwiseNot(result.Mask, inverse);
            using var outside = new Mat(target.Size(), MatType.CV_8UC3, Scalar.All(0)); diff.CopyTo(outside, inverse);
            using var flat = outside.Reshape(1);
            File.WriteAllText(Path.Combine(staging, "metrics.json"), JsonSerializer.Serialize(new
            {
                schemaVersion = 1, mode = "WholeHead", success = true, algorithm = "AlphaFeather",
                baseSize = new[] { target.Width, target.Height }, sourceSize = new[] { source.Width, source.Height },
                targetHead = new[] { targetHead.X, targetHead.Y, targetHead.Width, targetHead.Height },
                sourceHead = new[] { sourceHead.X, sourceHead.Y, sourceHead.Width, sourceHead.Height },
                options, result.Alignment, result.ElapsedMilliseconds,
                outsideMaskChangedChannels = Cv2.CountNonZero(flat), outsideMaskMaxDifference = Cv2.Norm(outside, NormTypes.INF),
                note = "Manual box alignment; optional BiSeNet semantic masks with ordered manual edits. No landmark reprojection error, identity/pose validation or photographic quality guarantee."
            }, JsonOptions));
            ct.ThrowIfCancellationRequested(); Directory.Move(staging, directory);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            // Keep explicit failure evidence without copying originals when composition itself failed.
            if (Directory.Exists(staging) && !Directory.Exists(directory))
            {
                File.WriteAllText(Path.Combine(staging, "metrics.json"), JsonSerializer.Serialize(new
                { schemaVersion = 1, mode = "WholeHead", success = false, error = ex.Message }, JsonOptions));
                Directory.Move(staging, directory);
            }
            throw;
        }
        finally { if (Directory.Exists(staging)) Directory.Delete(staging, true); }
    }

    /// <summary>Explicit opt-in only. Creates a new directory; never overwrites an earlier case.</summary>
    public static bool SaveCase(string directory, Mat target, Mat source, FaceSelection a, FaceSelection b,
        double featherRadius = 16, CancellationToken ct = default)
    {
        directory = Path.GetFullPath(directory);
        if (Directory.Exists(directory) || File.Exists(directory)) throw new IOException("调试目录已存在，请选择新目录。");
        ct.ThrowIfCancellationRequested();
        var staging = directory + $".{Guid.NewGuid():N}.partial";
        Directory.CreateDirectory(staging);
        var metrics = new List<object>();
        var allSucceeded = true;
        try
        {
            PhotoCodec.Export(target, Path.Combine(staging, "base.png"), [], ct: ct);
            PhotoCodec.Export(source, Path.Combine(staging, "source.png"), [], ct: ct);
            foreach (var method in Enum.GetValues<BlendMethod>())
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    using var result = CompositionService.Compose(target, source, a, b, new BlendOptions(method, featherRadius), ct);
                    if (method == BlendMethod.Alpha)
                    {
                        PhotoCodec.Export(result.Aligned, Path.Combine(staging, "aligned.png"), [], ct: ct);
                        PhotoCodec.Export(result.Mask, Path.Combine(staging, "mask.png"), [], ct: ct);
                    }
                    var file = method switch { BlendMethod.Alpha => "alpha-result.png", BlendMethod.NormalClone => "poisson-result.png", _ => "mixed-result.png" };
                    PhotoCodec.Export(result.Composite, Path.Combine(staging, file), [], ct: ct);
                    using var diff = new Mat(); using var inverse = new Mat();
                    Cv2.Absdiff(target, result.Composite, diff); Cv2.BitwiseNot(result.Mask, inverse);
                    using var outside = new Mat(target.Size(), MatType.CV_8UC3, Scalar.All(0)); diff.CopyTo(outside, inverse);
                    using var flat = outside.Reshape(1);
                    metrics.Add(new { algorithm = method.ToString(), success = true, error = (string?)null,
                        result.Alignment, result.ElapsedMilliseconds, featherRadius,
                        outsideMaskChangedChannels = Cv2.CountNonZero(flat),
                        outsideMaskMaxDifference = Cv2.Norm(outside, NormTypes.INF), output = file });
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex) { allSucceeded = false; metrics.Add(new { algorithm = method.ToString(), success = false, error = ex.Message }); }
            }
            var report = new { schemaVersion = 1, createdUtc = DateTimeOffset.UtcNow,
                baseSize = new[] { target.Width, target.Height }, sourceSize = new[] { source.Width, source.Height },
                baseFace = DescribeFace(a), sourceFace = DescribeFace(b), results = metrics,
                note = "PNG debug pixels are EXIF-oriented BGR converted by codec; original metadata/ICC is not copied. Visual quality requires human review." };
            File.WriteAllText(Path.Combine(staging, "metrics.json"), JsonSerializer.Serialize(report, JsonOptions));
            ct.ThrowIfCancellationRequested();
            Directory.Move(staging, directory);
            return allSucceeded;
        }
        finally { if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true); }
    }
}
