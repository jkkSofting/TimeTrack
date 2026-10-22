using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;

namespace Zeitmanagement.Helpers
{
    /// <summary>
    /// Tells whether the user is currently presenting or sharing their screen, i.e. whether
    /// something purely decorative should keep out of the way.
    /// </summary>
    internal static class PresentationDetector
    {
        /// <summary>Full-screen app, slideshow, or any app currently sharing/recording the screen.</summary>
        public static bool IsPresenting() => IsFullScreenOrPresentationMode() || IsScreenCaptureActive() || IsPresentingInTeams();

        #region Full screen / Presentation Settings

        // Values of QUERY_USER_NOTIFICATION_STATE (shellapi.h).
        private const int QUNS_NOT_PRESENT = 1;
        private const int QUNS_BUSY = 2;
        private const int QUNS_RUNNING_D3D_FULL_SCREEN = 3;
        private const int QUNS_PRESENTATION_MODE = 4;

        [DllImport("shell32.dll")]
        private static extern int SHQueryUserNotificationState(out int state);

        /// <summary>
        /// The same signal Windows uses to suppress notifications: true while a full-screen app
        /// or slideshow is in the foreground, Presentation Settings are on, or the user is away
        /// (screen saver/locked session).
        /// </summary>
        private static bool IsFullScreenOrPresentationMode()
        {
            if (SHQueryUserNotificationState(out var state) != 0)
                return false;

            return state == QUNS_NOT_PRESENT
                || state == QUNS_BUSY
                || state == QUNS_RUNNING_D3D_FULL_SCREEN
                || state == QUNS_PRESENTATION_MODE;
        }

        #endregion

        #region Screen sharing / recording

        private const string ConsentStorePath = @"Software\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore";

        private static readonly string[] CaptureCapabilities =
        {
            "graphicsCaptureProgrammatic",
            "graphicsCaptureWithoutBorder"
        };

        /// <summary>
        /// Windows records every app that uses the Graphics Capture API (window sharing in
        /// meetings, OBS, ...) in the capability consent store, with a start and a stop timestamp.
        /// While a capture is running, the start is newer than the stop. Apps that grab whole
        /// screens by other means (Teams does) are not listed here.
        /// </summary>
        private static bool IsScreenCaptureActive()
        {
            foreach (var capability in CaptureCapabilities)
            {
                using (var key = Registry.CurrentUser.OpenSubKey($@"{ConsentStorePath}\{capability}"))
                {
                    if (key != null && HasActiveCapture(key))
                        return true;
                }
            }

            return false;
        }

        // Packaged apps are listed directly below the capability; classic exes below "NonPackaged".
        private static bool HasActiveCapture(RegistryKey capabilityKey)
        {
            foreach (var name in capabilityKey.GetSubKeyNames())
            {
                using (var appKey = capabilityKey.OpenSubKey(name))
                {
                    if (appKey == null)
                        continue;

                    var active = name == "NonPackaged" ? HasActiveCapture(appKey) : IsInUse(appKey);
                    if (active)
                        return true;
                }
            }

            return false;
        }

        private static bool IsInUse(RegistryKey appKey)
        {
            return appKey.GetValue("LastUsedTimeStart") is long start
                && appKey.GetValue("LastUsedTimeStop") is long stop
                && start > stop;
        }

        #endregion

        #region Teams screen sharing

        // While Teams shares a screen it opens a transparent, click-through, topmost window of this
        // class that draws the colored border around the shared area. The class name is not
        // localized, unlike the "Sharing control bar" window.
        private const string TeamsShareBorderClass = "ScreenBorderWindow";
        private const string TeamsProcessName = "ms-teams";

        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetClassName(IntPtr hWnd, StringBuilder className, int maxCount);

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

        /// <summary>Microsoft Teams is currently sharing the screen (e.g. presenting in a meeting).</summary>
        public static bool IsPresentingInTeams()
        {
            var found = false;
            var className = new StringBuilder(64);

            EnumWindows((hWnd, lParam) =>
            {
                if (!IsWindowVisible(hWnd))
                    return true;

                className.Clear();
                GetClassName(hWnd, className, className.Capacity);
                if (className.ToString() != TeamsShareBorderClass)
                    return true;

                found = IsOwnedByTeams(hWnd);
                return !found;
            }, IntPtr.Zero);

            return found;
        }

        private static bool IsOwnedByTeams(IntPtr hWnd)
        {
            GetWindowThreadProcessId(hWnd, out var processId);

            try
            {
                using (var process = Process.GetProcessById((int)processId))
                    return process.ProcessName.Equals(TeamsProcessName, StringComparison.OrdinalIgnoreCase);
            }
            catch (ArgumentException)
            {
                return false; // process ended in the meantime
            }
        }

        #endregion
    }
}
