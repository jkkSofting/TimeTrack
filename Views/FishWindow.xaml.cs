using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Zeitmanagement.Helpers;

namespace Zeitmanagement.Views
{
    /// <summary>
    /// Easter egg companion of <see cref="PenguinWindow"/>: the fish the penguin throws at the
    /// user. The penguin sits "behind" the screen, so the fish flies towards the viewer, splats
    /// against the glass, and slides down towards the taskbar, leaving a wet trail that fades
    /// out shortly after. Closes itself when done.
    /// The fish can be caught with a click, but only once it sticks to the glass (not while it
    /// is still flying) and before it is gone - and it doesn't make that easy: it only sticks
    /// for a moment, then wriggles from side to side while sliding down faster and faster.
    /// Catches are counted in the settings. Only the fish itself takes clicks; everything else
    /// falls through to whatever is underneath.
    /// </summary>
    public partial class FishWindow : Window
    {
        private const double MaxAreaHeight = 480;
        private const double LaunchHeightAboveBottom = 66; // about where the penguin's beak is
        private const double MaxSideOffset = 40;
        private const double FishHalfLength = 45;

        // Animation timeline in seconds after the penguin released the fish.
        private const double FlightEnd = 0.30;
        private const double SquashPeak = 0.38;
        private const double SquashEnd = 0.55; // from here on the fish can be caught
        private const double SlideStart = 0.65;
        private const double SlideDuration = 1.5;
        private const double SlideEnd = SlideStart + SlideDuration;
        private const double TrailFadeDuration = 1.0;

        // Wriggling while sliding down: sideways swings and a bit of tilt.
        private const int WriggleSwings = 6;
        private const double WriggleOffset = 14;
        private const double WriggleAngle = 15;

        // Feedback after a catch.
        private const double CaughtVanishDuration = 0.35;
        private const double CaughtTextDuration = 1.0;
        private const double CaughtTextRise = 40;

        private static readonly Random Random = new Random();

        private readonly double _releaseDelay;
        private readonly Stopwatch _clock = new Stopwatch();
        private double _splatX;
        private double _splatY;
        private bool _isCaught;
        private bool _isSlideFinished;
        private bool _isClosed;

        /// <summary>Raised once the fish has slid down out of sight (the trail is still fading).</summary>
        public event EventHandler SlideFinished;

