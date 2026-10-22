using System;
using System.Diagnostics;
using System.Windows.Threading;
using Zeitmanagement.Views;
using Forms = System.Windows.Forms;

namespace Zeitmanagement.Helpers
{
    /// <summary>
    /// Easter egg scheduler: at random intervals, a penguin pops up from behind the taskbar of a
    /// randomly chosen monitor at a random horizontal position, waves, and disappears again
    /// (see <see cref="PenguinWindow"/>). Only one penguin is visible at a time; the next
    /// appearance is scheduled once the previous one is gone. While the user is presenting
    /// (full-screen app, slideshow, screen sharing), the penguin skips its turn.
    /// </summary>
    internal sealed class PenguinEasterEgg : IDisposable
    {
        // Random pause between two appearances (also before the first one after app start).
        private static readonly TimeSpan MinDelay = TimeSpan.FromMinutes(60);
        private static readonly TimeSpan MaxDelay = TimeSpan.FromMinutes(90);

        private readonly Random _random = new Random();
        private readonly DispatcherTimer _timer = new DispatcherTimer();
        private PenguinWindow _window;
        private bool _disposed;

        public PenguinEasterEgg()
        {
            _timer.Tick += OnTimerTick;
        }

        public void Start() => ScheduleNext();

        private void ScheduleNext()
        {
            var range = (MaxDelay - MinDelay).TotalMilliseconds;
            _timer.Interval = MinDelay + TimeSpan.FromMilliseconds(_random.NextDouble() * range);
            _timer.Start();
        }

        private void OnTimerTick(object sender, EventArgs e)
        {
            _timer.Stop();

            try
            {
                if (!Properties.Settings.Default.PenguinEnabled || PresentationDetector.IsPresenting())
                    ScheduleNext();
                else
                    ShowPenguin();
            }
            catch (Exception ex)
            {
                // Purely decorative - never let a failure here take down the time tracker.
                Debug.WriteLine($"Penguin easter egg failed: {ex}");
                ScheduleNext();
            }
        }

        private void ShowPenguin()
        {
            var screens = Forms.Screen.AllScreens;
            var screen = screens[_random.Next(screens.Length)];

            _window = new PenguinWindow(screen.WorkingArea, _random.NextDouble());
            _window.Closed += (s, e) =>
            {
                _window = null;
                if (!_disposed)
                    ScheduleNext();
            };
            _window.Show();
        }

        public void Dispose()
        {
            _disposed = true;
            _timer.Stop();
            _window?.Close();
        }
    }
}
