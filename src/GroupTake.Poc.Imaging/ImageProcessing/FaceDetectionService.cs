using GroupTake.Poc.Imaging.Models;
using OpenCvSharp;
using OpenCvSharp.Dnn;

namespace GroupTake.Poc.Imaging.ImageProcessing;

/// <summary>
/// CPU-only YuNet 2023 inference. OpenCvSharp 4.10 does not wrap FaceDetectorYN, so decode its
/// documented stride heads through the existing DNN API. No new ONNX dependency or identity matching.
/// Reference: https://github.com/opencv/opencv/blob/4.10.0/modules/objdetect/src/face_detect.cpp
/// Model and license: https://github.com/opencv/opencv_zoo/tree/main/models/face_detection_yunet
/// </summary>
public static class FaceDetectionService
{
    private static readonly string[] OutputNames = ["cls_8", "cls_16", "cls_32", "obj_8", "obj_16", "obj_32",
        "bbox_8", "bbox_16", "bbox_32", "kps_8", "kps_16", "kps_32"];

    public static IReadOnlyList<FaceSelection> Detect(Mat image, string modelPath, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (!File.Exists(modelPath)) throw new FileNotFoundException("YuNet 模型不存在；请手动选择人脸，或加载 face_detection_yunet_2023mar.onnx。", modelPath);
        if (image.Empty() || image.Type() != MatType.CV_8UC3) throw new ArgumentException("检测需要有效 BGR8 图像。");
        using var net = CvDnn.ReadNetFromOnnx(modelPath);
        var scale = Math.Min(1, 1920d / Math.Max(image.Width, image.Height));
        var w = Math.Max(1, (int)Math.Round(image.Width * scale)); var h = Math.Max(1, (int)Math.Round(image.Height * scale));
        using var resized = new Mat(); Cv2.Resize(image, resized, new Size(w, h), interpolation: InterpolationFlags.Area);
        using var padded = new Mat(); Cv2.CopyMakeBorder(resized, padded, 0, (32 - h % 32) % 32, 0, (32 - w % 32) % 32, BorderTypes.Constant, Scalar.All(0));
        using var blob = CvDnn.BlobFromImage(padded);
        net.SetInput(blob);
        var outputs = OutputNames.Select(_ => new Mat()).ToArray();
        try
        {
            ct.ThrowIfCancellationRequested();
            net.Forward(outputs, OutputNames);
            ct.ThrowIfCancellationRequested();
            var arrays = outputs.Select(m =>
            {
                var values = new float[checked((int)m.Total())];
                System.Runtime.InteropServices.Marshal.Copy(m.Data, values, 0, values.Length);
                return values;
            }).ToArray();
            var candidates = new List<FaceSelection>(); var scores = new List<float>();
            for (var level = 0; level < 3; level++)
            {
                var stride = 8 << level; var columns = padded.Width / stride; var rows = padded.Height / stride;
                if (arrays[level].Length != rows * columns || arrays[level + 6].Length != rows * columns * 4
                    || arrays[level + 9].Length != rows * columns * 10)
                    throw new InvalidOperationException("模型输出不兼容，请使用 YuNet 2023mar，或回退手动模式。");
                for (var index = 0; index < rows * columns; index++)
                {
                    var score = MathF.Sqrt(Math.Clamp(arrays[level][index], 0, 1) * Math.Clamp(arrays[level + 3][index], 0, 1));
                    if (!float.IsFinite(score) || score < 0.8f) continue;
                    var cx = (index % columns + arrays[level + 6][index * 4]) * stride;
                    var cy = (index / columns + arrays[level + 6][index * 4 + 1]) * stride;
                    var bw = MathF.Exp(arrays[level + 6][index * 4 + 2]) * stride;
                    var bh = MathF.Exp(arrays[level + 6][index * 4 + 3]) * stride;
                    var sx = image.Width / (double)w; var sy = image.Height / (double)h;
                    if (!float.IsFinite(bw) || !float.IsFinite(bh) || bw < 4 || bh < 4 || bw > padded.Width * 2 || bh > padded.Height * 2) continue;
                    var left = (int)Math.Floor((cx - bw / 2) * sx); var top = (int)Math.Floor((cy - bh / 2) * sy);
                    var right = (int)Math.Ceiling((cx + bw / 2) * sx); var bottom = (int)Math.Ceiling((cy + bh / 2) * sy);
                    var bounds = new Rect(left, top, Math.Max(1, right - left), Math.Max(1, bottom - top)) & new Rect(0, 0, image.Width, image.Height);
                    if (bounds.Width < 16 || bounds.Height < 16) continue;
                    var points = Enumerable.Range(0, 5).Select(n => new Point2f(
                        (float)((arrays[level + 9][index * 10 + n * 2] + index % columns) * stride * sx),
                        (float)((arrays[level + 9][index * 10 + n * 2 + 1] + index / columns) * stride * sy))).ToArray();
                    if (points.Any(p => !float.IsFinite(p.X) || !float.IsFinite(p.Y))) continue;
                    if (points[0].X > points[1].X) (points[0], points[1]) = (points[1], points[0]);
                    if (points[3].X > points[4].X) (points[3], points[4]) = (points[4], points[3]);
                    candidates.Add(new FaceSelection(bounds, points)); scores.Add(score);
                }
                ct.ThrowIfCancellationRequested();
            }
            if (candidates.Count == 0) return [];
            CvDnn.NMSBoxes(candidates.Select(f => f.Bounds), scores, 0.8f, 0.3f, out var keep, topK: 5000);
            return keep.Select(i => candidates[i]).OrderBy(f => f.Bounds.X).ThenBy(f => f.Bounds.Y).ToArray();
        }
        finally { foreach (var output in outputs) output.Dispose(); }
    }
}
