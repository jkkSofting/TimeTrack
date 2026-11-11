// Target: .NET Framework + C# 7.3
using System;
using System.Collections.Generic;
using Xunit;
using Zeitmanagement.Helpers;

namespace Zeitmanagement.Tests
{
    public class ExportPrefsTests
    {
        [Fact]
        public void A_full_selection_round_trips()
        {
            var prefs = new ExportPrefs
            {
                PeriodKey = ExportPeriod.LastMonth,
                From = "2026-10-01",
                To = "2026-10-31",
                ProjectNames = new List<string> { "Projekt Alpha", "Ärger & \"Co\"" },
                Kind = ExportProjectKind.ExternalOnly.ToString(),
                Grouping = ExportGrouping.ByWeek.ToString(),
                DurationFormat = ExportDurationFormat.Both.ToString(),
                ShowTimes = false,
                AlwaysShowProjectColumn = true,
                ShowCarrierColumn = true,
                ShowDaySubtotals = false,
                ShowClient = true,
                Title = "Stundennachweis – Oktober",
                Landscape = true,
                ShowBranding = false,
                CsvInternational = true,
                CsvIsoDates = true,
                CsvTotalRow = true
            };

            var back = ExportPrefs.FromJson(prefs.ToJson());

            Assert.Equal(ExportPeriod.LastMonth, back.PeriodKey);
            Assert.Equal(new[] { "Projekt Alpha", "Ärger & \"Co\"" }, back.ProjectNames.ToArray());
            Assert.Equal(ExportProjectKind.ExternalOnly, back.GetKind());
            Assert.Equal(ExportGrouping.ByWeek, back.GetGrouping());
            Assert.Equal(ExportDurationFormat.Both, back.GetDurationFormat());
            Assert.False(back.GetShowTimes());
            Assert.True(back.AlwaysShowProjectColumn);
            Assert.True(back.ShowCarrierColumn);
            Assert.False(back.GetShowDaySubtotals());
            Assert.True(back.ShowClient);
            Assert.Equal("Stundennachweis – Oktober", back.Title);
            Assert.True(back.Landscape);
            Assert.False(back.GetShowBranding());
            Assert.True(back.CsvInternational);
            Assert.True(back.CsvIsoDates);
            Assert.True(back.CsvTotalRow);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("{kaputt")]
        [InlineData("[1,2,3]")]
        [InlineData("null")]
        [InlineData("<xml/>")]
        public void Empty_or_broken_text_gives_the_defaults_and_never_throws(string json)
        {
            var prefs = ExportPrefs.FromJson(json);

            Assert.Equal(ExportProjectKind.All, prefs.GetKind());
            Assert.Equal(ExportGrouping.ByDay, prefs.GetGrouping());
            Assert.Equal(ExportDurationFormat.Decimal, prefs.GetDurationFormat());
            Assert.True(prefs.GetShowTimes());
            Assert.True(prefs.GetShowDaySubtotals());
            Assert.False(prefs.ShowClient);   // Der Auftraggeber ist optional: ohne Wahl steht er nicht im Dokument
            Assert.True(prefs.GetShowBranding());
            Assert.False(prefs.Landscape);
            Assert.Null(prefs.ProjectNames);
        }

        [Fact]
        public void Missing_fields_keep_their_defaults_so_older_settings_still_load()
        {
            var prefs = ExportPrefs.FromJson("{\"grouping\":\"ByProject\"}");

            Assert.Equal(ExportGrouping.ByProject, prefs.GetGrouping());
            Assert.True(prefs.GetShowTimes());            // nicht gespeichert = Standard (an), nicht aus
            Assert.Equal(ExportDurationFormat.Decimal, prefs.GetDurationFormat());
        }

        [Theory]
        [InlineData("Unsinn")]
        [InlineData("99")]
        [InlineData("")]
        public void Unknown_choices_fall_back_to_the_default(string value)
        {
            var prefs = new ExportPrefs { Kind = value, Grouping = value, DurationFormat = value };

            Assert.Equal(ExportProjectKind.All, prefs.GetKind());
            Assert.Equal(ExportGrouping.ByDay, prefs.GetGrouping());
            Assert.Equal(ExportDurationFormat.Decimal, prefs.GetDurationFormat());
        }

        [Fact]
        public void Rounding_is_never_stored_so_it_can_not_come_back_unnoticed()
        {
            // Eine Rundung ändert die abrechenbare Zeit: Sie muss jedes Mal ausdrücklich gewählt werden.
            Assert.DoesNotContain("round", new ExportPrefs().ToJson(), StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(typeof(ExportPrefs).GetProperties(), p => p.Name.IndexOf("Round", StringComparison.OrdinalIgnoreCase) >= 0);
        }

        [Fact]
        public void An_old_setting_that_still_contains_a_rounding_is_read_and_the_rounding_ignored()
        {
            var prefs = ExportPrefs.FromJson("{\"rounding\":15,\"grouping\":\"ByWeek\"}");

            Assert.Equal(ExportGrouping.ByWeek, prefs.GetGrouping());
        }

        [Fact]
        public void A_custom_range_is_read_only_when_it_is_valid()
        {
            DateTime from, to;

            Assert.True(new ExportPrefs { From = "2026-10-05", To = "2026-10-20" }.TryGetCustomRange(out from, out to));
            Assert.Equal(new DateTime(2026, 10, 5), from);
            Assert.Equal(new DateTime(2026, 10, 20), to);

            Assert.False(new ExportPrefs { From = "2026-10-20", To = "2026-10-05" }.TryGetCustomRange(out from, out to));   // verkehrt herum
            Assert.False(new ExportPrefs { From = "20.10.2026", To = "2026-10-31" }.TryGetCustomRange(out from, out to));    // falsches Format
            Assert.False(new ExportPrefs().TryGetCustomRange(out from, out to));
        }

        [Fact]
        public void Dates_are_written_as_iso_text()
        {
            Assert.Equal("2026-10-05", ExportPrefs.FormatDate(new DateTime(2026, 10, 5)));
        }
    }
}
