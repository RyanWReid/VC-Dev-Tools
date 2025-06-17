using System;
using System.Globalization;
using System.Windows.Data;

namespace VCDevTool.Client.Converters
{
    public class BooleanToStringConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            bool boolValue = (bool)value;
            
            if (parameter != null)
            {
                string paramStr = parameter.ToString() ?? "";
                string[] values = paramStr.Split(',');
                return boolValue ? values[0] : values.Length > 1 ? values[1] : string.Empty;
            }
            
            return boolValue.ToString();
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
} 