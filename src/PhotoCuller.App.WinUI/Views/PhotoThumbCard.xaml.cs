using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using PhotoCuller.App.WinUI.ViewModels;
using PhotoCuller.Core.Enums;
using Windows.UI;

namespace PhotoCuller.App.WinUI.Views;

public sealed partial class PhotoThumbCard : UserControl
{
    public static readonly DependencyProperty PhotoProperty =
        DependencyProperty.Register(nameof(Photo), typeof(PhotoItemViewModel),
            typeof(PhotoThumbCard),
            new PropertyMetadata(null, OnPhotoChanged));

    public PhotoItemViewModel? Photo
    {
        get => (PhotoItemViewModel?)GetValue(PhotoProperty);
        set => SetValue(PhotoProperty, value);
    }

    public event EventHandler<PhotoItemViewModel>? PhotoSelected;

    public PhotoThumbCard() => InitializeComponent();

    private static void OnPhotoChanged(
        DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is PhotoThumbCard card)
            card.Refresh(e.NewValue as PhotoItemViewModel);
    }

    private void Refresh(PhotoItemViewModel? vm)
    {
        if (vm is null) return;

        // Thumbnail
        if (!string.IsNullOrEmpty(vm.ThumbnailPath) && File.Exists(vm.ThumbnailPath))
            ThumbImage.Source = new BitmapImage(new Uri(vm.ThumbnailPath));

        // Score
        ScoreText.Text = vm.ScoreLabel;

        // Status badge
        var (label, bg) = vm.Status switch
        {
            RecommendationStatus.Keep or RecommendationStatus.ManualKeep
                => ("Keep",    Color.FromArgb(200, 34,  197, 94)),
            RecommendationStatus.Review
                => ("Similar", Color.FromArgb(140, 100, 116, 139)),
            RecommendationStatus.ToDelete or RecommendationStatus.ManualDelete
                => ("Delete",  Color.FromArgb(200, 239, 68,  68)),
            _ => ("—", Color.FromArgb(120, 80, 80, 80)),
        };

        StatusText.Text = label;
        StatusBadge.Background = new SolidColorBrush(bg);

        // Green border for Keep
        KeepBorder.Visibility = vm.IsKeep ? Visibility.Visible : Visibility.Collapsed;
    }

    private void Root_Click(object sender, RoutedEventArgs e)
    {
        if (Photo is not null)
            PhotoSelected?.Invoke(this, Photo);
    }
}
