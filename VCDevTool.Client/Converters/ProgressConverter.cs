using System;
using System.Globalization;
using System.Windows.Data;

namespace VCDevTool.Client.Converters
{
    public class ProgressConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            // For a progress bar, we need to return a percentage value that can be used for the Width
            
            double progress = 0;
            
            if (value is double doubleValue)
            {
                progress = doubleValue;
            }
            else if (value is string stringValue && double.TryParse(stringValue.Replace("%", ""), out double parsedValue))
            {
                progress = parsedValue / 100;
            }
            
            // Ensure the value is in the range [0,1]
            progress = Math.Max(0, Math.Min(1, progress));
            
            // Return a percentage string which will be bound to the Width property
            // without showing the percentage text
            return $"{progress * 100}%";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
} 