// Target: .NET Framework + C# 7.3
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Xunit;
using Zeitmanagement.Helpers;

namespace Zeitmanagement.Tests
{
    public class ExcelReaderTests
    {
        // ---------- Abnahmefall 1 und 2: Zuordnung ----------

        [Fact]
        public void Case1_two_carriers_three_projects_three_ignored_one_missing()
        {
            var preview = Fixtures.Preview(Fixtures.StandardWorkbook(), Fixtures.StandardProjects());

            Assert.False(preview.HasBlockingErrors);
            Assert.Equal(new[] { Fixtures.Step1, Fixtures.Kd06 }, preview.Rows.Select(r => r.CarrierText).ToArray());
            Assert.Equal(2, preview.MatchedCount);
            Assert.Equal(3, preview.LinkedProjectCount);
            Assert.Equal(5, preview.ScannedCount);
            Assert.Equal(3, preview.IgnoredCount);
            Assert.Single(preview.MissingCarriers);
            Assert.Equal("9999999X99ZZ999.01", preview.MissingCarriers[0]);

            // Wurzel und Kind werden nie "per Präfix" importiert
            Assert.DoesNotContain(preview.Rows, r => r.CarrierText == Fixtures.Root);
            Assert.DoesNotContain(preview.Rows, r => r.CarrierText == Fixtures.Step1Org);
        }

        [Fact]
        public void Case1_shared_carrier_is_one_snapshot_linked_to_all_projects()
        {
            var preview = Fixtures.Preview(Fixtures.StandardWorkbook(), Fixtures.StandardProjects());
            var step1 = preview.Rows.Single(r => r.CarrierText == Fixtures.Step1);

            Assert.Equal(new[] { "Projekt A", "Projekt B" }, step1.LinkedProjects.Select(p => p.Name).ToArray());
            Assert.Null(step1.PlannedCost);
            Assert.Equal(83333.31m, step1.ActualCost);
        }

        [Fact]
        public void Case1_money_is_exact_decimal_even_for_float_noise()
        {
            var preview = Fixtures.Preview(Fixtures.StandardWorkbook(), Fixtures.StandardProjects());
            var kd06 = preview.Rows.Single(r => r.CarrierText == Fixtures.Kd06);

            Assert.Equal(2260.3000000000002m, kd06.ActualCost);
        }

        [Fact]
        public void Case2_carrier_matches_after_normalization_and_when_it_only_contains_the_psp_element()
        {
            var projects = new[]
            {
                new LocalProjectInfo { Id = 1, Name = "Variante", Carrier = "  1500410a24rd001.01\u00A0" },
                new LocalProjectInfo { Id = 2, Name = "Mit Text", Carrier = "Kostenstelle 1500630C26IC001.02.06 (Datenbank)", IsExternal = true }
            };

            var preview = Fixtures.Preview(Fixtures.StandardWorkbook(), projects);

            Assert.Equal(new[] { Fixtures.Step1, Fixtures.Kd06 }, preview.Rows.Select(r => r.CarrierText).ToArray());
        }

        [Theory]
        [InlineData("1500410A24RD00101")]          // fehlender Punkt
        [InlineData("1500410A24RD001. 01")]        // zusätzliches Leerzeichen
        [InlineData("1500410A24RD001.1")]          // geänderte führende Null
        [InlineData("1500410A24RD001")]            // nur das Elternprojekt (Wurzel), nicht das Kind .01
        public void Case2_near_misses_do_not_match_the_child_element(string localCarrier)
        {
            var preview = Fixtures.Preview(Fixtures.StandardWorkbook(), new[] { Fixtures.Project(1, localCarrier) });

            Assert.DoesNotContain(preview.Rows, r => r.CarrierText == Fixtures.Step1);
        }

        [Fact]
        public void Case2_a_parent_only_carrier_matches_only_the_root_row()
        {
            var preview = Fixtures.Preview(Fixtures.StandardWorkbook(), new[] { Fixtures.Project(1, Fixtures.Root) });

            Assert.Equal(new[] { Fixtures.Root }, preview.Rows.Select(r => r.CarrierText).ToArray());
        }

        [Fact]
        public void Case2_numeric_looking_ids_remain_text()
        {
            var wb = new WorkbookBuilder();
            Fixtures.AddSnapshotTimestamp(wb, Fixtures.SapSerial);
            var sheet = Fixtures.AddPrimarySheet(wb);
            Fixtures.PrimaryRow(sheet, 2, "P", "0012300", "Eltern", "0012345", "Mit führenden Nullen", 10, 5);

            var matching = Fixtures.Preview(wb, new[] { Fixtures.Project(1, "PSP 0012345") });
            var notMatching = Fixtures.Preview(wb, new[] { Fixtures.Project(1, "12345") });

            Assert.Equal("0012345", matching.Rows.Single().CarrierText);
            Assert.Empty(notMatching.Rows);
            Assert.True(notMatching.HasBlockingErrors); // nichts passt: Import ist gesperrt
        }

