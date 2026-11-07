using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using GroupTake.Poc.Imaging.Models;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using OpenCvSharp;

namespace GroupTake.Poc.Imaging.Services;

public sealed record HeadSegmentationResult(HeadMaskPatch Mask, double Milliseconds, string ModelSha256);

/// <summary>CPU-only yakhyo/face-parsing model. No Python process or photo upload.</summary>
public sealed class BiSeNetHeadSegmentationService : IDisposable
{
    private readonly InferenceSession _session;
    private readonly string _sha;
    public BiSeNetHeadSegmentationService(string path)
    {
        using (var stream = File.OpenRead(path)) _sha = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
        if (_sha is not ("0d9bd318e46987c3bdbfacae9e2c0f461cae1c6ac6ea6d43bbe541a91727e33f"
            or "5b805bba7b5660ab7070b5a381dcf75e5b3e04199f1e9387232a77a00095102e"))
            throw new ArgumentException("请选择已验证的 yakhyo BiSeNet ResNet18/34 ONNX 权重。");
        using var options = new SessionOptions { IntraOpNumThreads = 4, InterOpNumThreads = 1,
            GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL };
        _session = new InferenceSession(path, options);
    }

    public HeadSegmentationResult Segment(Mat image, Rect head, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested(); var timer = Stopwatch.StartNew();
        if (image.Type() != MatType.CV_8UC3 || head.Width < 16 || head.Height < 16 || head.X < 0 || head.Y < 0
            || head.Right > image.Width || head.Bottom > image.Height) throw new ArgumentException("请先框选完整头部。");
        var side = (int)Math.Ceiling(Math.Max(head.Width, head.Height) * 1.25);
        var x = Math.Max(0, (int)(head.X + head.Width / 2d - side / 2d));
        var y = Math.Max(0, (int)(head.Y + head.Height * .62 - side / 2d));
        var crop = new Rect(x, y, Math.Min(side, image.Width - x), Math.Min(side, image.Height - y));
        using var region = new Mat(image, crop);
        var tensor = new DenseTensor<float>(PrepareInput(region), [1, 3, 512, 512]);
        using var outputs = _session.Run([NamedOnnxValue.CreateFromTensor(_session.InputNames[0], tensor)]);
        ct.ThrowIfCancellationRequested();
        var output = outputs.First().AsTensor<float>();
        if (!output.Dimensions.SequenceEqual(new[] { 1, 19, 512, 512 })) throw new InvalidDataException("BiSeNet 输出尺寸不兼容。");
        var pixels = DecodeHead(output.ToArray(), 512 * 512);
        using var small = new Mat(512, 512, MatType.CV_8UC1); Marshal.Copy(pixels, 0, small.Data, pixels.Length);
        using var resized = new Mat(); Cv2.Resize(small, resized, crop.Size, 0, 0, InterpolationFlags.Nearest);
        var native = new byte[crop.Width * crop.Height]; Marshal.Copy(resized.Data, native, 0, native.Length);
        // Clip to the user's selection: nearby group members and rear clothing are otherwise common false positives.
        for (var row = 0; row < crop.Height; row++)
        {
            ct.ThrowIfCancellationRequested();
            for (var col = 0; col < crop.Width; col++)
                if (!head.Contains(new Point(crop.X + col, crop.Y + row))) native[row * crop.Width + col] = 0;
        }
        if (native.Count(p => p != 0) < 64) throw new InvalidOperationException("没有得到有效头部蒙版，请调整框选或改用手动路线。");
        return new(new(crop.X, crop.Y, crop.Width, crop.Height, image.Width, image.Height, native,
            $"BiSeNet:{_sha};classes=1..13,17;hat-excluded"), timer.Elapsed.TotalMilliseconds, _sha);
    }

    public static float[] PrepareInput(Mat bgr)
    {
        if (bgr.Empty() || bgr.Type() != MatType.CV_8UC3) throw new ArgumentException("需要 BGR 8 位照片。");
        using var resized = new Mat(); Cv2.Resize(bgr, resized, new Size(512, 512), 0, 0, InterpolationFlags.Linear);
        var bytes = new byte[512 * 512 * 3]; Marshal.Copy(resized.Data, bytes, 0, bytes.Length);
        var result = new float[bytes.Length]; float[] mean = [.485f, .456f, .406f], std = [.229f, .224f, .225f];
        for (var c = 0; c < 3; c++) for (var p = 0; p < 512 * 512; p++)
            result[c * 512 * 512 + p] = (bytes[p * 3 + 2 - c] / 255f - mean[c]) / std[c];
        return result;
    }

    public static byte[] DecodeHead(float[] logits, int area)
    {
        if (area <= 0 || logits.LongLength != (long)area * 19) throw new ArgumentException("需要 19 类 CHW logits。");
        var mask = new byte[area];
        for (var p = 0; p < area; p++)
        {
            var best = 0;
            for (var c = 1; c < 19; c++) if (logits[c * area + p] > logits[best * area + p]) best = c;
            mask[p] = (byte)(best is >= 1 and <= 13 or 17 ? 255 : 0);
        }
        return mask;
    }
    public void Dispose() => _session.Dispose();
}
