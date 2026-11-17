// Target: .NET Framework + C# 7.3
using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Linq;
using Xunit;
using Zeitmanagement.Helpers;

namespace Zeitmanagement.Tests
{
    /// <summary>Rundung je Projekt: Aufrundung der Buchungen für die SAP-Verrechnung (Rechner, Optionen, Datenbank).</summary>
    public class SapBookingRoundingTests
    {
        private static readonly DateTime Day = new DateTime(2026, 11, 9);

        private static int M(int h, int m) { return h * 60 + m; }

        private static SapBookingInput In(string project, bool external, int startMin, int endMin, int rounding, DateTime? date = null)
        {
            return new SapBookingInput
            {
                Date = date ?? Day,
                Project = project,
                IsExternal = external,
                StartMin = startMin,
                EndMin = endMin,
                RoundingMinutes = rounding
            };
        }

        private static SapDayResult OneDay(params SapBookingInput[] inputs)
        {
            return SapBookingCalculator.Calculate(inputs).Days.Single();
        }

        // ---------- Optionen ----------

        [Theory]
        [InlineData(0, 0)]
        [InlineData(5, 5)]
        [InlineData(10, 10)]
        [InlineData(15, 15)]
        [InlineData(60, 60)]
        [InlineData(7, 0)]
        [InlineData(-15, 0)]
        [InlineData(30, 0)]
        public void Normalize_keeps_allowed_values_and_maps_invalid_to_off(int input, int expected)
        {
            Assert.Equal(expected, RoundingOptions.Normalize(input));
        }

        [Fact]
        public void Labels_and_allowed_values()
        {
            Assert.Equal(new[] { 0, 5, 10, 15, 60 }, RoundingOptions.Allowed);
            Assert.Equal("Aus", RoundingOptions.Label(0));
            Assert.Equal("Aus", RoundingOptions.Label(99));
            Assert.Equal("5 Minuten", RoundingOptions.Label(5));
            Assert.Equal("15 Minuten", RoundingOptions.Label(15));
            Assert.Equal("60 Minuten", RoundingOptions.Label(60));
        }

        [Theory]
        [InlineData(2, 15, 15)]
        [InlineData(15, 15, 15)]
        [InlineData(16, 15, 30)]
        [InlineData(17, 15, 30)]
        [InlineData(30, 15, 30)]
        [InlineData(1, 5, 5)]
        [InlineData(5, 5, 5)]
        [InlineData(6, 5, 10)]
        [InlineData(11, 10, 20)]
        [InlineData(20, 10, 20)]
        [InlineData(1, 60, 60)]
        [InlineData(60, 60, 60)]
        [InlineData(61, 60, 120)]
        [InlineData(17, 0, 17)]
        [InlineData(17, 7, 17)]
        public void RoundUp_rounds_to_next_full_unit(int length, int unit, int expected)
        {
            Assert.Equal(expected, RoundingOptions.RoundUp(length, unit));
        }

        // ---------- Rechner ----------

        [Fact]
        public void Off_leaves_everything_unchanged()
        {
            var day = OneDay(In("A", true, M(9, 0), M(9, 2), 0));

            Assert.Equal(2, day.GrossMin);
            Assert.Equal(2, day.ExternalMin);
            Assert.Equal(0, day.RoundedUpMin);
            Assert.Equal(M(9, 2), day.LastEndMin);
            Assert.Equal(M(9, 2), day.DisplayEndMin);
            var seg = Assert.Single(day.Segments);
            Assert.False(seg.IsRounding);
            Assert.Equal(M(9, 0), seg.StartMin);
            Assert.Equal(M(9, 2), seg.EndMin);
        }

        [Fact]
        public void Invalid_unit_behaves_like_off()
        {
            var day = OneDay(In("A", true, M(9, 0), M(9, 2), 7));

            Assert.Equal(2, day.GrossMin);
            Assert.Equal(0, day.RoundedUpMin);
        }

        [Fact]
        public void Two_minutes_at_15_count_as_15()
        {
            var day = OneDay(In("A", true, M(9, 0), M(9, 2), 15));

            Assert.Equal(15, day.GrossMin);
            Assert.Equal(15, day.ExternalMin);
            Assert.Equal(15, day.SapTotalMin);
            Assert.Equal(13, day.RoundedUpMin);
            Assert.Equal(15, day.Projects.Single().BookedMin);
        }

        [Fact]
        public void Seventeen_minutes_at_15_count_as_30()
        {
            var day = OneDay(In("A", true, M(9, 0), M(9, 17), 15));

            Assert.Equal(30, day.GrossMin);
            Assert.Equal(13, day.RoundedUpMin);
        }

