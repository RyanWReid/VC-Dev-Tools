using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace VCDevTool.Client.Converters
{
    public class SelectedNodeBorderConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            bool isSelected = (bool)value;
            
            return isSelected ? new SolidColorBrush(Colors.DeepSkyBlue) : new SolidColorBrush(Colors.Transparent);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
} 