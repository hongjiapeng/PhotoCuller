using CommunityToolkit.Mvvm.ComponentModel;
using PhotoCuller.Core.Enums;
using PhotoCuller.Core.Models;

namespace PhotoCuller.App.WinUI.ViewModels;

/// <summary>View-model wrapper around a PhotoItem for UI binding.</summary>
public sealed partial class PhotoItemViewModel : ObservableObject
{
    public PhotoItem Model { get; }

    public PhotoItemViewModel(PhotoItem model) => Model = model;

    public Guid   Id             => Model.Id;
    public string FileName       => Model.FileName;
    public string FilePath       => Model.FilePath;
    public long   FileSize       => Model.FileSize;
    public int    Width          => Model.Width;
    public int    Height         => Model.Height;
    public string Resolution     => Width > 0 && Height > 0 ? $"{Width} × {Height}" : "—";
    public string? ThumbnailPath => Model.ThumbnailPath;
    public float? OverallScore   => Model.OverallScore;
    public float? SharpnessScore => Model.SharpnessScore;
    public float? ExposureScore  => Model.ExposureScore;
    public float? ContrastScore  => Model.ContrastScore;
    public float? NoiseScore     => Model.NoiseScore;
    public float? ResolutionScore => Model.ResolutionScore;
    public float? CompositionScore => Model.CompositionScore;
    public float? ColorScore     => Model.ColorScore;

    public string? ExifTaken =>
        Model.ExifTakenTime?.ToString("yyyy-MM-dd HH:mm:ss")
        ?? Model.CreatedTime?.ToString("yyyy-MM-dd HH:mm:ss");

    public string? ModifiedTime =>
        Model.ModifiedTime?.ToString("yyyy-MM-dd HH:mm:ss");

    public RecommendationStatus Status => Model.RecommendationStatus;
    public bool IsKeep => Status is RecommendationStatus.Keep or RecommendationStatus.ManualKeep;
    public bool IsMovedToTrash => Model.IsMovedToTrash;
    public string? ErrorMessage => Model.ErrorMessage;

    public void SetStatus(RecommendationStatus status)
    {
        if (Model.RecommendationStatus == status)
            return;

        Model.RecommendationStatus = status;
        NotifyStatusChanged();
    }

    public void NotifyStatusChanged()
    {
        OnPropertyChanged(nameof(Status));
        OnPropertyChanged(nameof(IsKeep));
        OnPropertyChanged(nameof(IsMovedToTrash));
    }

    /// <summary>Score badge label for the group row (e.g. "92" or "—").</summary>
    public string ScoreLabel =>
        Model.OverallScore.HasValue
        ? ((int)Math.Round(Model.OverallScore.Value)).ToString()
        : "—";
}
