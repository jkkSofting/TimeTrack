// Target: .NET Framework + C# 7.3
using System;
using System.Data.SQLite;
using System.Linq;
using Xunit;

namespace Zeitmanagement.Tests
{
    /// <summary>Papierkorb (Soft-Delete) für Projekte und Buchungen.</summary>
    public class TrashTests
    {
        private static readonly DateTime Day = new DateTime(2026, 3, 2);

        private static void Seed(TimeTracker db)
        {
            db.AddOrEnsureProject("Alpha", "Kunde A", "KT-A");
            db.AddOrEnsureProject("Beta", "Kunde B", "KT-B");
        }

        private static int FirstEntryId(TimeTracker db, DateTime day, string projekt)
        {
            return db.GetTimeEntriesForDate(day).First(e => e.Projektname == projekt).Id;
        }

        private static double Hours(TimeTracker db, string projekt)
        {
            return db.GetProjects().First(p => p.Projektname == projekt).AnzahlStunden;
        }

        [Fact]
        public void TrashedEntry_IsInvisibleEverywhere_AndHoursAreCorrect()
        {
            using (var t = new TestDb())
            {
                var db = t.Db;
                Seed(db);
                db.AddTimeEntry(Day, "08:00", "10:00", "Alpha", "a1");
                db.AddTimeEntry(Day, "10:00", "11:00", "Alpha", "a2");
                int id = FirstEntryId(db, Day, "Alpha");

                Assert.Equal(3.0, Hours(db, "Alpha"), 3);
                Assert.True(db.MoveTimeEntryToTrash(id));

                Assert.Equal(1.0, Hours(db, "Alpha"), 3);
                Assert.Single(db.GetTimeEntriesForDate(Day));
                Assert.Single(db.GetTimeEntriesForProject("Alpha"));
                Assert.Single(db.GetTimeEntriesBetween(Day, Day));
                Assert.Equal(1.0, db.GetDailyHours(Day, Day)[Day], 3);
                Assert.Single(db.GetPluginTimeEntries(Day, Day));
                Assert.Single(db.GetTimeEntriesForProjectIds(db.GetLocalProjectInfos().Select(p => p.Id)));

                var trashed = db.GetTrashedTimeEntries();
                Assert.Single(trashed);
                Assert.Equal(id, trashed[0].Id);
                Assert.Equal("Alpha", trashed[0].Projektname);
                Assert.False(trashed[0].ProjektGeloescht);

                // Eine gelöschte Buchung lässt sich nicht mehr bearbeiten
                Assert.Throws<InvalidOperationException>(() => db.UpdateTimeEntry(id, Day, "08:00", "09:00", "Alpha"));
            }
        }

        [Fact]
        public void TrashedProject_IsInvisibleEverywhere_AndCannotBeBooked()
        {
            using (var t = new TestDb())
            {
                var db = t.Db;
                Seed(db);
                db.AddTimeEntry(Day, "08:00", "10:00", "Alpha");
                db.AddTimeEntry(Day, "10:00", "11:00", "Beta");

                Assert.True(db.MoveProjectToTrash("Alpha"));

                Assert.DoesNotContain(db.GetProjects(), p => p.Projektname == "Alpha");
                Assert.Single(db.GetTimeEntriesForDate(Day));
                Assert.Empty(db.GetTimeEntriesForProject("Alpha"));
                Assert.Single(db.GetTimeEntriesBetween(Day, Day));
                Assert.Equal(1.0, db.GetDailyHours(Day, Day)[Day], 3);
                Assert.Single(db.GetPluginProjects());
                Assert.Single(db.GetPluginTimeEntries(Day, Day));
                Assert.Single(db.GetLocalProjectInfos());
                Assert.Single(db.GetTimeEntriesForProjectIds(db.GetLocalProjectInfos().Select(p => p.Id)));
                Assert.Throws<InvalidOperationException>(() => db.AddTimeEntry(Day, "12:00", "13:00", "Alpha"));

                // Die Buchungen gehören zum Projekt und werden nicht einzeln gelistet
                Assert.Empty(db.GetTrashedTimeEntries());
                var tp = db.GetTrashedProjects();
                Assert.Single(tp);
                Assert.Equal("Alpha", tp[0].Projektname);
                Assert.Equal("Kunde A", tp[0].Kunde);
                Assert.Equal("KT-A", tp[0].Kostentraeger);
                Assert.Equal(1, tp[0].AnzahlBuchungen);
            }
        }

