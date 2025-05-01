using System.Globalization;

namespace AnkiPlus_MAUI.Converters
{
    public class WidthConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is double width)
            {
                return Math.Min((width - 30) / 2, 150); // 最大幅を120に制限
            }
            return 150;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

}
