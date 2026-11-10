// Target: .NET Framework + C# 7.3
using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Linq;
using Xunit;
using Zeitmanagement.Helpers;

namespace Zeitmanagement.Tests
{
    public class LocalHoursTests
    {
        private static LocalTimeEntry Entry(int id, int project, string date, string start, string end)
        {
            return new LocalTimeEntry { Id = id, ProjectId = project, DateText = date, Start = start, End = end };
        }

        // ---------- Abnahmefall 9 ----------

        [Fact]
        public void Case9_two_projects_on_one_carrier_add_up_on_the_same_day()
        {
            var entries = new[]
            {
                Entry(1, 1, "2026-11-02", "09:00", "10:30"),   // A: 1,5 h
                Entry(2, 2, "2026-11-02", "11:00", "12:00")    // B: 1,0 h
            };

            var result = LocalHoursCalculator.Calculate(entries, HoursBucketKind.Day);

            var day = result.Buckets.Single();
            Assert.Equal(150, day.Minutes);
            Assert.Equal(2.5, day.Hours);
            Assert.Equal("2,50 h", day.HoursText);
            Assert.Equal("2:30", day.HoursClockText);
            Assert.Equal(2, day.EntryCount);
        }

        [Fact]
        public void Case9_an_overnight_entry_counts_for_the_stored_date()
        {
            var entries = new[]
            {
                Entry(1, 1, "2026-11-02", "09:00", "10:30"),
                Entry(2, 2, "2026-11-02", "11:00", "12:00"),
                Entry(3, 2, "2026-11-03", "23:30", "00:30")    // über Mitternacht: 1,0 h am 3.11.
            };

            var days = LocalHoursCalculator.Calculate(entries, HoursBucketKind.Day);
            var month = LocalHoursCalculator.Calculate(entries, HoursBucketKind.Month);

            Assert.Equal(new[] { 150, 60 }, days.Buckets.Where(b => b.EntryCount > 0).Select(b => b.Minutes).ToArray());
            Assert.Equal(new DateTime(2026, 11, 3), days.Buckets.Last().Start);
            Assert.Equal(60, days.Buckets.Last().Minutes);

            var november = month.Buckets.Single();
            Assert.Equal(210, november.Minutes);
            Assert.Equal(3.5, november.Hours);
            Assert.Equal("November 2026", november.Label);
        }

        [Fact]
        public void Case9_entries_are_never_multiplied_through_the_shared_carrier()
        {
            using (var t = new TestDb())
            {
                t.Db.AddOrEnsureProject("A", "K", Fixtures.Step1);
                t.Db.AddOrEnsureProject("B", "K", Fixtures.Step1);
                t.Db.AddOrEnsureProject("Fremd", "K", "ANDERER.01");
                t.Db.AddTimeEntry(new DateTime(2026, 11, 2), "09:00", "10:30", "A");
                t.Db.AddTimeEntry(new DateTime(2026, 11, 2), "11:00", "12:00", "B");
                t.Db.AddTimeEntry(new DateTime(2026, 11, 2), "13:00", "20:00", "Fremd");

                var linked = CarrierLinking.LinkedProjects(t.Db.GetLocalProjectInfos(), CarrierKeys.Normalize(Fixtures.Step1));
                var ids = linked.Select(p => p.Id).ToList();

                // jede Projekt-ID höchstens einmal, auch wenn sie mehrfach übergeben wird
                var entries = t.Db.GetTimeEntriesForProjectIds(ids.Concat(ids));
                var result = LocalHoursCalculator.Calculate(entries.Concat(entries), HoursBucketKind.Month);

                Assert.Equal(2, ids.Count);
                Assert.Equal(2, entries.Count);
                Assert.Equal(150, result.Buckets.Single().Minutes);
                Assert.Equal(2, result.EntryCount);
                Assert.Empty(result.InvalidEntries);

                // nur lesend: weder Buchungen noch Summen der Projekte wurden verändert
                Assert.Equal(3, t.Scalar("SELECT COUNT(*) FROM time_entries"));
            }
        }

        // ---------- Buckets ----------

        [Fact]
        public void Weeks_follow_iso_8601_starting_on_monday()
        {
            int year;
            Assert.Equal(45, LocalHoursCalculator.IsoWeek(new DateTime(2026, 11, 2), out year));    // Montag
            Assert.Equal(2026, year);
            Assert.Equal(45, LocalHoursCalculator.IsoWeek(new DateTime(2026, 11, 8), out year));    // Sonntag
            Assert.Equal(46, LocalHoursCalculator.IsoWeek(new DateTime(2026, 11, 9), out year));
            Assert.Equal(53, LocalHoursCalculator.IsoWeek(new DateTime(2027, 1, 1), out year));     // gehört noch zu 2026
            Assert.Equal(2026, year);
            Assert.Equal(1, LocalHoursCalculator.IsoWeek(new DateTime(2024, 12, 30), out year));    // gehört schon zu 2025
            Assert.Equal(2025, year);

            var entries = new[]
            {
                Entry(1, 1, "2026-11-08", "08:00", "09:00"),   // Sonntag KW 45
                Entry(2, 1, "2026-11-09", "08:00", "10:00")    // Montag KW 46
            };
            var weeks = LocalHoursCalculator.Calculate(entries, HoursBucketKind.Week);

            Assert.Equal(new[] { "KW 45 2026", "KW 46 2026" }, weeks.Buckets.Select(b => b.Label).ToArray());
            Assert.Equal(new[] { 60, 120 }, weeks.Buckets.Select(b => b.Minutes).ToArray());
        }

