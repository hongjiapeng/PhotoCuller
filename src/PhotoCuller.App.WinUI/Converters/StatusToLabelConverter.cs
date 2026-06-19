using Microsoft.UI.Xaml.Data;
using PhotoCuller.Core.Enums;

namespace PhotoCuller.App.WinUI.Converters;

public sealed class StatusToLabelConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
        => value is RecommendationStatus s ? s switch
        {
            RecommendationStatus.Keep or RecommendationStatus.ManualKeep => "Keep",
            RecommendationStatus.Review        => "Similar",
            RecommendationStatus.ToDelete
                or RecommendationStatus.ManualDelete => "Delete",
            _ => "—",
        } : "—";

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotSupportedException();
}
