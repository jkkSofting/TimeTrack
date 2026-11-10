// Target: .NET Framework + C# 7.3
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Zeitmanagement.Helpers
{
    /// <summary>Eine lokale Buchung, wie sie in der Datenbank steht (Datum yyyy-MM-dd, Zeiten HH:mm).</summary>
    public sealed class LocalTimeEntry
    {
        public int Id { get; set; }
        public int ProjectId { get; set; }
        public string DateText { get; set; }
        public string Start { get; set; }
        public string End { get; set; }
        public string Description { get; set; }
    }

    public enum HoursBucketKind
    {
        Day,
        Week,
        Month
    }

    /// <summary>Lokal erfasste Stunden in einem Tag, einer ISO-Woche (Montag bis Sonntag) oder einem Monat.</summary>
    public sealed class HoursBucket
    {
        /// <summary>Erster Tag des Zeitraums (bei gewähltem Datumsbereich auf diesen begrenzt).</summary>
        public DateTime Start { get; set; }
        /// <summary>Letzter Tag des Zeitraums, einschließlich.</summary>
        public DateTime End { get; set; }
        public string Label { get; set; }
        public int Minutes { get; set; }
        public int CumulativeMinutes { get; set; }
        public int EntryCount { get; set; }

        public double Hours => Minutes / 60.0;
        public double CumulativeHours => CumulativeMinutes / 60.0;
        public string HoursText => LocalHoursCalculator.FormatDecimalHours(Minutes);
        public string HoursClockText => LocalHoursCalculator.FormatClock(Minutes);
        public string CumulativeText => LocalHoursCalculator.FormatDecimalHours(CumulativeMinutes);
    }

    /// <summary>Eine Buchung, deren Zeiten sich nicht lesen ließen. Sie wird gemeldet und nie mit erfundener Dauer gezählt.</summary>
    public sealed class InvalidTimeEntry
    {
        public int Id { get; set; }
        public string DateText { get; set; }
        public string Start { get; set; }
        public string End { get; set; }
        public string Reason { get; set; }
    }

    public sealed class LocalHoursResult
    {
        public List<HoursBucket> Buckets { get; } = new List<HoursBucket>();
        public List<InvalidTimeEntry> InvalidEntries { get; } = new List<InvalidTimeEntry>();
        public int EntryCount { get; set; }
        public int TotalMinutes { get; set; }
        public double TotalHours => TotalMinutes / 60.0;
    }

    /// <summary>
    /// Lokal erfasste Stunden aus den Buchungen der App (nicht aus SAP): jede Buchung genau einmal, Dauer wie in der
    /// Projektansicht (Ende vor Start = über Mitternacht, die Dauer zählt für das gespeicherte Datum).
    /// Parallele Buchungen zählen mehrfach; das Ergebnis ist keine mit SAP abgestimmte abrechenbare Zeit.
    /// </summary>
    public static class LocalHoursCalculator
    {
        private static readonly string[] TimeFormats = { @"h\:mm", @"hh\:mm" };
        private static readonly CultureInfo German = CultureInfo.GetCultureInfo("de-DE");

        /// <summary>Dauer in Minuten; liefert <c>false</c>, wenn Start oder Ende nicht lesbar sind.</summary>
        public static bool TryGetDurationMinutes(string start, string end, out int minutes)
        {
            minutes = 0;
            TimeSpan s, e;
            if (start == null || end == null) return false;
            if (!TimeSpan.TryParseExact(start.Trim(), TimeFormats, CultureInfo.InvariantCulture, out s)) return false;
            if (!TimeSpan.TryParseExact(end.Trim(), TimeFormats, CultureInfo.InvariantCulture, out e)) return false;

            var duration = e - s;
            if (duration < TimeSpan.Zero) duration = duration.Add(TimeSpan.FromDays(1)); // über Mitternacht
            minutes = (int)Math.Round(duration.TotalMinutes);
            return true;
        }

        /// <summary>
        /// Gruppiert die Buchungen in Tage, Wochen oder Monate. Ein Eintrag zählt auch dann nur einmal, wenn er mehrfach
        /// übergeben wird (z. B. weil mehrere lokale Projekte denselben Kostenträger teilen).
        /// Leere Zeiträume zwischen dem ersten und letzten Tag (bzw. im gewählten Bereich) erscheinen mit 0 Stunden.
        /// </summary>
        public static LocalHoursResult Calculate(IEnumerable<LocalTimeEntry> entries, HoursBucketKind kind, DateTime? from = null, DateTime? to = null)
        {
            var result = new LocalHoursResult();
            var seen = new HashSet<int>();
            var valid = new List<KeyValuePair<DateTime, int>>();

            foreach (var entry in entries ?? Enumerable.Empty<LocalTimeEntry>())
            {
                if (!seen.Add(entry.Id)) continue;

                DateTime date;
                if (!DateTime.TryParseExact(entry.DateText, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date))
                {
                    result.InvalidEntries.Add(Invalid(entry, "Datum nicht lesbar"));
                    continue;
                }

                int minutes;
                if (!TryGetDurationMinutes(entry.Start, entry.End, out minutes))
                {
                    result.InvalidEntries.Add(Invalid(entry, "Start- oder Endzeit nicht lesbar"));
                    continue;
                }

                if (from.HasValue && date < from.Value.Date) continue;
                if (to.HasValue && date > to.Value.Date) continue;

                valid.Add(new KeyValuePair<DateTime, int>(date, minutes));
            }

            result.EntryCount = valid.Count;
            result.TotalMinutes = valid.Sum(v => v.Value);

            if (valid.Count == 0 && !(from.HasValue && to.HasValue)) return result;

            var rangeStart = from.HasValue ? from.Value.Date : valid.Min(v => v.Key);
            var rangeEnd = to.HasValue ? to.Value.Date : valid.Max(v => v.Key);
            if (rangeEnd < rangeStart) return result;

            var totals = valid
                .GroupBy(v => BucketStart(v.Key, kind))
                .ToDictionary(g => g.Key, g => new KeyValuePair<int, int>(g.Sum(x => x.Value), g.Count()));

            var cumulative = 0;
            for (var start = BucketStart(rangeStart, kind); start <= rangeEnd; start = NextBucket(start, kind))
            {
                var last = NextBucket(start, kind).AddDays(-1);
                KeyValuePair<int, int> total;
                totals.TryGetValue(start, out total);
                cumulative += total.Key;

                result.Buckets.Add(new HoursBucket
                {
                    Start = start < rangeStart ? rangeStart : start,
                    End = last > rangeEnd ? rangeEnd : last,
                    Label = Label(start, kind),
                    Minutes = total.Key,
                    EntryCount = total.Value,
                    CumulativeMinutes = cumulative
                });
            }

            return result;
        }

        private static InvalidTimeEntry Invalid(LocalTimeEntry e, string reason)
        {
            return new InvalidTimeEntry { Id = e.Id, DateText = e.DateText, Start = e.Start, End = e.End, Reason = reason };
        }

        public static DateTime BucketStart(DateTime date, HoursBucketKind kind)
        {
            date = date.Date;
            switch (kind)
            {
                case HoursBucketKind.Week:
                    return date.AddDays(-(((int)date.DayOfWeek + 6) % 7)); // ISO: Woche beginnt am Montag
                case HoursBucketKind.Month:
                    return new DateTime(date.Year, date.Month, 1);
                default:
                    return date;
            }
        }

        private static DateTime NextBucket(DateTime start, HoursBucketKind kind)
        {
            switch (kind)
            {
                case HoursBucketKind.Week: return start.AddDays(7);
                case HoursBucketKind.Month: return start.AddMonths(1);
                default: return start.AddDays(1);
            }
        }

        /// <summary>ISO-8601-Kalenderwoche und das zugehörige Wochenjahr (die Woche gehört zum Jahr ihres Donnerstags).</summary>
        public static int IsoWeek(DateTime date, out int isoYear)
        {
            var thursday = BucketStart(date, HoursBucketKind.Week).AddDays(3);
            isoYear = thursday.Year;
            return (thursday.DayOfYear - 1) / 7 + 1;
        }

        private static string Label(DateTime start, HoursBucketKind kind)
        {
            switch (kind)
            {
                case HoursBucketKind.Week:
                    int isoYear;
                    var week = IsoWeek(start, out isoYear);
                    return "KW " + week.ToString("00", CultureInfo.InvariantCulture) + " " + isoYear.ToString(CultureInfo.InvariantCulture);
                case HoursBucketKind.Month:
                    return start.ToString("MMMM yyyy", German);
                default:
                    return start.ToString("ddd dd.MM.yyyy", German);
            }
        }

        /// <summary>Dezimalstunden, z. B. "2,50 h"; gerundet wird nur die Anzeige.</summary>
        public static string FormatDecimalHours(int minutes)
        {
            return (minutes / 60.0).ToString("0.00", German) + " h";
        }

        /// <summary>Stunden und Minuten, auch über 24 Stunden, z. B. "26:30".</summary>
        public static string FormatClock(int minutes)
        {
            var sign = minutes < 0 ? "-" : string.Empty;
            minutes = Math.Abs(minutes);
            return sign + (minutes / 60).ToString(CultureInfo.InvariantCulture) + ":" + (minutes % 60).ToString("00", CultureInfo.InvariantCulture);
        }
    }
}
