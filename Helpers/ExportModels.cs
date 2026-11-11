// Target: .NET Framework + C# 7.3
using System;
using System.Collections.Generic;
using System.Globalization;

namespace Zeitmanagement.Helpers
{
    /// <summary>Wie die Buchungen im Nachweis gegliedert werden.</summary>
    public enum ExportGrouping
    {
        /// <summary>Chronologisch, mit einer Gruppe je Tag.</summary>
        ByDay,
        /// <summary>Eine Gruppe je Projekt, darin chronologisch.</summary>
        ByProject,
        /// <summary>Eine Gruppe je ISO-Kalenderwoche (Montag bis Sonntag).</summary>
        ByWeek
    }

    /// <summary>Welche Projekte nach ihrem Intern/Extern-Kennzeichen (extern = vom Kunden bezahlt) in den Nachweis kommen.</summary>
    public enum ExportProjectKind
    {
        All,
        ExternalOnly,
        InternalOnly
    }

    public enum ExportDurationFormat
    {
        /// <summary>Dezimalstunden, z. B. "1,50".</summary>
        Decimal,
        /// <summary>Stunden und Minuten, z. B. "1:30".</summary>
        Clock,
        /// <summary>Beides, z. B. "1,50 (1:30)".</summary>
        Both
    }

    public enum ExportWarningSeverity
    {
        /// <summary>Gut zu wissen, ändert nichts an der Richtigkeit.</summary>
        Info,
        /// <summary>Sollte vor dem Export geprüft werden.</summary>
        Warning
    }

    public enum ExportWarningKind
    {
        /// <summary>Buchungen mit nicht lesbaren Zeiten, sie fehlen im Nachweis.</summary>
        InvalidTimes,
        MissingDescription,
        /// <summary>Zwei Buchungen desselben Projekts überschneiden sich: Zeit ist doppelt gebucht.</summary>
        OverlapSameProject,
        /// <summary>Buchungen verschiedener Projekte laufen parallel; jede zählt voll.</summary>
        ParallelProjects,
        ZeroDuration,
        RoundedToZero,
        LongDay,
        Overnight
    }

    /// <summary>Was in den Nachweis soll. Reine Daten, damit sich die Auswahl auch speichern lässt.</summary>
    public sealed class ExportOptions
    {
        /// <summary>Erster Tag, einschließlich.</summary>
        public DateTime From { get; set; }
        /// <summary>Letzter Tag, einschließlich.</summary>
        public DateTime To { get; set; }
        /// <summary>Gewählte Projekte (Projektnamen); <c>null</c> = alle, leer = keins.</summary>
        public List<string> ProjectNames { get; set; }
        public ExportProjectKind Kind { get; set; }
        public ExportGrouping Grouping { get; set; }
        public ExportDurationFormat DurationFormat { get; set; }
        /// <summary>Dauer je Buchung auf dieses Vielfache von Minuten runden (kaufmännisch); 0 = nicht runden.</summary>
        public int RoundingMinutes { get; set; }
        /// <summary>Uhrzeiten Von/Bis zeigen; aus = nur die Dauer.</summary>
        public bool ShowTimes { get; set; }
        /// <summary>Projekt-Spalte zeigen; <c>null</c> = automatisch, wenn mehr als ein Projekt im Nachweis steht.</summary>
        public bool? ShowProjectColumn { get; set; }
        public bool ShowCarrierColumn { get; set; }

        public ExportOptions()
        {
            Grouping = ExportGrouping.ByDay;
            DurationFormat = ExportDurationFormat.Decimal;
            ShowTimes = true;
        }
    }

    /// <summary>Eine Buchung im Nachweis, mit lesbaren, normalisierten Zeiten und der Dauer.</summary>
    public sealed class ExportLine
    {
        public int Id { get; set; }
        public DateTime Date { get; set; }
        /// <summary>Beginn in Minuten seit Mitternacht (zum Sortieren).</summary>
        public int StartMinute { get; set; }
        /// <summary>Beginn als "HH:mm" (auch wenn in der Datenbank "9:00" steht).</summary>
        public string Start { get; set; }
        public string End { get; set; }
        /// <summary>Tatsächlich gebuchte Dauer in Minuten.</summary>
        public int RawMinutes { get; set; }
        /// <summary>Dauer im Nachweis in Minuten, nach der optionalen Rundung.</summary>
        public int Minutes { get; set; }
        /// <summary>Ende liegt vor dem Beginn: die Buchung geht über Mitternacht und zählt am Tag ihres Beginns.</summary>
        public bool Overnight { get; set; }
        public string Project { get; set; }
        public string Customer { get; set; }
        public string Carrier { get; set; }
        public bool IsExternal { get; set; }
        /// <summary>Beschreibung ohne umgebende Leerzeichen, nie <c>null</c>.</summary>
        public string Description { get; set; }

        public string DateText => ExportFormat.Date(Date);
        public string WeekdayText => ExportFormat.WeekdayShort(Date);
    }

    /// <summary>Eine Gruppe von Buchungen (ein Tag, ein Projekt oder eine Woche) mit Zwischensumme.</summary>
    public sealed class ExportGroup
    {
        public string Title { get; set; }
        /// <summary>Zusatzzeile, z. B. der Datumsbereich einer Woche; sonst leer.</summary>
        public string Subtitle { get; set; }
        public List<ExportLine> Lines { get; } = new List<ExportLine>();
        public int Minutes { get; set; }
        public int Count => Lines.Count;
    }

