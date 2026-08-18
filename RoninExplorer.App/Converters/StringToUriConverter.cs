using System.Globalization;
using System.Windows.Data;

namespace RoninExplorer.App;

/// <summary>MediaElement.Source is a Uri, not a string — trivial adapter for binding it directly to a file path.</summary>
public sealed class StringToUriConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is string path && !string.IsNullOrWhiteSpace(path) ? new Uri(path, UriKind.Absolute) : null;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
