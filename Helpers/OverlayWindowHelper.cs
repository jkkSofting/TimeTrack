using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace Zeitmanagement.Helpers
{
    /// <summary>
    /// Win32 window styles shared by the easter egg overlays (penguin, fish): they must neither
    /// steal focus from whatever the user is working in nor show up in Alt+Tab.
    /// </summary>
    internal static class OverlayWindowHelper
    {
        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_TRANSPARENT = 0x00000020;
        private const int WS_EX_TOOLWINDOW = 0x00000080;
        private const int WS_EX_NOACTIVATE = 0x08000000;

        [DllImport("user32.dll")]
        private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll")]
        private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

        /// <summary>
        /// Call from <c>OnSourceInitialized</c>. With <paramref name="clickThrough"/>, all mouse
        /// clicks fall through to whatever is underneath; without it, only the opaque pixels of
        /// the (layered, transparent) window catch clicks and the rest still falls through.
        /// </summary>
        public static void Apply(Window window, bool clickThrough)
        {
            var hwnd = new WindowInteropHelper(window).Handle;
            var style = GetWindowLong(hwnd, GWL_EXSTYLE) | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE;
            if (clickThrough)
                style |= WS_EX_TRANSPARENT;

            SetWindowLong(hwnd, GWL_EXSTYLE, style);
        }
    }
}
