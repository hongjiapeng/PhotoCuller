using Microsoft.Extensions.Logging;
using PhotoCuller.Core.Enums;
using PhotoCuller.Core.Interfaces.Ai;
using PhotoCuller.Core.Interfaces.Scoring;
using PhotoCuller.Core.Interfaces.Storage;
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
    private readonly ILocalAiModelProvider _localAi;
    private readonly IAiModelProfileRepository _modelProfiles;
    private readonly ISettingsRepository _settings;
    private readonly ILogger<PhotoScoringService> _logger;

    public PhotoScoringService(
        ISharpnessAnalyzer sharpness,
        IExposureAnalyzer exposure,
        IContrastAnalyzer contrast,
        INoiseAnalyzer noise,
        ICompositionAnalyzer composition,
        IColorAnalyzer color,
        ResolutionAnalyzer resolution,
        ILocalAiModelProvider localAi,
        IAiModelProfileRepository modelProfiles,
        ISettingsRepository settings,
        ILogger<PhotoScoringService> logger)
    {
        _sharpness   = sharpness;
        _exposure    = exposure;
        _contrast    = contrast;
        _noise       = noise;
        _composition = composition;
        _color       = color;
        _resolution  = resolution;
        _localAi     = localAi;
        _modelProfiles = modelProfiles;
        _settings    = settings;
        _logger      = logger;
    }

    public async Task ScoreAsync(
        PhotoItem photo,
        ScoringWeights weights,
        CancellationToken ct = default)
    {
        var aiContext = await ResolveAiContextAsync(ct);
        await ScoreAsync(photo, weights, aiContext, ct);
    }

    private async Task ScoreAsync(
        PhotoItem photo,
        ScoringWeights weights,
        AiScoringContext? aiContext,
        CancellationToken ct)
    {
        // Run all dimension scorers in parallel
        var (sharpness, exposure, contrast, noise, resolution, composition, color, aesthetic) =
            await RunAllAsync(photo, aiContext, ct);

        photo.SharpnessScore   = sharpness;
        photo.ExposureScore    = exposure;
        photo.ContrastScore    = contrast;
        photo.NoiseScore       = noise;
        photo.ResolutionScore  = resolution;
        photo.CompositionScore = composition;
        photo.ColorScore       = color;
        photo.AestheticScore   = aesthetic;
        photo.ScoringModel     = aesthetic.HasValue
            ? $"{aiContext!.Profile.Name} ({aiContext.Profile.LocalModelPreset})"
            : null;

        photo.OverallScore = ComputeOverall(
            photo,
            weights,
            aiContext?.BlendPercent ?? 0);

        _logger.LogDebug(
            "Scored {File}: overall={Score:F1} " +
            "(sharp={S:F0} exp={E:F0} con={C:F0} noise={N:F0} res={R:F0} comp={Co:F0} col={Cl:F0} aes={A:F0})",
            photo.FileName,
            photo.OverallScore,
            sharpness, exposure, contrast, noise, resolution, composition, color, aesthetic);
    }

    public async Task ScoreRangeAsync(
        IReadOnlyList<PhotoItem> photos,
        ScoringWeights weights,
        IProgress<int>? progress = null,
        CancellationToken ct = default)
    {
        int done = 0;
        var aiContext = await ResolveAiContextAsync(ct);

        // Bounded parallelism: limit to (CPU - 1) concurrent scorers
        var semaphore = new SemaphoreSlim(Math.Max(1, Environment.ProcessorCount - 1));

        var tasks = photos.Select(async photo =>
        {
            await semaphore.WaitAsync(ct);
            try
            {
                await ScoreAsync(photo, weights, aiContext, ct);
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
                         float? color, float? aesthetic)>
        RunAllAsync(
            PhotoItem photo,
            AiScoringContext? aiContext,
            CancellationToken ct)
    {
        var tSharpness   = _sharpness.AnalyzeAsync(photo.FilePath, ct);
        var tExposure    = _exposure.AnalyzeAsync(photo.FilePath, ct);
        var tContrast    = _contrast.AnalyzeAsync(photo.FilePath, ct);
        var tNoise       = _noise.AnalyzeAsync(photo.FilePath, ct);
        var tResolution  = _resolution.AnalyzeAsync(photo, ct);
        var tComposition = _composition.AnalyzeAsync(photo.FilePath, ct);
        var tColor       = _color.AnalyzeAsync(photo.FilePath, ct);
        var tAesthetic   = ScoreAestheticAsync(photo.FilePath, aiContext, ct);

        await Task.WhenAll(tSharpness, tExposure, tContrast,
                           tNoise, tResolution, tComposition, tColor, tAesthetic);

        return (await tSharpness, await tExposure, await tContrast,
                await tNoise, await tResolution, await tComposition, await tColor,
                await tAesthetic);
    }

    private async Task<float?> ScoreAestheticAsync(
        string filePath,
        AiScoringContext? context,
        CancellationToken ct)
    {
        if (context is null)
            return null;

        var normalized = await _localAi.ScoreAsync(context.Profile, filePath, ct);
        return normalized.HasValue
            ? Math.Clamp(normalized.Value * 100f, 0f, 100f)
            : null;
    }

    private async Task<AiScoringContext?> ResolveAiContextAsync(CancellationToken ct)
    {
        try
        {
            var settings = await _settings.LoadAsync(ct);
            if (!settings.EnableLocalAi || settings.AestheticBlendPercent <= 0)
                return null;

            var profiles = await _modelProfiles.GetAllAsync(ct);
            var profile = settings.SelectedModelProfileId.HasValue
                ? profiles.FirstOrDefault(p => p.Id == settings.SelectedModelProfileId.Value)
                : null;

            profile ??= profiles.FirstOrDefault(p =>
                p.IsEnabled
                && p.ProviderType == ProviderType.Local
                && p.ModelType == ModelType.Aesthetic);

            if (profile is null
                || !profile.IsEnabled
                || profile.ProviderType != ProviderType.Local
                || profile.ModelType != ModelType.Aesthetic
                || string.IsNullOrWhiteSpace(profile.LocalModelPath))
            {
                _logger.LogInformation(
                    "Local AI scoring is enabled, but no enabled local aesthetic model is configured.");
                return null;
            }

            if (!await _localAi.TestConnectionAsync(profile, ct))
                return null;

            return new AiScoringContext(
                profile,
                Math.Clamp(settings.AestheticBlendPercent, 0, 100));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Local aesthetic scoring is unavailable; using technical scores only.");
            return null;
        }
    }

    internal static float ComputeOverall(
        PhotoItem photo,
        ScoringWeights weights,
        int aestheticBlendPercent = 0)
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

        // Re-normalise if some dimensions returned null.
        var technicalScore = (float)Math.Clamp(score / usedWeight, 0, 100);

        if (!photo.AestheticScore.HasValue || aestheticBlendPercent <= 0)
            return technicalScore;

        var blend = Math.Clamp(aestheticBlendPercent, 0, 100) / 100f;
        return Math.Clamp(
            technicalScore * (1f - blend) + photo.AestheticScore.Value * blend,
            0f,
            100f);
    }

    private sealed record AiScoringContext(
        AiModelProfile Profile,
        int BlendPercent);
}