        [Fact]
        public void No_match_disables_import_and_explains_it()
        {
            var preview = Fixtures.Preview(Fixtures.StandardWorkbook(), new[] { Fixtures.Project(1, "ZZZ") });

            Assert.False(preview.IsImportable);
            Assert.Contains(preview.Diagnostics, d => d.IsError && d.Message.Contains("bestehenden Kostentr"));
            Assert.Equal(0, preview.MatchedCount);
        }

        [Fact]
        public void No_projects_with_carrier_disables_import()
        {
            var preview = Fixtures.Preview(Fixtures.StandardWorkbook(), new List<LocalProjectInfo>());

            Assert.False(preview.IsImportable);
            Assert.True(preview.HasBlockingErrors);
        }

        [Fact]
        public void Normalization_equal_but_differently_spelled_carriers_produce_a_warning()
        {
            var projects = new[] { Fixtures.Project(1, Fixtures.Step1), Fixtures.Project(2, Fixtures.Step1.ToLowerInvariant() + " ") };

            var preview = Fixtures.Preview(Fixtures.StandardWorkbook(), projects);

            Assert.Contains(preview.Diagnostics, d => d.IsWarning && d.Message.Contains("Normalisierung"));
            Assert.Equal(2, preview.LinkedProjectCount);
            Assert.Single(preview.Rows);
        }

        // ---------- Abnahmefall 3: Rechenregeln ----------

        [Fact]
        public void Case3_cost_math_for_overrun_zero_plan_unknown_plan_and_credit()
        {
            Assert.Equal(-2000m, CostMath.Remaining(10000m, 12000m));
            Assert.Equal(1.2m, CostMath.Consumption(10000m, 12000m));
            Assert.True(CostMath.IsOverrun(10000m, 12000m));

            Assert.Null(CostMath.Consumption(0m, 10m));
            Assert.Equal(-10m, CostMath.Remaining(0m, 10m));

            Assert.Null(CostMath.Remaining(null, 10m));
            Assert.Null(CostMath.Consumption(null, 10m));

            Assert.Equal(110m, CostMath.Remaining(100m, -10m));
            Assert.False(CostMath.IsOverrun(100m, -10m));
        }

        [Fact]
        public void Case3_missing_and_zero_stay_distinct_and_negative_actual_is_kept_and_flagged()
        {
            var wb = new WorkbookBuilder();
            Fixtures.AddSnapshotTimestamp(wb, Fixtures.SapSerial);
            var sheet = Fixtures.AddPrimarySheet(wb);
            Fixtures.PrimaryRow(sheet, 2, "P", "P1", "Projekt", "P1.01", "Plan leer", null, 10);
            Fixtures.PrimaryRow(sheet, 3, "P", "P1", "Projekt", "P1.02", "Plan null", 0, 10);
            Fixtures.PrimaryRow(sheet, 4, "P", "P1", "Projekt", "P1.03", "Gutschrift", 100, -10);
            Fixtures.PrimaryRow(sheet, 5, "P", "P1", "Projekt", "P1.04", "Überschritten", 10000, 12000);

            var preview = Fixtures.Preview(wb, new[] { Fixtures.Project(1, "P1.01 P1.02 P1.03 P1.04") });

            Assert.False(preview.HasBlockingErrors);
            var unknownPlan = preview.Rows.Single(r => r.CarrierText == "P1.01");
            Assert.Null(unknownPlan.PlannedCost);
            Assert.Null(unknownPlan.RemainingCost);
            Assert.Equal(CostMath.MissingText, unknownPlan.PlannedText);

            var zeroPlan = preview.Rows.Single(r => r.CarrierText == "P1.02");
            Assert.Equal(0m, zeroPlan.PlannedCost);
            Assert.Null(zeroPlan.Consumption);

            var credit = preview.Rows.Single(r => r.CarrierText == "P1.03");
            Assert.Equal(110m, credit.RemainingCost);
            Assert.True(credit.HasNegativeAmount);
            Assert.Contains(preview.Diagnostics, d => d.IsWarning && d.Message.Contains("Negativer Istbetrag") && d.Location == "SAP#2PL!G4");

            var overrun = preview.Rows.Single(r => r.CarrierText == "P1.04");
            Assert.Equal(-2000m, overrun.RemainingCost);
            Assert.Equal(1.2m, overrun.Consumption);
        }

        // ---------- Abnahmefall 4: Layout und Duplikate ----------

