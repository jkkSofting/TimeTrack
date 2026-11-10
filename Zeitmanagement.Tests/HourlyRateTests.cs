// Target: .NET Framework + C# 7.3
using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;
using Zeitmanagement.Helpers;

namespace Zeitmanagement.Tests
{
    /// <summary>Umrechnung Geld → rechnerische Stunden über einen vom Benutzer eingetragenen Stundensatz.</summary>
    public class HourlyRateTests
    {
        private static decimal? Parse(string text, out bool valid)
        {
            decimal? rate;
            valid = CostMath.TryParseHourlyRate(text, out rate);
            return rate;
        }

        [Theory]
        [InlineData("95,50", "95.5")]
        [InlineData("95.50", "95.5")]
        [InlineData(" 95 ", "95")]
        [InlineData("1.234,56", "1234.56")]
        [InlineData("1,234.56", "1234.56")]
        [InlineData("120", "120")]
        [InlineData("0,5", "0.5")]
        [InlineData("1.000.000", null)]       // über dem Höchstwert
        public void Valid_rates_are_read_in_german_and_english_notation(string text, string expected)
        {
            bool valid;
            var rate = Parse(text, out valid);

            if (expected == null)
            {
                Assert.False(valid);
                return;
            }

            Assert.True(valid);
            Assert.Equal(decimal.Parse(expected, System.Globalization.CultureInfo.InvariantCulture), rate);
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        public void Empty_means_no_rate_and_is_valid(string text)
        {
            bool valid;
            Assert.Null(Parse(text, out valid));
            Assert.True(valid);
        }

        [Theory]
        [InlineData("abc")]
        [InlineData("0")]
        [InlineData("0,00")]
        [InlineData("-5")]
        [InlineData("95,5,0")]
        [InlineData("95 €")]
        [InlineData("100001")]
        public void Invalid_rates_are_rejected(string text)
        {
            bool valid;
            Assert.Null(Parse(text, out valid));
            Assert.False(valid);
        }

        [Fact]
        public void Hours_are_amount_divided_by_rate_and_stay_negative_for_overruns()
        {
            Assert.Equal(100m, CostMath.ToHours(10000m, 100m));
            Assert.Equal(-20m, CostMath.ToHours(-2000m, 100m));          // Rest bei Überschreitung
            Assert.Equal(833.3331m, CostMath.ToHours(83333.31m, 100m));
            Assert.Equal(0m, CostMath.ToHours(0m, 95.5m));               // bekannte Null ist Null
        }

        [Fact]
        public void Nothing_is_converted_without_a_known_amount_or_a_positive_rate()
        {
            Assert.Null(CostMath.ToHours(null, 100m));      // nicht geliefert bleibt unbekannt, nicht 0 h
            Assert.Null(CostMath.ToHours(100m, null));
            Assert.Null(CostMath.ToHours(100m, 0m));
            Assert.Null(CostMath.ToHours(100m, -1m));
        }

        [Fact]
        public void Hours_text_distinguishes_missing_amount_from_missing_rate()
        {
            Assert.Equal("833,33 h", CostMath.FormatHours(83333.31m, 100m));
            Assert.Equal("-20,00 h", CostMath.FormatHours(-2000m, 100m));
            Assert.Equal(CostMath.MissingText, CostMath.FormatHours(null, 100m));
            Assert.Equal("–", CostMath.FormatHours(100m, null));
            Assert.Equal("95,50", CostMath.FormatRate(95.5m));
            Assert.Equal(string.Empty, CostMath.FormatRate(null));
        }

        private static LocalProjectInfo Project(int id)
        {
            return new LocalProjectInfo { Id = id, Name = "P" + id, Customer = "K", Carrier = "X" };
        }

        private static ControllingOverviewRow Row(decimal? plan, decimal? actual)
        {
            return new ControllingOverviewRow
            {
                Observation = new ControllingObservation
                {
                    CarrierKey = "P", CarrierText = "P", SourceTimestamp = new DateTime(2026, 11, 9), PlannedCost = plan, ActualCost = actual
                }
            };
        }

        [Fact]
        public void Overview_rows_show_converted_hours_only_with_a_rate_and_notify_when_it_changes()
        {
            var row = Row(10000m, 12000m);
            var notified = new List<string>();
            row.PropertyChanged += (s, e) => notified.Add(e.PropertyName);

            Assert.Equal("–", row.PlannedHoursText);
            Assert.False(row.HasRate);

            row.SetRate(new RateResolution(100m, false));

            Assert.True(row.HasRate);
            Assert.Equal("100,00 h", row.PlannedHoursText);
            Assert.Equal("120,00 h", row.ActualHoursText);
            Assert.Equal("-20,00 h", row.RemainingHoursText);
            Assert.Equal(new[] { string.Empty }, notified.ToArray());   // leerer Name = alle Eigenschaften neu lesen

            row.SetRate(new RateResolution(100m, false));
            Assert.Single(notified);                                    // unverändert: keine Meldung
        }

        [Fact]
        public void Missing_plan_stays_not_delivered_in_hours_too()
        {
            var row = Row(null, 500m);
            row.SetRate(new RateResolution(100m, false));

            Assert.Equal(CostMath.MissingText, row.PlannedHoursText);
            Assert.Equal(CostMath.MissingText, row.RemainingHoursText);
            Assert.Equal("5,00 h", row.ActualHoursText);
        }

        // ---------- Der Satz gehört zum Projekt ----------

        [Fact]
        public void The_rate_of_a_single_linked_project_applies()
        {
            var rates = new Dictionary<int, decimal> { { 1, 95.5m } };

            var resolution = ControllingRates.Resolve(new[] { Project(1) }, rates);

            Assert.Equal(95.5m, resolution.Rate);
            Assert.False(resolution.IsAmbiguous);
        }

        [Fact]
        public void Projects_without_a_rate_do_not_get_one_from_other_projects_or_a_global_default()
        {
            var rates = new Dictionary<int, decimal> { { 1, 95.5m } };

            var resolution = ControllingRates.Resolve(new[] { Project(2) }, rates);   // Projekt 2 hat keinen Satz

            Assert.Null(resolution.Rate);
            Assert.False(resolution.IsAmbiguous);
            Assert.Null(ControllingRates.Resolve(new[] { Project(1) }, new Dictionary<int, decimal>()).Rate);
            Assert.Null(ControllingRates.Resolve(null, rates).Rate);
        }

        [Fact]
        public void Projects_sharing_a_carrier_with_the_same_rate_agree_and_unset_projects_stay_out()
        {
            var rates = new Dictionary<int, decimal> { { 1, 100m }, { 2, 100m } };

            Assert.Equal(100m, ControllingRates.Resolve(new[] { Project(1), Project(2) }, rates).Rate);
            Assert.Equal(100m, ControllingRates.Resolve(new[] { Project(1), Project(3) }, rates).Rate);   // 3 hat keinen Satz
        }

        [Fact]
        public void Projects_sharing_a_carrier_with_different_rates_are_ambiguous_and_nothing_is_converted()
        {
            var rates = new Dictionary<int, decimal> { { 1, 100m }, { 2, 120m } };

            var resolution = ControllingRates.Resolve(new[] { Project(1), Project(2) }, rates);

            Assert.True(resolution.IsAmbiguous);
            Assert.Null(resolution.Rate);          // kein Mittelwert, kein "erstes" Projekt

            var row = Row(10000m, 12000m);
            row.SetRate(resolution);
            Assert.True(row.HasRate);
            Assert.Equal(ControllingOverviewRow.AmbiguousRateText, row.ActualHoursText);
            Assert.Equal(ControllingOverviewRow.AmbiguousRateText, row.PlannedHoursText);
            Assert.Equal(ControllingOverviewRow.AmbiguousRateText, row.FormatHours(1m));
        }

        [Fact]
        public void Rates_are_stored_per_project_and_currency_context_and_survive_a_restart()
        {
            using (var t = new TestDb())
            {
                t.Db.AddOrEnsureProject("A", "K", "P1.01");
                t.Db.AddOrEnsureProject("B", "K", "P1.01");
                var ids = t.Db.GetLocalProjectInfos().ToDictionary(p => p.Name, p => p.Id);

                Assert.Empty(t.Db.GetControllingProjectRates("EUR"));

                t.Db.SetControllingProjectRate(ids["A"], "EUR", 95.5m);
                t.Db.SetControllingProjectRate(ids["B"], "EUR", 120m);
                t.Db.SetControllingProjectRate(ids["A"], "UNSPECIFIED", 80m);
                t.Restart();

                var eur = t.Db.GetControllingProjectRates("EUR");
                Assert.Equal(95.5m, eur[ids["A"]]);
                Assert.Equal(120m, eur[ids["B"]]);                              // jedes Projekt hat seinen eigenen Satz
                Assert.Equal(80m, t.Db.GetControllingProjectRates("UNSPECIFIED")[ids["A"]]);
                Assert.Empty(t.Db.GetControllingProjectRates("CHF"));           // ein Satz gilt nie für eine andere Währung

                t.Db.SetControllingProjectRate(ids["A"], "EUR", 100m);          // überschreiben
                Assert.Equal(100m, t.Db.GetControllingProjectRates("EUR")[ids["A"]]);
                Assert.Equal(120m, t.Db.GetControllingProjectRates("EUR")[ids["B"]]);

                t.Db.SetControllingProjectRate(ids["A"], "EUR", null);          // leer = Satz entfernen
                Assert.False(t.Db.GetControllingProjectRates("EUR").ContainsKey(ids["A"]));
                Assert.True(t.Db.GetControllingProjectRates("EUR").ContainsKey(ids["B"]));
            }
        }

        [Fact]
        public void Deleting_a_project_removes_only_its_rate_and_leaves_imports_alone()
        {
            using (var t = new TestDb())
            {
                t.Db.AddOrEnsureProject("A", "K", "P1.01");
                t.Db.AddOrEnsureProject("B", "K", "P1.01");
                var ids = t.Db.GetLocalProjectInfos().ToDictionary(p => p.Name, p => p.Id);
                Drafts.Save(t.Db, Drafts.Make(new DateTime(2026, 11, 9, 14, 30, 0), 100m, 50m));
                t.Db.SetControllingProjectRate(ids["A"], "UNSPECIFIED", 90m);
                t.Db.SetControllingProjectRate(ids["B"], "UNSPECIFIED", 110m);

                t.Db.DeleteProject("A");

                var rates = t.Db.GetControllingProjectRates("UNSPECIFIED");
                Assert.False(rates.ContainsKey(ids["A"]));
                Assert.Equal(110m, rates[ids["B"]]);
                Assert.Single(t.Db.GetControllingImports());
                Assert.Equal(1, t.Scalar("SELECT COUNT(*) FROM controlling_snapshots"));
            }
        }

        [Fact]
        public void Setting_a_rate_does_not_touch_imports_projects_or_time_entries()
        {
            using (var t = new TestDb())
            {
                t.Db.AddOrEnsureProject("A", "K", "P1.01");
                t.Db.AddTimeEntry(new DateTime(2026, 11, 2), "09:00", "10:30", "A");
                Drafts.Save(t.Db, Drafts.Make(new DateTime(2026, 11, 9, 14, 30, 0), 100m, 50m));
                var id = t.Db.GetLocalProjectInfos().Single().Id;

                t.Db.SetControllingProjectRate(id, "UNSPECIFIED", 95m);

                Assert.Equal(1, t.Scalar("SELECT COUNT(*) FROM projects"));
                Assert.Equal(1, t.Scalar("SELECT COUNT(*) FROM time_entries"));
                Assert.Equal(1, t.Scalar("SELECT COUNT(*) FROM controlling_snapshots"));
                Assert.Equal(50m, t.Db.GetControllingCarrierHistory(CarrierKeys.Normalize("P1.01"), "UNSPECIFIED").Single().ActualCost);
                Assert.Equal(1.5, t.Db.GetProjects().Single().AnzahlStunden);   // lokale Stunden bleiben die gebuchten
            }
        }
    }
}
