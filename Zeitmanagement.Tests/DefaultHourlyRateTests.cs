// Target: .NET Framework + C# 7.3
using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.IO;
using System.Linq;
using Xunit;
using Zeitmanagement.Helpers;

namespace Zeitmanagement.Tests
{
    /// <summary>
    /// Standard-Stundensatz im Projektcontrolling: gilt für jedes Projekt ohne eigenen Satz, ein Satz im Projekt überschreibt ihn.
    /// </summary>
    public class DefaultHourlyRateTests
    {
        private static LocalProjectInfo P(int id)
        {
            return new LocalProjectInfo { Id = id, Name = "P" + id, Customer = "K", Carrier = "X.01" };
        }

        // ---------- Auflösung je PSP-Element ----------

        [Fact]
        public void Without_a_default_nothing_changes_projects_without_a_rate_stay_out()
        {
            var rates = new Dictionary<int, decimal> { { 1, 100m } };

            Assert.Equal(100m, ControllingRates.Resolve(new[] { P(1), P(2) }, rates).Rate);   // Projekt 2 ohne Satz bleibt außen vor
            Assert.Null(ControllingRates.Resolve(new[] { P(2) }, rates).Rate);
            Assert.Null(ControllingRates.Resolve(new[] { P(2) }, rates, null).Rate);
        }

        [Fact]
        public void A_project_without_its_own_rate_takes_the_default()
        {
            var resolution = ControllingRates.Resolve(new[] { P(1) }, new Dictionary<int, decimal>(), 95m);

            Assert.Equal(95m, resolution.Rate);
            Assert.False(resolution.IsAmbiguous);
        }

        [Fact]
        public void A_rate_in_the_project_overrides_the_default_for_that_project()
        {
            var rates = new Dictionary<int, decimal> { { 1, 120m } };

            Assert.Equal(120m, ControllingRates.Resolve(new[] { P(1) }, rates, 95m).Rate);
            Assert.Equal(95m, ControllingRates.Resolve(new[] { P(2) }, rates, 95m).Rate);
        }

        [Fact]
        public void Projects_on_one_carrier_with_different_effective_rates_are_ambiguous_as_before()
        {
            var rates = new Dictionary<int, decimal> { { 1, 120m } };   // Projekt 2 erbt den Standard 95

            var resolution = ControllingRates.Resolve(new[] { P(1), P(2) }, rates, 95m);

            Assert.True(resolution.IsAmbiguous);
            Assert.Null(resolution.Rate);
        }

        [Fact]
        public void An_override_equal_to_the_default_is_just_that_rate()
        {
            var rates = new Dictionary<int, decimal> { { 1, 95m } };

            var resolution = ControllingRates.Resolve(new[] { P(1), P(2) }, rates, 95m);

            Assert.Equal(95m, resolution.Rate);
            Assert.False(resolution.IsAmbiguous);
        }

        [Fact]
        public void Two_projects_on_the_default_share_one_rate()
        {
            var resolution = ControllingRates.Resolve(new[] { P(1), P(2), P(3) }, new Dictionary<int, decimal>(), 95m);

            Assert.Equal(95m, resolution.Rate);
            Assert.False(resolution.IsAmbiguous);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-5)]
        public void A_default_up_to_zero_counts_as_none(int value)
        {
            Assert.Null(ControllingRates.Resolve(new[] { P(1) }, new Dictionary<int, decimal>(), (decimal)value).Rate);
        }

        [Fact]
        public void Missing_inputs_are_tolerated()
        {
            Assert.Null(ControllingRates.Resolve(null, new Dictionary<int, decimal>(), 95m).Rate);
            Assert.Equal(95m, ControllingRates.Resolve(new[] { P(1) }, null, 95m).Rate);
            Assert.Null(ControllingRates.Resolve(new LocalProjectInfo[0], new Dictionary<int, decimal>(), 95m).Rate);   // kein Projekt, keine Umrechnung
        }

        // ---------- Datenbank ----------

        [Fact]
        public void No_default_is_stored_at_first()
        {
            using (var t = new TestDb())
            {
                Assert.Null(t.Db.GetControllingDefaultRate("EUR"));
                Assert.Null(t.Db.GetControllingDefaultRate(CostMath.CurrencyContext(null)));
            }
        }

        [Fact]
        public void The_default_round_trips_exactly_and_survives_a_restart()
        {
            using (var t = new TestDb())
            {
                t.Db.SetControllingDefaultRate("EUR", 95.55m);
                t.Restart();

                Assert.Equal(95.55m, t.Db.GetControllingDefaultRate("EUR"));
            }
        }

