using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace VCDevTool.Client.Converters
{
    public class BooleanToMarginConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool isActive)
            {
                string margins = parameter as string ?? "4,0,0,0,44,0,0,0";
                string[] marginValues = margins.Split(',');
                
                if (isActive && marginValues.Length >= 8)
                {
                    // Return the active margin (right aligned)
                    return new Thickness(
                        double.Parse(marginValues[4]),
                        double.Parse(marginValues[5]),
                        double.Parse(marginValues[6]),
                        double.Parse(marginValues[7])
                    );
                }
                else
                {
                    // Return the inactive margin (left aligned)
                    return new Thickness(
                        double.Parse(marginValues[0]),
                        double.Parse(marginValues[1]),
                        double.Parse(marginValues[2]),
                        double.Parse(marginValues[3])
                    );
                }
            }
            
            return new Thickness(4, 0, 0, 0);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
} 