        [Fact]
        public void Empty_buckets_inside_the_range_show_zero_and_cumulative_is_a_running_sum()
        {
            var entries = new[]
            {
                Entry(1, 1, "2026-11-02", "09:00", "10:00"),
                Entry(2, 1, "2026-11-05", "09:00", "11:00")
            };

            var result = LocalHoursCalculator.Calculate(entries, HoursBucketKind.Day);

            Assert.Equal(4, result.Buckets.Count);   // 2.-5. November
            Assert.Equal(new[] { 60, 0, 0, 120 }, result.Buckets.Select(b => b.Minutes).ToArray());
            Assert.Equal(new[] { 60, 60, 60, 180 }, result.Buckets.Select(b => b.CumulativeMinutes).ToArray());
            Assert.Equal("1,00 h", result.Buckets[0].CumulativeText);
        }

        [Fact]
        public void A_selected_range_limits_entries_and_clamps_partial_buckets()
        {
            var entries = new[]
            {
                Entry(1, 1, "2026-10-30", "09:00", "10:00"),
                Entry(2, 1, "2026-11-04", "09:00", "10:00"),
                Entry(3, 1, "2026-11-20", "09:00", "10:00")
            };

            var result = LocalHoursCalculator.Calculate(entries, HoursBucketKind.Week, new DateTime(2026, 11, 4), new DateTime(2026, 11, 10));

            Assert.Equal(1, result.EntryCount);
            Assert.Equal(2, result.Buckets.Count);                       // KW 45 und KW 46
            Assert.Equal(new DateTime(2026, 11, 4), result.Buckets[0].Start);
            Assert.Equal(new DateTime(2026, 11, 8), result.Buckets[0].End);
            Assert.Equal(new DateTime(2026, 11, 10), result.Buckets[1].End);
            Assert.Equal(60, result.TotalMinutes);
        }

        [Fact]
        public void Empty_selected_range_gives_zero_buckets_and_no_entries_gives_no_buckets()
        {
            var none = LocalHoursCalculator.Calculate(new LocalTimeEntry[0], HoursBucketKind.Day);
            var ranged = LocalHoursCalculator.Calculate(new LocalTimeEntry[0], HoursBucketKind.Day, new DateTime(2026, 11, 2), new DateTime(2026, 11, 3));

            Assert.Empty(none.Buckets);
            Assert.Equal(0, none.TotalMinutes);
            Assert.Equal(new[] { 0, 0 }, ranged.Buckets.Select(b => b.Minutes).ToArray());
        }

        [Fact]
        public void Invalid_entries_are_reported_and_never_given_an_invented_duration()
        {
            var entries = new[]
            {
                Entry(1, 1, "2026-11-02", "09:00", "10:00"),
                Entry(2, 1, "2026-11-02", "xx:yy", "10:00"),
                Entry(3, 1, "kein-datum", "09:00", "10:00")
            };

            var result = LocalHoursCalculator.Calculate(entries, HoursBucketKind.Day);

            Assert.Equal(60, result.TotalMinutes);
            Assert.Equal(new[] { 2, 3 }, result.InvalidEntries.Select(e => e.Id).ToArray());
        }

        [Fact]
        public void Precision_is_kept_in_minutes_and_hours_above_24_are_formatted()
        {
            var entries = Enumerable.Range(1, 30).Select(i => Entry(i, 1, "2026-11-02", "09:00", "09:55")).ToList();   // 30 x 55 min

            var result = LocalHoursCalculator.Calculate(entries, HoursBucketKind.Day);

            Assert.Equal(1650, result.Buckets.Single().Minutes);
            Assert.Equal("27:30", result.Buckets.Single().HoursClockText);
            Assert.Equal("27,50 h", result.Buckets.Single().HoursText);
        }

        [Fact]
        public void Duration_convention_matches_the_projects_page()
        {
            int minutes;
            Assert.True(LocalHoursCalculator.TryGetDurationMinutes("9:00", "10:30", out minutes));
            Assert.Equal(90, minutes);
            Assert.True(LocalHoursCalculator.TryGetDurationMinutes("23:30", "00:30", out minutes));
            Assert.Equal(60, minutes);
            Assert.True(LocalHoursCalculator.TryGetDurationMinutes("09:00", "09:00", out minutes));
            Assert.Equal(0, minutes);
            Assert.False(LocalHoursCalculator.TryGetDurationMinutes("09:00", null, out minutes));
        }
    }
}
