using Microsoft.UI;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;
using PhotoCuller.Core.Enums;
using Windows.UI;

namespace PhotoCuller.App.WinUI.Converters;

/// <summary>
/// Maps RecommendationStatus to a badge background colour.
/// Keep → green, Review → amber, ToDelete/ManualDelete → red, else → transparent.
/// </summary>
public sealed class StatusToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is not RecommendationStatus status)
            return new SolidColorBrush(Colors.Transparent);

        return status switch
        {
            RecommendationStatus.Keep or RecommendationStatus.ManualKeep
                => new SolidColorBrush(Color.FromArgb(255, 34, 197, 94)),   // green-500
            RecommendationStatus.Review
                => new SolidColorBrush(Color.FromArgb(255, 234, 179, 8)),   // yellow-500
            RecommendationStatus.ToDelete or RecommendationStatus.ManualDelete
                => new SolidColorBrush(Color.FromArgb(255, 239, 68, 68)),   // red-500
            _ => new SolidColorBrush(Color.FromArgb(180, 100, 116, 139)),   // slate-500
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotSupportedException();
}
