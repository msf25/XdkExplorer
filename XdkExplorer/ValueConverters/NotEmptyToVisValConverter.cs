using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace XdkExplorer.ValueConverters;

/// <summary>Visible for a non-empty string or any other non-null value, collapsed for null or an empty string.</summary>
public sealed class NotEmptyToVisValConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value == null || value is string { Length: 0 } ? Visibility.Collapsed : Visibility.Visible;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
