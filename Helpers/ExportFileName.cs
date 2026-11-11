// Target: .NET Framework + C# 7.3
using System;
using System.IO;
using System.Linq;
using System.Text;

namespace Zeitmanagement.Helpers
{
    /// <summary>Dateinamen für exportierte Nachweise: sinnvoller Vorschlag und Bereinigung für Windows.</summary>
    public static class ExportFileName
    {
        private const int MaxStemLength = 100;
        private static readonly string[] ReservedNames =
        {
            "CON", "PRN", "AUX", "NUL",
            "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
            "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
        };

        /// <summary>
        /// Vorschlag wie <c>Tätigkeitsnachweis_Projekt Alpha_2026-10.pdf</c>: Titel, dann das Projekt (bei mehreren der Kunde,
        /// sonst "Projekte"), dann der gewählte Zeitraum.
        /// </summary>
        public static string Suggest(ExportReport report, string title, string extension)
        {
            if (report == null) throw new ArgumentNullException(nameof(report));

            var parts = new System.Collections.Generic.List<string>
            {
                string.IsNullOrWhiteSpace(title) ? "Tätigkeitsnachweis" : title.Trim()
            };

            var subject = Subject(report);
            if (subject.Length > 0) parts.Add(subject);

            var o = report.Options;
            if (o != null) parts.Add(PeriodPart(o.From.Date, o.To.Date));

            var ext = string.IsNullOrWhiteSpace(extension) ? string.Empty : "." + extension.Trim().TrimStart('.');
            return Sanitize(string.Join("_", parts.Select(p => Sanitize(p)))) + ext;
        }

        /// <summary>
        /// Ersetzt Zeichen, die in Windows-Dateinamen nicht erlaubt sind, durch "_", entfernt Punkte und Leerzeichen am Ende,
        /// kürzt zu lange Namen und schützt vor reservierten Namen wie "CON".
        /// </summary>
        public static string Sanitize(string name)
        {
            var invalid = Path.GetInvalidFileNameChars();
            var sb = new StringBuilder();
            foreach (var c in name ?? string.Empty)
            {
                sb.Append(invalid.Contains(c) || char.IsControl(c) ? '_' : c);
            }

            var result = sb.ToString().Trim().TrimEnd('.', ' ');
            if (result.Length > MaxStemLength) result = result.Substring(0, MaxStemLength).TrimEnd('.', ' ');
            if (result.Length == 0) result = "Export";

            // Reservierte Gerätenamen sind auch mit Endung ("CON.pdf") nicht erlaubt.
            var stem = result.Split('.')[0].Trim();
            if (ReservedNames.Contains(stem, StringComparer.OrdinalIgnoreCase)) result = "_" + result;
            return result;
        }

        private static string Subject(ExportReport report)
        {
            if (report.Projects.Count == 1) return report.Projects[0].Project;

            var customers = report.Projects.Select(p => p.Customer).Where(c => !string.IsNullOrWhiteSpace(c)).Distinct().ToList();
            if (report.Projects.Count > 1) return customers.Count == 1 ? customers[0] : "Projekte";
            return string.Empty;
        }

        private static string PeriodPart(DateTime from, DateTime to)
        {
            if (from == to) return ExportFormat.IsoDate(from);
            // Nur ein ganzer Kalendermonat heißt "2026-10"; ein Teil davon nennt beide Tage.
            var wholeMonth = from.Day == 1 && from.Year == to.Year && from.Month == to.Month && to == from.AddMonths(1).AddDays(-1);
            if (wholeMonth) return from.ToString("yyyy-MM", System.Globalization.CultureInfo.InvariantCulture);
            return ExportFormat.IsoDate(from) + "_bis_" + ExportFormat.IsoDate(to);
        }
    }
}
