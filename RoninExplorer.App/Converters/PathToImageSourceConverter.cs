using System.Globalization;
using System.IO;
using System.Windows.Data;
using System.Windows.Media.Imaging;

namespace RoninExplorer.App;

/// <summary>Decodes an image file path into a preview-sized BitmapImage for the Details panel — DecodePixelWidth caps memory/decode cost since these are just previews, not full-resolution loads.</summary>
public sealed class PathToImageSourceConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string path || string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return null;

        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.UriSource = new Uri(path, UriKind.Absolute);
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.DecodePixelWidth = 400;
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch
        {
            return null; // corrupt/unreadable/in-use image — show nothing rather than crash the binding
        }
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
