using System;
using System.Windows;
using Microsoft.Win32;

namespace Zeitmanagement.Helpers
{
    /// <summary>
    /// Thin wrapper around <see cref="SystemEvents"/> that reports Windows session lock/unlock
    /// and sleep/resume transitions as plain events, always marshalled onto the UI thread.
    /// </summary>
    internal sealed class SessionActivityMonitor : IDisposable
    {
        public event Action Locked;
        public event Action Unlocked;
        public event Action Suspending;
        public event Action Resumed;

        public SessionActivityMonitor()
        {
            SystemEvents.SessionSwitch += OnSessionSwitch;
            SystemEvents.PowerModeChanged += OnPowerModeChanged;
        }

        private void OnSessionSwitch(object sender, SessionSwitchEventArgs e)
        {
            if (e.Reason == SessionSwitchReason.SessionLock)
                Raise(Locked);
            else if (e.Reason == SessionSwitchReason.SessionUnlock)
                Raise(Unlocked);
        }

        private void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
        {
            if (e.Mode == PowerModes.Suspend)
                Raise(Suspending);
            else if (e.Mode == PowerModes.Resume)
                Raise(Resumed);
        }

        private static void Raise(Action handler)
        {
            if (handler == null)
                return;

            // SystemEvents can invoke on a non-UI thread in some hosting scenarios; marshal
            // defensively so subscribers can safely touch DispatcherTimers/bound properties.
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher == null || dispatcher.CheckAccess())
                handler.Invoke();
            else
                dispatcher.BeginInvoke(handler);
        }

        public void Dispose()
        {
            SystemEvents.SessionSwitch -= OnSessionSwitch;
            SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        }
    }
}