        [Theory]
        [InlineData(15)]
        [InlineData(30)]
        [InlineData(45)]
        public void Exact_multiple_at_15_is_unchanged(int minutes)
        {
            var day = OneDay(In("A", true, M(9, 0), M(9, 0) + minutes, 15));

            Assert.Equal(minutes, day.GrossMin);
            Assert.Equal(0, day.RoundedUpMin);
            Assert.All(day.Segments, s => Assert.False(s.IsRounding));
        }

        [Theory]
        [InlineData(5, 7, 10)]
        [InlineData(10, 11, 20)]
        [InlineData(60, 61, 120)]
        [InlineData(60, 1, 60)]
        public void Other_units(int unit, int length, int expectedCounted)
        {
            var day = OneDay(In("A", true, M(9, 0), M(9, 0) + length, unit));

            Assert.Equal(expectedCounted, day.GrossMin);
            Assert.Equal(expectedCounted - length, day.RoundedUpMin);
        }

        [Fact]
        public void Padding_is_appended_at_the_end_and_flagged_as_rounding_segment()
        {
            var day = OneDay(In("A", true, M(9, 0), M(9, 2), 15));

            Assert.Equal(2, day.Segments.Count);
            var real = day.Segments.Single(s => !s.IsRounding);
            var pad = day.Segments.Single(s => s.IsRounding);

            Assert.Equal(M(9, 0), real.StartMin);
            Assert.Equal(M(9, 2), real.EndMin);
            Assert.Equal(M(9, 2), pad.StartMin);
            Assert.Equal(M(9, 15), pad.EndMin);
            Assert.Equal(real.Lane, pad.Lane);
            Assert.Equal(SapSegmentKind.External, real.Kind);
            Assert.Equal(SapSegmentKind.External, pad.Kind);
        }

        [Fact]
        public void Segments_cover_booking_exactly_without_double_counting()
        {
            var day = OneDay(In("A", true, M(9, 0), M(9, 17), 15));

            Assert.Equal(day.GrossMin, day.Segments.Sum(s => s.EndMin - s.StartMin));
            Assert.Equal(day.RoundedUpMin, day.Segments.Where(s => s.IsRounding).Sum(s => s.EndMin - s.StartMin));
        }

        [Fact]
        public void Working_time_span_stays_real_and_display_end_includes_padding()
        {
            var day = OneDay(
                In("A", true, M(9, 0), M(10, 0), 0),
                In("A", true, M(10, 0), M(10, 2), 15));

            Assert.Equal(M(9, 0), day.FirstStartMin);
            Assert.Equal(M(10, 2), day.LastEndMin);
            Assert.Equal(62, day.SpanMin);
            Assert.Equal(M(10, 15), day.DisplayEndMin);
            Assert.Equal(75, day.GrossMin);
        }

        [Fact]
        public void Rounded_external_booking_covers_more_parallel_internal_time()
        {
            // extern 09:00-09:10, intern 09:05-09:20 (ohne Rundung)
            var plain = OneDay(
                In("Ext", true, M(9, 0), M(9, 10), 0),
                In("Int", false, M(9, 5), M(9, 20), 0));
            Assert.Equal(5, plain.CoveredByExternalMin);
            Assert.Equal(10, plain.InternalBookedMin);

            // extern auf 15 aufgerundet: 09:00-09:15 deckt 10 Minuten der internen Zeit ab
            var rounded = OneDay(
                In("Ext", true, M(9, 0), M(9, 10), 15),
                In("Int", false, M(9, 5), M(9, 20), 0));
            Assert.Equal(10, rounded.CoveredByExternalMin);
            Assert.Equal(5, rounded.InternalBookedMin);
            Assert.Equal(15, rounded.ExternalMin);

            var intSegments = rounded.Segments.Where(s => s.Project == "Int").OrderBy(s => s.StartMin).ToList();
            Assert.Equal(SapSegmentKind.InternalCoveredByExternal, intSegments[0].Kind);
            Assert.Equal(M(9, 5), intSegments[0].StartMin);
            Assert.Equal(M(9, 15), intSegments[0].EndMin);
            // die interne Zeit ist reale Zeit des internen Projekts: nicht als Aufrundung gekennzeichnet
            Assert.All(intSegments, s => Assert.False(s.IsRounding));
            Assert.Contains(rounded.Segments, s => s.Project == "Ext" && s.IsRounding && s.StartMin == M(9, 10) && s.EndMin == M(9, 15));
        }

        [Fact]
        public void Rounded_surplus_is_carried_to_later_internal_time()
        {
            var day = OneDay(
                In("ExtA", true, M(9, 0), M(9, 2), 15),
                In("ExtB", true, M(9, 0), M(9, 15), 0),
                In("Int", false, M(10, 0), M(10, 30), 0));

            Assert.Equal(15, day.SurplusGeneratedMin);
            Assert.Equal(15, day.CoveredBySurplusMin);
            Assert.Equal(15, day.InternalBookedMin);
        }

