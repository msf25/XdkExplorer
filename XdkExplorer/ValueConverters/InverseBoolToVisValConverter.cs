using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace XdkExplorer.ValueConverters;

/// <summary>Collapsed for true, visible otherwise.</summary>
public sealed class InverseBoolToVisValConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is true ? Visibility.Collapsed : Visibility.Visible;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
