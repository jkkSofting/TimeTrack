using System;

namespace Zeitmanagement.Helpers
{
    /// <summary>
    /// Pure decision logic of the 20:00 news anchor easter egg (see <see cref="DaubnerEasterEgg"/>): when is
    /// the next appearance and may it happen right now. Free of any UI or system access so it can
    /// be tested.
    /// </summary>
    internal static class DaubnerSchedule
    {
        /// <summary>
        /// Local times of the appearance, ascending. To add another time, just add an entry here;
        /// everything else (scheduling, penguin keeping out of the way) follows from this list.
        /// </summary>
        public static readonly TimeSpan[] TimesOfDay = { TimeSpan.FromHours(20) };

        /// <summary>
        /// How long after the scheduled time an appearance is still allowed. Later (after standby,
        /// a blocked UI thread, ...) it is skipped for the day instead of being made up for.
        /// </summary>
        public static readonly TimeSpan Tolerance = TimeSpan.FromSeconds(90);

        /// <summary>
        /// The next scheduled time (strictly after <paramref name="now"/>): the next one still
        /// ahead today, otherwise the first one tomorrow.
        /// </summary>
        public static DateTime NextOccurrence(DateTime now)
        {
            foreach (var time in TimesOfDay)
            {
                var candidate = now.Date + time;
                if (now < candidate)
                    return candidate;
            }

            return now.Date.AddDays(1) + TimesOfDay[0];
        }

        /// <summary>True if <paramref name="now"/> is within the window of any of today's scheduled times.</summary>
        public static bool IsWithinAnyWindow(DateTime now)
        {
            foreach (var time in TimesOfDay)
            {
                if (IsWithinWindow(now, now.Date + time))
                    return true;
            }

            return false;
        }

        /// <summary>True from the scheduled time up to and including the tolerance after it.</summary>
        public static bool IsWithinWindow(DateTime now, DateTime scheduled)
        {
            var late = now - scheduled;
            return late >= TimeSpan.Zero && late <= Tolerance;
        }

        /// <summary>
        /// Whether the anchor may appear now. Only the penguin fans get her (she replaces the
        /// penguin), and never while the session is locked, the user is presenting, she already
        /// came for this scheduled time, or the scheduled time is not (just) now.
        /// </summary>
        public static bool ShouldShow(DateTime now, DateTime scheduled, bool penguinEnabled, bool locked, bool presenting, bool alreadyShown)
        {
            return penguinEnabled
                && !locked
                && !presenting
                && !alreadyShown
                && IsWithinWindow(now, scheduled);
        }
    }
}