        [Fact]
        public void RestoreProject_BringsBackEntries_ButNotIndividuallyTrashedOnes()
        {
            using (var t = new TestDb())
            {
                var db = t.Db;
                Seed(db);
                db.AddTimeEntry(Day, "08:00", "10:00", "Alpha", "bleibt");
                db.AddTimeEntry(Day, "10:00", "11:00", "Alpha", "einzeln");
                int single = db.GetTimeEntriesForDate(Day).First(e => e.Beschreibung == "einzeln").Id;
                db.MoveTimeEntryToTrash(single);
                db.MoveProjectToTrash("Alpha");

                // einzeln gelöschte Buchung wird weiter gelistet (Projekt im Papierkorb), die andere nicht
                var te = db.GetTrashedTimeEntries();
                Assert.Single(te);
                Assert.True(te[0].ProjektGeloescht);
                Assert.Equal(1, db.GetTrashedProjects()[0].AnzahlBuchungen);

                Assert.Equal("Alpha", db.RestoreProject("Alpha"));

                Assert.Empty(db.GetTrashedProjects());
                Assert.Equal(2.0, Hours(db, "Alpha"), 3);
                var entries = db.GetTimeEntriesForProject("Alpha").ToList();
                Assert.Single(entries);
                Assert.Equal("bleibt", entries[0].Beschreibung);
                Assert.Single(db.GetTrashedTimeEntries());
                Assert.False(db.GetTrashedTimeEntries()[0].ProjektGeloescht);
            }
        }

        [Fact]
        public void RestoreTimeEntry_RestoresTrashedProjectToo()
        {
            using (var t = new TestDb())
            {
                var db = t.Db;
                Seed(db);
                db.AddTimeEntry(Day, "08:00", "10:00", "Alpha");
                db.AddTimeEntry(Day, "10:00", "11:00", "Alpha");
                int id = FirstEntryId(db, Day, "Alpha");

                db.MoveTimeEntryToTrash(id);
                Assert.Equal("Alpha", db.RestoreTimeEntry(id));
                Assert.Empty(db.GetTrashedTimeEntries());
                Assert.Equal(3.0, Hours(db, "Alpha"), 3);
                Assert.Null(db.RestoreTimeEntry(id)); // nicht (mehr) im Papierkorb

                db.MoveTimeEntryToTrash(id);
                db.MoveProjectToTrash("Alpha");
                Assert.Equal("Alpha", db.RestoreTimeEntry(id));
                Assert.Empty(db.GetTrashedProjects());
                Assert.Empty(db.GetTrashedTimeEntries());
                Assert.Equal(2, db.GetTimeEntriesForProject("Alpha").Count());
                Assert.Equal(3.0, Hours(db, "Alpha"), 3);
            }
        }

        [Fact]
        public void Purge_DeletesOnlyTrashedItems()
        {
            using (var t = new TestDb())
            {
                var db = t.Db;
                Seed(db);
                db.AddTimeEntry(Day, "08:00", "09:00", "Alpha");
                db.AddTimeEntry(Day, "09:00", "10:00", "Beta");
                int a = FirstEntryId(db, Day, "Alpha");

                Assert.False(db.PurgeProject("Alpha"));  // aktiv: nichts passiert
                Assert.False(db.PurgeTimeEntry(a));
                Assert.Equal(2, db.GetTimeEntriesForDate(Day).Count());

                db.MoveTimeEntryToTrash(a);
                Assert.True(db.PurgeTimeEntry(a));
                Assert.Empty(db.GetTrashedTimeEntries());
                Assert.Equal(0L, t.Scalar("SELECT COUNT(*) FROM time_entries WHERE projekt_id = (SELECT id FROM projects WHERE projektname='Alpha')"));

                db.MoveProjectToTrash("Beta");
                Assert.True(db.PurgeProject("Beta"));
                Assert.Empty(db.GetTrashedProjects());
                Assert.Equal(0L, t.Scalar("SELECT COUNT(*) FROM projects WHERE projektname='Beta'"));
                Assert.Equal(0L, t.Scalar("SELECT COUNT(*) FROM time_entries te WHERE te.projekt_id NOT IN (SELECT id FROM projects)"));
            }
        }

        [Fact]
        public void EmptyTrash_RemovesEverythingTrashed_AndKeepsActiveData()
        {
            using (var t = new TestDb())
            {
                var db = t.Db;
                Seed(db);
                db.AddOrEnsureProject("Gamma", "Kunde C", "KT-C");
                db.AddTimeEntry(Day, "08:00", "09:00", "Alpha");
                db.AddTimeEntry(Day, "09:00", "10:00", "Alpha");
                db.AddTimeEntry(Day, "10:00", "11:00", "Beta");
                db.AddTimeEntry(Day, "11:00", "12:00", "Gamma");
                db.MoveTimeEntryToTrash(db.GetTimeEntriesForDate(Day).First(e => e.Projektname == "Alpha").Id);
                db.MoveProjectToTrash("Beta");

                db.EmptyTrash();

                Assert.Empty(db.GetTrashedProjects());
                Assert.Empty(db.GetTrashedTimeEntries());
                Assert.Equal(2L, t.Scalar("SELECT COUNT(*) FROM time_entries"));
                Assert.Equal(2L, t.Scalar("SELECT COUNT(*) FROM projects"));
                Assert.Equal(2, db.GetProjects().Count());
            }
        }