        [Fact]
        public void Case4_reordered_headers_fail()
        {
            var wb = new WorkbookBuilder();
            var sheet = wb.Sheet("SAP#2PL", true);
            var headers = Fixtures.PrimaryHeaders.ToArray();
            var tmp = headers[5]; headers[5] = headers[6]; headers[6] = tmp; // Plan und Ist vertauscht
            sheet.Row(1, headers.Cast<object>().ToArray());
            Fixtures.PrimaryRow(sheet, 2, "P", "P1", "Projekt", "P1.01", "Name", 1, 2);

            var preview = Fixtures.Preview(wb, new[] { Fixtures.Project(1, "P1.01") });

            Assert.True(preview.HasBlockingErrors);
            Assert.Contains(preview.Diagnostics, d => d.IsError && d.Message.Contains("F:") && d.Message.Contains("Planbetrag"));
            Assert.Empty(preview.Rows);
        }

        [Fact]
        public void Case4_missing_required_header_fails()
        {
            var wb = new WorkbookBuilder();
            var sheet = wb.Sheet("SAP#2PL", true);
            sheet.Row(1, "Profitcenter", "Externe Projekt-ID", "Externe Projekt-ID", "Externe PSP-Element-ID", "Externe PSP-Element-ID", "Planbetrag in Buchungskreisw\u00e4hrung");
            Fixtures.PrimaryRow(sheet, 2, "P", "P1", "Projekt", "P1.01", "Name", 1, 2);

            var preview = Fixtures.Preview(wb, new[] { Fixtures.Project(1, "P1.01") });

            Assert.True(preview.HasBlockingErrors);
            Assert.Contains(preview.Diagnostics, d => d.IsError && d.Message.Contains("G:"));
        }

        [Fact]
        public void Case4_identical_duplicate_keys_collapse_with_a_warning()
        {
            var wb = new WorkbookBuilder();
            Fixtures.AddSnapshotTimestamp(wb, Fixtures.SapSerial);
            var sheet = Fixtures.AddPrimarySheet(wb);
            Fixtures.PrimaryRow(sheet, 2, "P", "P1", "Projekt", "P1.01", "Name", 100, 50);
            Fixtures.PrimaryRow(sheet, 3, "P", "P1", "Projekt", "P1.01", "Name", 100, 50);

            var preview = Fixtures.Preview(wb, new[] { Fixtures.Project(1, "P1.01") });

            Assert.False(preview.HasBlockingErrors);
            Assert.Single(preview.Rows);
            Assert.Equal(50m, preview.Rows[0].ActualCost);
            Assert.Contains(preview.Diagnostics, d => d.IsWarning && d.Message.Contains("identischen Werten"));
        }

        [Fact]
        public void Case4_conflicting_duplicates_for_a_matched_key_block_the_import()
        {
            var wb = new WorkbookBuilder();
            Fixtures.AddSnapshotTimestamp(wb, Fixtures.SapSerial);
            var sheet = Fixtures.AddPrimarySheet(wb);
            Fixtures.PrimaryRow(sheet, 2, "P", "P1", "Projekt", "P1.01", "Name", 100, 50);
            Fixtures.PrimaryRow(sheet, 3, "P", "P1", "Projekt", "P1.01", "Name", 100, 60);

            var preview = Fixtures.Preview(wb, new[] { Fixtures.Project(1, "P1.01") });

            Assert.True(preview.HasBlockingErrors);
            Assert.False(preview.IsImportable);
            Assert.Contains(preview.Diagnostics, d => d.IsError && d.Message.Contains("Widerspr") && d.Message.Contains("Zeilen 2, 3"));
        }

        [Fact]
        public void Case4_conflicting_duplicates_of_an_unmatched_key_are_harmless()
        {
            var wb = new WorkbookBuilder();
            Fixtures.AddSnapshotTimestamp(wb, Fixtures.SapSerial);
            var sheet = Fixtures.AddPrimarySheet(wb);
            Fixtures.PrimaryRow(sheet, 2, "P", "P1", "Projekt", "P1.01", "Name", 100, 50);
            Fixtures.PrimaryRow(sheet, 3, "P", "P1", "Projekt", "FREMD.01", "Name", 100, 50);
            Fixtures.PrimaryRow(sheet, 4, "P", "P1", "Projekt", "FREMD.01", "Name", 100, 60);

            var preview = Fixtures.Preview(wb, new[] { Fixtures.Project(1, "P1.01") });

            Assert.False(preview.HasBlockingErrors);
            Assert.Single(preview.Rows);
            Assert.Equal(1, preview.IgnoredCount);
        }

