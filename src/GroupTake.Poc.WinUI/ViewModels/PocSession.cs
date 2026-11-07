using GroupTake.Poc.Imaging.Models;
using GroupTake.Poc.Imaging.ImageProcessing;
using GroupTake.Poc.Imaging.Services;
using OpenCvSharp;

namespace GroupTake.Poc.WinUI.ViewModels;

/// <summary>Owns native images. The window serializes operations; edits invalidate the current composition.</summary>
public sealed class PocSession : IDisposable
{
    public Mat? Base { get; private set; }
    public Mat? Source { get; private set; }
    public string BasePath { get; private set; } = "";
    public string SourcePath { get; private set; } = "";
    public CompositionResult? Result { get; private set; }
    public FaceSelection? ComposedBaseFace { get; private set; }
    public FaceSelection? ComposedSourceFace { get; private set; }
    public HeadReplacementOptions? HeadOptions { get; private set; }
    public void SetPhoto(bool isBase, Mat photo, string path)
    {
        Invalidate();
        if (isBase) { Base?.Dispose(); Base = photo; BasePath = path; }
        else { Source?.Dispose(); Source = photo; SourcePath = path; }
    }
    public void SetResult(CompositionResult result, FaceSelection a, FaceSelection b, HeadReplacementOptions? headOptions = null)
    { Invalidate(); Result = result; ComposedBaseFace = a; ComposedSourceFace = b; HeadOptions = headOptions; }
    public void Invalidate() { Result?.Dispose(); Result = null; ComposedBaseFace = ComposedSourceFace = null; HeadOptions = null; }
    public void Dispose() { Invalidate(); Base?.Dispose(); Source?.Dispose(); }
}
