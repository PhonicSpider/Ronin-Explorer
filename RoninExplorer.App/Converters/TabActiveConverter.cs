using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace RoninExplorer.App;

/// <summary>Two-value MultiBinding converter for tab-strip highlighting: values[0] is this tab, values[1] is MainViewModel.ActiveTab — returns the accent-tinted panel brush when they're the same instance, transparent otherwise. A plain DataTrigger can't compare two bindings against each other, hence the MultiBinding.</summary>
public sealed class TabActiveConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object? parameter, CultureInfo culture)
    {
        bool isActive = values.Length == 2 && ReferenceEquals(values[0], values[1]);
        if (!isActive) return Brushes.Transparent;

        // A low-opacity tint of the accent color reads clearly as "selected"
        // against either a light or dark surface (unlike PanelBackgroundBrush,
        // whose default is itself transparent until the user picks a skin).
        if (Application.Current.TryFindResource("AccentBrush") is SolidColorBrush accent)
        {
            var tint = new SolidColorBrush(accent.Color) { Opacity = 0.18 };
            tint.Freeze();
            return tint;
        }
        return Brushes.Transparent;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
