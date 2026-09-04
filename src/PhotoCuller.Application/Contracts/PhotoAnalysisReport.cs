using PhotoCuller.Core.Enums;

namespace PhotoCuller.Application.Contracts;

/// <summary>Versioned, platform-neutral result consumed by CLI, Skill and Agent clients.</summary>
public sealed record PhotoAnalysisReport(
    string Version,
    string SourceDirectory,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset CompletedAtUtc,
    IReadOnlyList<AnalyzedPhoto> Photos,
    IReadOnlyList<AnalyzedPhotoGroup> Groups)
{
    public const string CurrentVersion = "1.0";
}

public sealed record AnalyzedPhoto(
    Guid Id,
    string Path,
    string FileName,
    string Extension,
    long FileSizeBytes,
    int Width,
    int Height,
    DateTimeOffset? TakenAt,
    PhotoQuality Quality,
    RecommendationStatus Recommendation,
    Guid? GroupId,
    string? Error);

public sealed record PhotoQuality(
    float? Overall,
    float? Sharpness,
    float? Exposure,
    float? Contrast,
    float? Noise,
    float? Resolution,
    float? Composition,
    float? Color,
    float? Aesthetic);

public sealed record AnalyzedPhotoGroup(
    Guid Id,
    GroupType Type,
    float Similarity,
    Guid? RecommendedPhotoId,
    IReadOnlyList<Guid> PhotoIds);
