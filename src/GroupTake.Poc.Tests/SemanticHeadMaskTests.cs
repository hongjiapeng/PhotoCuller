using GroupTake.Poc.Imaging.ImageProcessing;
using GroupTake.Poc.Imaging.Models;
using GroupTake.Poc.Imaging.Services;
using OpenCvSharp;
using System.Text.Json;
using Xunit;

namespace GroupTake.Poc.Tests;

public sealed class SemanticHeadMaskTests
{
    [Fact]
    public void PrepareInput_UsesRgbImageNetNormalizationInChwOrder()
    {
        using var bgr = new Mat(2, 3, MatType.CV_8UC3, new Scalar(0, 128, 255));
        var input = BiSeNetHeadSegmentationService.PrepareInput(bgr);
        Assert.Equal(3 * 512 * 512, input.Length);
        Assert.Equal((1f - .485f) / .229f, input[0], 5);
        Assert.Equal((128 / 255f - .456f) / .224f, input[512 * 512], 5);
        Assert.Equal(-.406f / .225f, input[2 * 512 * 512 + 1], 5);
    }

    [Fact]
    public void DecodeHead_ExcludesBackgroundNeckClothesAndHat_ContainsFacialPartsAndHair()
    {
        var logits = new float[19 * 19];
        for (var c = 0; c < 19; c++) logits[c * 19 + c] = 10;
        Assert.Equal(new byte[] { 0,255,255,255,255,255,255,255,255,255,255,255,255,255,0,0,0,255,0 },
            BiSeNetHeadSegmentationService.DecodeHead(logits, 19));
        Assert.Throws<ArgumentException>(() => BiSeNetHeadSegmentationService.DecodeHead([], 0));
        Assert.Throws<ArgumentException>(() => BiSeNetHeadSegmentationService.DecodeHead(new float[18], 1));
    }

    [Fact]
    public void Materialize_PlacesNativeCropAndRejectsMismatchedDimensionsOrNonBinaryData()
    {
        var patch = new HeadMaskPatch(20, 30, 2, 2, 100, 100, [255, 0, 0, 255]);
        using var mask = HeadMaskEditor.Materialize(patch, new Size(100, 100));
        Assert.Equal(255, mask.At<byte>(30, 20)); Assert.Equal(255, mask.At<byte>(31, 21));
        Assert.Equal(2, Cv2.CountNonZero(mask));
        Assert.Throws<ArgumentException>(() => HeadMaskEditor.Materialize(patch, new Size(101, 100)));
        Assert.Throws<ArgumentException>(() => HeadMaskEditor.Materialize(patch with { Pixels = [1, 0, 0, 0] }, new Size(100, 100)));
        Assert.Throws<ArgumentException>(() => HeadMaskEditor.Materialize(patch with { X = 99 }, new Size(100, 100)));
    }

    [Fact]
    public void Edits_EraseRestoreInOrder_ClipRestoreToSelectedHead()
    {
        using var mask = new Mat(100, 100, MatType.CV_8UC1, Scalar.All(255));
        var erase = new MaskProtectionStroke(8, [[50, 50]]);
        HeadMaskEditor.Apply(mask, [erase, new(3, [[50, 50]], true)], new(40, 40, 20, 20));
        Assert.Equal(255, mask.At<byte>(50, 50)); Assert.Equal(0, mask.At<byte>(50, 56));
        HeadMaskEditor.Apply(mask, [new(6, [[50, 50]])], new(40, 40, 20, 20));
        Assert.Equal(0, mask.At<byte>(50, 50));
        mask.SetTo(Scalar.All(0));
        HeadMaskEditor.Apply(mask, [new(8, [[40, 40]], true)], new(40, 40, 20, 20));
        Assert.Equal(255, mask.At<byte>(40, 40)); Assert.Equal(0, mask.At<byte>(40, 39));
        Assert.Throws<OperationCanceledException>(() => HeadMaskEditor.Apply(mask, [erase], new(40, 40, 20, 20), new CancellationToken(true)));
    }

    [Fact]
    public void History_UndoRedoResetAndNewBranch_KeepIndependentSnapshots()
    {
        var history = new MaskEditHistory(); var points = new float[][] { [10, 20] };
        history.Undo(); history.Redo(); Assert.Empty(history.Snapshot());
        history.Add(new(3, points)); points[0][0] = 99;
        Assert.Equal(10, history.Snapshot()[0].Points[0][0]);
        history.Add(new(4, [[11, 21]], true)); history.Undo();
        Assert.Single(history.Snapshot()); Assert.True(history.CanRedo);
        history.Redo(); Assert.True(history.Snapshot()[1].Restore);
        history.Snapshot()[0].Points[0][0] = 88; Assert.Equal(10, history.Snapshot()[0].Points[0][0]);
        history.Undo(); history.Add(new(5, [[12, 22]])); Assert.False(history.CanRedo);
        history.Clear(); Assert.Empty(history.Snapshot()); Assert.False(history.CanUndo); Assert.False(history.CanRedo);
    }

