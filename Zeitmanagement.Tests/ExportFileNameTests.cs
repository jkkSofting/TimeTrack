// Target: .NET Framework + C# 7.3
using System;
using System.IO;
using System.Linq;
using Xunit;
using Zeitmanagement.Helpers;

namespace Zeitmanagement.Tests
{
    public class ExportFileNameTests
    {
        private static ExportReport Report(string from, string to, params string[][] projects)
        {
            // projects: { Name, Kunde }
            var rows = projects.Select(p => new TimeTracker.ProjectRow { Projektname = p[0], Kunde = p[1], Kostentraeger = "K" }).ToArray();
            var entries = rows.Select((p, i) => new TimeTracker.TimeEntryRow
            {
                Id = i + 1,
                Datum = DateTime.Parse(from),
                Startzeit = "09:00",
                Endzeit = "10:00",
                Projektname = p.Projektname,
                Beschreibung = "x"
            }).ToArray();
            return ExportReportBuilder.Build(entries, rows, new ExportOptions { From = DateTime.Parse(from), To = DateTime.Parse(to) });
        }

        [Fact]
        public void A_single_project_and_a_whole_month_give_title_project_and_month()
        {
            var report = Report("2026-10-01", "2026-10-31", new[] { "Projekt Alpha", "Muster GmbH" });

            Assert.Equal("Tätigkeitsnachweis_Projekt Alpha_2026-10.pdf", ExportFileName.Suggest(report, null, "pdf"));
        }

        [Fact]
        public void Several_projects_of_one_customer_use_the_customer()
        {
            var report = Report("2026-10-01", "2026-10-31", new[] { "A", "Muster GmbH" }, new[] { "B", "Muster GmbH" });

            Assert.Equal("Tätigkeitsnachweis_Muster GmbH_2026-10.csv", ExportFileName.Suggest(report, "", ".csv"));
        }

        [Fact]
        public void Projects_of_several_customers_are_just_projects()
        {
            var report = Report("2026-10-01", "2026-10-31", new[] { "A", "K1" }, new[] { "B", "K2" });

            Assert.Equal("Tätigkeitsnachweis_Projekte_2026-10.pdf", ExportFileName.Suggest(report, "  ", "pdf"));
        }

        [Theory]
        [InlineData("2026-10-05", "2026-10-05", "2026-10-05")]
        [InlineData("2026-10-05", "2026-10-20", "2026-10-05_bis_2026-10-20")]   // Teil eines Monats: beide Tage nennen
        [InlineData("2026-10-01", "2026-12-31", "2026-10-01_bis_2026-12-31")]
        [InlineData("2026-02-01", "2026-02-28", "2026-02")]                      // ganzer Februar
        public void Period_names_a_whole_month_by_month_and_anything_else_by_its_days(string from, string to, string expected)
        {
            var report = Report(from, to, new[] { "A", "K" });

            Assert.Equal("Tätigkeitsnachweis_A_" + expected + ".pdf", ExportFileName.Suggest(report, null, "pdf"));
        }

        [Fact]
        public void A_custom_title_replaces_the_default()
        {
            var report = Report("2026-10-01", "2026-10-31", new[] { "A", "K" });

            Assert.StartsWith("Stundennachweis_A_", ExportFileName.Suggest(report, "Stundennachweis", "pdf"));
        }

        [Fact]
        public void Suggested_names_never_contain_characters_windows_rejects()
        {
            var report = Report("2026-10-01", "2026-10-31", new[] { "Projekt: A/B \"neu\" <x>|?*", "K" });

            var name = ExportFileName.Suggest(report, "Nachweis\\Titel", "pdf");

            Assert.Empty(name.Where(c => Path.GetInvalidFileNameChars().Contains(c)));
            Assert.EndsWith(".pdf", name);
        }

        [Theory]
        [InlineData("a/b:c", "a_b_c")]
        [InlineData("  Name. . ", "Name")]
        [InlineData("", "Export")]
        [InlineData(null, "Export")]
        [InlineData("CON", "_CON")]
        [InlineData("com1.pdf", "_com1.pdf")]
        [InlineData("Konzept", "Konzept")]
        public void Sanitize_replaces_trims_and_protects_reserved_names(string input, string expected)
        {
            Assert.Equal(expected, ExportFileName.Sanitize(input));
        }

        [Fact]
        public void Very_long_names_are_cut()
        {
            var name = ExportFileName.Sanitize(new string('a', 300));

            Assert.Equal(100, name.Length);
        }
    }
}
