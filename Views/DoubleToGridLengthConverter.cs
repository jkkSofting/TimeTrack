using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace Zeitmanagement.Views
{
    /// <summary>
    /// Converts a double fraction (0.0–1.0) to a star-based GridLength.
    /// E.g. 0.25 → "0.25*" so Grid columns scale proportionally.
    /// </summary>
    public sealed class DoubleToGridLengthConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is double d && d > 0)
                return new GridLength(d, GridUnitType.Star);
            return new GridLength(0, GridUnitType.Star);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}
