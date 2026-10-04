using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace XdkExplorer.ValueConverters;

/// <summary>Visible for null or an empty string, used for placeholder text.</summary>
public sealed class EmptyToVisValConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return string.IsNullOrEmpty(value as string) ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
