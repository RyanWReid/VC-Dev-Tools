using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace VCDevTool.Client.Converters
{
    public class StatusToColorConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is string status)
            {
                // Convert status text to uppercase for comparison
                status = status.ToUpperInvariant();
                
                switch (status)
                {
                    case "AVAILABLE":
                        return new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#069E2D")); // Green
                    case "OFFLINE":
                        return new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#FF5252")); // Red
                    case "RUNNING":
                        return new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#FFA500")); // Orange
                    default:
                        return new SolidColorBrush(Colors.White); // Default color
                }
            }
            
            // If not a string or null, return default color
            return new SolidColorBrush(Colors.White);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
} 