    [Fact]
    public void Compose_SemanticMasksExcludeInteriorGarment_AndWarpSourceEditsAtNativeSize()
    {
        using var target = new Mat(240, 320, MatType.CV_8UC3, Scalar.All(50));
        using var source = new Mat(240, 320, MatType.CV_8UC3, Scalar.All(200));
        var targetPatch = new HeadMaskPatch(40, 30, 100, 130, 320, 240, Enumerable.Repeat((byte)255, 13000).ToArray());
        var sourcePatch = targetPatch with { X = 60, Y = 50, Pixels = targetPatch.Pixels.ToArray() };
        for (var y = 105; y < 130; y++) for (var x = 0; x < 100; x++) targetPatch.Pixels[y * 100 + x] = 0;
        var options = new HeadReplacementOptions(0, TargetMask: targetPatch, SourceMask: sourcePatch,
            SourceProtection: [new(8, [[110, 125]])]);
        using var result = HeadReplacementService.Compose(target, source, new(40, 30, 100, 130), new(60, 50, 100, 130), options);
        Assert.Equal(target.Size(), result.Composite.Size());
        Assert.Equal(new Vec3b(50,50,50), result.Composite.At<Vec3b>(140, 90)); // clothing INSIDE old ellipse
        Assert.Equal(new Vec3b(50,50,50), result.Composite.At<Vec3b>(105, 90)); // source edit translated -20,-20
        Assert.Equal(new Vec3b(200,200,200), result.Composite.At<Vec3b>(80, 90));
        Assert.Equal(new Vec3b(50,50,50), result.Composite.At<Vec3b>(25, 90));
        using var restored = HeadReplacementService.Compose(target, source, new(40, 30, 100, 130), new(60, 50, 100, 130),
            options with { SourceProtection = [new(8, [[110, 125]]), new(6, [[110, 125]], true)] });
        Assert.Equal(new Vec3b(200,200,200), restored.Composite.At<Vec3b>(105, 90));
        Assert.Equal(new Vec3b(50,50,50), target.At<Vec3b>(80, 90)); Assert.Equal(new Vec3b(200,200,200), source.At<Vec3b>(80, 90));
    }

    [Fact]
    public void SemanticExport_JsonRoundtripRetainsEditsAndNativePixels_WithRawEditedEvidence()
    {
        var folder = Path.Combine(Path.GetTempPath(), "GroupTake.Tests", Guid.NewGuid().ToString("N"));
        try
        {
            using var a = new Mat(240, 320, MatType.CV_8UC3, Scalar.All(50));
            using var b = new Mat(240, 320, MatType.CV_8UC3, Scalar.All(200));
            var patch = new HeadMaskPatch(40, 30, 100, 130, 320, 240, Enumerable.Repeat((byte)255, 13000).ToArray());
            var options = new HeadReplacementOptions(0, TargetMask: patch, SourceMask: patch,
                TargetProtection: [new(8, [[90, 90]]), new(3, [[90, 90]], true)]);
            var restoredOptions = JsonSerializer.Deserialize<HeadReplacementOptions>(JsonSerializer.Serialize(options))!;
            DebugExporter.SaveHeadCase(folder, a, b, new(40, 30, 100, 130), new(40, 30, 100, 130), restoredOptions);
            using var expected = HeadReplacementService.Compose(a, b, new(40, 30, 100, 130), new(40, 30, 100, 130), options);
            using var output = PhotoCodec.Load(Path.Combine(folder, "head-result.png"));
            Assert.Equal(a.Size(), output.Size()); Assert.Equal(0, Cv2.Norm(expected.Composite, output, NormTypes.INF));
            using var raw = Cv2.ImRead(Path.Combine(folder, "target-mask-raw.png"), ImreadModes.Grayscale);
            using var edited = Cv2.ImRead(Path.Combine(folder, "target-mask-edited.png"), ImreadModes.Grayscale);
            Assert.Equal(255, raw.At<byte>(90, 96)); Assert.Equal(0, edited.At<byte>(90, 96));
            Assert.Equal(255, edited.At<byte>(90, 90));
            Assert.True(File.Exists(Path.Combine(folder, "source-mask-edited.png")));
        }
        finally { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
    }
}
