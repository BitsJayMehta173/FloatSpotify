using System;
using System.Globalization;
using System.Windows.Data;

namespace FloatingNote
{
    public class HalfSizeConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is double d)
                return d * 0.3; // This is 0.3, not half, but keeping your original logic
            return 20.0;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}