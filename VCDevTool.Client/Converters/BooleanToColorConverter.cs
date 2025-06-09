using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace VCDevTool.Client.Converters
{
    public class BooleanToColorConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool isActive)
            {
                string colors = parameter as string ?? "#8B0000,#2E8B57";
                string[] colorValues = colors.Split(',');
                
                if (isActive && colorValues.Length > 1)
                {
                    // Return the active color (green)
                    return (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(colorValues[1]);
                }
                else
                {
                    // Return the inactive color (red)
                    return (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(colorValues[0]);
                }
            }
            
            return Colors.Gray;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
} 