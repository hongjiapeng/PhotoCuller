using Microsoft.Extensions.Logging;
using PhotoCuller.Core.Interfaces.Scoring;
using PhotoCuller.Core.Models;
using PhotoCuller.Core.Scoring;
using PhotoCuller.Infrastructure.ImageProcessing.Scorers;

namespace PhotoCuller.Infrastructure.ImageProcessing;

/// <summary>
/// Coordinates all scoring dimensions and computes the weighted overall score.
/// Failed dimensions are recorded as null but do not block other dimensions.
/// Parallel scoring via Task.WhenAll for throughput on multi-core machines.
/// </summary>
public sealed class PhotoScoringService : IPhotoScoringService
{
    private readonly ISharpnessAnalyzer _sharpness;
    private readonly IExposureAnalyzer _exposure;
    private readonly IContrastAnalyzer _contrast;
    private readonly INoiseAnalyzer _noise;
    private readonly ICompositionAnalyzer _composition;
    private readonly IColorAnalyzer _color;
    private readonly ResolutionAnalyzer _resolution;
    private readonly ILogger<PhotoScoringService> _logger;

    public PhotoScoringService(
        ISharpnessAnalyzer sharpness,
        IExposureAnalyzer exposure,
        IContrastAnalyzer contrast,
        INoiseAnalyzer noise,
        ICompositionAnalyzer composition,
        IColorAnalyzer color,
        ResolutionAnalyzer resolution,
        ILogger<PhotoScoringService> logger)
    {
        _sharpness   = sharpness;
        _exposure    = exposure;
        _contrast    = contrast;
        _noise       = noise;
        _composition = composition;
        _color       = color;
        _resolution  = resolution;
        _logger      = logger;
    }

    public async Task ScoreAsync(
        PhotoItem photo,
        ScoringWeights weights,
        CancellationToken ct = default)
    {
        // Run all dimension scorers in parallel
        var (sharpness, exposure, contrast, noise, resolution, composition, color) =
            await RunAllAsync(photo, ct);

        photo.SharpnessScore   = sharpness;
        photo.ExposureScore    = exposure;
        photo.ContrastScore    = contrast;
        photo.NoiseScore       = noise;
        photo.ResolutionScore  = resolution;
        photo.CompositionScore = composition;
        photo.ColorScore       = color;

        photo.OverallScore = ComputeOverall(photo, weights);

        _logger.LogDebug(
            "Scored {File}: overall={Score:F1} " +
            "(sharp={S:F0} exp={E:F0} con={C:F0} noise={N:F0} res={R:F0} comp={Co:F0} col={Cl:F0})",
            photo.FileName,
            photo.OverallScore,
            sharpness, exposure, contrast, noise, resolution, composition, color);
    }

    public async Task ScoreRangeAsync(
        IReadOnlyList<PhotoItem> photos,
        ScoringWeights weights,
        IProgress<int>? progress = null,
        CancellationToken ct = default)
    {
        int done = 0;
        // Bounded parallelism: limit to (CPU - 1) concurrent scorers
        var semaphore = new SemaphoreSlim(Math.Max(1, Environment.ProcessorCount - 1));

        var tasks = photos.Select(async photo =>
        {
            await semaphore.WaitAsync(ct);
            try
            {
                await ScoreAsync(photo, weights, ct);
                progress?.Report(Interlocked.Increment(ref done));
            }
            finally
            {
                semaphore.Release();
            }
        });

        await Task.WhenAll(tasks);
    }

    // ── Internals ────────────────────────────────────────────────────────────

    private async Task<(float? sharpness, float? exposure, float? contrast,
                         float? noise, float? resolution, float? composition,
                         float? color)>
        RunAllAsync(PhotoItem photo, CancellationToken ct)
    {
        var tSharpness   = _sharpness.AnalyzeAsync(photo.FilePath, ct);
        var tExposure    = _exposure.AnalyzeAsync(photo.FilePath, ct);
        var tContrast    = _contrast.AnalyzeAsync(photo.FilePath, ct);
        var tNoise       = _noise.AnalyzeAsync(photo.FilePath, ct);
        var tResolution  = _resolution.AnalyzeAsync(photo, ct);
        var tComposition = _composition.AnalyzeAsync(photo.FilePath, ct);
        var tColor       = _color.AnalyzeAsync(photo.FilePath, ct);

        await Task.WhenAll(tSharpness, tExposure, tContrast,
                           tNoise, tResolution, tComposition, tColor);

        return (await tSharpness, await tExposure, await tContrast,
                await tNoise, await tResolution, await tComposition, await tColor);
    }

    private static float ComputeOverall(PhotoItem photo, ScoringWeights weights)
    {
        // If weights don't sum to 100, skip overall score
        if (!weights.IsValid()) return 0f;

        double score = 0;
        double usedWeight = 0;

        void Add(float? value, int weightPercent)
        {
            if (value is null || weightPercent == 0) return;
            score += value.Value * weightPercent;
            usedWeight += weightPercent;
        }

        Add(photo.SharpnessScore,   weights.SharpnessPercent);
        Add(photo.ExposureScore,    weights.ExposurePercent);
        Add(photo.ContrastScore,    weights.ContrastPercent);
        Add(photo.NoiseScore,       weights.NoisePercent);
        Add(photo.ResolutionScore,  weights.ResolutionPercent);
        Add(photo.CompositionScore, weights.CompositionPercent);
        Add(photo.ColorScore,       weights.ColorPercent);

        if (usedWeight == 0) return 0f;

        // Re-normalise if some dimensions returned null
        return (float)Math.Clamp(score / usedWeight, 0, 100);
    }
}
