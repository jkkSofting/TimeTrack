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
    /// <see cref="CallForPractice"/> shows a penguin on demand (from the settings) whose catches
    /// are not counted in the statistics; it does not change the regular schedule.
    /// </summary>
    internal sealed class PenguinEasterEgg : IDisposable
    {
        /// <summary>Shortest pause between two appearances the settings allow.</summary>
        public const int MinDelayLimitMinutes = 10;

        /// <summary>The running scheduler, or null before it is created and after it is disposed.</summary>
        public static PenguinEasterEgg Current { get; private set; }

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
            Current = this;
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
                else if (DaubnerEasterEgg.IsActiveWindow(DateTime.Now))
                    ScheduleNext(); // the 20:00 news anchor takes the stage instead: no penguin around that time
                else if (_window != null)
                    ScheduleNext(); // a penguin called up for practice is still here: no second one at the same time
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

            ShowOn(screen, countsInStats: true, scheduleNextWhenClosed: true);
        }

        /// <summary>
        /// Shows a penguin right away on the monitor of the main window, for practice. Its catches
        /// are not counted. Ignores the "enabled" setting and presentation detection. Returns false
        /// if a penguin is already visible or the scheduler is gone.
        /// </summary>
        public bool CallForPractice()
        {
            if (_disposed || _window != null)
                return false;

            try
            {
                ShowOn(MainWindowScreen(), countsInStats: false, scheduleNextWhenClosed: false);
                return true;
            }
            catch (Exception ex)
            {
                // Purely decorative - never let a failure here take down the time tracker.
                Debug.WriteLine($"Penguin practice call failed: {ex}");
                return false;
            }
        }

        internal static Forms.Screen MainWindowScreen()
        {
            var mainWindow = System.Windows.Application.Current?.MainWindow;
            if (mainWindow == null)
                return Forms.Screen.PrimaryScreen;

            return Forms.Screen.FromHandle(new System.Windows.Interop.WindowInteropHelper(mainWindow).Handle);
        }

        private void ShowOn(Forms.Screen screen, bool countsInStats, bool scheduleNextWhenClosed)
        {
            _window = new PenguinWindow(screen.WorkingArea, _random.NextDouble(), countsInStats);
            _window.Closed += (s, e) =>
            {
                _window = null;
                if (scheduleNextWhenClosed && !_disposed)
                    ScheduleNext();
            };
            _window.Show();
        }

        public void Dispose()
        {
            _disposed = true;
            if (Current == this)
                Current = null;

            Properties.Settings.Default.PropertyChanged -= OnSettingsChanged;
            _timer.Stop();
            _window?.Close();
        }
    }
}
