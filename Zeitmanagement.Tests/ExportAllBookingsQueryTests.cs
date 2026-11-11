// Target: .NET Framework + C# 7.3
using System;
using System.Linq;
using Xunit;
using Zeitmanagement.Helpers;

namespace Zeitmanagement.Tests
{
    /// <summary>
    /// "Alle Buchungen" liest ohne Datumsgrenze (von DateTime.MinValue bis DateTime.MaxValue) und leitet den Zeitraum aus den
    /// Buchungen ab. Die Abfrage vergleicht Text (yyyy-MM-dd): Das muss auch für die äußersten Werte stimmen.
    /// </summary>
    public class ExportAllBookingsQueryTests
    {
        [Fact]
        public void A_query_without_date_limits_returns_every_booking_of_every_year()
        {
            using (var t = new TestDb())
            {
                t.Db.AddOrEnsureProject("Alpha", "Muster GmbH", "K.01");
                t.Db.AddOrEnsureProject("Beta", "Muster GmbH", "K.02");
                t.Db.AddTimeEntry(new DateTime(1999, 12, 31), "09:00", "10:00", "Alpha", "sehr alt");
                t.Db.AddTimeEntry(new DateTime(2024, 2, 29), "09:00", "10:00", "Alpha", "Schalttag");
                t.Db.AddTimeEntry(new DateTime(2026, 11, 11), "13:00", "14:30", "Beta", "heute");
                t.Db.AddTimeEntry(new DateTime(9998, 12, 31), "09:00", "10:00", "Alpha", "ganz weit weg");

                var all = t.Db.GetTimeEntriesBetween(DateTime.MinValue, DateTime.MaxValue);

                Assert.Equal(4, all.Count);
                Assert.Equal(new DateTime(1999, 12, 31), all.First().Datum);
                Assert.Equal(new DateTime(9998, 12, 31), all.Last().Datum);
            }
        }

        [Fact]
        public void The_report_over_all_bookings_spans_first_to_last_booking_of_the_chosen_projects_only()
        {
            using (var t = new TestDb())
            {
                t.Db.AddOrEnsureProject("Alpha", "Muster GmbH", "K.01");
                t.Db.AddOrEnsureProject("Beta", "Muster GmbH", "K.02");
                t.Db.AddTimeEntry(new DateTime(2025, 3, 3), "09:00", "10:00", "Alpha", "erste");
                t.Db.AddTimeEntry(new DateTime(2026, 10, 28), "09:00", "11:00", "Alpha", "letzte");
                t.Db.AddTimeEntry(new DateTime(2024, 1, 15), "09:00", "10:00", "Beta", "anderes Projekt, früher");
                t.Db.AddTimeEntry(new DateTime(2026, 11, 5), "09:00", "10:00", "Beta", "anderes Projekt, später");

                // Wie die Seite es macht: alles laden, Von/Bis aus den Buchungen der gewählten Projekte ableiten.
                var entries = t.Db.GetTimeEntriesBetween(DateTime.MinValue, DateTime.MaxValue);
                var dates = entries.Where(e => e.Projektname == "Alpha").Select(e => e.Datum.Date).ToList();
                var options = new ExportOptions { From = dates.Min(), To = dates.Max(), ProjectNames = new System.Collections.Generic.List<string> { "Alpha" } };

                var report = ExportReportBuilder.Build(entries, t.Db.GetProjects(), options);

                Assert.Equal(2, report.EntryCount);
                Assert.Equal(180, report.TotalMinutes);
                Assert.Equal(new DateTime(2025, 3, 3), report.FirstDate);
                Assert.Equal(new DateTime(2026, 10, 28), report.LastDate);
                Assert.Equal("03.03.2025 – 28.10.2026", ExportFormat.Period(options.From, options.To));
            }
        }
    }
}
