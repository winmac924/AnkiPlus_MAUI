using System.Globalization;

namespace AnkiPlus_MAUI.Converters
{
    public class HeightConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is double width)
            {
                return Math.Min((width - 30) * 1.33, 200); // 最大高さを160に制限
            }
            return 200;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
