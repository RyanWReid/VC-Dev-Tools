using System;
using System.Globalization;
using System.Windows.Data;

namespace VCDevTool.Client.Converters
{
    public class StringContainsConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is string sourceString && parameter is string searchString)
            {
                return sourceString.Contains(searchString, StringComparison.OrdinalIgnoreCase);
            }
            
            return false;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
} 