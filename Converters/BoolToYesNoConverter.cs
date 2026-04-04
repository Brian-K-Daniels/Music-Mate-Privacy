using System;
using System.Globalization;
using Microsoft.Maui.Controls;

namespace musicmate.Converters
{
    public class BoolToYesNoConverter : IValueConverter
    {
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is bool b)
                return b ? "Premium" : "Free";
            return "Free";
        }

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is string s)
                return string.Equals(s, "Premium", StringComparison.OrdinalIgnoreCase);
            return false;
        }
    }
}