    /// <summary>Stunden je Projekt im Nachweis.</summary>
    public sealed class ExportProjectSummary
    {
        public string Project { get; set; }
        public string Customer { get; set; }
        public string Carrier { get; set; }
        public bool IsExternal { get; set; }
        public int Count { get; set; }
        public int Minutes { get; set; }
        /// <summary>Anteil an der Gesamtzeit in Prozent (0 bis 100).</summary>
        public double SharePercent { get; set; }
    }

    public sealed class ExportWarning
    {
        public ExportWarningKind Kind { get; set; }
        public ExportWarningSeverity Severity { get; set; }
        public string Message { get; set; }
        /// <summary>Betroffene Buchungen (Datenbank-IDs), falls es einzelne Buchungen sind.</summary>
        public List<int> EntryIds { get; } = new List<int>();
    }

    /// <summary>Das Ergebnis der Aufbereitung: alles, was PDF, CSV und die Vorschau anzeigen.</summary>
    public sealed class ExportReport
    {
        public ExportOptions Options { get; set; }
        /// <summary>Alle Buchungen chronologisch (Datum, Beginn).</summary>
        public List<ExportLine> Lines { get; } = new List<ExportLine>();
        /// <summary>Dieselben Buchungen gegliedert nach <see cref="ExportOptions.Grouping"/>.</summary>
        public List<ExportGroup> Groups { get; } = new List<ExportGroup>();
        public List<ExportProjectSummary> Projects { get; } = new List<ExportProjectSummary>();
        /// <summary>Buchungen mit nicht lesbaren Zeiten: nicht im Nachweis, nie mit erfundener Dauer gezählt.</summary>
        public List<InvalidTimeEntry> Skipped { get; } = new List<InvalidTimeEntry>();
        public List<ExportWarning> Warnings { get; } = new List<ExportWarning>();
        /// <summary>Hinweise, die zum Verständnis der Zahlen auch im Dokument stehen (Rundung, Auslassungen).</summary>
        public List<string> Notes { get; } = new List<string>();

        public int EntryCount => Lines.Count;
        public bool IsEmpty => Lines.Count == 0;
        public int TotalMinutes { get; set; }
        /// <summary>Gesamtzeit ohne Rundung.</summary>
        public int RawTotalMinutes { get; set; }
        /// <summary>Tage, an denen mindestens eine Buchung liegt.</summary>
        public int WorkDays { get; set; }
        public int AverageMinutesPerWorkDay => WorkDays == 0 ? 0 : (int)Math.Round(TotalMinutes / (double)WorkDays, MidpointRounding.AwayFromZero);
        public DateTime? FirstDate { get; set; }
        public DateTime? LastDate { get; set; }
        public bool HasWarnings => Warnings.Count > 0;

        /// <summary>Soll die Projekt-Spalte erscheinen? (Wahl des Nutzers, sonst automatisch bei mehreren Projekten.)</summary>
        public bool ShowProjectColumn => Options != null && Options.ShowProjectColumn.HasValue
            ? Options.ShowProjectColumn.Value
            : Projects.Count > 1;
    }

    /// <summary>Zahlen und Datumsangaben im Nachweis: immer deutsch und unabhängig von der Windows-Sprache.</summary>
    public static class ExportFormat
    {
        public static readonly CultureInfo German = CultureInfo.GetCultureInfo("de-DE");

        // Fest eingetragen statt aus der Kultur gelesen: Die Kürzel hängen sonst von der Windows-Version ab.
        private static readonly string[] WeekdaysShort = { "So", "Mo", "Di", "Mi", "Do", "Fr", "Sa" };
        private static readonly string[] WeekdaysLong = { "Sonntag", "Montag", "Dienstag", "Mittwoch", "Donnerstag", "Freitag", "Samstag" };

        public static string Date(DateTime date)
        {
            return date.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture);
        }

        public static string IsoDate(DateTime date)
        {
            return date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }

        public static string WeekdayShort(DateTime date)
        {
            return WeekdaysShort[(int)date.DayOfWeek];
        }

        public static string WeekdayLong(DateTime date)
        {
            return WeekdaysLong[(int)date.DayOfWeek];
        }

        /// <summary>Dezimalstunden mit zwei Stellen, z. B. "1,50" (ohne Einheit).</summary>
        public static string Decimal(int minutes)
        {
            return (minutes / 60.0).ToString("0.00", German);
        }

        /// <summary>Stunden und Minuten, z. B. "1:30"; auch über 24 Stunden.</summary>
        public static string Clock(int minutes)
        {
            return LocalHoursCalculator.FormatClock(minutes);
        }

        public static string Duration(int minutes, ExportDurationFormat format)
        {
            switch (format)
            {
                case ExportDurationFormat.Clock: return Clock(minutes);
                case ExportDurationFormat.Both: return Decimal(minutes) + " (" + Clock(minutes) + ")";
                default: return Decimal(minutes);
            }
        }

        /// <summary>"01.10.2026 – 31.10.2026", an einem einzelnen Tag nur das Datum.</summary>
        public static string Period(DateTime from, DateTime to)
        {
            return from.Date == to.Date ? Date(from) : Date(from) + " – " + Date(to);
        }

        public static string Plural(int count, string singular, string plural)
        {
            return count.ToString(CultureInfo.InvariantCulture) + " " + (count == 1 ? singular : plural);
        }

        /// <summary>"HH:mm" aus Minuten seit Mitternacht.</summary>
        public static string TimeOfDay(int minuteOfDay)
        {
            return (minuteOfDay / 60).ToString("00", CultureInfo.InvariantCulture) + ":" + (minuteOfDay % 60).ToString("00", CultureInfo.InvariantCulture);
        }
    }
}
