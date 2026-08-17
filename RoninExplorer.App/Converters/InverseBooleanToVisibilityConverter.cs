using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace RoninExplorer.App;

/// <summary>True → Collapsed, False → Visible — used where a plain BooleanToVisibilityConverter would show the wrong element (e.g. hiding a button when a bool is true instead of false).</summary>
public sealed class InverseBooleanToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is Visibility.Visible;
}
