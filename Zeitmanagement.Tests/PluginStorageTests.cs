// Target: .NET Framework + C# 7.3
using System;
using System.Data.SQLite;
using System.Linq;
using Xunit;
using Zeitmanagement.Helpers.Plugins;

namespace Zeitmanagement.Tests
{
    /// <summary>Ablage der Plugins in <c>plugin_records</c> und lesender Datenzugriff für Plugins.</summary>
    public class PluginStorageTests
    {
        [Fact]
        public void Values_round_trip_and_survive_a_restart()
        {
            using (var t = new TestDb())
            {
                new PluginStorage(t.Db, "map").Set("mapping", "42", "{\"orderId\":7}");
                t.Restart();

                Assert.Equal("{\"orderId\":7}", new PluginStorage(t.Db, "map").Get("mapping", "42"));
            }
        }

        [Fact]
        public void Setting_again_overwrites_and_null_deletes()
        {
            using (var t = new TestDb())
            {
                var storage = new PluginStorage(t.Db, "map");
                storage.Set("c", "k", "1");
                storage.Set("c", "k", "2");
                Assert.Equal("2", storage.Get("c", "k"));
                Assert.Equal(1, t.Scalar("SELECT COUNT(*) FROM plugin_records"));

                storage.Set("c", "k", null);
                Assert.Null(storage.Get("c", "k"));
                Assert.Equal(0, t.Scalar("SELECT COUNT(*) FROM plugin_records"));
            }
        }

        [Fact]
        public void Delete_reports_whether_the_entry_existed()
        {
            using (var t = new TestDb())
            {
                var storage = new PluginStorage(t.Db, "map");
                storage.Set("c", "k", "x");

                Assert.True(storage.Delete("c", "k"));
                Assert.False(storage.Delete("c", "k"));
                Assert.Null(storage.Get("c", "k"));
            }
        }

        [Fact]
        public void List_returns_only_the_requested_collection()
        {
            using (var t = new TestDb())
            {
                var storage = new PluginStorage(t.Db, "map");
                storage.Set("sync", "b", "2");
                storage.Set("sync", "a", "1");
                storage.Set("other", "c", "3");

                var list = storage.List("sync");
                Assert.Equal(new[] { "a", "b" }, list.Keys.ToArray());
                Assert.Equal("1", list["a"]);
                Assert.Empty(storage.List("leer"));
            }
        }

        [Fact]
        public void Two_plugins_do_not_see_each_others_entries()
        {
            using (var t = new TestDb())
            {
                var first = new PluginStorage(t.Db, "first");
                var second = new PluginStorage(t.Db, "second");
                first.Set("c", "k", "eins");
                second.Set("c", "k", "zwei");
                first.SetSecret("token", "geheim");

                Assert.Equal("eins", first.Get("c", "k"));
                Assert.Equal("zwei", second.Get("c", "k"));
                Assert.Single(second.List("c"));
                Assert.Null(second.GetSecret("token"));

                second.Delete("c", "k");
                Assert.Equal("eins", first.Get("c", "k"));
            }
        }

        [Fact]
        public void Reserved_collections_and_invalid_names_are_rejected()
        {
            using (var t = new TestDb())
            {
                var storage = new PluginStorage(t.Db, "map");

                Assert.Throws<ArgumentException>(() => storage.Get(PluginStorage.SecretCollection, "token"));
                Assert.Throws<ArgumentException>(() => storage.Set("$host", "k", "v"));
                Assert.Throws<ArgumentException>(() => storage.List(""));
                Assert.Throws<ArgumentException>(() => storage.Set("c", null, "v"));
                Assert.Throws<ArgumentException>(() => storage.Set("c", new string('k', 201), "v"));
            }
        }

        [Theory]
        [InlineData("map", true)]
        [InlineData("timetrack.map-sync_2", true)]
        [InlineData("Map", false)]
        [InlineData("$host", false)]
        [InlineData("", false)]
        [InlineData(null, false)]
        [InlineData("mit leerzeichen", false)]
        public void Plugin_ids_are_validated(string id, bool valid)
        {
            Assert.Equal(valid, PluginStorage.IsValidPluginId(id));
        }

        [Fact]
        public void Secrets_round_trip_and_are_not_stored_in_plain_text()
        {
            using (var t = new TestDb())
            {
                var storage = new PluginStorage(t.Db, "map");
                storage.SetSecret("token", "tt_SehrGeheimesToken123");
                t.Restart();

                Assert.Equal("tt_SehrGeheimesToken123", new PluginStorage(t.Db, "map").GetSecret("token"));
                Assert.Equal(1, t.Scalar("SELECT COUNT(*) FROM plugin_records WHERE collection = '$secret'"));
                Assert.Equal(0, t.Scalar("SELECT COUNT(*) FROM plugin_records WHERE json LIKE '%SehrGeheim%'"));
            }
        }