        [Fact]
        public void AddOrEnsureProject_NeverRevivesTrashedProject_WithSameName()
        {
            using (var t = new TestDb())
            {
                var db = t.Db;
                Seed(db);
                db.AddTimeEntry(Day, "08:00", "10:00", "Alpha", "alt");
                db.MoveProjectToTrash("Alpha");

                db.AddOrEnsureProject("Alpha", "Neukunde", "KT-NEU");

                var active = db.GetProjects().First(p => p.Projektname == "Alpha");
                Assert.Equal("Neukunde", active.Kunde);
                Assert.Equal(0.0, active.AnzahlStunden, 3);
                Assert.Empty(db.GetTimeEntriesForProject("Alpha"));

                var trashed = db.GetTrashedProjects().Single();
                Assert.Equal("Alpha (gelöscht)", trashed.Projektname);
                Assert.Equal("Kunde A", trashed.Kunde);
                Assert.Equal(1, trashed.AnzahlBuchungen);

                // Zweiter Durchlauf: Zähler
                db.MoveProjectToTrash("Alpha");
                db.AddOrEnsureProject("Alpha", "Dritter", "KT-3");
                Assert.Equal(new[] { "Alpha (gelöscht 2)", "Alpha (gelöscht)" }.OrderBy(x => x),
                             db.GetTrashedProjects().Select(p => p.Projektname).OrderBy(x => x));
            }
        }

        [Fact]
        public void RestoreProject_WithNameConflict_UsesWiederhergestelltName()
        {
            using (var t = new TestDb())
            {
                var db = t.Db;
                Seed(db);
                db.AddTimeEntry(Day, "08:00", "10:00", "Alpha");
                db.MoveProjectToTrash("Alpha");
                db.AddOrEnsureProject("Alpha", "Neukunde", "KT-NEU"); // altes -> "Alpha (gelöscht)"

                string name = db.RestoreProject("Alpha (gelöscht)");

                Assert.Equal("Alpha (wiederhergestellt)", name);
                Assert.Empty(db.GetTrashedProjects());
                Assert.Equal("Neukunde", db.GetProjects().First(p => p.Projektname == "Alpha").Kunde);
                var restored = db.GetProjects().First(p => p.Projektname == name);
                Assert.Equal("Kunde A", restored.Kunde);
                Assert.Equal(2.0, restored.AnzahlStunden, 3);
                Assert.Single(db.GetTimeEntriesForProject(name));
                Assert.Empty(db.GetTimeEntriesForProject("Alpha"));

                // weiterer Konflikt -> Zähler
                db.MoveProjectToTrash(name);
                db.AddOrEnsureProject(name, "X", "KT-X");
                Assert.Null(db.RestoreProject("gibt es nicht"));
            }
        }

        [Fact]
        public void RestoreProject_WhenNameBecameFreeAgain_ReturnsOriginalName()
        {
            using (var t = new TestDb())
            {
                var db = t.Db;
                Seed(db);
                db.MoveProjectToTrash("Alpha");
                db.AddOrEnsureProject("Alpha", "Neu", "KT-N");     // altes -> "Alpha (gelöscht)"
                db.UpdateProject("Alpha", "Alpha2", "Neu", "KT-N"); // Name "Alpha" wieder frei

                Assert.Equal("Alpha", db.RestoreProject("Alpha (gelöscht)"));
                Assert.Equal("Kunde A", db.GetProjects().First(p => p.Projektname == "Alpha").Kunde);
            }
        }

        [Fact]
        public void UpdateProject_RenamingToNameOfTrashedProject_Works()
        {
            using (var t = new TestDb())
            {
                var db = t.Db;
                Seed(db);
                db.MoveProjectToTrash("Alpha");

                db.UpdateProject("Beta", "Alpha", "Kunde B", "KT-B");

                Assert.Contains(db.GetProjects(), p => p.Projektname == "Alpha");
                Assert.Equal("Alpha (gelöscht)", db.GetTrashedProjects().Single().Projektname);
            }
        }

