using System;
using System.Globalization;
using System.Windows.Data;

namespace VCDevTool.Client.Converters
{
    public class WidthPercentageConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            // Expect values[0] = progress fraction (0.0–1.0), values[1] = total width in pixels
            double progress = 0;
            double totalWidth = 0;

            if (values.Length >= 1 && values[0] is double d0)
                progress = d0;
            else if (values.Length >= 1 && double.TryParse(values[0]?.ToString(), out double p))
                progress = p;

            if (values.Length >= 2 && values[1] is double d1)
                totalWidth = d1;
            else if (values.Length >= 2 && double.TryParse(values[1]?.ToString(), out double w))
                totalWidth = w;

            // Clamp progress between 0 and 1
            progress = Math.Max(0, Math.Min(1, progress));
            // Compute and return pixel width
            return progress * totalWidth;
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
} 