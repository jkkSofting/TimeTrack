// Target: .NET Framework + C# 7.3
using System;
using System.IO;
using System.Linq;
using System.Text;
using Xunit;
using Zeitmanagement.Helpers;

namespace Zeitmanagement.Tests
{
    /// <summary>CSV-Ausgabe des Tätigkeitsnachweises: Format für deutsches Excel, Quoting, Schutz vor Formel-Injektion.</summary>
    public class CsvExporterTests
    {
        private static TimeTracker.TimeEntryRow E(int id, string date, string start, string end, string project, string description)
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

        private static ExportReport Report(ExportOptions options, params TimeTracker.TimeEntryRow[] entries)
        {
            var projects = new[]
            {
                new TimeTracker.ProjectRow { Projektname = "Projekt A", Kunde = "Muster GmbH", Kostentraeger = "KST-1", IstExtern = true },
                new TimeTracker.ProjectRow { Projektname = "=Projekt B", Kunde = "Kunde; 2", Kostentraeger = "KST-2", IstExtern = false }
            };
            return ExportReportBuilder.Build(entries, projects, options);
        }

        private static ExportOptions Options()
        {
            return new ExportOptions { From = new DateTime(2026, 10, 1), To = new DateTime(2026, 10, 31) };
        }

        private static string[] Rows(string csv)
        {
            return csv.Split(new[] { "\r\n" }, StringSplitOptions.None).Where(r => r.Length > 0).ToArray();
        }

        [Fact]
        public void Default_format_is_for_german_excel_with_semicolons_decimal_commas_and_crlf()
        {
            var report = Report(Options(), E(1, "2026-10-05", "09:00", "10:30", "Projekt A", "Konzept"));

            var csv = CsvExporter.Build(report);

            Assert.EndsWith("\r\n", csv);
            var rows = Rows(csv);
            Assert.Equal("Datum;Wochentag;Von;Bis;Dauer (h);Dauer (h:mm);Minuten;Projekt;Kunde;Kostenträger;Extern/Intern;Beschreibung", rows[0]);
            Assert.Equal("05.10.2026;Mo;09:00;10:30;1,50;1:30;90;Projekt A;Muster GmbH;KST-1;Extern;Konzept", rows[1]);
            Assert.Equal(2, rows.Length);
        }

        [Fact]
        public void International_format_uses_commas_and_decimal_points()
        {
            var report = Report(Options(), E(1, "2026-10-05", "09:00", "10:30", "Projekt A", "Konzept"));

            var rows = Rows(CsvExporter.Build(report, new CsvOptions { International = true }));

            Assert.StartsWith("Datum,Wochentag,Von,Bis,Dauer (h),", rows[0]);
            Assert.Equal("05.10.2026,Mo,09:00,10:30,1.50,1:30,90,Projekt A,Muster GmbH,KST-1,Extern,Konzept", rows[1]);
        }

        [Fact]
        public void Iso_dates_are_optional()
        {
            var report = Report(Options(), E(1, "2026-10-05", "09:00", "10:30", "Projekt A", "x"));

            var rows = Rows(CsvExporter.Build(report, new CsvOptions { IsoDates = true }));

            Assert.StartsWith("2026-10-05;Mo;", rows[1]);
        }

        [Fact]
        public void Texts_with_delimiter_quotes_or_line_breaks_are_quoted()
        {
            var report = Report(Options(),
                E(1, "2026-10-05", "09:00", "10:00", "Projekt A", "a;b"),
                E(2, "2026-10-05", "10:00", "11:00", "Projekt A", "sagte \"hallo\""),
                E(3, "2026-10-05", "11:00", "12:00", "Projekt A", "Zeile 1\r\nZeile 2"));

            var csv = CsvExporter.Build(report);

            Assert.Contains(";\"a;b\"\r\n", csv);
            Assert.Contains(";\"sagte \"\"hallo\"\"\"\r\n", csv);
            Assert.Contains(";\"Zeile 1\r\nZeile 2\"\r\n", csv);
        }

        [Fact]
        public void A_comma_needs_no_quotes_in_the_german_format_but_does_in_the_international_one()
        {
            var report = Report(Options(), E(1, "2026-10-05", "09:00", "10:00", "Projekt A", "eins, zwei"));

            Assert.Contains(";eins, zwei\r\n", CsvExporter.Build(report));
            Assert.Contains(",\"eins, zwei\"\r\n", CsvExporter.Build(report, new CsvOptions { International = true }));
        }