        [Fact]
        public void Delete_StaysPermanent()
        {
            using (var t = new TestDb())
            {
                var db = t.Db;
                Seed(db);
                db.AddTimeEntry(Day, "08:00", "09:00", "Alpha");
                db.DeleteTimeEntry(FirstEntryId(db, Day, "Alpha"));
                db.DeleteProject("Alpha");
                Assert.Empty(db.GetTrashedProjects());
                Assert.Empty(db.GetTrashedTimeEntries());
                Assert.Equal(0L, t.Scalar("SELECT COUNT(*) FROM projects WHERE projektname='Alpha'"));
            }
        }

        [Fact]
        public void Trash_SurvivesRestart()
        {
            using (var t = new TestDb())
            {
                Seed(t.Db);
                t.Db.AddTimeEntry(Day, "08:00", "09:00", "Alpha");
                t.Db.MoveProjectToTrash("Alpha");
                t.Restart();
                Assert.Single(t.Db.GetTrashedProjects());
                Assert.DoesNotContain(t.Db.GetProjects(), p => p.Projektname == "Alpha");
            }
        }

        [Fact]
        public void CountTimeEntries_IgnoresTrashedEntries()
        {
            using (var t = new TestDb())
            {
                var db = t.Db;
                Seed(db);
                db.AddTimeEntry(Day, "08:00", "09:00", "Alpha", "a1");
                db.AddTimeEntry(Day, "09:00", "10:00", "Alpha", "a2");
                Assert.Equal(2, db.CountTimeEntries("Alpha"));

                Assert.True(db.MoveTimeEntryToTrash(FirstEntryId(db, Day, "Alpha")));
                Assert.Equal(1, db.CountTimeEntries("Alpha"));

                Assert.True(db.MoveProjectToTrash("Alpha"));
                Assert.Equal(0, db.CountTimeEntries("Alpha"));
            }
        }

        [Fact]
        public void Migration_OldDatabaseWithoutTrashColumns_IsUpgraded_AndSumsRecalculated()
        {
            string path = Fixtures.TempDbPath();
            using (var conn = new SQLiteConnection("Data Source=" + path))
            {
                conn.Open();
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = @"
                    CREATE TABLE projects(
                        id INTEGER PRIMARY KEY AUTOINCREMENT, projektname TEXT NOT NULL UNIQUE, kunde TEXT NOT NULL,
                        anzahl_stunden REAL NOT NULL DEFAULT 0.0, kostentraeger TEXT NOT NULL,
                        ist_extern INTEGER NOT NULL DEFAULT 0, rundung_minuten INTEGER NOT NULL DEFAULT 0,
                        quickselect_aktiv INTEGER NOT NULL DEFAULT 1, rundung_global INTEGER NOT NULL DEFAULT 1);
                    CREATE TABLE time_entries(
                        id INTEGER PRIMARY KEY AUTOINCREMENT, datum TEXT NOT NULL, startzeit TEXT NOT NULL, endzeit TEXT NOT NULL,
                        projekt_id INTEGER NOT NULL, beschreibung TEXT,
                        FOREIGN KEY(projekt_id) REFERENCES projects(id) ON DELETE CASCADE);
                    CREATE TRIGGER te_ai AFTER INSERT ON time_entries BEGIN
                      UPDATE projects SET anzahl_stunden = 99 WHERE id = NEW.projekt_id; END;
                    INSERT INTO projects (projektname, kunde, kostentraeger, anzahl_stunden) VALUES ('Alt', 'K', 'KT', 99);
                    INSERT INTO time_entries (datum, startzeit, endzeit, projekt_id) VALUES ('2026-03-02', '08:00', '10:00', 1);";
                    cmd.ExecuteNonQuery();
                }
            }
            SQLiteConnection.ClearAllPools();

            using (var db = new TimeTracker(path))
            {
                var p = db.GetProjects().Single();
                Assert.Equal("Alt", p.Projektname);
                Assert.Equal(2.0, p.AnzahlStunden, 3); // einmalige Neuberechnung

                db.AddTimeEntry(Day, "10:00", "11:00", "Alt");
                Assert.Equal(3.0, db.GetProjects().Single().AnzahlStunden, 3); // neuer Trigger statt des alten
                Assert.True(db.MoveTimeEntryToTrash(db.GetTimeEntriesForDate(Day).Single(e => e.Startzeit == "10:00").Id));
                Assert.Equal(2.0, db.GetProjects().Single().AnzahlStunden, 3);
            }

            // idempotent: erneutes Öffnen ändert nichts
            SQLiteConnection.ClearAllPools();
            using (var db = new TimeTracker(path))
            {
                Assert.Equal(2.0, db.GetProjects().Single().AnzahlStunden, 3);
                Assert.Single(db.GetTrashedTimeEntries());
            }
            SQLiteConnection.ClearAllPools();
            try { System.IO.Directory.Delete(System.IO.Path.GetDirectoryName(path), true); } catch (System.IO.IOException) { }
        }
    }
}