        [Fact]
        public void Case4_malformed_numeric_cell_on_an_unmatched_row_does_not_block_valid_rows()
        {
            var wb = new WorkbookBuilder();
            Fixtures.AddSnapshotTimestamp(wb, Fixtures.SapSerial);
            var sheet = Fixtures.AddPrimarySheet(wb);
            Fixtures.PrimaryRow(sheet, 2, "P", "P1", "Projekt", "P1.01", "Passend", 100, 50);
            sheet.Row(3, "P", "P1", "Projekt", "FREMD.01", "Fremd");
            sheet.Set("F3", "kaputt");
            sheet.Set("G3", "#DIV/0!", Cv.Error);

            var preview = Fixtures.Preview(wb, new[] { Fixtures.Project(1, "P1.01") });

            Assert.False(preview.HasBlockingErrors);
            Assert.Single(preview.Rows);
            Assert.Equal(1, preview.IgnoredCount);
            Assert.DoesNotContain(preview.Diagnostics, d => d.Location != null && d.Location.EndsWith("F3"));
        }

        [Fact]
        public void Case4_non_numeric_cost_in_a_matched_row_blocks_with_cell_reference()
        {
            var wb = new WorkbookBuilder();
            Fixtures.AddSnapshotTimestamp(wb, Fixtures.SapSerial);
            var sheet = Fixtures.AddPrimarySheet(wb);
            sheet.Row(2, "P", "P1", "Projekt", "P1.01", "Passend");
            sheet.Set("F2", "12.5 EUR");
            sheet.Set("G2", "#REF!", Cv.Error);

            var preview = Fixtures.Preview(wb, new[] { Fixtures.Project(1, "P1.01") });

            Assert.True(preview.HasBlockingErrors);
            Assert.Contains(preview.Diagnostics, d => d.IsError && d.Location == "SAP#2PL!F2");
            Assert.Contains(preview.Diagnostics, d => d.IsError && d.Location == "SAP#2PL!G2");
        }

        [Fact]
        public void Totals_placeholders_and_formatted_tail_rows_are_not_records()
        {
            var wb = new WorkbookBuilder();
            Fixtures.AddSnapshotTimestamp(wb, Fixtures.SapSerial);
            var sheet = Fixtures.AddPrimarySheet(wb);
            Fixtures.PrimaryRow(sheet, 2, "P", "P1", "Projekt", "P1.01", "Name", 100, 50);
            foreach (var placeholder in new[] { "Gesamtsumme", "Gesamtergebnis", "-", "(Leer)" })
                sheet.Set("D" + (3 + Array.IndexOf(new[] { "Gesamtsumme", "Gesamtergebnis", "-", "(Leer)" }, placeholder)), placeholder);
            sheet.Set("D7", 0.0, Cv.Number);                 // Platzhalter "0"
            sheet.Set("A8", null, Cv.Blank);                 // nur formatierte Zeilen am Ende
            sheet.Set("D9", null, Cv.Blank);

            var preview = Fixtures.Preview(wb, new[] { Fixtures.Project(1, "P1.01") });

            Assert.False(preview.HasBlockingErrors);
            Assert.Equal(1, preview.ScannedCount);
            Assert.Single(preview.Rows);
        }

        [Fact]
        public void A_numeric_stored_psp_id_that_matches_a_carrier_is_surfaced_as_an_error()
        {
            var wb = new WorkbookBuilder();
            Fixtures.AddSnapshotTimestamp(wb, Fixtures.SapSerial);
            var sheet = Fixtures.AddPrimarySheet(wb);
            Fixtures.PrimaryRow(sheet, 2, "P", "P1", "Projekt", "P1.01", "Passend", 1, 1);
            sheet.Set("D3", 12345.0, Cv.Number);

            var matching = Fixtures.Preview(wb, new[] { Fixtures.Project(1, "P1.01"), Fixtures.Project(2, "PSP 12345") });
            var unrelated = Fixtures.Preview(wb, new[] { Fixtures.Project(1, "P1.01") });

            Assert.True(matching.HasBlockingErrors);
            Assert.Contains(matching.Diagnostics, d => d.IsError && d.Location == "SAP#2PL!D3");
            Assert.False(unrelated.HasBlockingErrors);
            Assert.Contains(unrelated.Diagnostics, d => d.IsWarning && d.Location == "SAP#2PL!D3");
        }

        // ---------- Abnahmefall 5: Zellformate und Blätter ----------

