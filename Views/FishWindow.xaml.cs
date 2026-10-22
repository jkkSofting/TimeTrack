using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Zeitmanagement.Helpers;

namespace Zeitmanagement.Views
{
    /// <summary>
    /// Easter egg companion of <see cref="PenguinWindow"/>: the fish the penguin throws at the
    /// user. The penguin sits "behind" the screen, so the fish flies towards the viewer, splats
    /// against the glass, and slides down towards the taskbar, leaving a wet trail that fades
    /// out shortly after. Click-through; closes itself when done.
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
        private const double SquashEnd = 0.55;
        private const double SlideStart = 0.75;
        private const double SlideDuration = 1.8;
        private const double SlideEnd = SlideStart + SlideDuration;
        private const double TrailFadeDuration = 1.0;

        private static readonly Random Random = new Random();

        private readonly double _releaseDelay;

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

        private void StartAnimations()
        {
            // The fish sticks to the glass at a random spot above the penguin, crooked and head down.
            var splatX = Width / 2 + (Random.NextDouble() * 2 - 1) * MaxSideOffset;
            var splatY = Math.Max(FishHalfLength + 5, Height * (0.25 + 0.35 * Random.NextDouble()));
            var finalAngle = 90 + (Random.NextDouble() * 2 - 1) * 30;
            var travel = Height - splatY + FishHalfLength; // far enough to vanish behind the taskbar

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

            FishMove.BeginAnimation(TranslateTransform.XProperty, Animation(
                Key(launchDx, 0),
                Key(0, FlightEnd, easeOut)));

            var moveY = Animation(
                Key(launchDy, 0),
                Key(0, FlightEnd, easeOut),
                Key(0, SlideStart),
                Key(travel, SlideEnd, slideEase));
            moveY.Completed += (s, e) => SlideFinished?.Invoke(this, EventArgs.Empty);
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

            FishRotate.BeginAnimation(RotateTransform.AngleProperty, Animation(
                Key(finalAngle - 720, 0),
                Key(finalAngle, FlightEnd, easeOut)));

            TrailScale.BeginAnimation(ScaleTransform.ScaleYProperty, Animation(
                Key(0, 0),
                Key(0, SlideStart),
                Key(1, SlideEnd, slideEase)));

            var trailFade = Animation(
                Key(1, 0),
                Key(1, SlideEnd),
                Key(0, SlideEnd + TrailFadeDuration));
            trailFade.Completed += (s, e) => Close();
            Trail.BeginAnimation(OpacityProperty, trailFade);
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
