using System;

namespace Zeitmanagement.Helpers
{
    /// <summary>
    /// Pure decision logic of the 20:00 easter egg (see <see cref="DaubnerEasterEgg"/>): when is
    /// the next appearance and may it happen right now. Free of any UI or system access so it can
    /// be tested.
    /// </summary>
    internal static class DaubnerSchedule
    {
        /// <summary>Local time of the appearance.</summary>
        public static readonly TimeSpan TimeOfDay = TimeSpan.FromHours(20);

        /// <summary>
        /// How long after the scheduled time an appearance is still allowed. Later (after standby,
        /// a blocked UI thread, ...) it is skipped for the day instead of being made up for.
        /// </summary>
        public static readonly TimeSpan Tolerance = TimeSpan.FromSeconds(90);

        /// <summary>The next 20:00:00 local time: today if <paramref name="now"/> is before it, otherwise tomorrow.</summary>
        public static DateTime NextOccurrence(DateTime now)
        {
            var today = now.Date + TimeOfDay;
            return now < today ? today : today.AddDays(1);
        }

        /// <summary>True from 20:00:00 up to and including the tolerance after it.</summary>
        public static bool IsWithinWindow(DateTime now, DateTime scheduled)
        {
            var late = now - scheduled;
            return late >= TimeSpan.Zero && late <= Tolerance;
        }

        /// <summary>
        /// Whether the anchor may appear now. Only the penguin fans get her (she replaces the
        /// penguin), and never while the session is locked, the user is presenting, she already
        /// came today, or the scheduled time is not (just) now.
        /// </summary>
        public static bool ShouldShow(DateTime now, DateTime scheduled, bool penguinEnabled, bool locked, bool presenting, bool alreadyShownToday)
        {
            return penguinEnabled
                && !locked
                && !presenting
                && !alreadyShownToday
                && IsWithinWindow(now, scheduled);
        }
    }
}