        [Fact]
        public void Case5_shared_inline_and_rich_text_strings_are_read_without_phonetics()
        {
            var wb = new WorkbookBuilder();
            Fixtures.AddSnapshotTimestamp(wb, Fixtures.SapSerial);
            var sheet = Fixtures.AddPrimarySheet(wb);
            sheet.Set("A2", "SAE410", Cv.Inline);
            sheet.Set("B2", "P1", Cv.Shared);
            sheet.Set("C2", "Projekt Rich Text", Cv.Rich);
            sheet.Set("D2", "P1.01", Cv.Rich);
            sheet.Set("E2", "Name inline", Cv.Inline);
            sheet.Set("F2", 100.5, Cv.Number);
            sheet.Set("G2", 20.25, Cv.FormulaNumber);

            var preview = Fixtures.Preview(wb, new[] { Fixtures.Project(1, "P1.01") });

            var row = preview.Rows.Single();
            Assert.Equal("P1.01", row.CarrierText);
            Assert.Equal("SAE410", row.ProfitCenter);
            Assert.Equal("Projekt Rich Text", row.ExternalProjectName);
            Assert.Equal("Name inline", row.PspName);
            Assert.Equal(100.5m, row.PlannedCost);
            Assert.Equal(20.25m, row.ActualCost);
        }

        [Fact]
        public void Case5_sparse_cells_keep_their_coordinates()
        {
            var wb = new WorkbookBuilder();
            Fixtures.AddSnapshotTimestamp(wb, Fixtures.SapSerial);
            var sheet = Fixtures.AddPrimarySheet(wb);
            // Spalten A, B, C und F fehlen: die dritte vorhandene Zelle ist nicht Spalte C
            sheet.Set("D2", "P1.01");
            sheet.Set("E2", "Nur Name");
            sheet.Set("G2", 7.5, Cv.Number);

            var preview = Fixtures.Preview(wb, new[] { Fixtures.Project(1, "P1.01") });

            var row = preview.Rows.Single();
            Assert.Null(row.ProfitCenter);
            Assert.Null(row.ExternalParentId);
            Assert.Equal("Nur Name", row.PspName);
            Assert.Null(row.PlannedCost);
            Assert.Equal(7.5m, row.ActualCost);
        }

        [Fact]
        public void Case5_hidden_sheets_and_alternative_order_and_relationship_ids_work()
        {
            var wb = Fixtures.StandardWorkbook();
            wb.ReverseSheetOrder = true;
            wb.Sheets.First(s => s.Name == "SAP#2PL").RelationshipId = "rId77";
            wb.Sheets.First(s => s.Name == "Projekte").RelationshipId = "rId3";

            var preview = Fixtures.Preview(wb, Fixtures.StandardProjects());

            Assert.Equal(2, preview.MatchedCount);
            Assert.Equal(Fixtures.SapTimestamp, preview.WorkbookTimestamp);
        }

        [Fact]
        public void Case5_missing_optional_helper_does_not_prevent_raw_import()
        {
            var preview = Fixtures.Preview(Fixtures.StandardWorkbook(), Fixtures.StandardProjects());

            Assert.False(preview.HasBlockingErrors);
            Assert.Equal(2, preview.MatchedCount);
            Assert.Contains(preview.Diagnostics, d => d.IsWarning && d.Message.Contains("_PrjListe"));
        }

        [Fact]
        public void Case5_missing_raw_sheet_blocks_and_never_falls_back_to_the_pivot()
        {
            var wb = new WorkbookBuilder();
            Fixtures.AddSnapshotTimestamp(wb, Fixtures.SapSerial);
            Fixtures.AddHelperSheet(wb, Fixtures.StandardHelperRows);

            var preview = Fixtures.Preview(wb, new[] { Fixtures.Project(1, Fixtures.Step1) });

            Assert.True(preview.HasBlockingErrors);
            Assert.Contains(preview.Diagnostics, d => d.IsError && d.Message.Contains("SAP#2PL"));
            Assert.Empty(preview.Rows);
        }

        [Fact]
        public void Case5_helper_enrichment_uses_cached_values_and_trims_labels_but_raw_blank_plan_stays_unknown()
        {
            var wb = Fixtures.StandardWorkbook();
            Fixtures.AddHelperSheet(wb, Fixtures.StandardHelperRows);
            Fixtures.AddParentSheet(wb);

            var preview = Fixtures.Preview(wb, new[] { Fixtures.Project(1, Fixtures.Step1) });

            Assert.False(preview.HasBlockingErrors);
            var row = preview.Rows.Single();
            Assert.Null(row.PlannedCost); // roh leer, auch wenn die Hilfsliste Plan-Kosten 0 zeigt
            Assert.Equal(83333.31m, row.ActualCost);

            var parent = preview.Read.Parents[CarrierKeys.Normalize(Fixtures.Root)];
            Assert.Equal("Claudio Amato", parent.Manager);   // volle Namen aus SAP#1PL gehen vor dem Kürzel der Hilfsliste
            Assert.Equal("Freigegeben", parent.Status);
            Assert.Null(parent.Customer);                     // "-" = nicht verfügbar
            Assert.Equal(Fixtures.ExcelDate(45292), parent.PlannedStart);
            Assert.Equal(Fixtures.ExcelDate(46387), parent.PlannedEnd);
            Assert.Equal(0m, parent.OrderValueTotal);         // bekannte Null ist etwas anderes als "nicht verfügbar"
            Assert.Equal("62D1001I_DTS10 Produktentwicklung", parent.ParentName);
        }

