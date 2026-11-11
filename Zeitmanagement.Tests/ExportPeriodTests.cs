// Target: .NET Framework + C# 7.3
using System;
using Xunit;
using Zeitmanagement.Helpers;

namespace Zeitmanagement.Tests
{
    public class ExportPeriodTests
    {
        private static string Range(string key, string today)
        {
            DateTime from, to;
            Assert.True(ExportPeriod.TryResolve(key, DateTime.Parse(today), out from, out to));
            return ExportFormat.IsoDate(from) + " bis " + ExportFormat.IsoDate(to);
        }

        [Fact]
        public void Months_are_whole_calendar_months()
        {
            Assert.Equal("2026-11-01 bis 2026-11-30", Range(ExportPeriod.ThisMonth, "2026-11-11"));
            Assert.Equal("2026-10-01 bis 2026-10-31", Range(ExportPeriod.LastMonth, "2026-11-11"));
            Assert.Equal("2025-12-01 bis 2025-12-31", Range(ExportPeriod.LastMonth, "2026-01-15"));   // Jahreswechsel
            Assert.Equal("2026-02-01 bis 2026-02-28", Range(ExportPeriod.ThisMonth, "2026-02-10"));
            Assert.Equal("2028-02-01 bis 2028-02-29", Range(ExportPeriod.ThisMonth, "2028-02-10"));   // Schaltjahr
        }

        [Theory]
        [InlineData("2026-01-01", "2026-01-01 bis 2026-03-31", "2025-10-01 bis 2025-12-31")]
        [InlineData("2026-03-31", "2026-01-01 bis 2026-03-31", "2025-10-01 bis 2025-12-31")]
        [InlineData("2026-04-01", "2026-04-01 bis 2026-06-30", "2026-01-01 bis 2026-03-31")]
        [InlineData("2026-11-11", "2026-10-01 bis 2026-12-31", "2026-07-01 bis 2026-09-30")]
        public void Quarters_follow_the_calendar_quarters(string today, string thisQuarter, string lastQuarter)
        {
            Assert.Equal(thisQuarter, Range(ExportPeriod.ThisQuarter, today));
            Assert.Equal(lastQuarter, Range(ExportPeriod.LastQuarter, today));
        }

        [Fact]
        public void Years_and_the_last_thirty_days()
        {
            Assert.Equal("2026-01-01 bis 2026-12-31", Range(ExportPeriod.ThisYear, "2026-11-11"));
            Assert.Equal("2025-01-01 bis 2025-12-31", Range(ExportPeriod.LastYear, "2026-11-11"));
            Assert.Equal("2026-10-13 bis 2026-11-11", Range(ExportPeriod.Last30Days, "2026-11-11"));   // heute eingeschlossen: 30 Tage
        }

        [Fact]
        public void Custom_has_no_fixed_range()
        {
            DateTime from, to;
            Assert.False(ExportPeriod.TryResolve(ExportPeriod.Custom, new DateTime(2026, 11, 11), out from, out to));
            Assert.False(ExportPeriod.TryResolve("irgendwas", new DateTime(2026, 11, 11), out from, out to));
        }

        [Fact]
        public void Every_choice_has_a_german_text()
        {
            foreach (var key in ExportPeriod.Keys)
            {
                Assert.False(string.IsNullOrWhiteSpace(ExportPeriod.Text(key)));
            }
            Assert.Equal("Letzter Monat", ExportPeriod.Text(ExportPeriod.LastMonth));
            Assert.Equal("Benutzerdefiniert", ExportPeriod.Text(ExportPeriod.Custom));
        }
    }
}
