using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media.Imaging;

namespace PhotoCuller.App.WinUI.Converters;

/// <summary>Converts a file-system thumbnail path to a BitmapImage.</summary>
public sealed class PathToImageConverter : IValueConverter
{
    public object? Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is not string path || string.IsNullOrEmpty(path)) return null;
        if (!File.Exists(path)) return null;

        try
        {
            var image = new BitmapImage();
            image.UriSource = new Uri(path);
            return image;
        }
        catch
        {
            return null;
        }
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotSupportedException();
}
