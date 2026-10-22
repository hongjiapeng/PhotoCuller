using CommunityToolkit.Mvvm.ComponentModel;
using PhotoCuller.Core.Enums;
using PhotoCuller.Core.Models;

namespace PhotoCuller.App.WinUI.ViewModels;

/// <summary>View-model wrapper around a PhotoItem for UI binding.</summary>
public sealed partial class PhotoItemViewModel : ObservableObject
{
    public PhotoItem Model { get; private set; }
    private bool _animateScanEntrance;

    public PhotoItemViewModel(PhotoItem model, bool animateScanEntrance = false)
    {
        Model = model;
        _animateScanEntrance = animateScanEntrance;
    }

    /// <summary>Replaces the preview with its completed analysis without replacing the card.</summary>
    public void UpdateAnalysis(PhotoItem model)
    {
        if (model.Id != Id)
            throw new ArgumentException("The completed photo must match the preview.", nameof(model));

        Model = model;
        _animateScanEntrance = false;
        OnPropertyChanged(string.Empty);
    }

    /// <summary>Virtualization and sorting must not replay scan entrance animations.</summary>
    public bool ConsumeScanEntrance()
    {
        var animate = _animateScanEntrance;
        _animateScanEntrance = false;
        return animate;
    }

    public Guid   Id             => Model.Id;
    public string FileName       => Model.FileName;
    public string FilePath       => Model.FilePath;
    public long   FileSize       => Model.FileSize;
    public string FileSizeLabel  => FormatSize(Model.FileSize);
    public string FileType       => Path.GetExtension(Model.FilePath).TrimStart('.').ToUpperInvariant() is { Length: > 0 } ext
        ? ext
        : "—";
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
    public float? AestheticScore => Model.AestheticScore;
    public string? ScoringModel  => Model.ScoringModel;

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

#pragma warning disable MVVMTK0045
    [ObservableProperty] private bool _isAlgorithmRecommended;
    [ObservableProperty] private bool _isRecommendedInSelectedGroup;
    [ObservableProperty] private bool _isCurrentComparePhoto;
    [ObservableProperty] private bool _isCurrentInspectPhoto;
    [ObservableProperty] private bool _isCurrentBrowseAlternative;
    [ObservableProperty] private bool _isLibrarySelected;
#pragma warning restore MVVMTK0045

    private static string FormatSize(long bytes) => bytes switch
    {
        >= 1_073_741_824 => $"{bytes / 1_073_741_824.0:F2} GB",
        >= 1_048_576     => $"{bytes / 1_048_576.0:F2} MB",
        >= 1_024         => $"{bytes / 1_024.0:F0} KB",
        _                => $"{bytes} B",
    };
}
