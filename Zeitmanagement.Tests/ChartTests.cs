// Target: .NET Framework + C# 7.3
using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;
using Zeitmanagement.Helpers;

namespace Zeitmanagement.Tests
{
    /// <summary>Datenaufbereitung und Achsen des Stand-Diagramms (das Zeichnen selbst ist ein WPF-Steuerelement).</summary>
    public class ChartTests
    {
        private static readonly DateTime T1 = new DateTime(2026, 9, 30, 14, 30, 0);
        private static readonly DateTime T2 = new DateTime(2026, 10, 31, 14, 30, 0);
        private static readonly DateTime T3 = new DateTime(2026, 11, 9, 14, 30, 0);

        private static ControllingObservation Obs(string key, DateTime ts, decimal? plan, decimal? actual)
        {
            return new ControllingObservation { CarrierKey = key, CarrierText = key, SourceTimestamp = ts, PlannedCost = plan, ActualCost = actual };
        }

        private static ChartRowInput Input(string key, decimal? rate, params ControllingObservation[] history)
        {
            var row = new ControllingOverviewRow { Observation = history.Last() };
            if (rate.HasValue) row.SetRate(new RateResolution(rate, false));
            return new ChartRowInput(row, history);
        }

        private static ChartData Percent(IReadOnlyList<ChartRowInput> rows, string selected = null, IReadOnlyList<LocalHoursPoint> local = null, int max = ControllingChartBuilder.MaxSeries)
        {
            return ControllingChartBuilder.Build(rows, selected, ChartMode.Percent, true, local, max);
        }

        private static ChartData Hours(IReadOnlyList<ChartRowInput> rows, string selected = null, IReadOnlyList<LocalHoursPoint> local = null, bool computed = true)
        {
            return ControllingChartBuilder.Build(rows, selected, ChartMode.Hours, computed, local);
        }

        // ---------- Relativ (%) ----------

        [Fact]
        public void Percent_shows_ist_divided_by_plan_per_sap_stand_with_a_100_percent_reference()
        {
            var data = Percent(new[] { Input("A", null, Obs("A", T1, 3000m, 1500m), Obs("A", T2, 3000m, 2250m)) });

            var series = data.Series.Single(s => s.Kind == ChartSeriesKind.Actual);
            Assert.Equal(new[] { 50.0, 75.0 }, series.Points.Select(p => p.Y).ToArray());
            Assert.Equal(new[] { T1, T2 }, series.Points.Select(p => p.X).ToArray());
            Assert.Equal("50,0 %", series.Points.First().ValueText);
            Assert.Contains("Ist 1.500,00 von Plan 3.000,00", series.Points.First().Tip);

            var reference = data.Series.Single(s => s.Kind == ChartSeriesKind.Reference);
            Assert.All(reference.Points, p => Assert.Equal(100.0, p.Y));
            Assert.True(data.YMax >= 100 && data.YMin <= 0);
            Assert.True(data.HasData);
        }

        [Fact]
        public void An_unknown_value_breaks_the_line_and_is_never_drawn_as_zero()
        {
            var data = Percent(new[]
            {
                Input("A", null, Obs("A", T1, 3000m, 1500m), Obs("A", T2, null, 2000m), Obs("A", T3, 3000m, 2400m))
            });

            var series = data.Series.Single(s => s.Kind == ChartSeriesKind.Actual);
            Assert.Equal(2, series.Segments.Count);                 // Lücke beim Stand ohne Plan
            Assert.Equal(new[] { 50.0, 80.0 }, series.Points.Select(p => p.Y).ToArray());
            Assert.DoesNotContain(series.Points, p => p.Y == 0);
        }

        [Fact]
        public void An_overrun_goes_above_100_and_the_axis_keeps_it_visible()
        {
            var data = Percent(new[] { Input("A", null, Obs("A", T1, 10000m, 12000m)) });

            Assert.Equal(120.0, data.Series.Single(s => s.Kind == ChartSeriesKind.Actual).Points.Single().Y);
            Assert.True(data.YMax >= 120);
        }

