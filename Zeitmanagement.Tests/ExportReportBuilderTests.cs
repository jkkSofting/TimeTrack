// Target: .NET Framework + C# 7.3
using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;
using Zeitmanagement.Helpers;

namespace Zeitmanagement.Tests
{
    /// <summary>Aufbereitung der Buchungen für den Tätigkeitsnachweis (Filter, Dauer, Rundung, Gliederung, Prüfungen).</summary>
    public class ExportReportBuilderTests
    {
        private static TimeTracker.TimeEntryRow E(int id, string date, string start, string end, string project, string description = "Arbeit")
        {
            return new TimeTracker.TimeEntryRow
            {
                Id = id,
                Datum = DateTime.ParseExact(date, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
                Startzeit = start,
                Endzeit = end,
                Projektname = project,
                Beschreibung = description
            };
        }

        private static TimeTracker.ProjectRow P(string name, string customer = "Kunde 1", string carrier = "KST-1", bool external = false)
        {
            return new TimeTracker.ProjectRow { Projektname = name, Kunde = customer, Kostentraeger = carrier, IstExtern = external };
        }

        private static ExportOptions Options(string from, string to, params string[] projects)
        {
            return new ExportOptions
            {
                From = DateTime.ParseExact(from, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
                To = DateTime.ParseExact(to, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
                ProjectNames = projects.Length == 0 ? null : projects.ToList()
            };
        }

        private static ExportReport Build(IEnumerable<TimeTracker.TimeEntryRow> entries, ExportOptions options, params TimeTracker.ProjectRow[] projects)
        {
            return ExportReportBuilder.Build(entries, projects.Length == 0 ? new[] { P("A"), P("B") } : projects, options);
        }

        // ---------- Filter ----------

        [Fact]
        public void Range_is_inclusive_on_both_ends_and_other_days_are_left_out()
        {
            var entries = new[]
            {
                E(1, "2026-09-30", "09:00", "10:00", "A"),
                E(2, "2026-10-01", "09:00", "10:00", "A"),
                E(3, "2026-10-31", "09:00", "10:00", "A"),
                E(4, "2026-11-01", "09:00", "10:00", "A")
            };

            var report = Build(entries, Options("2026-10-01", "2026-10-31"));

            Assert.Equal(new[] { 2, 3 }, report.Lines.Select(l => l.Id).ToArray());
            Assert.Equal(new DateTime(2026, 10, 1), report.FirstDate);
            Assert.Equal(new DateTime(2026, 10, 31), report.LastDate);
        }

        [Fact]
        public void Only_the_chosen_projects_are_included()
        {
            var entries = new[]
            {
                E(1, "2026-10-05", "09:00", "10:00", "A"),
                E(2, "2026-10-05", "10:00", "11:00", "B")
            };

            var onlyB = Build(entries, Options("2026-10-01", "2026-10-31", "B"));
            var none = Build(entries, new ExportOptions { From = new DateTime(2026, 10, 1), To = new DateTime(2026, 10, 31), ProjectNames = new List<string>() });
            var all = Build(entries, Options("2026-10-01", "2026-10-31"));

            Assert.Equal(new[] { "B" }, onlyB.Lines.Select(l => l.Project).ToArray());
            Assert.True(none.IsEmpty);
            Assert.Equal(2, all.EntryCount);
        }

        [Fact]
        public void External_and_internal_filter_use_the_project_flag()
        {
            var entries = new[]
            {
                E(1, "2026-10-05", "09:00", "10:00", "Extern"),
                E(2, "2026-10-05", "10:00", "11:00", "Intern")
            };
            var projects = new[] { P("Extern", external: true), P("Intern", external: false) };

            var options = Options("2026-10-01", "2026-10-31");
            options.Kind = ExportProjectKind.ExternalOnly;
            Assert.Equal(new[] { "Extern" }, ExportReportBuilder.Build(entries, projects, options).Lines.Select(l => l.Project).ToArray());

            options.Kind = ExportProjectKind.InternalOnly;
            Assert.Equal(new[] { "Intern" }, ExportReportBuilder.Build(entries, projects, options).Lines.Select(l => l.Project).ToArray());
        }

        [Fact]
        public void Customer_and_carrier_come_from_the_project()
        {
            var report = Build(new[] { E(1, "2026-10-05", "09:00", "10:00", "A") }, Options("2026-10-01", "2026-10-31"),
                P("A", "Muster GmbH", "1500410A24RD001.01", true));

            var line = report.Lines.Single();
            Assert.Equal("Muster GmbH", line.Customer);
            Assert.Equal("1500410A24RD001.01", line.Carrier);
            Assert.True(line.IsExternal);
        }

        [Fact]
        public void The_same_entry_counts_once_even_if_passed_twice()
        {
            var entry = E(7, "2026-10-05", "09:00", "10:00", "A");

            var report = Build(new[] { entry, entry }, Options("2026-10-01", "2026-10-31"));

            Assert.Equal(1, report.EntryCount);
            Assert.Equal(60, report.TotalMinutes);
        }

        [Fact]
        public void An_empty_selection_gives_an_empty_report_without_warnings()
        {
            var report = Build(new TimeTracker.TimeEntryRow[0], Options("2026-10-01", "2026-10-31"));

            Assert.True(report.IsEmpty);
            Assert.Equal(0, report.TotalMinutes);
            Assert.Equal(0, report.WorkDays);
            Assert.Equal(0, report.AverageMinutesPerWorkDay);
            Assert.Null(report.FirstDate);
            Assert.Empty(report.Groups);
            Assert.Empty(report.Warnings);
        }

        // ---------- Dauer und Zeiten ----------

        [Fact]
        public void An_entry_over_midnight_counts_for_the_stored_date_and_is_flagged()
        {
            var report = Build(new[] { E(1, "2026-10-05", "23:30", "00:30", "A") }, Options("2026-10-01", "2026-10-31"));

            var line = report.Lines.Single();
            Assert.Equal(60, line.Minutes);
            Assert.True(line.Overnight);
            Assert.Equal(new DateTime(2026, 10, 5), line.Date);
            Assert.Contains(report.Warnings, w => w.Kind == ExportWarningKind.Overnight && w.Severity == ExportWarningSeverity.Info);
        }

        [Fact]
        public void Unreadable_times_are_left_out_and_reported_never_counted()
        {
            var entries = new[]
            {
                E(1, "2026-10-05", "09:00", "10:00", "A"),
                E(2, "2026-10-05", "abc", "10:00", "A"),
                E(3, "2026-10-05", "09:00", null, "A")
            };

            var report = Build(entries, Options("2026-10-01", "2026-10-31"));

            Assert.Equal(1, report.EntryCount);
            Assert.Equal(60, report.TotalMinutes);
            Assert.Equal(new[] { 2, 3 }, report.Skipped.Select(s => s.Id).ToArray());
            var warning = report.Warnings.Single(w => w.Kind == ExportWarningKind.InvalidTimes);
            Assert.Equal(ExportWarningSeverity.Warning, warning.Severity);
            Assert.Equal(new[] { 2, 3 }, warning.EntryIds.ToArray());
            Assert.Contains(report.Notes, n => n.Contains("nicht lesbaren Zeiten"));
        }

        [Fact]
        public void Unreadable_entries_outside_the_filter_are_not_reported()
        {
            var entries = new[] { E(1, "2026-09-05", "kaputt", "10:00", "A"), E(2, "2026-10-05", "09:00", "10:00", "B") };

            var report = Build(entries, Options("2026-10-01", "2026-10-31", "B"));

            Assert.Empty(report.Skipped);
            Assert.DoesNotContain(report.Warnings, w => w.Kind == ExportWarningKind.InvalidTimes);
        }

        [Fact]
        public void Entries_are_sorted_by_the_parsed_start_not_by_the_stored_text()
        {
            // "9:00" steht so in der Datenbank, wenn UpdateTimeEntry ungeprüft speichert; als Text läge es hinter "10:00".
            var entries = new[]
            {
                E(1, "2026-10-05", "10:00", "11:00", "A"),
                E(2, "2026-10-05", "9:00", "9:45", "A"),
                E(3, "2026-10-04", "23:00", "23:30", "A")
            };

            var report = Build(entries, Options("2026-10-01", "2026-10-31"));

            Assert.Equal(new[] { 3, 2, 1 }, report.Lines.Select(l => l.Id).ToArray());
            var nine = report.Lines.Single(l => l.Id == 2);
            Assert.Equal("09:00", nine.Start);
            Assert.Equal("09:45", nine.End);
            Assert.Equal(45, nine.Minutes);
        }

        [Fact]
        public void Description_is_trimmed_and_never_null()
        {
            var report = Build(new[]
            {
                E(1, "2026-10-05", "09:00", "10:00", "A", "  Konzept  \r\n"),
                E(2, "2026-10-05", "10:00", "11:00", "A", null)
            }, Options("2026-10-01", "2026-10-31"));

            Assert.Equal("Konzept", report.Lines[0].Description);
            Assert.Equal(string.Empty, report.Lines[1].Description);
        }

        // ---------- Rundung ----------

        [Theory]
        [InlineData(0, 15, 0)]
        [InlineData(7, 15, 0)]    // knapp unter der Hälfte: ab
        [InlineData(8, 15, 15)]   // knapp über der Hälfte: auf
        [InlineData(22, 15, 15)]
        [InlineData(23, 15, 30)]
        [InlineData(60, 15, 60)]
        [InlineData(37, 30, 30)]
        [InlineData(45, 30, 60)]  // genau die Hälfte rundet auf
        [InlineData(13, 0, 13)]   // 0 = nicht runden
        [InlineData(13, 1, 13)]
        public void Rounding_goes_to_the_nearest_step_and_half_rounds_up(int minutes, int step, int expected)
        {
            Assert.Equal(expected, ExportReportBuilder.Round(minutes, step));
        }

        [Fact]
        public void Rounding_applies_per_entry_and_the_note_names_the_unrounded_total()
        {
            var entries = new[]
            {
                E(1, "2026-10-05", "09:00", "09:08", "A"),   // 8  -> 15
                E(2, "2026-10-05", "10:00", "10:22", "A"),   // 22 -> 15
                E(3, "2026-10-05", "11:00", "12:00", "A")    // 60 -> 60
            };
            var options = Options("2026-10-01", "2026-10-31");
            options.RoundingMinutes = 15;

            var report = Build(entries, options);

            Assert.Equal(new[] { 15, 15, 60 }, report.Lines.Select(l => l.Minutes).ToArray());
            Assert.Equal(new[] { 8, 22, 60 }, report.Lines.Select(l => l.RawMinutes).ToArray());
            Assert.Equal(90, report.TotalMinutes);
            Assert.Equal(90, report.RawTotalMinutes);
            Assert.Contains(report.Notes, n => n.Contains("auf 15 Minuten gerundet") && n.Contains("1,50 h"));
        }

        [Fact]
        public void Rounding_down_to_zero_is_warned_and_counts_nothing()
        {
            var options = Options("2026-10-01", "2026-10-31");
            options.RoundingMinutes = 15;

            var report = Build(new[] { E(1, "2026-10-05", "09:00", "09:05", "A") }, options);

            Assert.Equal(0, report.TotalMinutes);
            Assert.Equal(5, report.RawTotalMinutes);
            Assert.Contains(report.Warnings, w => w.Kind == ExportWarningKind.RoundedToZero);
        }

        [Fact]
        public void Without_rounding_there_is_no_rounding_note()
        {
            var report = Build(new[] { E(1, "2026-10-05", "09:00", "10:00", "A") }, Options("2026-10-01", "2026-10-31"));

            Assert.DoesNotContain(report.Notes, n => n.Contains("gerundet auf") || n.Contains("kaufmännisch"));
        }

        [Fact]
        public void Decimal_note_appears_only_when_the_shown_values_do_not_add_up()
        {
            // 3 x 7 Minuten = je "0,12" (zusammen 0,36), die Summe 21 Minuten ist "0,35".
            var uneven = new[]
            {
                E(1, "2026-10-05", "09:00", "09:07", "A"),
                E(2, "2026-10-05", "10:00", "10:07", "A"),
                E(3, "2026-10-05", "11:00", "11:07", "A")
            };
            var even = new[]
            {
                E(1, "2026-10-05", "09:00", "09:15", "A"),
                E(2, "2026-10-05", "10:00", "10:15", "A")
            };
            var options = Options("2026-10-01", "2026-10-31");

            Assert.Contains(Build(uneven, options).Notes, n => n.Contains("auf zwei Stellen gerundet"));
            Assert.DoesNotContain(Build(even, options).Notes, n => n.Contains("auf zwei Stellen gerundet"));

            options.DurationFormat = ExportDurationFormat.Clock;
            Assert.DoesNotContain(Build(uneven, options).Notes, n => n.Contains("auf zwei Stellen gerundet"));
        }

        // ---------- Gliederung und Summen ----------

        [Fact]
        public void Grouping_by_day_builds_one_group_per_day_with_subtotals()
        {
            var entries = new[]
            {
                E(1, "2026-10-05", "09:00", "10:30", "A"),
                E(2, "2026-10-05", "11:00", "12:00", "B"),
                E(3, "2026-10-07", "09:00", "10:00", "A")
            };

            var report = Build(entries, Options("2026-10-01", "2026-10-31"));

            Assert.Equal(2, report.Groups.Count);
            Assert.Equal("Mo, 05.10.2026", report.Groups[0].Title);
            Assert.Equal(150, report.Groups[0].Minutes);
            Assert.Equal(2, report.Groups[0].Count);
            Assert.Equal("Mi, 07.10.2026", report.Groups[1].Title);
            Assert.Equal(60, report.Groups[1].Minutes);
        }

        [Fact]
        public void Grouping_by_project_sorts_the_groups_by_name_and_keeps_time_order_inside()
        {
            var entries = new[]
            {
                E(1, "2026-10-06", "09:00", "10:00", "Zeta"),
                E(2, "2026-10-05", "09:00", "10:00", "Zeta"),
                E(3, "2026-10-05", "11:00", "12:00", "Ärger")
            };
            var options = Options("2026-10-01", "2026-10-31");
            options.Grouping = ExportGrouping.ByProject;

            var report = Build(entries, options, P("Zeta", "K1", "C1"), P("Ärger", "K2", "C2"));

            Assert.Equal(new[] { "Ärger", "Zeta" }, report.Groups.Select(g => g.Title).ToArray());
            Assert.Equal("K1 · C1", report.Groups[1].Subtitle);
            Assert.Equal(new[] { 2, 1 }, report.Groups[1].Lines.Select(l => l.Id).ToArray());
            Assert.Equal(120, report.Groups[1].Minutes);
        }

        [Fact]
        public void Grouping_by_iso_week_handles_the_turn_of_the_year()
        {
            // 2026 hat 53 Kalenderwochen: Do 31.12.2026 und Fr 01.01.2027 gehören beide zu KW 53, Mo 04.01.2027 beginnt KW 1.
            var entries = new[]
            {
                E(1, "2026-12-31", "09:00", "10:00", "A"),
                E(2, "2027-01-01", "09:00", "10:00", "A"),
                E(3, "2027-01-04", "09:00", "10:00", "A")
            };
            var options = Options("2026-12-29", "2027-01-05");
            options.Grouping = ExportGrouping.ByWeek;

            var report = Build(entries, options);

            Assert.Equal(new[] { "KW 53 / 2026", "KW 01 / 2027" }, report.Groups.Select(g => g.Title).ToArray());
            Assert.Equal(120, report.Groups[0].Minutes);
            // Der Datumsbereich wird auf den gewählten Zeitraum begrenzt.
            Assert.Equal("29.12.2026 – 03.01.2027", report.Groups[0].Subtitle);
            Assert.Equal("04.01.2027 – 05.01.2027", report.Groups[1].Subtitle);
        }

        [Fact]
        public void Totals_work_days_and_average_follow_from_the_lines()
        {
            var entries = new[]
            {
                E(1, "2026-10-05", "09:00", "11:00", "A"),   // 120
                E(2, "2026-10-05", "12:00", "13:00", "A"),   //  60
                E(3, "2026-10-06", "09:00", "10:00", "A")    //  60
            };

            var report = Build(entries, Options("2026-10-01", "2026-10-31"));

            Assert.Equal(240, report.TotalMinutes);
            Assert.Equal(2, report.WorkDays);
            Assert.Equal(120, report.AverageMinutesPerWorkDay);
            Assert.Equal(report.TotalMinutes, report.Groups.Sum(g => g.Minutes));
            Assert.Equal(report.TotalMinutes, report.Projects.Sum(p => p.Minutes));
        }

        [Fact]
        public void Project_summary_is_sorted_by_hours_and_shows_the_share()
        {
            var entries = new[]
            {
                E(1, "2026-10-05", "09:00", "10:00", "Klein"),   //  60
                E(2, "2026-10-05", "10:00", "13:00", "Gross")    // 180
            };

            var report = Build(entries, Options("2026-10-01", "2026-10-31"), P("Klein"), P("Gross"));

            Assert.Equal(new[] { "Gross", "Klein" }, report.Projects.Select(p => p.Project).ToArray());
            Assert.Equal(75.0, report.Projects[0].SharePercent, 6);
            Assert.Equal(25.0, report.Projects[1].SharePercent, 6);
            Assert.True(report.ShowProjectColumn);
        }

        [Fact]
        public void Project_column_is_automatic_for_several_projects_unless_chosen()
        {
            var one = Build(new[] { E(1, "2026-10-05", "09:00", "10:00", "A") }, Options("2026-10-01", "2026-10-31"));
            Assert.False(one.ShowProjectColumn);

            var options = Options("2026-10-01", "2026-10-31");
            options.ShowProjectColumn = true;
            Assert.True(Build(new[] { E(1, "2026-10-05", "09:00", "10:00", "A") }, options).ShowProjectColumn);
        }

        // ---------- Prüfungen ----------

        [Fact]
        public void Clean_data_has_no_warnings()
        {
            var entries = new[]
            {
                E(1, "2026-10-05", "09:00", "10:00", "A"),
                E(2, "2026-10-05", "10:00", "11:00", "A")   // grenzt nur an, überschneidet nicht
            };

            var report = Build(entries, Options("2026-10-01", "2026-10-31"));

            Assert.Empty(report.Warnings);
            Assert.False(report.HasWarnings);
        }

        [Fact]
        public void Missing_description_counts_blank_and_whitespace_texts()
        {
            var entries = new[]
            {
                E(1, "2026-10-05", "09:00", "10:00", "A", null),
                E(2, "2026-10-05", "10:00", "11:00", "A", "   "),
                E(3, "2026-10-05", "11:00", "12:00", "A", "Konzept")
            };

            var report = Build(entries, Options("2026-10-01", "2026-10-31"));

            var warning = report.Warnings.Single(w => w.Kind == ExportWarningKind.MissingDescription);
            Assert.Equal(ExportWarningSeverity.Warning, warning.Severity);
            Assert.Equal("2 Buchungen haben keine Beschreibung.", warning.Message);
            Assert.Equal(new[] { 1, 2 }, warning.EntryIds.ToArray());
        }

        [Fact]
        public void Overlap_in_the_same_project_is_a_warning_and_names_the_first_day()
        {
            var entries = new[]
            {
                E(1, "2026-10-05", "09:00", "10:00", "A"),
                E(2, "2026-10-05", "09:30", "11:00", "A"),
                E(3, "2026-10-05", "13:00", "14:00", "A")
            };

            var report = Build(entries, Options("2026-10-01", "2026-10-31"));

            var warning = report.Warnings.Single(w => w.Kind == ExportWarningKind.OverlapSameProject);
            Assert.Equal(ExportWarningSeverity.Warning, warning.Severity);
            Assert.Equal(new[] { 1, 2 }, warning.EntryIds.ToArray());
            Assert.Contains("05.10.2026", warning.Message);
        }

        [Fact]
        public void Parallel_bookings_of_different_projects_are_only_information()
        {
            var entries = new[]
            {
                E(1, "2026-10-05", "09:00", "10:00", "A"),
                E(2, "2026-10-05", "09:30", "10:30", "B")
            };

            var report = Build(entries, Options("2026-10-01", "2026-10-31"));

            Assert.DoesNotContain(report.Warnings, w => w.Kind == ExportWarningKind.OverlapSameProject);
            var info = report.Warnings.Single(w => w.Kind == ExportWarningKind.ParallelProjects);
            Assert.Equal(ExportWarningSeverity.Info, info.Severity);
            Assert.Equal(120, report.TotalMinutes);   // jede Buchung zählt voll
        }

        [Fact]
        public void An_entry_over_midnight_overlaps_the_next_days_early_entry()
        {
            var entries = new[]
            {
                E(1, "2026-10-05", "23:00", "01:00", "A"),
                E(2, "2026-10-06", "00:30", "02:00", "A")
            };

            var report = Build(entries, Options("2026-10-01", "2026-10-31"));

            Assert.Contains(report.Warnings, w => w.Kind == ExportWarningKind.OverlapSameProject);
        }

        [Fact]
        public void A_zero_length_entry_is_warned_but_never_overlaps()
        {
            var entries = new[]
            {
                E(1, "2026-10-05", "09:00", "10:00", "A"),
                E(2, "2026-10-05", "09:30", "09:30", "A")
            };

            var report = Build(entries, Options("2026-10-01", "2026-10-31"));

            Assert.Contains(report.Warnings, w => w.Kind == ExportWarningKind.ZeroDuration);
            Assert.DoesNotContain(report.Warnings, w => w.Kind == ExportWarningKind.OverlapSameProject);
        }

        [Fact]
        public void A_day_with_more_than_twelve_hours_is_noted()
        {
            var entries = new[]
            {
                E(1, "2026-10-05", "06:00", "14:00", "A"),
                E(2, "2026-10-05", "14:00", "19:30", "A")   // zusammen 13,5 h
            };

            var report = Build(entries, Options("2026-10-01", "2026-10-31"));

            var info = report.Warnings.Single(w => w.Kind == ExportWarningKind.LongDay);
            Assert.Equal(ExportWarningSeverity.Info, info.Severity);
            Assert.Contains("05.10.2026 (13,50 h)", info.Message);
        }

        [Fact]
        public void Exactly_twelve_hours_is_not_noted()
        {
            var report = Build(new[] { E(1, "2026-10-05", "06:00", "18:00", "A") }, Options("2026-10-01", "2026-10-31"));

            Assert.DoesNotContain(report.Warnings, w => w.Kind == ExportWarningKind.LongDay);
        }

        [Fact]
        public void Warnings_come_before_information()
        {
            var entries = new[]
            {
                E(1, "2026-10-05", "23:00", "00:30", "A", "Nacht"),   // über Mitternacht = Info
                E(2, "2026-10-06", "09:00", "10:00", "A", null)       // fehlende Beschreibung = Warnung
            };

            var report = Build(entries, Options("2026-10-01", "2026-10-31"));

            Assert.Equal(ExportWarningSeverity.Warning, report.Warnings.First().Severity);
            Assert.Equal(ExportWarningSeverity.Info, report.Warnings.Last().Severity);
        }

        // ---------- Format ----------

        [Fact]
        public void Format_is_german_and_independent_of_the_windows_language()
        {
            Assert.Equal("1,50", ExportFormat.Decimal(90));
            Assert.Equal("1:30", ExportFormat.Clock(90));
            Assert.Equal("26:30", ExportFormat.Clock(26 * 60 + 30));
            Assert.Equal("1,50 (1:30)", ExportFormat.Duration(90, ExportDurationFormat.Both));
            Assert.Equal("Mo", ExportFormat.WeekdayShort(new DateTime(2026, 10, 5)));
            Assert.Equal("Sonntag", ExportFormat.WeekdayLong(new DateTime(2026, 10, 11)));
            Assert.Equal("05.10.2026", ExportFormat.Date(new DateTime(2026, 10, 5)));
            Assert.Equal("05.10.2026", ExportFormat.Period(new DateTime(2026, 10, 5), new DateTime(2026, 10, 5)));
            Assert.Equal("01.10.2026 – 31.10.2026", ExportFormat.Period(new DateTime(2026, 10, 1), new DateTime(2026, 10, 31)));
        }
    }
}
