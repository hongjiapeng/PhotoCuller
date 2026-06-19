using Microsoft.UI.Xaml.Data;

namespace PhotoCuller.App.WinUI.Converters;

/// <summary>Formats a nullable float score as "92" or "—".</summary>
public sealed class ScoreConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
        => value is float f ? ((int)Math.Round(f)).ToString() : "—";

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotSupportedException();
}