        [Theory]
        [InlineData("=SUMME(A1:A9)", "'=SUMME(A1:A9)")]
        [InlineData("+49 170 1234", "'+49 170 1234")]
        [InlineData("-1+1", "'-1+1")]
        [InlineData("@cmd", "'@cmd")]
        [InlineData("Konzept = gut", "Konzept = gut")]
        [InlineData("", "")]
        public void Text_starting_with_a_formula_character_is_neutralized(string input, string expected)
        {
            Assert.Equal(expected, CsvExporter.Neutralize(input));
        }

        [Fact]
        public void Formula_injection_is_stopped_in_project_customer_and_description()
        {
            var report = Report(Options(), E(1, "2026-10-05", "09:00", "10:00", "=Projekt B", "=HYPERLINK(\"http://x\")"));

            var row = Rows(CsvExporter.Build(report))[1];

            Assert.Contains(";'=Projekt B;", row);
            Assert.EndsWith(";\"'=HYPERLINK(\"\"http://x\"\")\"", row);
        }

        [Fact]
        public void Numbers_and_dates_are_not_touched_by_the_injection_guard()
        {
            var report = Report(Options(), E(1, "2026-10-05", "09:00", "10:00", "Projekt A", "x"));

            var row = Rows(CsvExporter.Build(report))[1];

            Assert.StartsWith("05.10.2026;Mo;09:00;10:00;1,00;1:00;60;", row);
        }

        [Fact]
        public void Hiding_the_times_drops_the_from_and_to_columns()
        {
            var options = Options();
            options.ShowTimes = false;
            var report = Report(options, E(1, "2026-10-05", "09:00", "10:00", "Projekt A", "x"));

            var rows = Rows(CsvExporter.Build(report));

            Assert.StartsWith("Datum;Wochentag;Dauer (h);", rows[0]);
            Assert.DoesNotContain("Von", rows[0]);
            Assert.StartsWith("05.10.2026;Mo;1,00;", rows[1]);
        }

        [Fact]
        public void Total_row_is_optional_and_adds_up_from_minutes()
        {
            var report = Report(Options(),
                E(1, "2026-10-05", "09:00", "10:30", "Projekt A", "a"),
                E(2, "2026-10-06", "09:00", "09:45", "Projekt A", "b"));

            var without = Rows(CsvExporter.Build(report));
            var with = Rows(CsvExporter.Build(report, new CsvOptions { IncludeTotalRow = true }));

            Assert.Equal(3, without.Length);
            Assert.Equal(4, with.Length);
            Assert.Equal("Summe;;;;2,25;2:15;135;;;;;", with[3]);
        }

        [Fact]
        public void Rounded_durations_are_what_the_file_contains()
        {
            var options = Options();
            options.RoundingMinutes = 15;
            var report = Report(options, E(1, "2026-10-05", "09:00", "09:08", "Projekt A", "a"));

            var row = Rows(CsvExporter.Build(report))[1];

            Assert.Contains(";0,25;0:15;15;", row);
        }

        [Fact]
        public void Empty_report_gives_only_the_header()
        {
            var report = Report(Options());

            var rows = Rows(CsvExporter.Build(report));

            Assert.Single(rows);
            Assert.StartsWith("Datum;", rows[0]);
        }

        [Fact]
        public void File_is_utf8_with_byte_order_mark_so_excel_reads_umlauts()
        {
            var report = Report(Options(), E(1, "2026-10-05", "09:00", "10:00", "Projekt A", "Prüfung der Größe – fertig"));
            var path = Path.Combine(Path.GetTempPath(), "TimeTrackTests", Guid.NewGuid().ToString("N") + ".csv");
            Directory.CreateDirectory(Path.GetDirectoryName(path));

            try
            {
                CsvExporter.WriteFile(path, report);

                var bytes = File.ReadAllBytes(path);
                Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, bytes.Take(3).ToArray());
                var text = new UTF8Encoding(false).GetString(bytes, 3, bytes.Length - 3);
                Assert.Contains("Kostenträger", text);
                Assert.Contains("Prüfung der Größe – fertig", text);
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Fact]
        public void ToBytes_prepends_the_bom_exactly_once()
        {
            var bytes = CsvExporter.ToBytes("a;b\r\n");

            Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, bytes.Take(3).ToArray());
            Assert.Equal(3 + 5, bytes.Length);
        }
    }
}
