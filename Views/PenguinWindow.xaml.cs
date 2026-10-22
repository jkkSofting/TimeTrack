using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Drawing = System.Drawing;

namespace Zeitmanagement.Views
{
    /// <summary>
    /// Easter egg: a small penguin that pops up from behind the taskbar, waves one flipper and
    /// sinks back down. The window sits directly on top of the taskbar edge and clips the
    /// penguin at its bottom border, which is what makes it look like it is hiding behind the
    /// taskbar. It is click-through and never takes focus, and closes itself when the
    /// animation is done.
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
        private const double SinkEnd = SinkStart + 0.7;

        private const double FlipperRestAngle = -12;
        private const double FlipperRaisedAngle = -140;
        private const double FlipperWaveAngle = -112;

        private readonly Drawing.Rectangle _workAreaPixels;
        private readonly double _relativeX;

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
            MakeClickThroughAndNonActivating();
            PlaceOnTaskbarEdge();
        }

        protected override void OnContentRendered(EventArgs e)
        {
            base.OnContentRendered(e);
            StartAnimations();
        }

        private void PlaceOnTaskbarEdge()
        {
            var fromDevice = PresentationSource.FromVisual(this).CompositionTarget.TransformFromDevice;
            var bottomLeft = fromDevice.Transform(new Point(_workAreaPixels.Left, _workAreaPixels.Bottom));
            var areaWidth = fromDevice.Transform(new Point(_workAreaPixels.Width, 0)).X;

            Left = bottomLeft.X + Math.Max(0, areaWidth - Width) * _relativeX;
            Top = bottomLeft.Y - Height;
        }

        private void StartAnimations()
        {
            var slide = new DoubleAnimationUsingKeyFrames();
            slide.KeyFrames.Add(new LinearDoubleKeyFrame(HiddenOffset, At(0)));
            slide.KeyFrames.Add(new EasingDoubleKeyFrame(0, At(RiseEnd), new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.4 }));
            slide.KeyFrames.Add(new LinearDoubleKeyFrame(0, At(SinkStart)));
            slide.KeyFrames.Add(new EasingDoubleKeyFrame(HiddenOffset, At(SinkEnd), new QuadraticEase { EasingMode = EasingMode.EaseIn }));
            slide.Completed += (s, e) => Close();

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

        private static KeyTime At(double seconds) => KeyTime.FromTimeSpan(TimeSpan.FromSeconds(seconds));

        #region Win32

        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_TRANSPARENT = 0x00000020;
        private const int WS_EX_TOOLWINDOW = 0x00000080;
        private const int WS_EX_NOACTIVATE = 0x08000000;

        [DllImport("user32.dll")]
        private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll")]
        private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

        /// <summary>
        /// Lets mouse clicks fall through to whatever is underneath (the window briefly covers
        /// the bottom edge of other windows), keeps it from stealing focus and hides it from Alt+Tab.
        /// </summary>
        private void MakeClickThroughAndNonActivating()
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            var style = GetWindowLong(hwnd, GWL_EXSTYLE);
            SetWindowLong(hwnd, GWL_EXSTYLE, style | WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE);
        }

        #endregion
    }
}
