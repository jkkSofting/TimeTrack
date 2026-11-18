// Target: .NET Framework + C# 7.3
using System;
using Xunit;
using Zeitmanagement.Helpers;

namespace Zeitmanagement.Tests
{
    public class DaubnerScheduleTests
    {
        private static readonly DateTime Scheduled = new DateTime(2026, 11, 17, 20, 0, 0);

        [Fact]
        public void Next_occurrence_before_eight_in_the_morning_is_eight_today()
        {
            Assert.Equal(new DateTime(2026, 11, 17, 8, 0, 0), DaubnerSchedule.NextOccurrence(new DateTime(2026, 11, 17, 0, 0, 0)));
            Assert.Equal(new DateTime(2026, 11, 17, 8, 0, 0), DaubnerSchedule.NextOccurrence(new DateTime(2026, 11, 17, 7, 59, 59)));
        }

        [Fact]
        public void Next_occurrence_between_morning_and_evening_is_eight_pm_today()
        {
            Assert.Equal(new DateTime(2026, 11, 17, 20, 0, 0), DaubnerSchedule.NextOccurrence(new DateTime(2026, 11, 17, 8, 0, 0)));
            Assert.Equal(new DateTime(2026, 11, 17, 20, 0, 0), DaubnerSchedule.NextOccurrence(new DateTime(2026, 11, 17, 9, 30, 0)));
            Assert.Equal(new DateTime(2026, 11, 17, 20, 0, 0), DaubnerSchedule.NextOccurrence(new DateTime(2026, 11, 17, 19, 59, 59)));
        }

        [Fact]
        public void Next_occurrence_after_eight_pm_is_eight_tomorrow_morning()
        {
            Assert.Equal(new DateTime(2026, 11, 18, 8, 0, 0), DaubnerSchedule.NextOccurrence(new DateTime(2026, 11, 17, 20, 0, 1)));
            Assert.Equal(new DateTime(2026, 11, 18, 8, 0, 0), DaubnerSchedule.NextOccurrence(new DateTime(2026, 11, 17, 23, 59, 59)));
        }

        [Fact]
        public void Next_occurrence_at_exactly_a_scheduled_time_is_the_following_one()
        {
            Assert.Equal(new DateTime(2026, 11, 18, 8, 0, 0), DaubnerSchedule.NextOccurrence(Scheduled));
        }

        [Fact]
        public void Next_occurrence_crosses_month_and_year_boundaries()
        {
            Assert.Equal(new DateTime(2026, 12, 1, 8, 0, 0), DaubnerSchedule.NextOccurrence(new DateTime(2026, 11, 30, 21, 0, 0)));
            Assert.Equal(new DateTime(2027, 1, 1, 8, 0, 0), DaubnerSchedule.NextOccurrence(new DateTime(2026, 12, 31, 20, 30, 0)));
            Assert.Equal(new DateTime(2028, 2, 29, 8, 0, 0), DaubnerSchedule.NextOccurrence(new DateTime(2028, 2, 28, 22, 0, 0)));
        }

        [Fact]
        public void Next_occurrence_drops_the_time_of_day()
        {
            Assert.Equal(TimeSpan.FromHours(8), DaubnerSchedule.NextOccurrence(new DateTime(2026, 5, 5, 3, 4, 5, 678)).TimeOfDay);
            Assert.Equal(TimeSpan.FromHours(20), DaubnerSchedule.NextOccurrence(new DateTime(2026, 5, 5, 12, 4, 5, 678)).TimeOfDay);
        }

        [Fact]
        public void Within_any_window_covers_both_scheduled_times()
        {
            Assert.True(DaubnerSchedule.IsWithinAnyWindow(new DateTime(2026, 11, 17, 8, 0, 30)));
            Assert.True(DaubnerSchedule.IsWithinAnyWindow(new DateTime(2026, 11, 17, 20, 0, 30)));
            Assert.False(DaubnerSchedule.IsWithinAnyWindow(new DateTime(2026, 11, 17, 7, 59, 59)));
            Assert.False(DaubnerSchedule.IsWithinAnyWindow(new DateTime(2026, 11, 17, 8, 1, 31)));
            Assert.False(DaubnerSchedule.IsWithinAnyWindow(new DateTime(2026, 11, 17, 14, 0, 0)));
        }

        [Fact]
        public void Shows_in_the_morning_window_too()
        {
            var morning = new DateTime(2026, 11, 17, 8, 0, 0);
            Assert.True(DaubnerSchedule.ShouldShow(morning.AddSeconds(3), morning, true, false, false, false));
        }

        [Fact]
        public void Shows_when_all_conditions_are_met()
        {
            Assert.True(DaubnerSchedule.ShouldShow(Scheduled, Scheduled, true, false, false, false));
            Assert.True(DaubnerSchedule.ShouldShow(Scheduled.AddSeconds(5), Scheduled, true, false, false, false));
        }

        [Fact]
        public void Shows_up_to_and_including_ninety_seconds_late()
        {
            Assert.True(DaubnerSchedule.ShouldShow(Scheduled.AddSeconds(90), Scheduled, true, false, false, false));
        }

        [Fact]
        public void Does_not_show_when_the_penguin_is_disabled()
        {
            Assert.False(DaubnerSchedule.ShouldShow(Scheduled, Scheduled, false, false, false, false));
        }

        [Fact]
        public void Does_not_show_when_the_session_is_locked()
        {
            Assert.False(DaubnerSchedule.ShouldShow(Scheduled, Scheduled, true, true, false, false));
        }

        [Fact]
        public void Does_not_show_while_presenting()
        {
            Assert.False(DaubnerSchedule.ShouldShow(Scheduled, Scheduled, true, false, true, false));
        }

        [Fact]
        public void Does_not_show_twice_for_the_same_scheduled_time()
        {
            Assert.False(DaubnerSchedule.ShouldShow(Scheduled, Scheduled, true, false, false, true));
        }

        [Fact]
        public void Does_not_show_too_early()
        {
            Assert.False(DaubnerSchedule.ShouldShow(Scheduled.AddSeconds(-1), Scheduled, true, false, false, false));
            Assert.False(DaubnerSchedule.ShouldShow(Scheduled.AddHours(-1), Scheduled, true, false, false, false));
        }

        [Fact]
        public void Does_not_show_too_late_and_does_not_catch_up()
        {
            Assert.False(DaubnerSchedule.ShouldShow(Scheduled.AddSeconds(91), Scheduled, true, false, false, false));
            Assert.False(DaubnerSchedule.ShouldShow(Scheduled.AddMinutes(30), Scheduled, true, false, false, false));
        }
    }
}
