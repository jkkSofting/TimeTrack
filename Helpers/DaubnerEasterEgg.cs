using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Win32;
using Zeitmanagement.Views;

namespace Zeitmanagement.Helpers
{
    /// <summary>
    /// Easter egg scheduler: when the app is running at one of the scheduled local times (see
    /// <see cref="DaubnerSchedule.TimesOfDay"/>, currently 08:00 and 20:00), a drawn news anchor
    /// appears on the taskbar of the main window's monitor instead of the penguin (see
    /// <see cref="DaubnerWindow"/>). She comes at most once per scheduled time and only if the moment is
    /// right: a missed time (standby, locked session, presenting, ...) is skipped, never made up
    /// for. She replaces the penguin, so whoever switched the penguin off does not get her either.
    /// If the sound is on, a short jingle plays while she is up. Everything here is purely
    /// decorative and must never disturb the time tracking.
    /// </summary>
    internal sealed class DaubnerEasterEgg : IDisposable
    {
        /// <summary>Shortest time she stays up, even without sound.</summary>
        private const double MinHoldSeconds = 6;

        /// <summary>The running scheduler, or null before it is created and after it is disposed.</summary>
        public static DaubnerEasterEgg Current { get; private set; }

        private readonly DispatcherTimer _timer = new DispatcherTimer();
        private DateTime _scheduled;
        private DateTime _lastShownScheduled = DateTime.MinValue;
        private DaubnerWindow _window;
        private TagesschauJingle _jingle;
        private bool _disposed;

        public DaubnerEasterEgg()
        {
            _timer.Tick += OnTimerTick;
            SystemEvents.PowerModeChanged += OnPowerModeChanged;
            SystemEvents.TimeChanged += OnTimeChanged;
            Current = this;
        }

        /// <summary>
        /// True from the scheduled time until shortly after it (the tolerance during which she may
        /// still appear) and for as long as she is visible. The penguin keeps out of the way then.
        /// </summary>
        public static bool IsActiveWindow(DateTime now)
        {
            if (Current?._window != null)
                return true;

            return DaubnerSchedule.IsWithinAnyWindow(now);
        }

        public void Start() => ScheduleNext();

        /// <summary>Arms the timer for the next scheduled time (calculated from the current clock, not accumulated).</summary>
        private void ScheduleNext()
        {
            if (_disposed)
                return;

            var now = DateTime.Now;
            _scheduled = DaubnerSchedule.NextOccurrence(now);
            Arm(_scheduled - now);
        }

        private void Arm(TimeSpan delay)
        {
            _timer.Stop();
            _timer.Interval = delay > TimeSpan.Zero ? delay : TimeSpan.FromMilliseconds(1);
            _timer.Start();
        }

        // SystemEvents are raised on a thread of their own: continue on the UI thread.
        private void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
        {
            if (e.Mode == PowerModes.Resume)
                RescheduleOnUiThread();
        }

        private void OnTimeChanged(object sender, EventArgs e) => RescheduleOnUiThread();

        private void RescheduleOnUiThread()
        {
            try
            {
                Application.Current?.Dispatcher.BeginInvoke(new Action(() =>
                {
                    try
                    {
                        // The clock jumped (standby, manual change, time sync): recalculate from now.
                        // An appearance that was missed in the meantime is not made up for.
                        ScheduleNext();
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Daubner easter egg reschedule failed: {ex}");
                    }
                }));
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Daubner easter egg reschedule failed: {ex}");
            }
        }

        private void OnTimerTick(object sender, EventArgs e)
        {
            _timer.Stop();

            try
            {
                var now = DateTime.Now;
                if (now < _scheduled)
                {
                    // The timer went off a moment early: wait for the rest.
                    Arm(_scheduled - now);
                    return;
                }

                var show = _window == null
                    && DaubnerSchedule.ShouldShow(
                        now,
                        _scheduled,
                        Properties.Settings.Default.PenguinEnabled,
                        SessionLockState.IsLocked(),
                        PresentationDetector.IsPresenting(),
                        _lastShownScheduled == _scheduled);

                if (show)
                    ShowAnchor(now);
            }
            catch (Exception ex)
            {
                // Purely decorative - never let a failure here take down the time tracker.
                Debug.WriteLine($"Daubner easter egg failed: {ex}");
            }
            finally
            {
                // Whether she came or not: the next chance is the next scheduled time.
                try
                {
                    ScheduleNext();
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Daubner easter egg scheduling failed: {ex}");
                }
            }
        }

        private void ShowAnchor(DateTime now)
        {
            // Building the window takes a moment: ask again right before it appears.
            if (SessionLockState.IsLocked() || PresentationDetector.IsPresenting())
                return;

            var holdSeconds = Math.Max(MinHoldSeconds, TagesschauJingle.DurationSeconds);
            var screen = PenguinEasterEgg.MainWindowScreen();

            _lastShownScheduled = _scheduled;
            _window = new DaubnerWindow(screen.WorkingArea, holdSeconds);
            _window.Closed += (s, e) =>
            {
                _window = null;
                StopJingle();
            };
            _window.Show();

            PlayJingleIfAudible();
        }

        private void PlayJingleIfAudible()
        {
            try
            {
                if (!SystemAudio.IsAudible())
                    return;

                _jingle = new TagesschauJingle();
                _jingle.Play();
            }
            catch (Exception ex)
            {
                // No sound is no drama: she still appears.
                Debug.WriteLine($"Daubner jingle failed: {ex}");
                StopJingle();
            }
        }

        private void StopJingle()
        {
            var jingle = _jingle;
            _jingle = null;
            if (jingle == null)
                return;

            try
            {
                jingle.Stop();
                jingle.Dispose();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Daubner jingle stop failed: {ex}");
            }
        }

        public void Dispose()
        {
            _disposed = true;
            if (Current == this)
                Current = null;

            SystemEvents.PowerModeChanged -= OnPowerModeChanged;
            SystemEvents.TimeChanged -= OnTimeChanged;
            _timer.Stop();
            StopJingle();
            _window?.Close();
        }
    }
}