        [Fact]
        public void Padding_that_ends_in_a_covered_part_is_split_at_the_real_end()
        {
            // intern 09:00-09:10 @15 (auf 09:15), extern 09:12-09:30: die Aufrundung liegt teils unter externer Zeit
            var day = OneDay(
                In("Int", false, M(9, 0), M(9, 10), 15),
                In("Ext", true, M(9, 12), M(9, 30), 0));

            var intSegs = day.Segments.Where(s => s.Project == "Int").OrderBy(s => s.StartMin).ToList();
            Assert.Equal(15, intSegs.Sum(s => s.EndMin - s.StartMin));
            Assert.Equal(5, intSegs.Where(s => s.IsRounding).Sum(s => s.EndMin - s.StartMin));
            Assert.All(intSegs.Where(s => s.IsRounding), s => Assert.True(s.StartMin >= M(9, 10)));
            Assert.All(intSegs.Where(s => !s.IsRounding), s => Assert.True(s.EndMin <= M(9, 10)));
            Assert.Equal(3, day.CoveredByExternalMin);
            Assert.Equal(12, day.InternalBookedMin);
        }

        [Fact]
        public void Padded_bookings_do_not_share_a_lane_when_they_overlap_after_padding()
        {
            var plain = OneDay(
                In("A", true, M(9, 0), M(9, 2), 0),
                In("B", true, M(9, 10), M(9, 20), 0));
            Assert.Equal(1, plain.LaneCount);

            var rounded = OneDay(
                In("A", true, M(9, 0), M(9, 2), 15),
                In("B", true, M(9, 10), M(9, 20), 0));
            Assert.Equal(2, rounded.LaneCount);
            var laneA = rounded.Segments.First(s => s.Project == "A").Lane;
            var laneB = rounded.Segments.First(s => s.Project == "B").Lane;
            Assert.NotEqual(laneA, laneB);
            Assert.All(rounded.Segments.Where(s => s.Project == "A"), s => Assert.Equal(laneA, s.Lane));
        }

        [Fact]
        public void Rounded_up_minutes_sum_per_project_day_and_month()
        {
            var result = SapBookingCalculator.Calculate(new[]
            {
                In("A", true, M(9, 0), M(9, 2), 15),                          // +13
                In("A", true, M(10, 0), M(10, 17), 15),                       // +13
                In("B", false, M(11, 0), M(11, 1), 60),                       // +59
                In("A", true, M(9, 0), M(9, 6), 5, Day.AddDays(1))            // +4
            });

            Assert.Equal(2, result.Days.Count);
            Assert.Equal(26 + 59, result.Days[0].RoundedUpMin);
            Assert.Equal(4, result.Days[1].RoundedUpMin);
            Assert.Equal(26 + 59 + 4, result.RoundedUpMin);

            var a = result.Projects.Single(p => p.Project == "A");
            var b = result.Projects.Single(p => p.Project == "B");
            Assert.Equal(30, a.RoundedUpMin);
            Assert.Equal(59, b.RoundedUpMin);
            Assert.Equal(26, result.Days[0].Projects.Single(p => p.Project == "A").RoundedUpMin);

            // Rundung ist in der Bruttozeit enthalten
            Assert.Equal(15 + 30 + 60 + 10, result.GrossMin);
            Assert.Equal(result.Days.Sum(d => d.SpanMin), result.SpanMin);
        }

        [Fact]
        public void Month_result_span_stays_real_even_with_rounding()
        {
            var result = SapBookingCalculator.Calculate(new[]
            {
                In("A", true, M(9, 0), M(9, 2), 15),
                In("A", true, M(9, 0), M(9, 2), 15, Day.AddDays(1))
            });

            Assert.Equal(4, result.SpanMin);
            Assert.Equal(26, result.RoundedUpMin);
        }

        // ---------- Datenbank ----------

        [Fact]
        public void New_database_has_the_rounding_column_and_defaults_to_off()
        {
            using (var t = new TestDb())
            {
                t.Db.AddOrEnsureProject("Alpha", "Kunde", "K.01");

                Assert.Equal(1, t.Scalar("SELECT COUNT(*) FROM pragma_table_info('projects') WHERE name = 'rundung_minuten'"));
                Assert.Equal(0, t.Db.GetProjects().Single().RundungMinuten);
            }
        }

