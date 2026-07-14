using System.Diagnostics;
using Microsoft.Win32;

namespace Zeitmanagement.Helpers
{
    /// <summary>
    /// Manages the per-user "Run at Windows startup" registration via the
    /// HKCU Run registry key. Requires no elevation.
    /// </summary>
    internal static class AutostartHelper
    {
        private const string RunKeyPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
        private const string ValueName = "Zeitmanagement";

        public static bool IsEnabled()
        {
            using (var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, false))
            {
                return key?.GetValue(ValueName) != null;
            }
        }

        public static void SetEnabled(bool enabled)
        {
            using (var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, true))
            {
                if (key == null)
                    return;

                if (enabled)
                {
                    var exePath = Process.GetCurrentProcess().MainModule.FileName;
                    key.SetValue(ValueName, $"\"{exePath}\"");
                }
                else if (key.GetValue(ValueName) != null)
                {
                    key.DeleteValue(ValueName);
                }
            }
        }
    }
}