        [Fact]
        public void Rows_without_a_defined_stand_are_left_out_with_a_note_and_an_empty_chart_explains_itself()
        {
            var rows = new[] { Input("A", null, Obs("A", T1, null, 500m)), Input("B", null, Obs("B", T1, 0m, 500m)) };

            var data = Percent(rows);

            Assert.False(data.HasData);
            Assert.Contains(data.Notes, n => n.Contains("2 Zeile(n) ohne definierten Stand"));
            Assert.Contains("Kein Stand zu zeichnen", data.EmptyText);
        }

        [Fact]
        public void Series_are_limited_sorted_by_stand_and_the_selected_row_always_comes_first()
        {
            var rows = Enumerable.Range(1, 12)
                .Select(i => Input("R" + i.ToString("00"), null, Obs("R" + i.ToString("00"), T1, 1000m, i * 50m)))
                .ToList();

            var data = Percent(rows, selected: "R02", max: 3);

            var actual = data.Series.Where(s => s.Kind == ChartSeriesKind.Actual).ToList();
            Assert.Equal(new[] { "R02", "R12", "R11" }, actual.Select(s => s.RowKey).ToArray());   // gewählt zuerst, dann höchster Stand
            Assert.True(actual[0].IsSelected);
            Assert.False(actual[1].IsSelected);
            Assert.Contains(data.Notes, n => n.Contains("3 von 12 Zeilen"));
            Assert.Equal(new[] { 0, 1, 2 }, actual.Select(s => s.ColorIndex).ToArray());
        }

        [Fact]
        public void A_single_stand_is_one_point_in_the_middle_with_no_invented_line()
        {
            var data = Percent(new[] { Input("A", null, Obs("A", T3, 1000m, 400m)) });

            var series = data.Series.Single(s => s.Kind == ChartSeriesKind.Actual);
            Assert.Single(series.Points);
            Assert.True(data.XMin < T3 && data.XMax > T3);
        }

        // ---------- Stunden ----------

        [Fact]
        public void Hours_convert_money_with_the_rate_and_only_for_rows_with_an_unambiguous_rate()
        {
            var rows = new[]
            {
                Input("A", 100m, Obs("A", T1, 10000m, 8000m), Obs("A", T2, 10000m, 12000m)),
                Input("B", null, Obs("B", T1, 10000m, 5000m))     // kein Satz
            };

            var data = Hours(rows);

            var actual = data.Series.Single(s => s.Kind == ChartSeriesKind.Actual);
            Assert.Equal(new[] { 80.0, 120.0 }, actual.Points.Select(p => p.Y).ToArray());
            Assert.Equal("120,00 h", actual.Points.Last().ValueText);
            Assert.Contains("A", actual.Name);
            Assert.DoesNotContain(data.Series, s => s.RowKey == "B");
            Assert.Contains(data.Notes, n => n.Contains("kein eindeutiger Stundensatz"));
        }

        [Fact]
        public void Ambiguous_rates_are_never_converted_in_the_chart()
        {
            var row = new ControllingOverviewRow { Observation = Obs("A", T1, 10000m, 8000m) };
            row.SetRate(new RateResolution(null, true));

            var data = Hours(new[] { new ChartRowInput(row, null) });

            Assert.False(data.HasData);
            Assert.Contains("Stundensatz", data.EmptyText);
        }

