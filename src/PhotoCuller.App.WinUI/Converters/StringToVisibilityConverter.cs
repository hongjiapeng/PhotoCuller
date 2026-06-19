using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;

namespace PhotoCuller.App.WinUI.Converters;

/// <summary>Returns Visible when the string is non-null/non-empty, Collapsed otherwise.</summary>
public sealed class StringToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        bool hasValue = value is string s && !string.IsNullOrEmpty(s);
        bool invert   = parameter is string p && p == "Invert";
        return (hasValue ^ invert) ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotSupportedException();
}
