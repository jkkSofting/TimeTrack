// Target: .NET Framework + C# 7.3
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace Zeitmanagement.Helpers
{
    public sealed class CsvOptions
    {
        /// <summary>
        /// <c>false</c> (Standard): für deutsches Excel – Trennzeichen Semikolon, Dezimalkomma.
        /// <c>true</c>: international – Trennzeichen Komma, Dezimalpunkt.
        /// </summary>
        public bool International { get; set; }

        /// <summary>Datum als <c>yyyy-MM-dd</c> statt <c>dd.MM.yyyy</c>.</summary>
        public bool IsoDates { get; set; }

        /// <summary>Am Ende eine Summenzeile anfügen (für reine Datenverarbeitung besser aus).</summary>
        public bool IncludeTotalRow { get; set; }
    }

    /// <summary>
    /// Schreibt einen <see cref="ExportReport"/> als CSV: eine Zeile je Buchung, UTF-8 mit BOM (damit Excel Umlaute richtig liest),
    /// Zeilenende CRLF. Texte mit Trennzeichen, Anführungszeichen oder Zeilenumbruch stehen in Anführungszeichen. Freitext,
    /// der mit einem Formelzeichen beginnt, wird entschärft, damit Excel ihn nicht als Formel ausführt.
    /// </summary>
    public static class CsvExporter
    {
        private static readonly char[] FormulaStarts = { '=', '+', '-', '@', '\t', '\r' };

        private sealed class Column
        {
            public string Header;
            public Func<ExportLine, string> Value;
            public Func<ExportReport, string> Total;
        }

        public static string Build(ExportReport report, CsvOptions options = null)
        {
            if (report == null) throw new ArgumentNullException(nameof(report));
            options = options ?? new CsvOptions();

            var delimiter = options.International ? ',' : ';';
            var culture = options.International ? CultureInfo.InvariantCulture : ExportFormat.German;
            var columns = Columns(report.Options, options, culture);

            var sb = new StringBuilder();
            AppendRow(sb, columns.Select(c => c.Header), delimiter);
            foreach (var line in report.Lines)
            {
                AppendRow(sb, columns.Select(c => c.Value(line)), delimiter);
            }

            if (options.IncludeTotalRow)
            {
                var cells = columns.Select(c => c.Total == null ? string.Empty : c.Total(report)).ToList();
                cells[0] = "Summe";
                AppendRow(sb, cells, delimiter);
            }

            return sb.ToString();
        }

        /// <summary>Der Text als Bytes: UTF-8 mit Byte-Order-Mark.</summary>
        public static byte[] ToBytes(string csv)
        {
            var encoding = new UTF8Encoding(true);
            var body = encoding.GetBytes(csv ?? string.Empty);
            var preamble = encoding.GetPreamble();
            var bytes = new byte[preamble.Length + body.Length];
            Buffer.BlockCopy(preamble, 0, bytes, 0, preamble.Length);
            Buffer.BlockCopy(body, 0, bytes, preamble.Length, body.Length);
            return bytes;
        }

        public static void WriteFile(string path, ExportReport report, CsvOptions options = null)
        {
            SafeFile.WriteAllBytes(path, ToBytes(Build(report, options)));
        }

        private static List<Column> Columns(ExportOptions export, CsvOptions csv, CultureInfo culture)
        {
            Func<int, string> hours = m => (m / 60.0).ToString("0.00", culture);
            Func<DateTime, string> date = d => csv.IsoDates ? ExportFormat.IsoDate(d) : ExportFormat.Date(d);
            var showTimes = export == null || export.ShowTimes;

            var columns = new List<Column>
            {
                new Column { Header = "Datum", Value = l => date(l.Date) },
                new Column { Header = "Wochentag", Value = l => l.WeekdayText }
            };
            if (showTimes)
            {
                columns.Add(new Column { Header = "Von", Value = l => l.Start });
                columns.Add(new Column { Header = "Bis", Value = l => l.End });
            }
            columns.Add(new Column { Header = "Dauer (h)", Value = l => hours(l.Minutes), Total = r => hours(r.TotalMinutes) });
            columns.Add(new Column { Header = "Dauer (h:mm)", Value = l => ExportFormat.Clock(l.Minutes), Total = r => ExportFormat.Clock(r.TotalMinutes) });
            columns.Add(new Column { Header = "Minuten", Value = l => l.Minutes.ToString(CultureInfo.InvariantCulture), Total = r => r.TotalMinutes.ToString(CultureInfo.InvariantCulture) });
            columns.Add(new Column { Header = "Projekt", Value = l => Neutralize(l.Project) });
            columns.Add(new Column { Header = "Kunde", Value = l => Neutralize(l.Customer) });
            columns.Add(new Column { Header = "Kostenträger", Value = l => Neutralize(l.Carrier) });
            columns.Add(new Column { Header = "Extern/Intern", Value = l => l.IsExternal ? "Extern" : "Intern" });
            columns.Add(new Column { Header = "Beschreibung", Value = l => Neutralize(l.Description) });
            return columns;
        }

        /// <summary>
        /// Schützt vor CSV-/Formel-Injektion: Beginnt ein Text mit <c>= + - @</c> (oder Tab/CR), würde Excel ihn als Formel
        /// ausführen. Ein vorangestelltes Hochkomma macht ihn zu normalem Text.
        /// </summary>
        public static string Neutralize(string text)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;
            return Array.IndexOf(FormulaStarts, text[0]) >= 0 ? "'" + text : text;
        }

        private static void AppendRow(StringBuilder sb, IEnumerable<string> cells, char delimiter)
        {
            var first = true;
            foreach (var cell in cells)
            {
                if (!first) sb.Append(delimiter);
                first = false;
                sb.Append(Quote(cell, delimiter));
            }
            sb.Append("\r\n");
        }

        private static string Quote(string value, char delimiter)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            if (value.IndexOf(delimiter) < 0 && value.IndexOf('"') < 0 && value.IndexOf('\r') < 0 && value.IndexOf('\n') < 0)
            {
                return value;
            }
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }
    }
}
