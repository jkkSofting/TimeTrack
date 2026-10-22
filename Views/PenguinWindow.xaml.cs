using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Zeitmanagement.Helpers;
using Drawing = System.Drawing;

namespace Zeitmanagement.Views
{
    /// <summary>
    /// Easter egg: a small penguin that pops up from behind the taskbar, waves one flipper and
    /// sinks back down. The window sits directly on top of the taskbar edge and clips the
    /// penguin at its bottom border, which is what makes it look like it is hiding behind the
    /// taskbar. Only the penguin itself catches mouse clicks (the transparent rest of the window
    /// lets them through) and the window never takes focus. If the penguin is clicked in time it
    /// throws a fish at the user (see <see cref="FishWindow"/>) and waits for it to slide down
    /// before it disappears. The window closes itself when it is done.
    /// </summary>
    public partial class PenguinWindow : Window
    {
        private const double HiddenOffset = 100; // == Height: penguin completely below the clip edge

        // Animation timeline in seconds.
        private const double RiseEnd = 0.7;
        private const double WaveStart = 0.9;
        private const double WaveRaised = 1.3;
        private const double WaveCycle = 0.5;
        private const int WaveCycles = 3;
        private const double WaveEnd = WaveRaised + WaveCycles * WaveCycle;
        private const double FlipperLowered = WaveEnd + 0.4;
        private const double SinkStart = FlipperLowered + 0.3;
        private const double SinkDuration = 0.7;

        private const double FlipperRestAngle = -12;
        private const double FlipperRaisedAngle = -140;
        private const double FlipperWaveAngle = -112;

        // Throwing a fish: a click only counts while the penguin is at least this far up.
        private const double ClickableBelowOffset = 60;
        private const double ThrowWindUp = 0.12;
        private const double ThrowRelease = 0.30; // the fish leaves the flipper
        private const double ThrowWindUpAngle = -165;
        private const double ThrowReleaseAngle = -25;
        private const double PauseAfterFish = 0.3;

        private readonly Drawing.Rectangle _workAreaPixels;
        private readonly double _relativeX;
        private double _availableHeight;
        private bool _hasThrown;
        private bool _isSinkingAfterThrow;
        private bool _isClosed;

        /// <param name="workAreaPixels">Work area (screen minus taskbar) of the target monitor in device pixels.</param>
        /// <param name="relativeX">Horizontal position within that work area, 0 = left edge, 1 = right edge.</param>
        public PenguinWindow(Drawing.Rectangle workAreaPixels, double relativeX)
        {
            _workAreaPixels = workAreaPixels;
            _relativeX = relativeX;
            InitializeComponent();
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            OverlayWindowHelper.Apply(this, clickThrough: false);
            PlaceOnTaskbarEdge();
        }

        protected override void OnContentRendered(EventArgs e)
        {
            base.OnContentRendered(e);
            StartAnimations();
        }

        protected override void OnClosed(EventArgs e)
        {
            _isClosed = true;
            base.OnClosed(e);
        }

        private void PlaceOnTaskbarEdge()
        {
            var fromDevice = PresentationSource.FromVisual(this).CompositionTarget.TransformFromDevice;
            var bottomLeft = fromDevice.Transform(new Point(_workAreaPixels.Left, _workAreaPixels.Bottom));
            var areaTop = fromDevice.Transform(new Point(0, _workAreaPixels.Top)).Y;
            var areaWidth = fromDevice.Transform(new Point(_workAreaPixels.Width, 0)).X;

            Left = bottomLeft.X + Math.Max(0, areaWidth - Width) * _relativeX;
            Top = bottomLeft.Y - Height;
            _availableHeight = bottomLeft.Y - areaTop;
        }

        private void StartAnimations()
        {
            var slide = new DoubleAnimationUsingKeyFrames();
            slide.KeyFrames.Add(new LinearDoubleKeyFrame(HiddenOffset, At(0)));
            slide.KeyFrames.Add(new EasingDoubleKeyFrame(0, At(RiseEnd), new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.4 }));
            slide.KeyFrames.Add(new LinearDoubleKeyFrame(0, At(SinkStart)));
            slide.KeyFrames.Add(new EasingDoubleKeyFrame(HiddenOffset, At(SinkStart + SinkDuration), new QuadraticEase { EasingMode = EasingMode.EaseIn }));
            slide.Completed += (s, e) =>
            {
                // A click replaces this animation; the throw then decides when the window closes.
                if (!_hasThrown)
                    Close();
            };

