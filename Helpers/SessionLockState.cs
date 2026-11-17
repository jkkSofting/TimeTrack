using System;
using System.Runtime.InteropServices;
using System.Text;

namespace Zeitmanagement.Helpers
{
    /// <summary>
    /// Tells whether the Windows session is currently locked, i.e. the lock screen or another
    /// secure desktop (UAC prompt, Ctrl+Alt+Del screen) is active instead of the user's desktop.
    /// </summary>
    internal static class SessionLockState
    {
        private const uint DESKTOP_SWITCHDESKTOP = 0x0100;
        private const int UOI_NAME = 2;
        private const string UserDesktopName = "Default";

        /// <summary>
        /// True while the session is locked or a secure desktop is in front. Never throws;
        /// if the state cannot be determined, the session is assumed to be unlocked.
        /// </summary>
        public static bool IsLocked()
        {
            try
            {
                // While the lock screen (Winlogon desktop) is active, the calling process
                // cannot open the input desktop and the call fails with access denied.
                var desktop = OpenInputDesktop(0, false, DESKTOP_SWITCHDESKTOP);
                if (desktop == IntPtr.Zero)
                    return true;

                try
                {
                    return !UserDesktopName.Equals(GetDesktopName(desktop), StringComparison.OrdinalIgnoreCase);
                }
                finally
                {
                    CloseDesktop(desktop);
                }
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static string GetDesktopName(IntPtr desktop)
        {
            // The length argument is in bytes, the buffer holds UTF-16 characters.
            const int capacity = 256;
            var name = new StringBuilder(capacity);

            if (!GetUserObjectInformation(desktop, UOI_NAME, name, capacity * 2, out _))
                return null;

            return name.ToString();
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr OpenInputDesktop(uint flags, bool inherit, uint desiredAccess);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool CloseDesktop(IntPtr desktop);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool GetUserObjectInformation(IntPtr obj, int index, StringBuilder info, int length, out int lengthNeeded);
    }
}
