using System.Text.Json;
using GroupTake.Poc.Imaging.Models;
using GroupTake.Poc.Imaging.ImageProcessing;
using GroupTake.Poc.Imaging.Services;
using OpenCvSharp;

var arguments = new Dictionary<string, string>();
for (var i = 0; i + 1 < args.Length; i += 2) arguments.Add(args[i], args[i + 1]);
using var cancellation = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cancellation.Cancel(); };
var ct = cancellation.Token;
try
{
    if (arguments.TryGetValue("--case", out var config))
    {
        var input = JsonSerializer.Deserialize<ManualCase>(File.ReadAllText(config), new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new ArgumentException("Invalid case JSON.");
        if (!input.Mode.Equals("Head", StringComparison.OrdinalIgnoreCase) && !input.Mode.Equals("Face", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Mode must be Head or Face.");
        using var target = PhotoCodec.Load(Path.GetFullPath(input.BasePath, Path.GetDirectoryName(Path.GetFullPath(config))!), ct);
        using var source = PhotoCodec.Load(Path.GetFullPath(input.SourcePath, Path.GetDirectoryName(Path.GetFullPath(config))!), ct);
        var headOptions = input.HeadOptions ?? new HeadReplacementOptions(input.FeatherRadius);
        if (arguments.TryGetValue("--segment", out var segmentationModel))
        {
            using var segmenter = new BiSeNetHeadSegmentationService(segmentationModel);
            var a = segmenter.Segment(target, input.BaseFace.ToSelection().Bounds, ct);
            var b = segmenter.Segment(source, input.SourceFace.ToSelection().Bounds, ct);
            headOptions = headOptions with { TargetMask = a.Mask, SourceMask = b.Mask };
            Console.WriteLine($"BiSeNet inference: {a.Milliseconds + b.Milliseconds:F0} ms (model load excluded)");
        }
        if (input.Mode.Equals("Head", StringComparison.OrdinalIgnoreCase))
            DebugExporter.SaveHeadCase(arguments["--output"], target, source, input.BaseFace.ToSelection().Bounds,
                input.SourceFace.ToSelection().Bounds, headOptions, ct);
        else if (!DebugExporter.SaveCase(arguments["--output"], target, source, input.BaseFace.ToSelection(), input.SourceFace.ToSelection(), input.FeatherRadius, ct))
            Environment.ExitCode = 2;
        Console.WriteLine(Path.GetFullPath(arguments["--output"]));
    }
    else if (arguments.TryGetValue("--detect", out var model))
    {
        using var image = PhotoCodec.Load(arguments["--photo"], ct);
        var faces = FaceDetectionService.Detect(image, model, ct);
        var output = arguments["--output"];
        if (File.Exists(output)) throw new IOException("Output exists.");
        File.WriteAllText(output, JsonSerializer.Serialize(faces.Select(DebugExporter.DescribeFace), new JsonSerializerOptions { WriteIndented = true }));
        using var annotated = image.Clone();
        for (var i = 0; i < faces.Count; i++)
        {
            Cv2.Rectangle(annotated, faces[i].Bounds, new Scalar(0, 255, 255), 3);
            Cv2.PutText(annotated, i.ToString(), faces[i].Bounds.TopLeft, HersheyFonts.HersheySimplex, 1.2, new Scalar(0, 0, 255), 3);
            foreach (var point in faces[i].Landmarks) Cv2.Circle(annotated, new Point((int)point.X, (int)point.Y), 3, new Scalar(0, 0, 255), -1);
        }
        PhotoCodec.Export(annotated, Path.ChangeExtension(output, ".png"), [], ct: ct);
        Console.WriteLine($"Detected {faces.Count} faces. No automatic identity correspondence.");
    }
    else if (arguments.TryGetValue("--sheet", out var folder))
    {
        var crop = arguments["--crop"].Split(',').Select(int.Parse).ToArray();
        var files = Directory.GetFiles(folder).Where(p => new[] { ".jpeg", ".jpg", ".png" }.Contains(Path.GetExtension(p).ToLowerInvariant())).Order().ToArray();
        const int cell = 360;
        using var sheet = new Mat(((files.Length + 3) / 4) * cell, 4 * cell, MatType.CV_8UC3, Scalar.All(30));
        for (var i = 0; i < files.Length; i++)
        {
            using var photo = PhotoCodec.Load(files[i], ct);
            var rect = new Rect(crop[0], crop[1], crop[2], crop[3]) & new Rect(0, 0, photo.Width, photo.Height);
            using var region = new Mat(photo, rect); using var resized = new Mat();
            var scale = Math.Min((cell - 10d) / rect.Width, (cell - 35d) / rect.Height);
            Cv2.Resize(region, resized, new Size((int)(rect.Width * scale), (int)(rect.Height * scale)));
            using var dst = new Mat(sheet, new Rect(i % 4 * cell, i / 4 * cell + 30, resized.Width, resized.Height)); resized.CopyTo(dst);
            Cv2.PutText(sheet, Path.GetFileName(files[i]), new Point(i % 4 * cell + 5, i / 4 * cell + 23), HersheyFonts.HersheySimplex, 0.6, Scalar.All(240), 1);
        }
        PhotoCodec.Export(sheet, arguments["--output"], [], ct: ct);
    }
    else throw new ArgumentException("Usage: --case manual-case.json --output NEW_DIRECTORY | --sheet PHOTO_DIRECTORY --crop x,y,width,height --output NEW.png");
}
catch (Exception ex) { Console.Error.WriteLine(ex.Message); Environment.ExitCode = 1; }

internal sealed record FaceInput(int[] Bounds, float[][]? Landmarks = null)
{
    public FaceSelection ToSelection() => new(new Rect(Bounds[0], Bounds[1], Bounds[2], Bounds[3]),
        Landmarks?.Select(p => new Point2f(p[0], p[1])).ToArray() ?? []);
}
internal sealed record ManualCase(string BasePath, string SourcePath, FaceInput BaseFace, FaceInput SourceFace,
    double FeatherRadius = 8, string Mode = "Head", HeadReplacementOptions? HeadOptions = null);
