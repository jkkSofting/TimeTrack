// Target: .NET Framework + C# 7.3
using System;
using System.Data.SQLite;
using System.IO;
using System.Linq;
using Xunit;

namespace Zeitmanagement.Tests
{
    /// <summary>Angaben für den Tätigkeitsnachweis je Projekt (Auftragsnummer, Auftraggeber) in der Datenbank.</summary>
    public class ExportMetaRepositoryTests
    {
        private static TimeTracker.ExportProjectMeta Meta(string number, string name = null, string block = null)
        {
            return new TimeTracker.ExportProjectMeta { Auftragsnummer = number, AuftraggeberName = name, AuftraggeberBlock = block };
        }

        [Fact]
        public void A_project_without_saved_details_has_none()
        {
            using (var t = new TestDb())
            {
                t.Db.AddOrEnsureProject("Alpha", "Muster GmbH", "K.01");

                Assert.Null(t.Db.GetExportProjectMeta("Alpha"));
                Assert.Null(t.Db.GetExportProjectMeta("gibt es nicht"));
                Assert.Null(t.Db.GetExportProjectMeta(null));
            }
        }

        [Fact]
        public void Details_round_trip_and_survive_a_restart()
        {
            using (var t = new TestDb())
            {
                t.Db.AddOrEnsureProject("Alpha", "Muster GmbH", "K.01");
                t.Db.SaveExportProjectMeta("Alpha", Meta("PO-4711", "Muster GmbH, Abt. IT", "Musterweg 2\n80331 München\nz. H. Frau Beispiel"));
                t.Restart();

                var meta = t.Db.GetExportProjectMeta("Alpha");
                Assert.Equal("PO-4711", meta.Auftragsnummer);
                Assert.Equal("Muster GmbH, Abt. IT", meta.AuftraggeberName);
                Assert.Equal("Musterweg 2\n80331 München\nz. H. Frau Beispiel", meta.AuftraggeberBlock);
                Assert.False(meta.IsEmpty);
            }
        }

        [Fact]
        public void Saving_again_replaces_the_details()
        {
            using (var t = new TestDb())
            {
                t.Db.AddOrEnsureProject("Alpha", "Muster GmbH", "K.01");
                t.Db.SaveExportProjectMeta("Alpha", Meta("PO-1", "Name 1", "Block 1"));
                t.Db.SaveExportProjectMeta("Alpha", Meta("PO-2"));

                var meta = t.Db.GetExportProjectMeta("Alpha");
                Assert.Equal("PO-2", meta.Auftragsnummer);
                Assert.Null(meta.AuftraggeberName);
                Assert.Null(meta.AuftraggeberBlock);
                Assert.Equal(1, t.Scalar("SELECT COUNT(*) FROM export_project_meta"));
            }
        }

        [Fact]
        public void Texts_are_trimmed_and_blank_ones_are_not_stored()
        {
            using (var t = new TestDb())
            {
                t.Db.AddOrEnsureProject("Alpha", "Muster GmbH", "K.01");
                t.Db.SaveExportProjectMeta("Alpha", Meta("  PO-9  \r\n", "   ", "\n"));

                var meta = t.Db.GetExportProjectMeta("Alpha");
                Assert.Equal("PO-9", meta.Auftragsnummer);
                Assert.Null(meta.AuftraggeberName);
                Assert.Null(meta.AuftraggeberBlock);
            }
        }

        [Fact]
        public void Saving_only_empty_details_removes_the_entry()
        {
            using (var t = new TestDb())
            {
                t.Db.AddOrEnsureProject("Alpha", "Muster GmbH", "K.01");
                t.Db.SaveExportProjectMeta("Alpha", Meta("PO-1"));

                t.Db.SaveExportProjectMeta("Alpha", Meta("", " ", null));

                Assert.Null(t.Db.GetExportProjectMeta("Alpha"));
                Assert.Equal(0, t.Scalar("SELECT COUNT(*) FROM export_project_meta"));
            }
        }

        [Fact]
        public void Details_belong_to_one_project_each()
        {
            using (var t = new TestDb())
            {
                t.Db.AddOrEnsureProject("Alpha", "Muster GmbH", "K.01");
                t.Db.AddOrEnsureProject("Beta", "Muster GmbH", "K.02");
                t.Db.SaveExportProjectMeta("Alpha", Meta("PO-A"));
                t.Db.SaveExportProjectMeta("Beta", Meta("PO-B"));

                Assert.Equal("PO-A", t.Db.GetExportProjectMeta("Alpha").Auftragsnummer);
                Assert.Equal("PO-B", t.Db.GetExportProjectMeta("Beta").Auftragsnummer);
            }
        }

        [Fact]
        public void Saving_for_an_unknown_project_does_nothing()
        {
            using (var t = new TestDb())
            {
                t.Db.SaveExportProjectMeta("gibt es nicht", Meta("PO-1"));
                t.Db.SaveExportProjectMeta("", Meta("PO-1"));
                t.Db.SaveExportProjectMeta(null, Meta("PO-1"));

                Assert.Equal(0, t.Scalar("SELECT COUNT(*) FROM export_project_meta"));
            }
        }

        [Fact]
        public void Details_follow_a_renamed_project()
        {
            using (var t = new TestDb())
            {
                t.Db.AddOrEnsureProject("Alpha", "Muster GmbH", "K.01");
                t.Db.SaveExportProjectMeta("Alpha", Meta("PO-1"));

                t.Db.UpdateProject("Alpha", "Alpha neu", "Muster GmbH", "K.01");

                Assert.Null(t.Db.GetExportProjectMeta("Alpha"));
                Assert.Equal("PO-1", t.Db.GetExportProjectMeta("Alpha neu").Auftragsnummer);
            }
        }

        [Fact]
        public void Deleting_the_project_deletes_its_details_but_nothing_else()
        {
            using (var t = new TestDb())
            {
                t.Db.AddOrEnsureProject("Alpha", "Muster GmbH", "K.01");
                t.Db.AddOrEnsureProject("Beta", "Muster GmbH", "K.02");
                t.Db.SaveExportProjectMeta("Alpha", Meta("PO-A"));
                t.Db.SaveExportProjectMeta("Beta", Meta("PO-B"));

                t.Db.DeleteProject("Alpha");

                Assert.Equal(1, t.Scalar("SELECT COUNT(*) FROM export_project_meta"));
                Assert.Equal("PO-B", t.Db.GetExportProjectMeta("Beta").Auftragsnummer);
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
                        INSERT INTO time_entries(datum, startzeit, endzeit, projekt_id, beschreibung) VALUES ('2026-11-02', '09:00', '10:30', 1, 'Altbuchung');";
                        cmd.ExecuteNonQuery();
                    }
                }
                SQLiteConnection.ClearAllPools();

                using (var db = new TimeTracker(path))
                {
                    Assert.Null(db.GetExportProjectMeta("Alt"));
                    db.SaveExportProjectMeta("Alt", Meta("PO-ALT"));

                    Assert.Equal("PO-ALT", db.GetExportProjectMeta("Alt").Auftragsnummer);
                    Assert.Equal("Altbuchung", db.GetTimeEntriesForProject("Alt").Single().Beschreibung);
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
