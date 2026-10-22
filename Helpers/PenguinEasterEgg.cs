using System;
using System.ComponentModel;
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
    /// The random pause between two appearances (also before the first one after app start)
    /// comes from the settings; changing it reschedules a pending appearance right away.
    /// </summary>
    internal sealed class PenguinEasterEgg : IDisposable
    {
        /// <summary>Shortest pause between two appearances the settings allow.</summary>
        public const int MinDelayLimitMinutes = 10;

        private readonly Random _random = new Random();
        private readonly DispatcherTimer _timer = new DispatcherTimer();
        private PenguinWindow _window;
        private int _scheduledMinMinutes;
        private int _scheduledMaxMinutes;
        private bool _disposed;

        public PenguinEasterEgg()
        {
            _timer.Tick += OnTimerTick;
            Properties.Settings.Default.PropertyChanged += OnSettingsChanged;
        }

        public void Start() => ScheduleNext();

        private void ScheduleNext()
        {
            var settings = Properties.Settings.Default;
            _scheduledMinMinutes = Math.Max(MinDelayLimitMinutes, settings.PenguinMinDelayMinutes);
            _scheduledMaxMinutes = Math.Max(_scheduledMinMinutes, settings.PenguinMaxDelayMinutes);

            var minDelay = TimeSpan.FromMinutes(_scheduledMinMinutes);
            var range = TimeSpan.FromMinutes(_scheduledMaxMinutes - _scheduledMinMinutes).TotalMilliseconds;
            _timer.Interval = minDelay + TimeSpan.FromMilliseconds(_random.NextDouble() * range);
            _timer.Start();
        }

        private void OnSettingsChanged(object sender, PropertyChangedEventArgs e)
        {
            // Only a pending appearance is rescheduled; a visible penguin schedules the next one
            // with the new values when it is gone. Saving unchanged values keeps the countdown.
            if (_disposed || !_timer.IsEnabled)
                return;

            var settings = Properties.Settings.Default;
            if (settings.PenguinMinDelayMinutes == _scheduledMinMinutes && settings.PenguinMaxDelayMinutes == _scheduledMaxMinutes)
                return;

            _timer.Stop();
            ScheduleNext();
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
            Properties.Settings.Default.PropertyChanged -= OnSettingsChanged;
            _timer.Stop();
            _window?.Close();
        }
    }
}