        [Fact]
        public void Case5_parent_amounts_are_never_summed_across_psp_rows_and_conflicts_make_the_field_unavailable()
        {
            var wb = Fixtures.StandardWorkbook();
            Fixtures.AddHelperSheet(wb, sheet =>
            {
                Fixtures.StandardHelperRows(sheet);
                sheet.Set("D4", 1000.0, Cv.Number);
                sheet.Set("D5", 1000.0, Cv.Number);   // gleicher Wert in beiden Zeilen: gilt einmal, nicht 2000
                sheet.Set("E4", 10.0, Cv.Number);
                sheet.Set("E5", 20.0, Cv.Number);     // widersprüchlich
            });

            var preview = Fixtures.Preview(wb, new[] { Fixtures.Project(1, Fixtures.Root + " " + Fixtures.Step1) });

            Assert.Equal(2, preview.MatchedCount);
            var parent = preview.Read.Parents[CarrierKeys.Normalize(Fixtures.Root)];
            Assert.Equal(1000m, parent.OrderValueTotal);
            Assert.Null(parent.InvoicedTotal);
            Assert.Contains(preview.Diagnostics, d => d.IsWarning && d.Message.Contains("Abgerechnet Gesamt"));
        }

        [Fact]
        public void Case5_error_caches_in_optional_enrichment_warn_but_do_not_fail_raw_costs()
        {
            var wb = Fixtures.StandardWorkbook();
            Fixtures.AddHelperSheet(wb, sheet =>
            {
                Fixtures.StandardHelperRows(sheet);
                sheet.Set("D5", "#N/A", Cv.Error);
                sheet.Set("D4", "#N/A", Cv.Error);
            });

            var preview = Fixtures.Preview(wb, new[] { Fixtures.Project(1, Fixtures.Step1) });

            Assert.False(preview.HasBlockingErrors);
            Assert.Equal(83333.31m, preview.Rows.Single().ActualCost);
            Assert.Null(preview.Read.Parents[CarrierKeys.Normalize(Fixtures.Root)].OrderValueTotal);
            Assert.Contains(preview.Diagnostics, d => d.IsWarning && d.Location != null && d.Location.StartsWith("_PrjListe!D"));
        }

        [Fact]
        public void Case5_unexpected_helper_layout_warns_and_skips_only_the_context()
        {
            var wb = Fixtures.StandardWorkbook();
            var helper = wb.Sheet("_PrjListe", true);
            helper.Row(3, "Etwas", "ganz", "anderes");

            var preview = Fixtures.Preview(wb, Fixtures.StandardProjects());

            Assert.False(preview.HasBlockingErrors);
            Assert.Equal(2, preview.MatchedCount);
            Assert.Contains(preview.Diagnostics, d => d.IsWarning && d.Message.Contains("unerwartetes Layout"));
        }

        [Fact]
        public void Case5_parent_id_is_taken_from_the_last_parentheses_group_only()
        {
            string description, id;
            Assert.True(ProjectControllingExcelReader.TrySplitTrailingId("62D1001I_DTS10 Produktentwicklung (1500410A24RD001)", out description, out id));
            Assert.Equal("1500410A24RD001", id);
            Assert.Equal("62D1001I_DTS10 Produktentwicklung", description);

            Assert.True(ProjectControllingExcelReader.TrySplitTrailingId("Entwicklung (Phase 1) (1500410A24RD001)  ", out description, out id));
            Assert.Equal("1500410A24RD001", id);
            Assert.Equal("Entwicklung (Phase 1)", description);

            Assert.False(ProjectControllingExcelReader.TrySplitTrailingId("Ohne Klammer am Ende (x) y", out description, out id));
            Assert.False(ProjectControllingExcelReader.TrySplitTrailingId("Leer ()", out description, out id));
        }

        [Fact]
        public void Case5_formulas_are_not_evaluated_the_cached_value_is_used()
        {
            var wb = new WorkbookBuilder();
            Fixtures.AddSnapshotTimestamp(wb, Fixtures.SapSerial);
            var sheet = Fixtures.AddPrimarySheet(wb);
            sheet.Row(2, "P", "P1", "Projekt", "P1.01", "Name");
            sheet.Set("F2", 123.45, Cv.FormulaNumber); // Formel Q4-R4: würde bei Auswertung etwas anderes ergeben

            var preview = Fixtures.Preview(wb, new[] { Fixtures.Project(1, "P1.01") });

            Assert.Equal(123.45m, preview.Rows.Single().PlannedCost);
        }

