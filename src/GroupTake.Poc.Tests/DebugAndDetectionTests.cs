using System.Text.Json;
using GroupTake.Poc.Imaging.ImageProcessing;
using GroupTake.Poc.Imaging.Models;
using GroupTake.Poc.Imaging.Services;
using OpenCvSharp;
using Xunit;

namespace GroupTake.Poc.Tests;

public sealed class DebugAndDetectionTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "GroupTake.Tests", Guid.NewGuid().ToString("N"));
    public DebugAndDetectionTests() => Directory.CreateDirectory(_directory);
    private static FaceSelection Face => new(new Rect(20, 20, 120, 130),
        [new(55, 65), new(105, 65), new(80, 85), new(60, 110), new(100, 110)]);
    [Fact]
    public void DebugCase_ExportsAllMethodsMatrixAndPixelProtectionMetrics()
    {
        using var a = new Mat(200, 200, MatType.CV_8UC3, Scalar.All(90));
        using var b = new Mat(200, 200, MatType.CV_8UC3, Scalar.All(130));
        Cv2.Circle(b, new Point(55, 65), 5, Scalar.All(20), -1);
        var output = Path.Combine(_directory, "case");
        Assert.True(DebugExporter.SaveCase(output, a, b, Face, Face));
        foreach (var name in new[] { "base.png", "source.png", "aligned.png", "mask.png", "alpha-result.png", "poisson-result.png", "mixed-result.png", "metrics.json" })
            Assert.True(File.Exists(Path.Combine(output, name)), name);
        using var report = JsonDocument.Parse(File.ReadAllText(Path.Combine(output, "metrics.json")));
        var results = report.RootElement.GetProperty("results"); Assert.Equal(3, results.GetArrayLength());
        foreach (var item in results.EnumerateArray())
        {
            Assert.True(item.GetProperty("success").GetBoolean()); Assert.Equal(0, item.GetProperty("outsideMaskChangedChannels").GetInt32());
            Assert.Equal(6, item.GetProperty("Alignment").GetProperty("Matrix").GetArrayLength());
        }
        Assert.Throws<IOException>(() => DebugExporter.SaveCase(output, a, b, Face, Face));
    }
    [Fact]
    public void DebugCase_FailureIsRecorded_CancellationLeavesNoPartialDirectory()
    {
        using var a = new Mat(200, 200, MatType.CV_8UC3, Scalar.All(90));
        var output = Path.Combine(_directory, "failed");
        Assert.False(DebugExporter.SaveCase(output, a, a, Face with { Landmarks = [] }, Face));
        using var report = JsonDocument.Parse(File.ReadAllText(Path.Combine(output, "metrics.json")));
        foreach (var item in report.RootElement.GetProperty("results").EnumerateArray())
        { Assert.False(item.GetProperty("success").GetBoolean()); Assert.False(string.IsNullOrWhiteSpace(item.GetProperty("error").GetString())); }
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() => DebugExporter.SaveCase(Path.Combine(_directory, "cancelled"), a, a, Face, Face, ct: cancellation.Token));
        Assert.Single(Directory.GetDirectories(_directory));
        Assert.Empty(Directory.GetDirectories(_directory, "*.partial"));
    }
    [Fact]
    public void Detection_MissingModelAndCancellation_AreExplicitErrors()
    {
        using var a = new Mat(200, 200, MatType.CV_8UC3, Scalar.All(90));
        Assert.Throws<FileNotFoundException>(() => FaceDetectionService.Detect(a, Path.Combine(_directory, "missing.onnx")));
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() => FaceDetectionService.Detect(a, "missing.onnx", cancellation.Token));
    }
    public void Dispose() => Directory.Delete(_directory, recursive: true);
}