        [Fact]
        public void Migration_adds_the_column_to_an_existing_database_and_keeps_data()
        {
            var path = Fixtures.TempDbPath();
            try
            {
                using (var conn = new SQLiteConnection("Data Source=" + path))
                {
                    conn.Open();
                    using (var cmd = conn.CreateCommand())
                    {
                        cmd.CommandText = @"
                        CREATE TABLE projects(
                            id INTEGER PRIMARY KEY AUTOINCREMENT,
                            projektname TEXT NOT NULL UNIQUE,
                            kunde TEXT NOT NULL,
                            anzahl_stunden REAL NOT NULL DEFAULT 0.0,
                            kostentraeger TEXT NOT NULL,
                            ist_extern INTEGER NOT NULL DEFAULT 0,
                            quickselect_aktiv INTEGER NOT NULL DEFAULT 1
                        );
                        INSERT INTO projects (projektname, kunde, kostentraeger, ist_extern) VALUES ('Alt', 'Kunde', 'K.01', 1);";
                        cmd.ExecuteNonQuery();
                    }
                }
                SQLiteConnection.ClearAllPools();

                for (int round = 0; round < 2; round++) // zweiter Durchlauf: Migration ist idempotent
                {
                    using (var db = new TimeTracker(path))
                    {
                        var p = db.GetProjects().Single();
                        Assert.Equal("Alt", p.Projektname);
                        Assert.True(p.IstExtern);
                        Assert.Equal(0, p.RundungMinuten);
                    }
                    SQLiteConnection.ClearAllPools();
                }
            }
            finally
            {
                SQLiteConnection.ClearAllPools();
                try { System.IO.Directory.Delete(System.IO.Path.GetDirectoryName(path), true); } catch (System.IO.IOException) { }
            }
        }

        [Fact]
        public void Rounding_roundtrips_through_add_update_and_reads()
        {
            using (var t = new TestDb())
            {
                t.Db.AddOrEnsureProject("Alpha", "Kunde", "K.01", istExtern: true, rundungMinuten: 15);
                t.Db.AddOrEnsureProject("Beta", "Kunde", "K.02");

                var projects = t.Db.GetProjects().ToDictionary(p => p.Projektname);
                Assert.Equal(15, projects["Alpha"].RundungMinuten);
                Assert.Equal(0, projects["Beta"].RundungMinuten);

                // UPSERT aktualisiert die Rundung ebenfalls
                t.Db.AddOrEnsureProject("Alpha", "Kunde", "K.01", istExtern: true, rundungMinuten: 60);
                Assert.Equal(60, t.Db.GetProjects().Single(p => p.Projektname == "Alpha").RundungMinuten);

                t.Db.UpdateProject("Beta", "Beta neu", "Kunde", "K.02", false, 5);
                Assert.Equal(5, t.Db.GetProjects().Single(p => p.Projektname == "Beta neu").RundungMinuten);

                t.Db.UpdateProject("Beta neu", "Beta neu", "Kunde", "K.02", false, 0);
                Assert.Equal(0, t.Db.GetProjects().Single(p => p.Projektname == "Beta neu").RundungMinuten);

                // bleibt nach Neustart erhalten
                t.Restart();
                Assert.Equal(60, t.Db.GetProjects().Single(p => p.Projektname == "Alpha").RundungMinuten);
            }
        }

        [Fact]
        public void Invalid_rounding_value_is_stored_as_off()
        {
            using (var t = new TestDb())
            {
                t.Db.AddOrEnsureProject("Alpha", "Kunde", "K.01", rundungMinuten: 7);

                Assert.Equal(0, t.Db.GetProjects().Single().RundungMinuten);
                Assert.Equal(0, t.Scalar("SELECT rundung_minuten FROM projects"));
            }
        }

        [Fact]
        public void Time_entries_carry_the_rounding_of_their_project()
        {
            using (var t = new TestDb())
            {
                t.Db.AddOrEnsureProject("Alpha", "Kunde", "K.01", rundungMinuten: 15);
                t.Db.AddOrEnsureProject("Beta", "Kunde", "K.02");
                t.Db.AddTimeEntry(Day, "09:00", "09:02", "Alpha");
                t.Db.AddTimeEntry(Day, "10:00", "10:30", "Beta");

                var entries = t.Db.GetTimeEntriesBetween(Day, Day);

                Assert.Equal(15, entries.Single(e => e.Projektname == "Alpha").RundungMinuten);
                Assert.Equal(0, entries.Single(e => e.Projektname == "Beta").RundungMinuten);

                // Rundung des Projekts ändern wirkt sich rückwirkend auf alle Buchungen aus
                t.Db.UpdateProject("Alpha", "Alpha", "Kunde", "K.01", false, 5);
                Assert.Equal(5, t.Db.GetTimeEntriesBetween(Day, Day).Single(e => e.Projektname == "Alpha").RundungMinuten);
            }
        }
    }
}
