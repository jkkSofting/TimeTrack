using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Zeitmanagement.Helpers;
using Drawing = System.Drawing;

namespace Zeitmanagement.Views
{
    /// <summary>
    /// Easter egg (scheduled times, see <see cref="DaubnerEasterEgg"/>): a drawn news anchor who rises from
    /// behind the taskbar, stays for a while with gentle breathing, a slight nod and the occasional
    /// blink, and sinks back down. Like the penguin, the window sits directly on top of the taskbar
    /// edge and clips the figure at its bottom border - the taskbar is the news desk her hands rest on.
    /// She is purely decorative: clicks fall through, the window never takes focus. It closes itself
    /// when she is gone.
    /// </summary>
    public partial class DaubnerWindow : Window
    {
        private const double HiddenOffset = 200; // == Height: figure completely below the clip edge

        // Animation timeline in seconds.
        private const double RiseDuration = 0.9;
        private const double SinkDuration = 0.8;

        private readonly Drawing.Rectangle _workAreaPixels;
        private readonly double _holdSeconds;

        /// <param name="workAreaPixels">Work area (screen minus taskbar) of the target monitor in device pixels.</param>
        /// <param name="holdSeconds">How long she stays fully up, after rising and before sinking.</param>
        public DaubnerWindow(Drawing.Rectangle workAreaPixels, double holdSeconds)
        {
            _workAreaPixels = workAreaPixels;
            _holdSeconds = Math.Max(0, holdSeconds);
            InitializeComponent();
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            OverlayWindowHelper.Apply(this, clickThrough: true);
            PlaceOnTaskbarEdge();
        }

        protected override void OnContentRendered(EventArgs e)
        {
            base.OnContentRendered(e);
            StartAnimations();
        }

        /// <summary>Centered horizontally in the work area, bottom edge on the top edge of the taskbar.</summary>
        private void PlaceOnTaskbarEdge()
        {
            var fromDevice = PresentationSource.FromVisual(this).CompositionTarget.TransformFromDevice;
            var bottomLeft = fromDevice.Transform(new Point(_workAreaPixels.Left, _workAreaPixels.Bottom));
            var areaWidth = fromDevice.Transform(new Point(_workAreaPixels.Width, 0)).X;

            Left = bottomLeft.X + Math.Max(0, areaWidth - Width) / 2;
            Top = bottomLeft.Y - Height;
        }

        private void StartAnimations()
        {
            // Rise, stay, sink - then the window is done.
            var slide = new DoubleAnimationUsingKeyFrames();
            slide.KeyFrames.Add(new LinearDoubleKeyFrame(HiddenOffset, At(0)));
            slide.KeyFrames.Add(new EasingDoubleKeyFrame(0, At(RiseDuration), new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.25 }));
            slide.KeyFrames.Add(new LinearDoubleKeyFrame(0, At(RiseDuration + _holdSeconds)));
            slide.KeyFrames.Add(new EasingDoubleKeyFrame(HiddenOffset, At(RiseDuration + _holdSeconds + SinkDuration), new QuadraticEase { EasingMode = EasingMode.EaseIn }));
            slide.Completed += (s, e) => Close();
            Shift.BeginAnimation(TranslateTransform.YProperty, slide);

            // Breathing: the body stretches a little from its bottom edge, the head rises along with it.
            var sine = new SineEase { EasingMode = EasingMode.EaseInOut };
            var breatheDuration = TimeSpan.FromSeconds(2.6);
            BreathScale.BeginAnimation(ScaleTransform.ScaleYProperty,
                new DoubleAnimation(1, 1.012, breatheDuration) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = sine });
            HeadBreath.BeginAnimation(TranslateTransform.YProperty,
                new DoubleAnimation(0, -0.9, breatheDuration) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = sine });

            // A barely visible sway of the head.
            HeadTilt.BeginAnimation(RotateTransform.AngleProperty,
                new DoubleAnimation(-0.8, 1.0, TimeSpan.FromSeconds(3.4)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = sine });

            // Nodding: a clear nod and a smaller one, then rest (cycle of 4.6 s).
            var ease = new QuadraticEase { EasingMode = EasingMode.EaseInOut };
            var nod = new DoubleAnimationUsingKeyFrames { RepeatBehavior = RepeatBehavior.Forever };
            nod.KeyFrames.Add(new LinearDoubleKeyFrame(0, At(0)));
            nod.KeyFrames.Add(new LinearDoubleKeyFrame(0, At(1.3)));
            nod.KeyFrames.Add(new EasingDoubleKeyFrame(2.2, At(1.65), ease));
            nod.KeyFrames.Add(new EasingDoubleKeyFrame(0, At(2.05), ease));
            nod.KeyFrames.Add(new EasingDoubleKeyFrame(1.2, At(2.45), ease));
            nod.KeyFrames.Add(new EasingDoubleKeyFrame(0, At(2.85), ease));
            nod.KeyFrames.Add(new LinearDoubleKeyFrame(0, At(4.6)));
            HeadNod.BeginAnimation(TranslateTransform.YProperty, nod);

            // Blinking: a quick close and open (cycle of 3.8 s).
            var blink = new DoubleAnimationUsingKeyFrames { RepeatBehavior = RepeatBehavior.Forever };
            blink.KeyFrames.Add(new LinearDoubleKeyFrame(1, At(0)));
            blink.KeyFrames.Add(new LinearDoubleKeyFrame(1, At(2.6)));
            blink.KeyFrames.Add(new LinearDoubleKeyFrame(0.08, At(2.72)));
            blink.KeyFrames.Add(new LinearDoubleKeyFrame(1, At(2.88)));
            blink.KeyFrames.Add(new LinearDoubleKeyFrame(1, At(3.8)));
            EyeBlink.BeginAnimation(ScaleTransform.ScaleYProperty, blink);
        }

        private static KeyTime At(double seconds) => KeyTime.FromTimeSpan(TimeSpan.FromSeconds(seconds));
    }
}
