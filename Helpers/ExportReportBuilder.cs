// Target: .NET Framework + C# 7.3
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Zeitmanagement.Helpers
{
    /// <summary>
    /// Bereitet Buchungen für den Tätigkeitsnachweis auf: filtert, ermittelt die Dauer, rundet auf Wunsch, gliedert, summiert
    /// und prüft die Daten. Eine reine Funktion ohne Datenbank und ohne WPF, damit PDF, CSV und Vorschau dieselben Zahlen zeigen.
    ///
    /// Grundsätze:
    ///  - Dauer wie in der Projektansicht (<see cref="LocalHoursCalculator.TryGetDurationMinutes"/>): Ende vor Beginn heißt über
    ///    Mitternacht, die Dauer zählt am gespeicherten Datum.
    ///  - Jede Buchung zählt einzeln, die Summe ist die Summe der Einzeldauern. Parallele Buchungen zählen mehrfach.
    ///  - Buchungen mit nicht lesbaren Zeiten werden nie mit erfundener Dauer gezählt, sondern ausgelassen und gemeldet.
    ///  - Sortiert wird nach dem gelesenen Beginn, nicht nach dem Text: Die Datenbank kann "9:00" enthalten, das als Text hinter "10:00" läge.
    /// </summary>
    public static class ExportReportBuilder
    {
        /// <summary>Ab so vielen Minuten an einem Tag gibt es einen Hinweis.</summary>
        public const int LongDayMinutes = 12 * 60;

        private const int MinutesPerDay = 24 * 60;
        private static readonly string[] TimeFormats = { @"h\:mm", @"hh\:mm" };
        private static readonly DateTime Epoch = new DateTime(2000, 1, 1);

        public static ExportReport Build(IEnumerable<TimeTracker.TimeEntryRow> entries, IEnumerable<TimeTracker.ProjectRow> projects, ExportOptions options)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));

            var report = new ExportReport { Options = options };
            var from = options.From.Date;
            var to = options.To.Date;

            var projectByName = new Dictionary<string, TimeTracker.ProjectRow>(StringComparer.Ordinal);
            foreach (var p in projects ?? Enumerable.Empty<TimeTracker.ProjectRow>())
            {
                if (p != null && p.Projektname != null) projectByName[p.Projektname] = p;
            }

            var selected = options.ProjectNames == null ? null : new HashSet<string>(options.ProjectNames, StringComparer.Ordinal);
            var seenIds = new HashSet<int>();

            foreach (var entry in entries ?? Enumerable.Empty<TimeTracker.TimeEntryRow>())
            {
                if (entry == null) continue;

                var date = entry.Datum.Date;
                if (date < from || date > to) continue;

                var projectName = entry.Projektname ?? string.Empty;
                if (selected != null && !selected.Contains(projectName)) continue;

                TimeTracker.ProjectRow project;
                projectByName.TryGetValue(projectName, out project);
                var external = project != null ? project.IstExtern : entry.IstExtern;
                if (options.Kind == ExportProjectKind.ExternalOnly && !external) continue;
                if (options.Kind == ExportProjectKind.InternalOnly && external) continue;

                // Dieselbe Buchung zählt nur einmal, auch wenn sie mehrfach übergeben wird (Id 0 = unbekannt, nie zusammenfassen).
                if (entry.Id > 0 && !seenIds.Add(entry.Id)) continue;

                int startMinute, endMinute, rawMinutes;
                if (!TryParseTime(entry.Startzeit, out startMinute) ||
                    !TryParseTime(entry.Endzeit, out endMinute) ||
                    !LocalHoursCalculator.TryGetDurationMinutes(entry.Startzeit, entry.Endzeit, out rawMinutes))
                {
                    report.Skipped.Add(new InvalidTimeEntry
                    {
                        Id = entry.Id,
                        DateText = ExportFormat.IsoDate(date),
                        Start = entry.Startzeit,
                        End = entry.Endzeit,
                        Reason = "Start- oder Endzeit nicht lesbar"
                    });
                    continue;
                }

                report.Lines.Add(new ExportLine
                {
                    Id = entry.Id,
                    Date = date,
                    StartMinute = startMinute,
                    Start = ExportFormat.TimeOfDay(startMinute),
                    End = ExportFormat.TimeOfDay(endMinute),
                    RawMinutes = rawMinutes,
                    Minutes = Round(rawMinutes, options.RoundingMinutes),
                    Overnight = endMinute < startMinute,
                    Project = projectName,
                    Customer = project?.Kunde ?? string.Empty,
                    Carrier = project?.Kostentraeger ?? string.Empty,
                    IsExternal = external,
                    Description = (entry.Beschreibung ?? string.Empty).Trim()
                });
            }

            var ordered = report.Lines
                .OrderBy(l => l.Date)
                .ThenBy(l => l.StartMinute)
                .ThenBy(l => l.RawMinutes)
                .ThenBy(l => l.Project, StringComparer.Ordinal)
                .ThenBy(l => l.Id)
                .ToList();
            report.Lines.Clear();
            report.Lines.AddRange(ordered);

            report.TotalMinutes = report.Lines.Sum(l => l.Minutes);
            report.RawTotalMinutes = report.Lines.Sum(l => l.RawMinutes);
            report.WorkDays = report.Lines.Select(l => l.Date).Distinct().Count();
            if (report.Lines.Count > 0)
            {
                report.FirstDate = report.Lines[0].Date;
                report.LastDate = report.Lines[report.Lines.Count - 1].Date;
            }

            BuildGroups(report, options, from, to);
            BuildProjectSummaries(report);
            BuildWarnings(report);
            BuildNotes(report, options);
            return report;
        }

        /// <summary>Rundet auf das nächste Vielfache von <paramref name="step"/> (kaufmännisch: die Hälfte rundet auf).</summary>
        public static int Round(int minutes, int step)
        {
            if (step <= 1) return minutes;
            return (int)(Math.Round(minutes / (double)step, MidpointRounding.AwayFromZero) * step);
        }

        private static bool TryParseTime(string text, out int minuteOfDay)
        {
            minuteOfDay = 0;
            TimeSpan time;
            if (text == null || !TimeSpan.TryParseExact(text.Trim(), TimeFormats, CultureInfo.InvariantCulture, out time)) return false;
            minuteOfDay = (int)time.TotalMinutes;
            return true;
        }

        // ---------- Gliederung und Summen ----------

        private static void BuildGroups(ExportReport report, ExportOptions options, DateTime from, DateTime to)
        {
            switch (options.Grouping)
            {
                case ExportGrouping.ByProject:
                    var german = StringComparer.Create(ExportFormat.German, true);
                    foreach (var byProject in report.Lines.GroupBy(l => l.Project).OrderBy(g => g.Key, german))
                    {
                        var first = byProject.First();
                        var group = new ExportGroup
                        {
                            Title = byProject.Key,
                            Subtitle = string.Join(" · ", new[] { first.Customer, first.Carrier }.Where(s => !string.IsNullOrWhiteSpace(s)))
                        };
                        group.Lines.AddRange(byProject);
                        group.Minutes = group.Lines.Sum(l => l.Minutes);
                        report.Groups.Add(group);
                    }
                    break;

                case ExportGrouping.ByWeek:
                    foreach (var byWeek in report.Lines.GroupBy(l => LocalHoursCalculator.BucketStart(l.Date, HoursBucketKind.Week)).OrderBy(g => g.Key))
                    {
                        int isoYear;
                        var week = LocalHoursCalculator.IsoWeek(byWeek.Key, out isoYear);
                        var start = byWeek.Key < from ? from : byWeek.Key;
                        var end = byWeek.Key.AddDays(6) > to ? to : byWeek.Key.AddDays(6);
                        var group = new ExportGroup
                        {
                            Title = "KW " + week.ToString("00", CultureInfo.InvariantCulture) + " / " + isoYear.ToString(CultureInfo.InvariantCulture),
                            Subtitle = ExportFormat.Period(start, end)
                        };
                        group.Lines.AddRange(byWeek);
                        group.Minutes = group.Lines.Sum(l => l.Minutes);
                        report.Groups.Add(group);
                    }
                    break;

                default:
                    foreach (var byDay in report.Lines.GroupBy(l => l.Date).OrderBy(g => g.Key))
                    {
                        var group = new ExportGroup { Title = ExportFormat.WeekdayShort(byDay.Key) + ", " + ExportFormat.Date(byDay.Key), Subtitle = string.Empty };
                        group.Lines.AddRange(byDay);
                        group.Minutes = group.Lines.Sum(l => l.Minutes);
                        report.Groups.Add(group);
                    }
                    break;
            }
        }

        private static void BuildProjectSummaries(ExportReport report)
        {
            var summaries = report.Lines
                .GroupBy(l => l.Project)
                .Select(g =>
                {
                    var first = g.First();
                    var minutes = g.Sum(l => l.Minutes);
                    return new ExportProjectSummary
                    {
                        Project = g.Key,
                        Customer = first.Customer,
                        Carrier = first.Carrier,
                        IsExternal = first.IsExternal,
                        Count = g.Count(),
                        Minutes = minutes,
                        SharePercent = report.TotalMinutes > 0 ? minutes * 100.0 / report.TotalMinutes : 0
                    };
                })
                .OrderByDescending(s => s.Minutes)
                .ThenBy(s => s.Project, StringComparer.Create(ExportFormat.German, true));
            report.Projects.AddRange(summaries);
        }

        // ---------- Prüfungen ----------

        private static void BuildWarnings(ExportReport report)
        {
            var lines = report.Lines;

            if (report.Skipped.Count > 0)
            {
                var n = report.Skipped.Count;
                var w = Warning(ExportWarningKind.InvalidTimes, ExportWarningSeverity.Warning,
                    n == 1 ? "1 Buchung hat nicht lesbare Zeiten und fehlt im Nachweis."
                           : n + " Buchungen haben nicht lesbare Zeiten und fehlen im Nachweis.");
                w.EntryIds.AddRange(report.Skipped.Where(s => s.Id > 0).Select(s => s.Id));
                report.Warnings.Add(w);
            }

            // Überschneidungen: zeitlich über Tagesgrenzen hinweg verglichen, damit auch Buchungen über Mitternacht erfasst sind.
            var sameProject = new bool[lines.Count];
            var parallel = new bool[lines.Count];
            DateTime? firstSameDate = null;
            var absStart = lines.Select(l => (long)(l.Date - Epoch).Days * MinutesPerDay + l.StartMinute).ToArray();
            for (var i = 0; i < lines.Count; i++)
            {
                var end = absStart[i] + lines[i].RawMinutes;
                for (var j = i + 1; j < lines.Count && absStart[j] < end; j++)
                {
                    if (lines[j].RawMinutes == 0) continue;
                    if (string.Equals(lines[i].Project, lines[j].Project, StringComparison.Ordinal))
                    {
                        sameProject[i] = sameProject[j] = true;
                        if (!firstSameDate.HasValue) firstSameDate = lines[j].Date;
                    }
                    else
                    {
                        parallel[i] = parallel[j] = true;
                    }
                }
            }

            var overlapCount = sameProject.Count(b => b);
            if (overlapCount > 0)
            {
                var w = Warning(ExportWarningKind.OverlapSameProject, ExportWarningSeverity.Warning,
                    overlapCount + " Buchungen überschneiden sich mit einer anderen Buchung desselben Projekts (erstmals am " +
                    ExportFormat.Date(firstSameDate.Value) + "): Die Zeit ist doppelt gebucht und zählt doppelt.");
                AddIds(w, lines, sameProject);
                report.Warnings.Add(w);
            }

            var missing = lines.Where(l => string.IsNullOrWhiteSpace(l.Description)).ToList();
            if (missing.Count > 0)
            {
                var w = Warning(ExportWarningKind.MissingDescription, ExportWarningSeverity.Warning,
                    missing.Count == 1 ? "1 Buchung hat keine Beschreibung." : missing.Count + " Buchungen haben keine Beschreibung.");
                w.EntryIds.AddRange(missing.Where(l => l.Id > 0).Select(l => l.Id));
                report.Warnings.Add(w);
            }

            var zero = lines.Where(l => l.RawMinutes == 0).ToList();
            if (zero.Count > 0)
            {
                var w = Warning(ExportWarningKind.ZeroDuration, ExportWarningSeverity.Warning,
                    zero.Count == 1 ? "1 Buchung hat die Dauer 0." : zero.Count + " Buchungen haben die Dauer 0.");
                w.EntryIds.AddRange(zero.Where(l => l.Id > 0).Select(l => l.Id));
                report.Warnings.Add(w);
            }

            var roundedToZero = lines.Where(l => l.RawMinutes > 0 && l.Minutes == 0).ToList();
            if (roundedToZero.Count > 0)
            {
                var w = Warning(ExportWarningKind.RoundedToZero, ExportWarningSeverity.Warning,
                    roundedToZero.Count == 1 ? "1 Buchung wurde auf 0 gerundet und zählt nicht."
                                             : roundedToZero.Count + " Buchungen wurden auf 0 gerundet und zählen nicht.");
                w.EntryIds.AddRange(roundedToZero.Where(l => l.Id > 0).Select(l => l.Id));
                report.Warnings.Add(w);
            }

            var parallelCount = parallel.Count(b => b);
            if (parallelCount > 0)
            {
                var w = Warning(ExportWarningKind.ParallelProjects, ExportWarningSeverity.Info,
                    ExportFormat.Plural(parallelCount, "Buchung läuft", "Buchungen laufen") +
                    " parallel zu einer Buchung eines anderen Projekts; jede zählt in der Summe voll.");
                AddIds(w, lines, parallel);
                report.Warnings.Add(w);
            }

            var longDays = lines.GroupBy(l => l.Date)
                .Select(g => new { Date = g.Key, Minutes = g.Sum(l => l.RawMinutes) })
                .Where(d => d.Minutes > LongDayMinutes)
                .OrderBy(d => d.Date)
                .ToList();
            if (longDays.Count > 0)
            {
                var shown = longDays.Take(3).Select(d => ExportFormat.Date(d.Date) + " (" + ExportFormat.Decimal(d.Minutes) + " h)");
                var more = longDays.Count > 3 ? " und " + (longDays.Count - 3) + " weitere" : string.Empty;
                report.Warnings.Add(Warning(ExportWarningKind.LongDay, ExportWarningSeverity.Info,
                    "Mehr als 12 Stunden an einem Tag gebucht: " + string.Join(", ", shown) + more + "."));
            }

            var overnight = lines.Where(l => l.Overnight).ToList();
            if (overnight.Count > 0)
            {
                var w = Warning(ExportWarningKind.Overnight, ExportWarningSeverity.Info,
                    overnight.Count == 1 ? "1 Buchung geht über Mitternacht und zählt am Tag ihres Beginns."
                                         : overnight.Count + " Buchungen gehen über Mitternacht und zählen am Tag ihres Beginns.");
                w.EntryIds.AddRange(overnight.Where(l => l.Id > 0).Select(l => l.Id));
                report.Warnings.Add(w);
            }

            // Dringendes zuerst; innerhalb gleicher Dringlichkeit bleibt die Reihenfolge von oben.
            var sorted = report.Warnings.OrderByDescending(w => w.Severity).ToList();
            report.Warnings.Clear();
            report.Warnings.AddRange(sorted);
        }

        private static ExportWarning Warning(ExportWarningKind kind, ExportWarningSeverity severity, string message)
        {
            return new ExportWarning { Kind = kind, Severity = severity, Message = message };
        }

        private static void AddIds(ExportWarning warning, List<ExportLine> lines, bool[] marked)
        {
            for (var i = 0; i < lines.Count; i++)
            {
                if (marked[i] && lines[i].Id > 0) warning.EntryIds.Add(lines[i].Id);
            }
        }

        // ---------- Hinweise im Dokument ----------

        private static void BuildNotes(ExportReport report, ExportOptions options)
        {
            if (options.RoundingMinutes > 1)
            {
                report.Notes.Add("Die Dauer jeder Buchung ist kaufmännisch auf " + options.RoundingMinutes + " Minuten gerundet. " +
                                 "Ungerundete Summe: " + ExportFormat.Decimal(report.RawTotalMinutes) + " h (" + ExportFormat.Clock(report.RawTotalMinutes) + ").");
            }

            if (options.DurationFormat != ExportDurationFormat.Clock && DecimalValuesDoNotAddUp(report))
            {
                report.Notes.Add("Dezimalwerte sind auf zwei Stellen gerundet; die Summen werden aus Minuten gebildet.");
            }

            if (report.Skipped.Count > 0)
            {
                report.Notes.Add(report.Skipped.Count == 1
                    ? "1 Buchung mit nicht lesbaren Zeiten ist nicht enthalten."
                    : report.Skipped.Count + " Buchungen mit nicht lesbaren Zeiten sind nicht enthalten.");
            }
        }

        /// <summary>Prüft am Angezeigten, ob die Summe der gerundeten Zeilen von der gerundeten Gesamtsumme abweicht.</summary>
        private static bool DecimalValuesDoNotAddUp(ExportReport report)
        {
            decimal shownLines = report.Lines.Sum(l => decimal.Parse(ExportFormat.Decimal(l.Minutes), ExportFormat.German));
            var shownTotal = decimal.Parse(ExportFormat.Decimal(report.TotalMinutes), ExportFormat.German);
            return shownLines != shownTotal;
        }
    }
}
