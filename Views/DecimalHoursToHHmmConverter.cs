using System;
using System.Globalization;
using System.Windows.Data;

namespace Zeitmanagement.Views
{
    /// <summary>
    /// Converts a decimal hour value (e.g. 2.5) to a string showing both formats: "2.50 h (2:30)"
    /// </summary>
    public sealed class DecimalHoursToHHmmConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            double hours = 0;
            if (value is double d)
                hours = d;
            else if (value is int i)
                hours = i;
            else if (value != null && double.TryParse(value.ToString().Replace(",", "."), NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed))
                hours = parsed;

            int totalMinutes = (int)Math.Round(Math.Abs(hours) * 60);
            int h = totalMinutes / 60;
            int m = totalMinutes % 60;
            string sign = hours < 0 ? "-" : "";

            string suffix = parameter as string ?? "h";
            return $"{hours:0.##} {suffix} ({sign}{h}:{m:D2})";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
    }
}