        /// <param name="penguinCenterX">Horizontal center of the penguin in DIPs.</param>
        /// <param name="bottomY">Taskbar edge (bottom of the penguin window) in DIPs.</param>
        /// <param name="availableHeight">Height of the screen's work area in DIPs; limits the window height.</param>
        /// <param name="releaseDelaySeconds">Time until the penguin lets go of the fish.</param>
        public FishWindow(double penguinCenterX, double bottomY, double availableHeight, double releaseDelaySeconds)
        {
            _releaseDelay = releaseDelaySeconds;
            InitializeComponent();

            Height = Math.Min(MaxAreaHeight, availableHeight);
            Left = penguinCenterX - Width / 2;
            Top = bottomY - Height;
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            OverlayWindowHelper.Apply(this, clickThrough: true);
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

        private void StartAnimations()
        {
            _clock.Start();

            // The fish sticks to the glass at a random spot above the penguin, crooked and head down.
            var splatX = Width / 2 + (Random.NextDouble() * 2 - 1) * MaxSideOffset;
            var splatY = Math.Max(FishHalfLength + 5, Height * (0.25 + 0.35 * Random.NextDouble()));
            var finalAngle = 90 + (Random.NextDouble() * 2 - 1) * 30;
            var travel = Height - splatY + FishHalfLength; // far enough to vanish behind the taskbar
            var wriggleSide = Random.Next(2) == 0 ? 1 : -1;
            _splatX = splatX;
            _splatY = splatY;

            Canvas.SetLeft(Fish, splatX - Fish.Width / 2);
            Canvas.SetTop(Fish, splatY - Fish.Height / 2);
            Canvas.SetLeft(Trail, splatX - Trail.Width / 2);
            Canvas.SetTop(Trail, splatY);
            Trail.Height = travel;

            var launchDx = Width / 2 - splatX;
            var launchDy = Height - LaunchHeightAboveBottom - splatY;

            var easeOut = new QuadraticEase { EasingMode = EasingMode.EaseOut };
            var easeIn = new QuadraticEase { EasingMode = EasingMode.EaseIn };
            // Sticks to the glass first, then gravity wins. The trail uses the same curve so its
            // end always stays at the fish.
            var slideEase = new CubicEase { EasingMode = EasingMode.EaseIn };

            var visible = Animation(new DiscreteDoubleKeyFrame(1, At(0)));
            Fish.BeginAnimation(OpacityProperty, visible);

            FishMove.BeginAnimation(TranslateTransform.XProperty, Animation(Wriggle(
                WriggleOffset * wriggleSide, 0,
                Key(launchDx, 0),
                Key(0, FlightEnd, easeOut),
                Key(0, SlideStart))));

            var moveY = Animation(
                Key(launchDy, 0),
                Key(0, FlightEnd, easeOut),
                Key(0, SlideStart),
                Key(travel, SlideEnd, slideEase));
            moveY.Completed += (s, e) =>
            {
                _isSlideFinished = true;
                SlideFinished?.Invoke(this, EventArgs.Empty);
            };
            FishMove.BeginAnimation(TranslateTransform.YProperty, moveY);

            // Grows while flying towards the viewer, then gets squashed by the impact.
            FishScale.BeginAnimation(ScaleTransform.ScaleXProperty, Animation(
                Key(0.25, 0),
                Key(1, FlightEnd, easeIn),
                Key(1.2, SquashPeak),
                Key(1, SquashEnd)));
            FishScale.BeginAnimation(ScaleTransform.ScaleYProperty, Animation(
                Key(0.25, 0),
                Key(1, FlightEnd, easeIn),
                Key(0.8, SquashPeak),
                Key(1, SquashEnd)));

            FishRotate.BeginAnimation(RotateTransform.AngleProperty, Animation(Wriggle(
                WriggleAngle * wriggleSide, finalAngle,
                Key(finalAngle - 720, 0),
                Key(finalAngle, FlightEnd, easeOut),
                Key(finalAngle, SlideStart))));

            TrailScale.BeginAnimation(ScaleTransform.ScaleYProperty, Animation(
                Key(0, 0),
                Key(0, SlideStart),
                Key(1, SlideEnd, slideEase)));

            var trailFade = Animation(
                Key(1, 0),
                Key(1, SlideEnd),
                Key(0, SlideEnd + TrailFadeDuration));
            trailFade.Completed += (s, e) => CloseOnce();
            Trail.BeginAnimation(OpacityProperty, trailFade);
        }

        /// <summary>
        /// Appends the side-to-side swings of the slide (starting at <see cref="SlideStart"/>)
        /// to the given key frames: alternating around <paramref name="center"/>, ending on it.
        /// </summary>
        private static DoubleKeyFrame[] Wriggle(double amplitude, double center, params DoubleKeyFrame[] before)
        {
            var keyFrames = new List<DoubleKeyFrame>(before);
            var swing = new SineEase { EasingMode = EasingMode.EaseInOut };
            for (var i = 1; i <= WriggleSwings; i++)
            {
                var offset = i == WriggleSwings ? 0 : (i % 2 == 1 ? amplitude : -amplitude);
                keyFrames.Add(Key(center + offset, SlideStart + i * SlideDuration / WriggleSwings, swing));
            }

            return keyFrames.ToArray();
        }

        private void Fish_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            e.Handled = true;

            // Still flying towards the glass, already gone, or caught: doesn't count.
            var sinceRelease = _clock.Elapsed.TotalSeconds - _releaseDelay;
            if (_isCaught || _isSlideFinished || sinceRelease < SquashEnd)
                return;

            _isCaught = true;

            try
            {
                CountCatch();
                ShowCatch();
            }
            catch (Exception ex)
            {
                // Purely decorative - never let a failure here take down the time tracker.
                Debug.WriteLine($"Catching the fish failed: {ex}");
                CloseOnce();
            }

            // The penguin doesn't need to wait for a fish that is no longer sliding down.
            SlideFinished?.Invoke(this, EventArgs.Empty);
        }

