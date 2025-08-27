using System;
using System.Globalization;
using System.Windows.Data;

namespace Zeitmanagement.Views
{
    public sealed class NumberFormatterConverter : IValueConverter
    {
        // parameter: optional suffix, z.B. "h"
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value == null) return "";
            double d;
            if (!double.TryParse(value.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out d))
                return value.ToString();

            string suffix = parameter as string ?? "";
            string s;
            double ad = Math.Abs(d);
            if (ad >= 1_000_000)
                s = (d / 1_000_000d).ToString("0.##", culture) + "M";
            else if (ad >= 1_000)
                s = (d / 1_000d).ToString("0.##", culture) + "k";
            else
                s = d.ToString("0.##", culture);

            return string.IsNullOrEmpty(suffix) ? s : $"{s} {suffix}";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
    }
}