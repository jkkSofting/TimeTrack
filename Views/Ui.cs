using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;

namespace Zeitmanagement.Views
{
    /// <summary>
    /// Attached properties read by the shared control styles (Themes/Controls.xaml), so views can
    /// give a button an icon or a text box a placeholder without restating the template.
    /// </summary>
    public static class Ui
    {
        /// <summary>Icon glyph (Segoe Fluent Icons / Segoe MDL2 Assets) shown before a button's content.</summary>
        public static readonly DependencyProperty GlyphProperty = DependencyProperty.RegisterAttached(
            "Glyph", typeof(string), typeof(Ui), new FrameworkPropertyMetadata(null));

        public static string GetGlyph(DependencyObject element) => (string)element.GetValue(GlyphProperty);
        public static void SetGlyph(DependencyObject element, string value) => element.SetValue(GlyphProperty, value);

        /// <summary>Hint shown in an empty text box.</summary>
        public static readonly DependencyProperty PlaceholderProperty = DependencyProperty.RegisterAttached(
            "Placeholder", typeof(string), typeof(Ui), new FrameworkPropertyMetadata(null));

        public static string GetPlaceholder(DependencyObject element) => (string)element.GetValue(PlaceholderProperty);
        public static void SetPlaceholder(DependencyObject element, string value) => element.SetValue(PlaceholderProperty, value);

        /// <summary>
        /// For a scroll viewer that only scrolls sideways inside a vertically scrolling page:
        /// passes the mouse wheel on to the page instead of swallowing it.
        /// </summary>
        public static readonly DependencyProperty ForwardMouseWheelProperty = DependencyProperty.RegisterAttached(
            "ForwardMouseWheel", typeof(bool), typeof(Ui), new PropertyMetadata(false, OnForwardMouseWheelChanged));

        public static bool GetForwardMouseWheel(DependencyObject element) => (bool)element.GetValue(ForwardMouseWheelProperty);
        public static void SetForwardMouseWheel(DependencyObject element, bool value) => element.SetValue(ForwardMouseWheelProperty, value);

        private static void OnForwardMouseWheelChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (!(d is ScrollViewer viewer))
                return;

            viewer.PreviewMouseWheel -= ForwardMouseWheel;
            if ((bool)e.NewValue)
                viewer.PreviewMouseWheel += ForwardMouseWheel;
        }

        private static void ForwardMouseWheel(object sender, MouseWheelEventArgs e)
        {
            var viewer = (ScrollViewer)sender;
            if (e.Handled || viewer.ScrollableHeight > 0)
                return;

            e.Handled = true;
            var forwarded = new MouseWheelEventArgs(e.MouseDevice, e.Timestamp, e.Delta)
            {
                RoutedEvent = UIElement.MouseWheelEvent,
                Source = sender
            };
            (VisualTreeHelper.GetParent(viewer) as UIElement)?.RaiseEvent(forwarded);
        }
    }

    /// <summary>
    /// True if the bound value equals the converter parameter (compared as text, ignoring case),
    /// e.g. to check the navigation entry of the currently shown page.
    /// </summary>
    public sealed class EqualsConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
            string.Equals(value?.ToString(), parameter?.ToString(), StringComparison.OrdinalIgnoreCase);

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
            Binding.DoNothing;
    }
}