        // ---------- Abnahmefall 6: Zeitstempel ----------

        [Fact]
        public void Case6_serial_converts_to_the_sap_timestamp()
        {
            var preview = Fixtures.Preview(Fixtures.StandardWorkbook(), Fixtures.StandardProjects());

            Assert.Equal(new DateTime(2026, 11, 9, 14, 30, 0), preview.WorkbookTimestamp);
            Assert.False(preview.HasTimestampConflict);
            Assert.False(preview.HasNoTimestamp);
        }

        [Fact]
        public void Case6_1904_date_system_gives_the_same_moment()
        {
            var wb = Fixtures.StandardWorkbook(withTimestamp: false);
            wb.Date1904 = true;
            Fixtures.AddSnapshotTimestamp(wb, Fixtures.SapSerial - 1462);

            var preview = Fixtures.Preview(wb, Fixtures.StandardProjects());

            Assert.Equal(new DateTime(2026, 11, 9, 14, 30, 0), preview.WorkbookTimestamp);
        }

        [Fact]
        public void Case6_conflicting_timestamps_need_an_explicit_choice()
        {
            var wb = Fixtures.StandardWorkbook();
            Fixtures.AddSnapshotTimestamp(wb, Fixtures.SapSerial + 1, "Auftr\u00e4ge");

            var preview = Fixtures.Preview(wb, Fixtures.StandardProjects());

            Assert.True(preview.HasTimestampConflict);
            Assert.Equal(2, preview.Read.Timestamps.Count);
            Assert.Equal("Projekte", preview.Read.PrimaryTimestamp.Sheet); // erster Kandidat
            Assert.Equal(new DateTime(2026, 11, 10, 14, 30, 0), preview.Read.Timestamps[1].Value);
        }

        [Fact]
        public void Case6_the_second_sheet_is_used_only_when_the_first_has_no_timestamp()
        {
            var wb = Fixtures.StandardWorkbook(withTimestamp: false);
            Fixtures.AddSnapshotTimestamp(wb, Fixtures.SapSerial, "Auftr\u00e4ge");

            var preview = Fixtures.Preview(wb, Fixtures.StandardProjects());

            Assert.False(preview.HasTimestampConflict);
            Assert.Equal("Auftr\u00e4ge", preview.Read.PrimaryTimestamp.Sheet);
        }

        [Fact]
        public void Case6_missing_timestamp_is_reported_and_is_never_replaced_by_file_or_import_time()
        {
            var preview = Fixtures.Preview(Fixtures.StandardWorkbook(withTimestamp: false), Fixtures.StandardProjects());

            Assert.True(preview.HasNoTimestamp);
            Assert.Null(preview.WorkbookTimestamp);
            Assert.Contains(preview.Diagnostics, d => d.IsWarning && d.Message.Contains("SAP-Stand"));
        }

        [Fact]
        public void Case6_the_unrelated_helper_timestamp_is_ignored()
        {
            var wb = Fixtures.StandardWorkbook(withTimestamp: false);
            Fixtures.AddHelperSheet(wb, sheet => sheet.Set("AA1", 46142.826388888891, Cv.Number));

            var preview = Fixtures.Preview(wb, Fixtures.StandardProjects());

            Assert.True(preview.HasNoTimestamp);
        }

        // ---------- Sicherheit und Grenzen ----------

        [Fact]
        public void Doctype_in_sheet_xml_is_rejected()
        {
            var bytes = Fixtures.StandardWorkbook().Build();
            var tampered = WorkbookBuilder.ReplacePart(bytes, name => name.StartsWith("xl/worksheets/sheet2", StringComparison.Ordinal) || name.EndsWith("sheet2.xml", StringComparison.Ordinal),
                xml => "<!DOCTYPE x [<!ENTITY a \"b\">]>" + xml.Substring(xml.IndexOf("?>", StringComparison.Ordinal) + 2));

            var preview = Fixtures.Preview(tampered, Fixtures.StandardProjects());

            Assert.True(preview.HasBlockingErrors);
            Assert.Empty(preview.Rows);
        }

        [Fact]
        public void Not_a_workbook_is_rejected_with_a_clear_message()
        {
            var preview = Fixtures.Preview(Encoding.UTF8.GetBytes("das ist keine Excel-Datei"), Fixtures.StandardProjects());

            Assert.True(preview.HasBlockingErrors);
            Assert.Contains(preview.Diagnostics, d => d.IsError && d.Message.Contains("keine g\u00fcltige Excel-Arbeitsmappe"));
        }

