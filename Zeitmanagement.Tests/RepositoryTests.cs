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
    /// <summary>Wegwerf-Datenbank im Temp-Ordner; die echte Datenbank der App wird nie angefasst.</summary>
    internal sealed class TestDb : IDisposable
    {
        public TestDb()
        {
            Path = Fixtures.TempDbPath();
            Db = new TimeTracker(Path);
        }

        public string Path { get; }
        public TimeTracker Db { get; private set; }

        /// <summary>Schließt und öffnet die Datenbank neu ("Neustart").</summary>
        public void Restart()
        {
            Db.Dispose();
            SQLiteConnection.ClearAllPools();
            Db = new TimeTracker(Path);
        }

        public long Scalar(string sql)
        {
            using (var conn = new SQLiteConnection("Data Source=" + Path + ";Read Only=True"))
            {
                conn.Open();
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = sql;
                    return Convert.ToInt64(cmd.ExecuteScalar());
                }
            }
        }

        public void Dispose()
        {
            Db.Dispose();
            SQLiteConnection.ClearAllPools();
            try { Directory.Delete(System.IO.Path.GetDirectoryName(Path), true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }

    internal static class Drafts
    {
        public static ControllingImportDraft Make(
            DateTime sourceTimestamp, decimal? plan, decimal? actual,
            string key = "P1.01", string fileHash = "file-1", string currency = null,
            string allowlistHash = "allow-1", string fileName = "stand.xlsx", string parentId = "P1")
        {
            var draft = new ControllingImportDraft
            {
                FileName = fileName,
                FileHash = fileHash,
                SourceTimestamp = sourceTimestamp,
                TimestampOrigin = TimestampOrigin.Workbook,
                CurrencyCode = currency,
                AllowlistHash = allowlistHash,
                PayloadHash = "payload|" + key + "|" + plan + "|" + actual,
                ScannedCount = 5,
                IgnoredCount = 4
            };
            draft.Rows.Add(new ControllingDraftRow
            {
                CarrierKey = CarrierKeys.Normalize(key),
                CarrierText = key,
                SourceSheet = "SAP#2PL",
                SourceRow = 2,
                ExternalParentId = parentId,
                ParentKey = CarrierKeys.NormalizeOrNull(parentId),
                ParentName = "Projekt",
                PspName = "PSP " + key,
                ProfitCenter = "SAE",
                PlannedCost = plan,
                ActualCost = actual
            });
            return draft;
        }

        public static ControllingSaveResult Save(TimeTracker db, ControllingImportDraft draft)
        {
            return db.SaveControllingImport(draft);
        }
    }

    public class RepositoryTests
    {
        private static readonly DateTime T1 = new DateTime(2026, 9, 30, 14, 30, 0);
        private static readonly DateTime T2 = new DateTime(2026, 10, 31, 14, 30, 0);
        private static readonly DateTime T3 = new DateTime(2026, 11, 9, 14, 30, 0);
        private static readonly string Key = CarrierKeys.Normalize("P1.01");

        [Fact]
        public void Schema_is_created_idempotently_and_survives_restart()
        {
            using (var t = new TestDb())
            {
                Drafts.Save(t.Db, Drafts.Make(T1, 100m, 50m));
                t.Restart();
                t.Restart();

                Assert.Single(t.Db.GetControllingImports());
                Assert.Equal(1, t.Scalar("SELECT COUNT(*) FROM controlling_snapshots"));
            }
        }

        [Fact]
        public void Old_database_migrates_without_data_loss()
        {
            var path = Fixtures.TempDbPath();
            try
            {
                // Datenbank, wie sie vor dem Controlling-Import aussah (nur Projekte und Buchungen)
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
                    var project = db.GetProjects().Single();
                    Assert.Equal("Alt", project.Projektname);
                    Assert.Equal("X.01", project.Kostentraeger);
                    Assert.Single(db.GetTimeEntriesForProject("Alt"));
                    Assert.Empty(db.GetControllingImports());

                    Drafts.Save(db, Drafts.Make(T1, 1m, 2m));
                    Assert.Single(db.GetControllingImports());
                }
            }
            finally
            {
                SQLiteConnection.ClearAllPools();
                try { Directory.Delete(Path.GetDirectoryName(path), true); } catch (IOException) { }
            }
        }

        [Fact]
        public void Money_is_stored_as_exact_decimal_text_and_unknown_stays_null()
        {
            using (var t = new TestDb())
            {
                var save = Drafts.Save(t.Db, Drafts.Make(T1, 10000.10m, 2260.3000000000002m));
                var unknown = Drafts.Make(T2, null, 0m, key: "P1.02");
                Drafts.Save(t.Db, unknown);
                t.Restart();

                var exact = t.Db.GetControllingBatch(save.ImportId).Single();
                Assert.Equal(10000.10m, exact.PlannedCost);
                Assert.Equal(2260.3000000000002m, exact.ActualCost);

                var batch = t.Db.GetControllingBatch(t.Db.GetControllingImports().First().Id).Single();
                Assert.Null(batch.PlannedCost);   // nicht geliefert, nicht 0
                Assert.Equal(0m, batch.ActualCost);
                Assert.Equal(1, t.Scalar("SELECT COUNT(*) FROM controlling_snapshots WHERE planned_cost IS NULL"));
                // Geld liegt als Text vor (nicht als REAL) und kann so nicht durch Gleitkommazahlen verfälscht werden
                Assert.Equal(2, t.Scalar("SELECT COUNT(*) FROM controlling_snapshots WHERE typeof(actual_cost) = 'text'"));
            }
        }

        // ---------- Abnahmefall 7: ein Stand = eine Beobachtung, Differenzen in Kosten, Reihenfolge ----------

        [Fact]
        public void Case7_one_import_yields_one_observation_without_invented_history()
        {
            using (var t = new TestDb())
            {
                Drafts.Save(t.Db, Drafts.Make(T3, 10000m, 12000m));

                Assert.Single(t.Db.GetControllingCarrierHistory(Key, "UNSPECIFIED"));
            }
        }

        [Fact]
        public void Case7_a_later_snapshot_gives_a_cost_delta_and_not_hours()
        {
            using (var t = new TestDb())
            {
                Drafts.Save(t.Db, Drafts.Make(T2, 10000m, 12000m, fileHash: "f2"));
                Drafts.Save(t.Db, Drafts.Make(T3, 10000m, 12500m, fileHash: "f3"));

                var history = t.Db.GetControllingCarrierHistory(Key, "UNSPECIFIED");

                Assert.Equal(2, history.Count);
                var delta = CostMath.Delta(history[0].ActualCost, history[1].ActualCost);
                Assert.Equal(500m, delta);
                Assert.Equal(0m, CostMath.Delta(history[0].PlannedCost, history[1].PlannedCost));
                Assert.Contains("+500,00", CostMath.FormatSignedMoney(delta));
            }
        }

        [Fact]
        public void Case7_import_order_does_not_change_the_chronological_latest()
        {
            using (var t = new TestDb())
            {
                // erst der neuere Stand, danach ein älterer
                Drafts.Save(t.Db, Drafts.Make(T3, 10000m, 12500m, fileHash: "f3"));
                Drafts.Save(t.Db, Drafts.Make(T2, 10000m, 12000m, fileHash: "f2"));

                var history = t.Db.GetControllingCarrierHistory(Key, "UNSPECIFIED");
                Assert.Equal(new[] { T2, T3 }, history.Select(h => h.SourceTimestamp).ToArray());

                var latest = t.Db.GetLatestControllingObservations(new[] { Key }, "UNSPECIFIED")[Key];
                Assert.Equal(T3, latest.SourceTimestamp);
                Assert.Equal(12500m, latest.ActualCost);

                // Der neueste Importstand (nach Quellstand) steht vorn in der Liste der Importe
                Assert.Equal(T3, t.Db.GetControllingImports().First().SourceTimestamp);
            }
        }

        [Fact]
        public void Case7_comparison_in_the_preview_uses_the_previous_comparable_snapshot()
        {
            using (var t = new TestDb())
            {
                Drafts.Save(t.Db, Drafts.Make(T2, 10000m, 12000m, fileHash: "f2"));

                var wb = new WorkbookBuilder();
                Fixtures.AddSnapshotTimestamp(wb, 46335.604166666664);
                var sheet = Fixtures.AddPrimarySheet(wb);
                Fixtures.PrimaryRow(sheet, 2, "P", "P1", "Projekt", "P1.01", "PSP", 10000, 12500);
                var preview = Fixtures.Preview(wb, new[] { Fixtures.Project(1, "P1.01") });

                var previous = t.Db.GetLatestControllingObservations(preview.Rows.Select(r => r.CarrierKey), "UNSPECIFIED", Fixtures.SapTimestamp);
                ControllingImportService.ApplyComparison(preview, previous, Fixtures.SapTimestamp);

                var row = preview.Rows.Single();
                Assert.Equal(500m, row.ActualDelta);
                Assert.Equal(0m, row.PlanDelta);
                Assert.False(row.IsCorrection);
                Assert.Contains("Seit Stand 31.10.2026", row.ComparisonText);
            }
        }

        [Fact]
        public void Different_currency_contexts_have_separate_histories()
        {
            using (var t = new TestDb())
            {
                Drafts.Save(t.Db, Drafts.Make(T1, 100m, 50m, fileHash: "a"));
                Drafts.Save(t.Db, Drafts.Make(T2, 100m, 60m, fileHash: "b", currency: "EUR"));
                Drafts.Save(t.Db, Drafts.Make(T3, 100m, 70m, fileHash: "c", currency: "chf"));

                Assert.Single(t.Db.GetControllingCarrierHistory(Key, "UNSPECIFIED"));
                Assert.Single(t.Db.GetControllingCarrierHistory(Key, "EUR"));
                Assert.Single(t.Db.GetControllingCarrierHistory(Key, "CHF"));
                Assert.Equal(60m, t.Db.GetControllingCarrierHistory(Key, "EUR").Single().ActualCost);
            }
        }

        // ---------- Abnahmefall 8: Idempotenz, erweiterte Kostenträger, Revisionen ----------

        [Fact]
        public void Case8_identical_reimport_writes_nothing()
        {
            using (var t = new TestDb())
            {
                var first = Drafts.Save(t.Db, Drafts.Make(T3, 100m, 50m));
                var again = Drafts.Save(t.Db, Drafts.Make(T3, 100m, 50m));

                Assert.Equal(ControllingSaveStatus.Saved, first.Status);
                Assert.Equal(ControllingSaveStatus.AlreadyImported, again.Status);
                Assert.Equal(first.ImportId, again.ImportId);
                Assert.Equal(1, t.Scalar("SELECT COUNT(*) FROM controlling_imports"));
                Assert.Equal(1, t.Scalar("SELECT COUNT(*) FROM controlling_snapshots"));
            }
        }

        [Fact]
        public void Case8_an_expanded_allowlist_imports_newly_eligible_keys_from_identical_bytes()
        {
            using (var t = new TestDb())
            {
                var bytes = Fixtures.StandardWorkbook().Build();
                var before = ControllingImportService.BuildPreview(bytes, "stand.xlsx", new[] { Fixtures.Project(1, Fixtures.Step1) });
                var after = ControllingImportService.BuildPreview(bytes, "stand.xlsx", new[] { Fixtures.Project(1, Fixtures.Step1), Fixtures.Project(2, Fixtures.Kd06) });

                Assert.Equal(before.FileHash, after.FileHash);
                Assert.NotEqual(before.AllowlistHash, after.AllowlistHash);

                var a = t.Db.SaveControllingImport(before.BuildDraft(Fixtures.SapTimestamp, TimestampOrigin.Workbook, null));
                var b = t.Db.SaveControllingImport(after.BuildDraft(Fixtures.SapTimestamp, TimestampOrigin.Workbook, null));

                Assert.Equal(ControllingSaveStatus.Saved, a.Status);
                Assert.Equal(ControllingSaveStatus.Saved, b.Status);
                Assert.Single(t.Db.GetControllingBatch(a.ImportId));
                Assert.Equal(2, t.Db.GetControllingBatch(b.ImportId).Count);

                // dieselbe Datei, dieselbe Auswahl, noch einmal: nichts Neues
                var c = t.Db.SaveControllingImport(after.BuildDraft(Fixtures.SapTimestamp, TimestampOrigin.Workbook, null));
                Assert.Equal(ControllingSaveStatus.AlreadyImported, c.Status);
            }
        }

        [Fact]
        public void Case8_a_correction_at_the_same_timestamp_is_a_revision_with_one_active_point()
        {
            using (var t = new TestDb())
            {
                var original = Drafts.Save(t.Db, Drafts.Make(T3, 100m, 50m, fileHash: "orig"));
                var correction = Drafts.Save(t.Db, Drafts.Make(T3, 100m, 55m, fileHash: "korr"));

                Assert.NotEqual(original.ImportId, correction.ImportId);

                var history = t.Db.GetControllingCarrierHistory(Key, "UNSPECIFIED");
                Assert.Single(history);                                  // nur ein aktiver Punkt
                Assert.Equal(55m, history[0].ActualCost);
                Assert.Equal(correction.ImportId, history[0].ImportId);
                Assert.Equal(2, history[0].RevisionCount);

                var revisions = t.Db.GetControllingCarrierRevisions(Key, T3, "UNSPECIFIED");
                Assert.Equal(new[] { 55m, 50m }, revisions.Select(r => r.ActualCost.Value).ToArray());

                // Die Charge des Originals bleibt unverändert einsehbar, ist aber überholt
                var originalBatch = t.Db.GetControllingBatch(original.ImportId).Single();
                Assert.Equal(50m, originalBatch.ActualCost);
                Assert.False(originalBatch.IsActive);
            }
        }

        [Fact]
        public void Case8_deleting_the_latest_correction_reveals_the_previous_revision()
        {
            using (var t = new TestDb())
            {
                var original = Drafts.Save(t.Db, Drafts.Make(T3, 100m, 50m, fileHash: "orig"));
                var correction = Drafts.Save(t.Db, Drafts.Make(T3, 100m, 55m, fileHash: "korr"));

                Assert.True(t.Db.DeleteControllingImport(correction.ImportId));

                var history = t.Db.GetControllingCarrierHistory(Key, "UNSPECIFIED");
                Assert.Single(history);
                Assert.Equal(50m, history[0].ActualCost);
                Assert.Equal(original.ImportId, history[0].ImportId);
                Assert.Equal(1, history[0].RevisionCount);
            }
        }

        [Fact]
        public void Deleting_an_import_removes_its_rows_and_orphaned_carriers_only()
        {
            using (var t = new TestDb())
            {
                var a = Drafts.Save(t.Db, Drafts.Make(T1, 1m, 1m, key: "P1.01", fileHash: "a"));
                var b = Drafts.Save(t.Db, Drafts.Make(T2, 1m, 1m, key: "P1.02", fileHash: "b"));
                Assert.Equal(2, t.Scalar("SELECT COUNT(*) FROM controlling_carriers"));

                Assert.True(t.Db.DeleteControllingImport(b.ImportId));

                Assert.Equal(1, t.Scalar("SELECT COUNT(*) FROM controlling_carriers"));
                Assert.Equal(1, t.Scalar("SELECT COUNT(*) FROM controlling_snapshots"));
                Assert.Single(t.Db.GetControllingBatch(a.ImportId));
                Assert.False(t.Db.DeleteControllingImport(9999));
            }
        }

        // ---------- Abnahmefall 11: Atomarität ----------

        [Fact]
        public void Case11_a_failing_save_leaves_no_partial_batch_and_does_not_touch_projects_or_entries()
        {
            using (var t = new TestDb())
            {
                t.Db.AddOrEnsureProject("Projekt", "Kunde", "P1.01");
                t.Db.AddTimeEntry(new DateTime(2026, 11, 2), "09:00", "10:30", "Projekt");
                Drafts.Save(t.Db, Drafts.Make(T1, 1m, 1m, fileHash: "ok"));
                var hoursBefore = t.Db.GetProjects().Single().AnzahlStunden;

                // zweite Zeile mit demselben PSP-Element verletzt UNIQUE(import_id, carrier_id) mitten in der Transaktion
                var broken = Drafts.Make(T2, 5m, 5m, fileHash: "broken");
                broken.Rows.Add(broken.Rows[0]);
                broken.Parents.Add(new ParentContext { ParentKey = "P1", ExternalParentId = "P1" });

                Assert.ThrowsAny<Exception>(() => t.Db.SaveControllingImport(broken));

                Assert.Equal(1, t.Scalar("SELECT COUNT(*) FROM controlling_imports"));
                Assert.Equal(1, t.Scalar("SELECT COUNT(*) FROM controlling_snapshots"));
                Assert.Equal(0, t.Scalar("SELECT COUNT(*) FROM controlling_project_context"));
                Assert.Equal(1, t.Scalar("SELECT COUNT(*) FROM controlling_carriers"));
                Assert.Equal(1, t.Scalar("SELECT COUNT(*) FROM projects"));
                Assert.Equal(1, t.Scalar("SELECT COUNT(*) FROM time_entries"));
                Assert.Equal(hoursBefore, t.Db.GetProjects().Single().AnzahlStunden);

                // die Verbindung bleibt benutzbar
                Assert.Equal(ControllingSaveStatus.Saved, Drafts.Save(t.Db, Drafts.Make(T2, 5m, 5m, fileHash: "again")).Status);
            }
        }

        [Fact]
        public void Case11_database_backup_copy_retains_import_history()
        {
            using (var t = new TestDb())
            {
                Drafts.Save(t.Db, Drafts.Make(T1, 100m, 50m));
                t.Db.Checkpoint();

                var copy = Path.Combine(Path.GetDirectoryName(t.Path), "backup.timetrack-backup");
                File.Copy(t.Path, copy);
                using (var restored = new TimeTracker(copy))
                {
                    Assert.Single(restored.GetControllingImports());
                    Assert.Equal(50m, restored.GetControllingCarrierHistory(Key, "UNSPECIFIED").Single().ActualCost);
                }
                SQLiteConnection.ClearAllPools();
            }
        }

        // ---------- Abnahmefall 12: lokale Projekte und gemeinsame Kostenträger ----------

        [Fact]
        public void Case12_deleting_or_editing_one_of_two_projects_sharing_a_carrier_keeps_the_history()
        {
            using (var t = new TestDb())
            {
                t.Db.AddOrEnsureProject("A", "K", Fixtures.Step1);
                t.Db.AddOrEnsureProject("B", "K", Fixtures.Step1);
                Drafts.Save(t.Db, Drafts.Make(T1, 100m, 50m, key: Fixtures.Step1));
                var carrier = CarrierKeys.Normalize(Fixtures.Step1);

                t.Db.DeleteProject("A");

                Assert.Single(t.Db.GetControllingCarrierHistory(carrier, "UNSPECIFIED"));
                var linked = CarrierLinking.LinkedProjects(t.Db.GetLocalProjectInfos(), carrier);
                Assert.Equal(new[] { "B" }, linked.Select(p => p.Name).ToArray());

                t.Db.UpdateProject("B", "B umbenannt", "K", "anderer Kostenträger");

                // Die alte Beobachtung wird nie auf den neuen Kostenträger umgelabelt
                var history = t.Db.GetControllingCarrierHistory(carrier, "UNSPECIFIED");
                Assert.Single(history);
                Assert.Equal(Fixtures.Step1, history[0].CarrierText);
                Assert.Empty(CarrierLinking.LinkedProjects(t.Db.GetLocalProjectInfos(), carrier));
            }
        }

        [Fact]
        public void Case12_a_carrier_that_is_no_longer_configured_disappears_from_current_views_but_not_from_history()
        {
            using (var t = new TestDb())
            {
                t.Db.AddOrEnsureProject("A", "K", Fixtures.Step1);
                var save = Drafts.Save(t.Db, Drafts.Make(T3, 100m, 50m, key: Fixtures.Step1));

                var configured = ControllingOverview.Current(t.Db.GetLocalProjectInfos(), t.Db.GetControllingBatch(save.ImportId));
                Assert.Single(configured);

                t.Db.UpdateProject("A", "A", "K", "etwas ganz anderes");

                var afterEdit = ControllingOverview.Current(t.Db.GetLocalProjectInfos(), t.Db.GetControllingBatch(save.ImportId));
                Assert.Empty(afterEdit);
                Assert.Single(t.Db.GetControllingBatch(save.ImportId));   // Historie bleibt erhalten
                Assert.Single(t.Db.GetControllingImports());
            }
        }

        // ---------- Übersicht und letzter bekannter Stand ----------

        [Fact]
        public void Missing_rows_are_not_zero_and_configured_carriers_absent_from_an_import_show_their_last_known_state()
        {
            using (var t = new TestDb())
            {
                t.Db.AddOrEnsureProject("A", "K", "P1.01");
                t.Db.AddOrEnsureProject("B", "K", "P1.02");
                Drafts.Save(t.Db, Drafts.Make(T2, 100m, 40m, key: "P1.01", fileHash: "x"));
                Drafts.Save(t.Db, Drafts.Make(T2, 200m, 90m, key: "P1.02", fileHash: "y"));
                var newer = Drafts.Save(t.Db, Drafts.Make(T3, 100m, 60m, key: "P1.01", fileHash: "z"));   // P1.02 fehlt im neueren Stand

                var projects = t.Db.GetLocalProjectInfos();
                var batch = t.Db.GetControllingBatch(newer.ImportId);
                var latest = t.Db.GetLatestControllingObservations(null, "UNSPECIFIED").Values;

                var current = ControllingOverview.Current(projects, batch);
                var lastKnown = ControllingOverview.LastKnown(projects, batch, latest);

                Assert.Equal(new[] { "P1.01" }, current.Select(r => r.CarrierText).ToArray());
                Assert.False(current[0].IsLastKnown);

                Assert.Equal(new[] { "P1.02" }, lastKnown.Select(r => r.CarrierText).ToArray());
                Assert.True(lastKnown[0].IsLastKnown);
                Assert.Equal(T2, lastKnown[0].SourceTimestamp);   // eigenes, älteres Quelldatum
                Assert.Equal(90m, lastKnown[0].ActualCost);       // nicht auf 0 gesetzt
            }
        }

        [Fact]
        public void Overview_rows_use_text_labels_for_status_and_missing_values()
        {
            Func<decimal?, decimal?, ControllingOverviewRow> row = (plan, actual) =>
            {
                var r = new ControllingOverviewRow
                {
                    Observation = new ControllingObservation { CarrierKey = "P", CarrierText = "P", PlannedCost = plan, ActualCost = actual, SourceTimestamp = T1 }
                };
                return r;
            };

            Assert.Equal("Budget überschritten", row(10000m, 12000m).StatusText);
            Assert.Equal("120,0 %", row(10000m, 12000m).ConsumptionText);
            Assert.Equal("-2.000,00", row(10000m, 12000m).RemainingText);
            Assert.Equal("Plan nicht geliefert", row(null, 10m).StatusText);
            Assert.Equal(CostMath.MissingText, row(null, 10m).PlannedText);
            Assert.Equal(CostMath.NoBudgetText, row(null, 10m).ConsumptionText);
            Assert.Equal(CostMath.NoBudgetText, row(0m, 10m).ConsumptionText);
            Assert.Equal("Im Budget", row(100m, 10m).StatusText);
            Assert.Equal(CostMath.MissingText, row(100m, null).ConsumptionText);
            Assert.Equal("110,00", row(100m, -10m).RemainingText);
        }

        // ---------- Projektkontext ----------

        [Fact]
        public void Parent_context_is_stored_per_import_and_only_for_accepted_parents()
        {
            using (var t = new TestDb())
            {
                var wb = Fixtures.StandardWorkbook();
                Fixtures.AddHelperSheet(wb, Fixtures.StandardHelperRows);
                Fixtures.AddParentSheet(wb);
                var preview = Fixtures.Preview(wb, new[] { Fixtures.Project(1, Fixtures.Step1) });
                var save = t.Db.SaveControllingImport(preview.BuildDraft(Fixtures.SapTimestamp, TimestampOrigin.Workbook, null));

                var parentKey = CarrierKeys.Normalize(Fixtures.Root);
                var context = t.Db.GetControllingParentContext(save.ImportId, parentKey);

                Assert.NotNull(context);
                Assert.Equal("Claudio Amato", context.Manager);
                Assert.Equal(Fixtures.ExcelDate(45292), context.PlannedStart);
                Assert.Equal(0m, context.OrderValueTotal);
                Assert.Null(context.Customer);
                Assert.Equal(1, t.Scalar("SELECT COUNT(*) FROM controlling_project_context"));   // das andere Projekt (Kd06) gehört nicht dazu
                Assert.Null(t.Db.GetControllingParentContext(save.ImportId, "UNBEKANNT"));
            }
        }

        [Fact]
        public void Import_info_keeps_only_the_file_name_the_source_time_and_utc_import_time()
        {
            using (var t = new TestDb())
            {
                var draft = Drafts.Make(T3, 1m, 1m, fileName: @"C:\vertraulich\Pfad\Datei.xlsx", currency: "eur");
                draft.Warnings.Add("Eine Warnung");
                draft.WarningCount = 1;
                var save = t.Db.SaveControllingImport(draft);

                var info = t.Db.GetControllingImport(save.ImportId);

                Assert.Equal("Datei.xlsx", info.FileName);
                Assert.Equal(T3, info.SourceTimestamp);
                Assert.Equal(DateTimeKind.Utc, info.ImportedUtc.Kind);
                Assert.Equal("EUR", info.CurrencyContext);
                Assert.Equal(1, info.WarningCount);
                Assert.Equal(new[] { "Eine Warnung" }, info.Warnings.ToArray());
                Assert.Equal(TimestampOrigin.Workbook, info.TimestampOrigin);
            }
        }

        [Fact]
        public void The_fingerprint_depends_on_every_part_of_the_import_identity()
        {
            var baseline = Drafts.Make(T3, 100m, 50m).Fingerprint();

            Assert.Equal(baseline, Drafts.Make(T3, 100m, 50m).Fingerprint());
            Assert.NotEqual(baseline, Drafts.Make(T2, 100m, 50m).Fingerprint());                          // Quellstand
            Assert.NotEqual(baseline, Drafts.Make(T3, 100m, 50m, currency: "EUR").Fingerprint());          // Währungskontext
            Assert.NotEqual(baseline, Drafts.Make(T3, 100m, 50m, allowlistHash: "andere").Fingerprint());  // Kostenträger-Auswahl
            Assert.NotEqual(baseline, Drafts.Make(T3, 100m, 50m, fileHash: "andere").Fingerprint());       // Datei
            Assert.NotEqual(baseline, Drafts.Make(T3, 100m, 51m).Fingerprint());                           // Nutzdaten
        }
    }
}
