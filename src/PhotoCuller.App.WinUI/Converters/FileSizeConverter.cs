using Microsoft.UI.Xaml.Data;

namespace PhotoCuller.App.WinUI.Converters;

/// <summary>Formats bytes as "2.34 MB", "450 KB", etc.</summary>
public sealed class FileSizeConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is not long bytes) return "—";
        return bytes switch
        {
            >= 1_073_741_824 => $"{bytes / 1_073_741_824.0:F2} GB",
            >= 1_048_576     => $"{bytes / 1_048_576.0:F2} MB",
            >= 1_024         => $"{bytes / 1_024.0:F0} KB",
            _                => $"{bytes} B",
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotSupportedException();
}
