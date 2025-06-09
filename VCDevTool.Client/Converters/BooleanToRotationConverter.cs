using System;
using System.Globalization;
using System.Windows.Data;

namespace VCDevTool.Client
{
    public class BooleanToRotationConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool isExpanded)
            {
                return isExpanded ? 90 : 0; // 90 degrees when expanded, 0 when collapsed
            }
            
            return 0; // Default to 0 degrees (collapsed)
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
} 