        [Fact]
        public void The_selected_row_also_shows_plan_in_hours_and_cumulated_local_hours()
        {
            var local = new[] { new LocalHoursPoint(T1.AddDays(-5), 0), new LocalHoursPoint(T1, 40), new LocalHoursPoint(T2, 70) };
            var data = Hours(new[] { Input("A", 100m, Obs("A", T1, 10000m, 8000m), Obs("A", T2, 10000m, 12000m)) }, selected: "A", local: local);

            var plan = data.Series.Single(s => s.Kind == ChartSeriesKind.Plan);
            Assert.True(plan.Dashed);
            Assert.All(plan.Points, p => Assert.Equal(100.0, p.Y));

            var booked = data.Series.Single(s => s.Kind == ChartSeriesKind.LocalBooked);
            Assert.Equal(-1, booked.ColorIndex);                                 // neutral: erkennbar andere Quelle
            Assert.Equal(new[] { 0.0, 40.0, 70.0 }, booked.Points.Select(p => p.Y).ToArray());
            Assert.Contains("nicht aus SAP", booked.Points.Last().Tip);
        }

        [Fact]
        public void Hiding_computed_hours_leaves_only_the_local_hours_of_the_selected_row()
        {
            var local = new[] { new LocalHoursPoint(T1, 40) };
            var data = Hours(new[] { Input("A", 100m, Obs("A", T1, 10000m, 8000m)) }, selected: "A", local: local, computed: false);

            Assert.Equal(new[] { ChartSeriesKind.LocalBooked }, data.Series.Select(s => s.Kind).ToArray());
            Assert.Contains(data.Notes, n => n.Contains("Berechnete Stunden sind ausgeblendet"));

            var none = Hours(new[] { Input("A", 100m, Obs("A", T1, 10000m, 8000m)) }, computed: false);
            Assert.False(none.HasData);
            Assert.Contains("ausgeblendet", none.EmptyText);
        }

        [Fact]
        public void Percent_mode_relates_local_hours_to_the_plan_in_hours()
        {
            // Plan 10.000 bei 100/h = 100 h; 25 gebuchte Stunden = 25 %
            var local = new[] { new LocalHoursPoint(T1, 25), new LocalHoursPoint(T2, 50) };
            var data = Percent(new[] { Input("A", 100m, Obs("A", T1, 10000m, 3000m)) }, selected: "A", local: local);

            var booked = data.Series.Single(s => s.Kind == ChartSeriesKind.LocalBooked);
            Assert.Equal(new[] { 25.0, 50.0 }, booked.Points.Select(p => p.Y).ToArray());
            Assert.Equal("25,0 % (25,00 h)", booked.Points.First().ValueText);
        }

        [Fact]
        public void Percent_mode_has_no_local_line_without_a_rate_or_a_positive_plan()
        {
            var local = new[] { new LocalHoursPoint(T1, 25) };

            Assert.DoesNotContain(Percent(new[] { Input("A", null, Obs("A", T1, 10000m, 3000m)) }, "A", local).Series, s => s.Kind == ChartSeriesKind.LocalBooked);
            Assert.DoesNotContain(Percent(new[] { Input("A", 100m, Obs("A", T1, null, 3000m)) }, "A", local).Series, s => s.Kind == ChartSeriesKind.LocalBooked);
        }

        // ---------- Lokale Stunden kumuliert ----------

        [Fact]
        public void Local_hours_accumulate_over_time_start_at_zero_and_count_each_entry_once()
        {
            var entries = new[]
            {
                new LocalTimeEntry { Id = 1, ProjectId = 1, DateText = "2026-11-02", Start = "09:00", End = "10:30" },
                new LocalTimeEntry { Id = 2, ProjectId = 2, DateText = "2026-11-02", Start = "11:00", End = "12:00" },
                new LocalTimeEntry { Id = 3, ProjectId = 2, DateText = "2026-11-04", Start = "23:30", End = "00:30" }
            };

            var points = ControllingChartBuilder.LocalCumulative(entries.Concat(entries));   // doppelt übergeben: zählt trotzdem einmal

            Assert.Equal(new DateTime(2026, 11, 1), points.First().Date);
            Assert.Equal(0.0, points.First().CumulativeHours);
            Assert.Equal(2.5, points.Single(p => p.Date == new DateTime(2026, 11, 2)).CumulativeHours);
            Assert.Equal(2.5, points.Single(p => p.Date == new DateTime(2026, 11, 3)).CumulativeHours);   // Tag ohne Buchung: Stand bleibt
            Assert.Equal(3.5, points.Last().CumulativeHours);
        }