        [Fact]
        public void Encrypted_or_legacy_compound_files_are_rejected()
        {
            var ole = new byte[] { 0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1, 0, 0, 0, 0 };

            var preview = Fixtures.Preview(ole, Fixtures.StandardProjects());

            Assert.True(preview.HasBlockingErrors);
            Assert.Contains(preview.Diagnostics, d => d.IsError && d.Message.Contains("verschl"));
        }

        [Fact]
        public void Too_many_rows_are_rejected()
        {
            var bytes = Fixtures.StandardWorkbook().Build();

            var preview = ControllingImportService.BuildPreview(bytes, "x.xlsx", Fixtures.StandardProjects(), new ReaderLimits { MaxRowsPerSheet = 3 });

            Assert.True(preview.HasBlockingErrors);
            Assert.Contains(preview.Diagnostics, d => d.IsError && d.Message.Contains("Zeilen"));
        }

        [Fact]
        public void Oversized_unpacked_content_is_rejected()
        {
            var bytes = Fixtures.StandardWorkbook().Build();

            var preview = ControllingImportService.BuildPreview(bytes, "x.xlsx", Fixtures.StandardProjects(), new ReaderLimits { MaxUncompressedBytes = 500 });

            Assert.True(preview.HasBlockingErrors);
            Assert.Contains(preview.Diagnostics, d => d.IsError && d.Message.Contains("zu gro"));
        }

        [Fact]
        public void Oversized_file_is_rejected_when_loading()
        {
            var path = Path.Combine(Path.GetTempPath(), "TimeTrackTests-" + Guid.NewGuid().ToString("N") + ".xlsx");
            try
            {
                File.WriteAllBytes(path, new byte[2048]);

                var preview = ControllingImportService.BuildPreviewFromFile(path, Fixtures.StandardProjects(), new ReaderLimits { MaxCompressedBytes = 1024 });

                Assert.True(preview.HasBlockingErrors);
                Assert.Contains(preview.Diagnostics, d => d.Message.Contains("zu gro"));
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Fact]
        public void Only_xlsx_extension_is_accepted_and_missing_files_give_clear_errors()
        {
            var macro = ControllingImportService.BuildPreviewFromFile(@"C:\nicht\da.xlsm", Fixtures.StandardProjects());
            var missing = ControllingImportService.BuildPreviewFromFile(Path.Combine(Path.GetTempPath(), "gibt-es-nicht-" + Guid.NewGuid().ToString("N") + ".xlsx"), Fixtures.StandardProjects());

            Assert.Contains(macro.Diagnostics, d => d.IsError && d.Message.Contains(".xlsx"));
            Assert.Contains(missing.Diagnostics, d => d.IsError && d.Message.Contains("nicht gefunden"));
        }

        [Fact]
        public void A_file_in_use_can_still_be_read_and_the_original_is_left_untouched()
        {
            var path = Path.Combine(Path.GetTempPath(), "TimeTrackTests-" + Guid.NewGuid().ToString("N") + ".xlsx");
            try
            {
                var bytes = Fixtures.StandardWorkbook().Build();
                File.WriteAllBytes(path, bytes);

                using (new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite))
                {
                    var preview = ControllingImportService.BuildPreviewFromFile(path, Fixtures.StandardProjects());

                    Assert.False(preview.HasBlockingErrors);
                    Assert.Equal(Path.GetFileName(path), preview.FileName);
                }

                Assert.Equal(bytes, File.ReadAllBytes(path));
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Fact]
        public void The_file_hash_is_the_sha256_of_the_parsed_bytes_and_the_name_is_a_basename()
        {
            var bytes = Fixtures.StandardWorkbook().Build();

            var preview = ControllingImportService.BuildPreview(bytes, @"C:\geheim\ordner\Datei.xlsx", Fixtures.StandardProjects());

            string expected;
            using (var sha = System.Security.Cryptography.SHA256.Create())
                expected = string.Concat(sha.ComputeHash(bytes).Select(b => b.ToString("x2")));
            Assert.Equal(expected, preview.FileHash);
            Assert.Equal("Datei.xlsx", preview.FileName);
        }

        [Fact]
        public void Unmatched_rows_expose_nothing_but_their_count()
        {
            var preview = Fixtures.Preview(Fixtures.StandardWorkbook(), Fixtures.StandardProjects());
            var draft = preview.BuildDraft(Fixtures.SapTimestamp, TimestampOrigin.Workbook, null);

            Assert.Equal(3, draft.IgnoredCount);
            Assert.DoesNotContain(draft.Rows, r => r.CarrierText == Fixtures.Root || r.CarrierText == Fixtures.Kd05 || r.CarrierText == Fixtures.Step1Org);
            Assert.DoesNotContain(draft.Warnings, w => w.Contains(Fixtures.Kd05) || w.Contains("KD_05_POCS"));
        }
    }
}