        [Fact]
        public void An_empty_secret_deletes_it_and_a_foreign_one_is_unreadable()
        {
            using (var t = new TestDb())
            {
                var storage = new PluginStorage(t.Db, "map");
                storage.SetSecret("token", "abc");
                storage.SetSecret("token", "");
                Assert.Null(storage.GetSecret("token"));

                // Wie nach dem Wiederherstellen auf einem anderen Rechner: DPAPI kann den Wert nicht entschlüsseln.
                t.Db.SetPluginRecord("map", PluginStorage.SecretCollection, "token", "dpapi:" + Convert.ToBase64String(new byte[] { 1, 2, 3, 4 }));
                Assert.Null(storage.GetSecret("token"));
                t.Db.SetPluginRecord("map", PluginStorage.SecretCollection, "token", "klartext");
                Assert.Null(storage.GetSecret("token"));
            }
        }

        [Fact]
        public void An_old_database_without_the_table_gets_it_and_keeps_its_data()
        {
            var path = Fixtures.TempDbPath();
            SQLiteConnection.CreateFile(path);
            using (var conn = new SQLiteConnection("Data Source=" + path))
            {
                conn.Open();
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = @"
                    CREATE TABLE projects(id INTEGER PRIMARY KEY AUTOINCREMENT, projektname TEXT NOT NULL UNIQUE, kunde TEXT NOT NULL,
                                          anzahl_stunden REAL NOT NULL DEFAULT 0.0, kostentraeger TEXT NOT NULL);
                    CREATE TABLE time_entries(id INTEGER PRIMARY KEY AUTOINCREMENT, datum TEXT NOT NULL, startzeit TEXT NOT NULL,
                                              endzeit TEXT NOT NULL, projekt_id INTEGER NOT NULL, beschreibung TEXT);
                    INSERT INTO projects(projektname, kunde, kostentraeger) VALUES ('Alt', 'Kunde', 'K.01');
                    INSERT INTO time_entries(datum, startzeit, endzeit, projekt_id, beschreibung) VALUES ('2026-11-10', '08:00', '09:30', 1, 'ABC-1');";
                    cmd.ExecuteNonQuery();
                }
            }
            SQLiteConnection.ClearAllPools();

            using (var db = new TimeTracker(path))
            {
                new PluginStorage(db, "map").Set("c", "k", "v");
                Assert.Equal("v", new PluginStorage(db, "map").Get("c", "k"));
                Assert.Single(db.GetPluginTimeEntries(new DateTime(2026, 11, 1), new DateTime(2026, 11, 30)));
            }
            SQLiteConnection.ClearAllPools();
        }

        [Fact]
        public void Plugins_read_projects_and_entries_with_ids_and_carrier()
        {
            using (var t = new TestDb())
            {
                t.Db.AddOrEnsureProject("Beta", "Kunde B", "1500410A24RD001.01", istExtern: true);
                t.Db.AddOrEnsureProject("Alpha", "Kunde A", "K.01");
                t.Db.AddTimeEntry(new DateTime(2026, 11, 10), "13:00", "14:15", "Beta", "ABC-12 Review");
                t.Db.AddTimeEntry(new DateTime(2026, 11, 10), "8:00", "9:30", "Alpha");
                t.Db.AddTimeEntry(new DateTime(2026, 11, 12), "08:00", "09:00", "Alpha", "außerhalb");

                var projects = t.Db.GetPluginProjects();
                Assert.Equal(new[] { "Alpha", "Beta" }, projects.Select(p => p.Name).ToArray());
                Assert.True(projects[1].IsExternal);
                Assert.Equal("1500410A24RD001.01", projects[1].CostCarrier);

                var entries = t.Db.GetPluginTimeEntries(new DateTime(2026, 11, 10), new DateTime(2026, 11, 11));
                Assert.Equal(2, entries.Count);
                Assert.Equal("Alpha", entries[0].ProjectName);
                Assert.Equal(TimeSpan.FromMinutes(90), entries[0].Duration);
                Assert.Null(entries[0].Description);
                Assert.Equal(projects[1].Id, entries[1].ProjectId);
                Assert.Equal("ABC-12 Review", entries[1].Description);
                Assert.Equal("1500410A24RD001.01", entries[1].CostCarrier);
                Assert.True(entries[1].Id > 0);
            }
        }
    }
}
