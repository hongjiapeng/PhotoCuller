using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using PhotoCuller.App.WinUI.ViewModels;
using PhotoCuller.Core.Enums;
using PhotoCuller.Core.Interfaces.Platform;

namespace PhotoCuller.App.WinUI.Views;

public sealed partial class PhotoDetailPanel : UserControl
{
    public static readonly DependencyProperty SelectedPhotoProperty =
        DependencyProperty.Register(nameof(SelectedPhoto), typeof(PhotoItemViewModel),
            typeof(PhotoDetailPanel),
            new PropertyMetadata(null, OnSelectedPhotoChanged));

    private IOpenFileLocationService? _openLocation;

    public PhotoDetailPanel()
    {
        InitializeComponent();
    }

    public PhotoItemViewModel? SelectedPhoto
    {
        get => (PhotoItemViewModel?)GetValue(SelectedPhotoProperty);
        set => SetValue(SelectedPhotoProperty, value);
    }

    public bool HasPhoto => SelectedPhoto is not null;

    private static void OnSelectedPhotoChanged(
        DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is PhotoDetailPanel panel)
            panel.RefreshUI(e.NewValue as PhotoItemViewModel);
    }

    private void RefreshUI(PhotoItemViewModel? vm)
    {
        if (vm is null) return;

        // Preview image
        if (!string.IsNullOrEmpty(vm.ThumbnailPath) && File.Exists(vm.ThumbnailPath))
        {
            var bmp = new BitmapImage(new Uri(vm.ThumbnailPath));
            PreviewImage.Source = bmp;
        }
        else
        {
            PreviewImage.Source = null;
        }

        FileNameLabel.Text = vm.FileName;

        // Scores
        OverallScoreLabel.Text = vm.OverallScore.HasValue
            ? ((int)Math.Round(vm.OverallScore.Value)).ToString()
            : "—";

        RowSharpness.Value   = vm.SharpnessScore;
        RowExposure.Value    = vm.ExposureScore;
        RowContrast.Value    = vm.ContrastScore;
        RowNoise.Value       = vm.NoiseScore;
        RowResolution.Value  = vm.ResolutionScore;
        RowComposition.Value = vm.CompositionScore;
        RowColor.Value       = vm.ColorScore;

        // File info
        InfoFileName.Text   = vm.FileName;
        InfoSize.Text       = FormatSize(vm.FileSize);
        InfoResolution.Text = vm.Resolution;
        InfoTaken.Text      = vm.ExifTaken ?? "—";
        InfoModified.Text   = vm.ModifiedTime ?? "—";
        InfoPath.Text       = vm.FilePath;

        // Recommendation reason (simple algorithm-based text)
        if (vm.IsKeep && vm.OverallScore.HasValue)
        {
            ReasonText.Text = BuildRecommendationReason(vm);
            ReasonText.Visibility  = Visibility.Visible;
            ReasonHeader.Visibility = Visibility.Visible;
        }
        else
        {
            ReasonText.Visibility  = Visibility.Collapsed;
            ReasonHeader.Visibility = Visibility.Collapsed;
        }
    }

    private static string BuildRecommendationReason(PhotoItemViewModel vm)
    {
        var reasons = new List<string>();

        if (vm.SharpnessScore >= 80) reasons.Add("清晰度高");
        if (vm.ExposureScore >= 80) reasons.Add("曝光正常");
        if (vm.ContrastScore >= 75) reasons.Add("对比度好");
        if (vm.ResolutionScore >= 85) reasons.Add("分辨率高");
        if (vm.CompositionScore >= 75) reasons.Add("构图较好");

        return reasons.Count > 0
            ? $"这张照片在{string.Join("、", reasons)}方面优于同组其他照片，建议保留。"
            : "综合评分最高，建议保留。";
    }

    private static string FormatSize(long bytes) => bytes switch
    {
        >= 1_073_741_824 => $"{bytes / 1_073_741_824.0:F2} GB",
        >= 1_048_576     => $"{bytes / 1_048_576.0:F2} MB",
        >= 1_024         => $"{bytes / 1_024.0:F0} KB",
        _                => $"{bytes} B",
    };

    private void BtnOpenLocation_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedPhoto is null) return;
        _openLocation ??= App.Services.GetService(typeof(IOpenFileLocationService))
            as IOpenFileLocationService;
        _openLocation?.OpenInExplorer(SelectedPhoto.FilePath);
    }

    private void BtnFavorite_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedPhoto is null) return;
        SelectedPhoto.Model.RecommendationStatus = RecommendationStatus.ManualKeep;
    }

    private void BtnMarkDelete_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedPhoto is null) return;
        SelectedPhoto.Model.RecommendationStatus = RecommendationStatus.ToDelete;
    }
}
