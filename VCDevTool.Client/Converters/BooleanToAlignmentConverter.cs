using System;
using System.Globalization;
using System.Windows.Data;

namespace VCDevTool.Client.Converters
{
    public class BooleanToAlignmentConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool isActive)
            {
                return isActive ? System.Windows.HorizontalAlignment.Right : System.Windows.HorizontalAlignment.Left;
            }
            
            return System.Windows.HorizontalAlignment.Left;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
} 