        [Fact]
        public void No_local_entries_give_no_points_and_long_periods_are_summarized_weekly()
        {
            Assert.Empty(ControllingChartBuilder.LocalCumulative(new LocalTimeEntry[0]));

            var many = Enumerable.Range(0, 600)
                .Select(i => new LocalTimeEntry { Id = i + 1, ProjectId = 1, DateText = new DateTime(2025, 1, 1).AddDays(i).ToString("yyyy-MM-dd"), Start = "09:00", End = "10:00" })
                .ToList();
            var points = ControllingChartBuilder.LocalCumulative(many);

            Assert.True(points.Count < 150);                          // wöchentlich statt täglich
            Assert.Equal(600.0, points.Last().CumulativeHours);        // nichts geht verloren
        }

        // ---------- Achsen ----------

        [Theory]
        [InlineData(17.3, 20)]
        [InlineData(0.07, 0.1)]
        [InlineData(2.2, 2.5)]
        [InlineData(4.9, 5)]
        [InlineData(1, 1)]
        [InlineData(730, 1000)]
        public void Nice_steps_are_1_2_2_5_5_times_a_power_of_ten(double raw, double expected)
        {
            Assert.Equal(expected, ChartAxis.NiceStep(raw), 9);
        }

        [Fact]
        public void Nice_range_covers_the_data_and_ticks_do_not_drift()
        {
            double min, max, step;
            ChartAxis.NiceRange(0, 104, 5, out min, out max, out step);

            Assert.Equal(0.0, min);
            Assert.Equal(125.0, max);
            Assert.Equal(25.0, step);
            Assert.Equal(new[] { 0.0, 25.0, 50.0, 75.0, 100.0, 125.0 }, ChartAxis.Ticks(min, max, step).ToArray());   // 100 % ist ein Teilstrich

            ChartAxis.NiceRange(-3, 7.2, 5, out min, out max, out step);
            Assert.True(min <= -3 && max >= 7.2);
            Assert.Equal(0.0, ChartAxis.Ticks(min, max, step).First(t => Math.Abs(t) < 1e-9));          // die Null ist ein Teilstrich
            Assert.Equal("0,0 h", ChartAxis.FormatTick(0.0, 0.5, ChartMode.Hours));
            Assert.Equal("40 %", ChartAxis.FormatTick(40, 20, ChartMode.Percent));
        }

        [Fact]
        public void Time_ticks_pick_the_finest_step_that_stays_readable_and_align_to_the_calendar()
        {
            var shortRange = ChartAxis.TimeTicks(new DateTime(2026, 11, 1), new DateTime(2026, 11, 20), 8);
            Assert.True(shortRange.Count <= 8);

            var weeks = ChartAxis.TimeTicks(new DateTime(2026, 10, 1), new DateTime(2026, 12, 15), 8);
            Assert.All(weeks, t => Assert.Equal(DayOfWeek.Monday, t.Value.DayOfWeek));

            var months = ChartAxis.TimeTicks(new DateTime(2026, 1, 15), new DateTime(2026, 12, 20), 8);
            Assert.All(months, t => Assert.Equal(1, t.Value.Day));
            Assert.True(months.Count <= 8 && months.Count >= 3);

            var years = ChartAxis.TimeTicks(new DateTime(2020, 3, 1), new DateTime(2026, 12, 20), 8);
            Assert.All(years, t => Assert.Equal(1, t.Value.Month));
            Assert.Equal("2022", years.First(t => t.Value.Year == 2022).Label);

            Assert.Single(ChartAxis.TimeTicks(new DateTime(2026, 11, 1), new DateTime(2026, 11, 1), 8));
        }
    }
}
