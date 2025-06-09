using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace VCDevTool.Client.Converters
{
    public class EnumEqualityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value == null || parameter == null)
                return Visibility.Collapsed;

            // Try to directly compare the enum value to the parameter string
            if (value.ToString().Equals(parameter.ToString(), StringComparison.OrdinalIgnoreCase))
                return Visibility.Visible;

            // Also try to parse the parameter as an enum value of the same type
            if (Enum.TryParse(value.GetType(), parameter.ToString(), true, out object enumValue))
            {
                return value.Equals(enumValue) ? Visibility.Visible : Visibility.Collapsed;
            }

            return Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
} 