using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using OpenCvSharp;
using PhotoCuller.Core.Interfaces.Ai;
using PhotoCuller.Core.Models;

namespace PhotoCuller.Infrastructure.AiProviders.LocalModels;

/// <summary>
/// Runs local aesthetic scoring models through ONNX Runtime.
///
/// Initial model contract:
/// - one float image tensor input, NCHW or NHWC
/// - RGB pixels normalized with CLIP mean/std
/// - one scalar float output per image
///
/// The profile preset controls conversion of the raw model output to 0-1.
/// </summary>
public sealed class OnnxAestheticModelProvider : ILocalAiModelProvider, IDisposable
{
    public const string PresetZeroToOne = "clip-aesthetic-0-1";
    public const string PresetMinusOneToOne = "clip-aesthetic-minus1-1";
    public const string PresetOneToTen = "clip-aesthetic-1-10";

    private static readonly float[] ClipMean = [0.48145466f, 0.4578275f, 0.40821073f];
    private static readonly float[] ClipStd = [0.26862954f, 0.26130258f, 0.27577711f];

    private readonly ConcurrentDictionary<string, Lazy<InferenceSession>> _sessions =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<Guid, SemaphoreSlim> _concurrencyLimits = new();
    private readonly ILogger<OnnxAestheticModelProvider> _logger;

    public OnnxAestheticModelProvider(ILogger<OnnxAestheticModelProvider> logger)
        => _logger = logger;

    public Task<bool> TestConnectionAsync(
        AiModelProfile profile,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        try
        {
            _ = GetSession(profile);
            return Task.FromResult(true);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Unable to load ONNX model {Model}", profile.LocalModelPath);
            return Task.FromResult(false);
        }
    }

    public Task LoadAsync(AiModelProfile profile, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        _ = GetSession(profile);
        return Task.CompletedTask;
    }

    public async Task<float?> ScoreAsync(
        AiModelProfile profile,
        string filePath,
        CancellationToken ct = default)
    {
        var gate = _concurrencyLimits.GetOrAdd(
            profile.Id,
            _ => new SemaphoreSlim(Math.Clamp(profile.MaxConcurrency, 1, 8)));

        await gate.WaitAsync(ct);
        try
        {
            return await Task.Run(() => Score(profile, filePath, ct), ct);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Aesthetic ONNX inference failed for {File} using {Model}",
                filePath,
                profile.LocalModelPath);
            return null;
        }
        finally
        {
            gate.Release();
        }
    }

    private float Score(
        AiModelProfile profile,
        string filePath,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var session = GetSession(profile);
        var (inputName, metadata) = GetImageInput(session);
        var shape = ResolveShape(metadata.Dimensions);

        using var image = Cv2.ImRead(filePath, ImreadModes.Color);
        if (image.Empty())
            throw new InvalidDataException($"Unable to decode image: {filePath}");

        using var prepared = ResizeAndCenterCrop(image, shape.Width, shape.Height);
        using var rgb = new Mat();
        Cv2.CvtColor(prepared, rgb, ColorConversionCodes.BGR2RGB);

        var tensor = CreateTensor(rgb, shape.Width, shape.Height, shape.IsNhwc);
        var inputs = new[] { NamedOnnxValue.CreateFromTensor(inputName, tensor) };

        ct.ThrowIfCancellationRequested();
        using var results = session.Run(inputs);
        var raw = results.First().AsEnumerable<float>().First();
        return NormalizeOutput(raw, profile.LocalModelPreset);
    }

    private InferenceSession GetSession(AiModelProfile profile)
    {
        var path = profile.LocalModelPath?.Trim();
        if (string.IsNullOrWhiteSpace(path))
            throw new InvalidOperationException("Local ONNX model path is empty.");
        if (!File.Exists(path))
            throw new FileNotFoundException("Local ONNX model was not found.", path);
        if (!string.Equals(Path.GetExtension(path), ".onnx", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Local aesthetic scoring currently supports .onnx files only.");

        var fullPath = Path.GetFullPath(path);
        return _sessions.GetOrAdd(
            fullPath,
            p => new Lazy<InferenceSession>(
                () => new InferenceSession(
                    p,
                    new SessionOptions
                    {
                        GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL,
                        InterOpNumThreads = 1,
                    }),
                LazyThreadSafetyMode.ExecutionAndPublication)).Value;
    }

    private static (string Name, NodeMetadata Metadata) GetImageInput(InferenceSession session)
    {
        foreach (var (name, metadata) in session.InputMetadata)
        {
            if (metadata.ElementType == typeof(float) && metadata.Dimensions.Length == 4)
                return (name, metadata);
        }

        throw new InvalidOperationException(
            "The ONNX model must contain a rank-4 float image input.");
    }

    private static (int Width, int Height, bool IsNhwc) ResolveShape(int[] dimensions)
    {
        var isNhwc = dimensions[3] == 3;
        var height = isNhwc ? dimensions[1] : dimensions[2];
        var width = isNhwc ? dimensions[2] : dimensions[3];

        // Dynamic image dimensions use the CLIP default.
        if (width <= 0) width = 224;
        if (height <= 0) height = 224;

        if (width > 2048 || height > 2048)
            throw new InvalidOperationException(
                $"Unsupported ONNX image input size: {width}x{height}.");

        return (width, height, isNhwc);
    }

    private static Mat ResizeAndCenterCrop(Mat source, int targetWidth, int targetHeight)
    {
        var scale = Math.Max(
            (double)targetWidth / source.Width,
            (double)targetHeight / source.Height);
        var resizedWidth = Math.Max(targetWidth, (int)Math.Round(source.Width * scale));
        var resizedHeight = Math.Max(targetHeight, (int)Math.Round(source.Height * scale));

        using var resized = new Mat();
        Cv2.Resize(
            source,
            resized,
            new Size(resizedWidth, resizedHeight),
            interpolation: scale < 1 ? InterpolationFlags.Area : InterpolationFlags.Cubic);

        var x = (resizedWidth - targetWidth) / 2;
        var y = (resizedHeight - targetHeight) / 2;
        return new Mat(resized, new Rect(x, y, targetWidth, targetHeight)).Clone();
    }

    private static DenseTensor<float> CreateTensor(
        Mat rgb,
        int width,
        int height,
        bool isNhwc)
    {
        var tensor = isNhwc
            ? new DenseTensor<float>([1, height, width, 3])
            : new DenseTensor<float>([1, 3, height, width]);

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var pixel = rgb.At<Vec3b>(y, x);
                for (var channel = 0; channel < 3; channel++)
                {
                    var normalized = (pixel[channel] / 255f - ClipMean[channel]) / ClipStd[channel];
                    if (isNhwc)
                        tensor[0, y, x, channel] = normalized;
                    else
                        tensor[0, channel, y, x] = normalized;
                }
            }
        }

        return tensor;
    }

    internal static float NormalizeOutput(float raw, string? preset) =>
        preset?.Trim().ToLowerInvariant() switch
        {
            PresetMinusOneToOne => Math.Clamp((raw + 1f) / 2f, 0f, 1f),
            PresetOneToTen => Math.Clamp((raw - 1f) / 9f, 0f, 1f),
            _ => Math.Clamp(raw, 0f, 1f),
        };

    public void Dispose()
    {
        foreach (var session in _sessions.Values)
        {
            if (session.IsValueCreated)
                session.Value.Dispose();
        }

        foreach (var gate in _concurrencyLimits.Values)
            gate.Dispose();
    }
}