            var wave = new DoubleAnimationUsingKeyFrames();
            wave.KeyFrames.Add(new LinearDoubleKeyFrame(FlipperRestAngle, At(0)));
            wave.KeyFrames.Add(new LinearDoubleKeyFrame(FlipperRestAngle, At(WaveStart)));
            wave.KeyFrames.Add(new EasingDoubleKeyFrame(FlipperRaisedAngle, At(WaveRaised), new QuadraticEase { EasingMode = EasingMode.EaseOut }));
            for (var i = 0; i < WaveCycles; i++)
            {
                var cycleStart = WaveRaised + i * WaveCycle;
                wave.KeyFrames.Add(new EasingDoubleKeyFrame(FlipperWaveAngle, At(cycleStart + WaveCycle / 2), new SineEase { EasingMode = EasingMode.EaseInOut }));
                wave.KeyFrames.Add(new EasingDoubleKeyFrame(FlipperRaisedAngle, At(cycleStart + WaveCycle), new SineEase { EasingMode = EasingMode.EaseInOut }));
            }
            wave.KeyFrames.Add(new EasingDoubleKeyFrame(FlipperRestAngle, At(FlipperLowered), new QuadraticEase { EasingMode = EasingMode.EaseInOut }));

            Shift.BeginAnimation(TranslateTransform.YProperty, slide);
            Flipper.BeginAnimation(RotateTransform.AngleProperty, wave);
        }

        private void Penguin_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            // Too slow (already sunk) or too early (still hidden): the penguin doesn't react.
            if (_hasThrown || Shift.Y > ClickableBelowOffset)
                return;

            _hasThrown = true;
            e.Handled = true;

            try
            {
                ThrowFish();
            }
            catch (Exception ex)
            {
                // Purely decorative - never let a failure here take down the time tracker.
                Debug.WriteLine($"Penguin fish throw failed: {ex}");
                SinkAfterThrow();
            }
        }

        private void ThrowFish()
        {
            // Take over from wherever the running animations currently are.
            var currentY = Shift.Y;
            var currentAngle = Flipper.Angle;
            Shift.BeginAnimation(TranslateTransform.YProperty, null);
            Flipper.BeginAnimation(RotateTransform.AngleProperty, null);
            Shift.Y = currentY;
            Flipper.Angle = currentAngle;

            // Stand up straight (in case the click came while sinking) and hold that position
            // until the fish is gone.
            var stayUp = new DoubleAnimationUsingKeyFrames();
            stayUp.KeyFrames.Add(new LinearDoubleKeyFrame(currentY, At(0)));
            stayUp.KeyFrames.Add(new EasingDoubleKeyFrame(0, At(0.2), new QuadraticEase { EasingMode = EasingMode.EaseOut }));
            Shift.BeginAnimation(TranslateTransform.YProperty, stayUp);

            var throwArm = new DoubleAnimationUsingKeyFrames();
            throwArm.KeyFrames.Add(new LinearDoubleKeyFrame(currentAngle, At(0)));
            throwArm.KeyFrames.Add(new EasingDoubleKeyFrame(ThrowWindUpAngle, At(ThrowWindUp), new QuadraticEase { EasingMode = EasingMode.EaseOut }));
            throwArm.KeyFrames.Add(new EasingDoubleKeyFrame(ThrowReleaseAngle, At(ThrowRelease), new QuadraticEase { EasingMode = EasingMode.EaseIn }));
            throwArm.KeyFrames.Add(new EasingDoubleKeyFrame(FlipperRestAngle, At(ThrowRelease + 0.4), new QuadraticEase { EasingMode = EasingMode.EaseInOut }));
            Flipper.BeginAnimation(RotateTransform.AngleProperty, throwArm);

            var fish = new FishWindow(Left + Width / 2, Top + Height, _availableHeight, ThrowRelease);
            fish.SlideFinished += (s, e) => SinkAfterThrow();
            fish.Closed += (s, e) => SinkAfterThrow(); // fallback, e.g. if the fish never got to slide
            fish.Show();
        }

        private void SinkAfterThrow()
        {
            if (_isSinkingAfterThrow || _isClosed)
                return;

            _isSinkingAfterThrow = true;

            var sink = new DoubleAnimation(0, HiddenOffset, TimeSpan.FromSeconds(SinkDuration))
            {
                BeginTime = TimeSpan.FromSeconds(PauseAfterFish),
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn }
            };
            sink.Completed += (s, e) => Close();
            Shift.BeginAnimation(TranslateTransform.YProperty, sink);
        }

        private static KeyTime At(double seconds) => KeyTime.FromTimeSpan(TimeSpan.FromSeconds(seconds));
    }
}
