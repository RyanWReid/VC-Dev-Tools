using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace VCDevTool.Client.Converters
{
    public class BooleanToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            bool visible = false; 
        
            
            // Handle NonZero parameter for integer values
            if (parameter is string paramStr && paramStr == "NonZero" && value is int intValue)
            {
                visible = intValue > 0; 
            }
            else if (value is bool boolValue)
            {
                visible = boolValue;
                
                // Inverse the result if parameter is "inverse"
                if (parameter is string parameterString && parameterString.ToLower() == "inverse")
                {
                    visible = !visible;
                }
            }
            
            return visible ? Visibility.Visible : Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
} 