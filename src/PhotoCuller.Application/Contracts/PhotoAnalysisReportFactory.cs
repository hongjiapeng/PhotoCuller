using PhotoCuller.Core.Models;

namespace PhotoCuller.Application.Contracts;

internal static class PhotoAnalysisReportFactory
{
    public static PhotoAnalysisReport Create(
        string sourceDirectory,
        DateTimeOffset startedAtUtc,
        DateTimeOffset completedAtUtc,
        IReadOnlyList<PhotoItem> photos,
        IReadOnlyList<PhotoGroup> groups)
        => new(
            PhotoAnalysisReport.CurrentVersion,
            sourceDirectory,
            startedAtUtc,
            completedAtUtc,
            photos.Select(ToResult).ToArray(),
            groups.Select(ToResult).ToArray());

    private static AnalyzedPhoto ToResult(PhotoItem photo)
        => new(
            photo.Id,
            photo.FilePath,
            photo.FileName,
            photo.FileExtension,
            photo.FileSize,
            photo.Width,
            photo.Height,
            ToOffset(photo.ExifTakenTime ?? photo.CreatedTime),
            new PhotoQuality(
                photo.OverallScore,
                photo.SharpnessScore,
                photo.ExposureScore,
                photo.ContrastScore,
                photo.NoiseScore,
                photo.ResolutionScore,
                photo.CompositionScore,
                photo.ColorScore,
                photo.AestheticScore),
            photo.RecommendationStatus,
            photo.GroupId,
            photo.ErrorMessage);

    private static AnalyzedPhotoGroup ToResult(PhotoGroup group)
        => new(
            group.Id,
            group.GroupType,
            group.SimilarityScore,
            group.RecommendedPhotoId,
            group.Photos.Select(photo => photo.Id).ToArray());

    private static DateTimeOffset? ToOffset(DateTime? value)
        => value switch
        {
            null => null,
            { Kind: DateTimeKind.Utc } utc => new DateTimeOffset(utc),
            { Kind: DateTimeKind.Local } local => new DateTimeOffset(local).ToUniversalTime(),
            { } unspecified => new DateTimeOffset(
                DateTime.SpecifyKind(unspecified, DateTimeKind.Utc)),
        };
}
