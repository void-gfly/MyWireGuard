using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace MyWireGuard.App.Converters;

[ValueConversion(typeof(int), typeof(bool))]
public sealed class IntEqualsBoolConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is int intValue
            && parameter is string paramString
            && int.TryParse(paramString, out int paramInt))
        {
            return intValue == paramInt;
        }

        return false;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is true
            && parameter is string paramString
            && int.TryParse(paramString, out int paramInt))
        {
            return paramInt;
        }

        return Binding.DoNothing;
    }
}