        [Fact]
        public void Setting_again_replaces_it_and_each_currency_has_its_own()
        {
            using (var t = new TestDb())
            {
                t.Db.SetControllingDefaultRate("EUR", 90m);
                t.Db.SetControllingDefaultRate("EUR", 100m);
                t.Db.SetControllingDefaultRate("USD", 120m);
                t.Db.SetControllingDefaultRate(CostMath.CurrencyContext(null), 80m);

                Assert.Equal(100m, t.Db.GetControllingDefaultRate("EUR"));
                Assert.Equal(120m, t.Db.GetControllingDefaultRate("USD"));
                Assert.Equal(80m, t.Db.GetControllingDefaultRate(CostMath.CurrencyContext(null)));
                Assert.Equal(3, t.Scalar("SELECT COUNT(*) FROM controlling_default_rates"));
            }
        }

        [Theory]
        [InlineData(null)]
        [InlineData(0)]
        [InlineData(-1)]
        public void Clearing_or_a_non_positive_rate_removes_the_default(int? value)
        {
            using (var t = new TestDb())
            {
                t.Db.SetControllingDefaultRate("EUR", 95m);

                t.Db.SetControllingDefaultRate("EUR", value.HasValue ? (decimal?)value.Value : null);

                Assert.Null(t.Db.GetControllingDefaultRate("EUR"));
                Assert.Equal(0, t.Scalar("SELECT COUNT(*) FROM controlling_default_rates"));
            }
        }

        [Fact]
        public void The_default_and_the_project_rates_are_independent_in_the_database()
        {
            using (var t = new TestDb())
            {
                t.Db.AddOrEnsureProject("Alpha", "Muster GmbH", "X.01");
                var id = t.Db.GetLocalProjectInfos().Single().Id;
                t.Db.SetControllingProjectRate(id, "EUR", 120m);
                t.Db.SetControllingDefaultRate("EUR", 95m);

                Assert.Equal(120m, t.Db.GetControllingProjectRates("EUR")[id]);
                Assert.Equal(95m, t.Db.GetControllingDefaultRate("EUR"));

                // Den Satz im Projekt zu entfernen lässt den Standard stehen; das Projekt fällt auf ihn zurück.
                t.Db.SetControllingProjectRate(id, "EUR", null);
                Assert.Empty(t.Db.GetControllingProjectRates("EUR"));
                Assert.Equal(95m, t.Db.GetControllingDefaultRate("EUR"));

                // Und das Löschen des Projekts rührt den Standard nicht an.
                t.Db.DeleteProject("Alpha");
                Assert.Equal(95m, t.Db.GetControllingDefaultRate("EUR"));
            }
        }

        [Fact]
        public void An_old_database_without_the_table_gets_it_and_keeps_its_data()
        {
            var path = Fixtures.TempDbPath();
            try
            {
                using (var conn = new SQLiteConnection("Data Source=" + path))
                {
                    conn.Open();
                    using (var cmd = conn.CreateCommand())
                    {
                        cmd.CommandText = @"
                        CREATE TABLE projects(id INTEGER PRIMARY KEY AUTOINCREMENT, projektname TEXT NOT NULL UNIQUE, kunde TEXT NOT NULL,
                                              anzahl_stunden REAL NOT NULL DEFAULT 0.0, kostentraeger TEXT NOT NULL);
                        CREATE TABLE time_entries(id INTEGER PRIMARY KEY AUTOINCREMENT, datum TEXT NOT NULL, startzeit TEXT NOT NULL,
                                                  endzeit TEXT NOT NULL, projekt_id INTEGER NOT NULL, beschreibung TEXT,
                                                  FOREIGN KEY(projekt_id) REFERENCES projects(id) ON DELETE CASCADE);
                        INSERT INTO projects(projektname, kunde, kostentraeger) VALUES ('Alt', 'Kunde', 'X.01');
                        INSERT INTO time_entries(datum, startzeit, endzeit, projekt_id) VALUES ('2026-11-02', '09:00', '10:30', 1);";
                        cmd.ExecuteNonQuery();
                    }
                }
                SQLiteConnection.ClearAllPools();

                using (var db = new TimeTracker(path))
                {
                    Assert.Null(db.GetControllingDefaultRate("EUR"));
                    db.SetControllingDefaultRate("EUR", 95m);

                    Assert.Equal(95m, db.GetControllingDefaultRate("EUR"));
                    Assert.Single(db.GetTimeEntriesForProject("Alt"));
                }
            }
            finally
            {
                SQLiteConnection.ClearAllPools();
                try { Directory.Delete(Path.GetDirectoryName(path), true); } catch (IOException) { }
            }
        }
    }
}
