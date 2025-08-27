using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;

namespace Zeitmanagement.Views
{
    public sealed class CountUpTextBlock : TextBlock
    {
        public static readonly DependencyProperty TargetValueProperty =
            DependencyProperty.Register(nameof(TargetValue), typeof(double), typeof(CountUpTextBlock),
                new PropertyMetadata(0.0, OnTargetChanged));

        public static readonly DependencyProperty AnimatedValueProperty =
            DependencyProperty.Register(nameof(AnimatedValue), typeof(double), typeof(CountUpTextBlock),
                new PropertyMetadata(0.0, OnAnimatedChanged));

        public static readonly DependencyProperty DurationMillisecondsProperty =
            DependencyProperty.Register(nameof(DurationMilliseconds), typeof(int), typeof(CountUpTextBlock),
                new PropertyMetadata(700));

        public static readonly DependencyProperty FormatStringProperty =
            DependencyProperty.Register(nameof(FormatString), typeof(string), typeof(CountUpTextBlock),
                new PropertyMetadata("0"));

        public double TargetValue
        {
            get => (double)GetValue(TargetValueProperty);
            set => SetValue(TargetValueProperty, value);
        }

        public double AnimatedValue
        {
            get => (double)GetValue(AnimatedValueProperty);
            set => SetValue(AnimatedValueProperty, value);
        }

        public int DurationMilliseconds
        {
            get => (int)GetValue(DurationMillisecondsProperty);
            set => SetValue(DurationMillisecondsProperty, value);
        }

        public string FormatString
        {
            get => (string)GetValue(FormatStringProperty);
            set => SetValue(FormatStringProperty, value);
        }

        private static void OnTargetChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var tb = (CountUpTextBlock)d;
            double from = tb.AnimatedValue;
            double to = (double)e.NewValue;

            var dur = new Duration(TimeSpan.FromMilliseconds(Math.Max(100, tb.DurationMilliseconds)));
            var ease = new QuarticEase { EasingMode = EasingMode.EaseOut };
            var da = new DoubleAnimation(from, to, dur) { EasingFunction = ease };
            tb.BeginAnimation(AnimatedValueProperty, da, HandoffBehavior.SnapshotAndReplace);
        }

        private static void OnAnimatedChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var tb = (CountUpTextBlock)d;
            var val = (double)e.NewValue;

            string fmt = string.IsNullOrWhiteSpace(tb.FormatString) ? "0" : tb.FormatString;
            tb.Text = val.ToString(fmt, CultureInfo.CurrentCulture);
        }
    }
}