        private static void CountCatch()
        {
            var settings = Properties.Settings.Default;
            settings.FishCaughtCount++;
            settings.Save();
        }

        private void ShowCatch()
        {
            // Freeze everything where it currently is, then let the fish vanish with a little pop.
            var x = FishMove.X;
            var y = FishMove.Y;
            Hold(FishMove, TranslateTransform.XProperty, x);
            Hold(FishMove, TranslateTransform.YProperty, y);
            Hold(FishRotate, RotateTransform.AngleProperty, FishRotate.Angle);
            Hold(TrailScale, ScaleTransform.ScaleYProperty, TrailScale.ScaleY);
            Hold(FishScale, ScaleTransform.ScaleXProperty, FishScale.ScaleX);
            Hold(FishScale, ScaleTransform.ScaleYProperty, FishScale.ScaleY);

            var pop = new DoubleAnimationUsingKeyFrames();
            pop.KeyFrames.Add(Key(FishScale.ScaleX, 0));
            pop.KeyFrames.Add(Key(1.35, CaughtVanishDuration * 0.4, new QuadraticEase { EasingMode = EasingMode.EaseOut }));
            pop.KeyFrames.Add(Key(0, CaughtVanishDuration, new QuadraticEase { EasingMode = EasingMode.EaseIn }));
            FishScale.BeginAnimation(ScaleTransform.ScaleXProperty, pop);
            FishScale.BeginAnimation(ScaleTransform.ScaleYProperty, pop);
            Fish.BeginAnimation(OpacityProperty, new DoubleAnimation(1, 0, TimeSpan.FromSeconds(CaughtVanishDuration)));
            Trail.BeginAnimation(OpacityProperty, new DoubleAnimation(Trail.Opacity, 0, TimeSpan.FromSeconds(TrailFadeDuration)));

            // "Gefangen!" rises from where the fish was caught.
            CaughtText.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            var textWidth = CaughtText.DesiredSize.Width;
            Canvas.SetLeft(CaughtText, Math.Max(0, Math.Min(Width - textWidth, _splatX + x - textWidth / 2)));
            Canvas.SetTop(CaughtText, Math.Max(0, _splatY + y - CaughtText.DesiredSize.Height / 2));

            var rise = new DoubleAnimation(0, -CaughtTextRise, TimeSpan.FromSeconds(CaughtTextDuration))
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            };
            CaughtTextMove.BeginAnimation(TranslateTransform.YProperty, rise);

            var textFade = new DoubleAnimationUsingKeyFrames();
            textFade.KeyFrames.Add(Key(1, 0));
            textFade.KeyFrames.Add(Key(1, CaughtTextDuration * 0.5));
            textFade.KeyFrames.Add(Key(0, CaughtTextDuration));
            textFade.Completed += (s, e) => CloseOnce();
            CaughtText.BeginAnimation(OpacityProperty, textFade);
        }

        /// <summary>Stops a running animation and keeps the property at the given value.</summary>
        private static void Hold(Animatable target, DependencyProperty property, double value)
        {
            target.BeginAnimation(property, null);
            target.SetValue(property, value);
        }

        private void CloseOnce()
        {
            if (!_isClosed)
                Close();
        }

        private DoubleAnimationUsingKeyFrames Animation(params DoubleKeyFrame[] keyFrames)
        {
            var animation = new DoubleAnimationUsingKeyFrames { BeginTime = TimeSpan.FromSeconds(_releaseDelay) };
            foreach (var keyFrame in keyFrames)
                animation.KeyFrames.Add(keyFrame);

            return animation;
        }

        private static DoubleKeyFrame Key(double value, double seconds, IEasingFunction easing = null)
        {
            return new EasingDoubleKeyFrame(value, At(seconds), easing);
        }

        private static KeyTime At(double seconds) => KeyTime.FromTimeSpan(TimeSpan.FromSeconds(seconds));
    }
}
