// Target: .NET Framework + C# 7.3
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Zeitmanagement.Helpers;

namespace Zeitmanagement.Tests
{
    /// <summary>Synthetische Arbeitsmappen nach den Layouts und Beispielwerten aus der Spezifikation.</summary>
    internal static class Fixtures
    {
        public const string Root = "1500410A24RD001";
        public const string Step1 = "1500410A24RD001.01";
        public const string Step1Org = "1500410A24RD001.01.01";
        public const string Kd05 = "1500630C26IC001.02.05";
        public const string Kd06 = "1500630C26IC001.02.06";

        /// <summary>Excel-Datumsseriennummer von "2026-11-09 14:30:00" im 1900-Datumssystem.</summary>
        public const double SapSerial = 46335.604166666664;

        public static readonly DateTime SapTimestamp = new DateTime(2026, 11, 9, 14, 30, 0);

        public static readonly string[] PrimaryHeaders =
        {
            "Profitcenter", "Externe Projekt-ID", "Externe Projekt-ID", "Externe PSP-Element-ID", "Externe PSP-Element-ID",
            "Planbetrag in Buchungskreiswährung", "Istbetrag in Buchungskreiswährung"
        };

        public static readonly string[] HelperHeaders =
        {
            "PSP-Element", "Profitcenter", "Kunde",
            "Auftragswert\nGesamt", "Abgerechnet  Gesamt", "Offen Gesamt", "Abg % Gesamt", "Prj FG\nGesamt", "WIP (offen) Gesamt",
            "Auftragswert lfd. Jahr", "Offen lfd. Jahr",
            "Projekt-ID", "PL", "Prj-Status", "Projekt", "PSP-Name",
            "Plan-Kosten", "Ist-Kosten", "Kosten offen", "FG"
        };

        public static readonly string[] ParentHeaders =
        {
            "Verantwortliche Kostenstelle", "Projekt", "Projektmanager", "Ausführungsstatus", "Geplanter Start", "Geplantes Ende"
        };

        public static SheetSpec AddPrimarySheet(WorkbookBuilder wb, bool hidden = true)
        {
            var sheet = wb.Sheet("SAP#2PL", hidden);
            sheet.Row(1, PrimaryHeaders.Cast<object>().ToArray());
            return sheet;
        }

        public static void PrimaryRow(SheetSpec sheet, int row, string profit, string parentId, string parentName, string carrier, string pspName, double? plan, double? actual)
        {
            sheet.Row(row, profit, parentId, parentName, carrier, pspName, plan, actual);
        }

        /// <summary>Die fünf beobachteten Zeilen aus Abschnitt 9.1 (Plan jeweils leer), Zeile 520/521 direkt nach Zeile 4.</summary>
        public static WorkbookBuilder StandardWorkbook(bool withTimestamp = true)
        {
            var wb = new WorkbookBuilder();
            if (withTimestamp) AddSnapshotTimestamp(wb, SapSerial);

            var sheet = AddPrimarySheet(wb);
            PrimaryRow(sheet, 2, "SAE410", Root, "62D1001I_DTS10 Produktentwicklung", Root, "62D1001I_DTS10 Produktentwicklung", null, 15733.48);
            PrimaryRow(sheet, 3, "SAE410", Root, "62D1001I_DTS10 Produktentwicklung", Step1, "6D1010ITI_DTS10 Produktentwicklung Step1", null, 83333.31);
            PrimaryRow(sheet, 4, "SAE410", Root, "62D1001I_DTS10 Produktentwicklung", Step1Org, "DTS10_ORG_DTS10 Organisation PMT", null, 30383.5);
            PrimaryRow(sheet, 520, "SAE630", "1500630C26IC001", "GLX Verrechnung 1500630", Kd05, "KD_05_POCS", null, 612.5);
            PrimaryRow(sheet, 521, "SAE630", "1500630C26IC001", "GLX Verrechnung 1500630", Kd06, "KD_06_DAT(STW)", null, 0);
            sheet.Set("G521", "2260.3000000000002", Cv.Number);   // genau so steht der Wert im Original (Gleitkomma-Rauschen)
            return wb;
        }

        public static void AddSnapshotTimestamp(WorkbookBuilder wb, double serial, string sheetName = "Projekte")
        {
            var sheet = wb.Sheets.FirstOrDefault(s => s.Name == sheetName) ?? wb.Sheet(sheetName);
            sheet.Set("D1", "SAP-Daten von");
            sheet.Set("E1", serial, Cv.Number);
        }

        public static List<LocalProjectInfo> StandardProjects()
        {
            return new List<LocalProjectInfo>
            {
                new LocalProjectInfo { Id = 1, Name = "Projekt A", Customer = "Kunde 1", Carrier = Step1, IsExternal = true },
                new LocalProjectInfo { Id = 2, Name = "Projekt B", Customer = "Kunde 1", Carrier = Step1, IsExternal = false },
                new LocalProjectInfo { Id = 3, Name = "Projekt C", Customer = "Kunde 2", Carrier = Kd06, IsExternal = false },
                new LocalProjectInfo { Id = 4, Name = "Projekt D", Customer = "Kunde 3", Carrier = "9999999X99ZZ999.01", IsExternal = true }
            };
        }

        public static ControllingImportPreview Preview(WorkbookBuilder wb, IReadOnlyList<LocalProjectInfo> projects, string name = "SAE-Projektcontrolling_2026-11.xlsx")
        {
            return ControllingImportService.BuildPreview(wb.Build(), name, projects);
        }

        public static ControllingImportPreview Preview(byte[] xlsx, IReadOnlyList<LocalProjectInfo> projects, string name = "test.xlsx")
        {
            return ControllingImportService.BuildPreview(xlsx, name, projects);
        }

        public static LocalProjectInfo Project(int id, string carrier)
        {
            return new LocalProjectInfo { Id = id, Name = "P" + id, Customer = "K", Carrier = carrier };
        }

        public static void AddHelperSheet(WorkbookBuilder wb, Action<SheetSpec> fill = null)
        {
            var sheet = wb.Sheet("_PrjListe", true);
            sheet.Row(3, HelperHeaders.Cast<object>().ToArray());
            fill?.Invoke(sheet);
        }

        /// <summary>Zeile 4/5 der Hilfsliste wie in Abschnitt 9.1 (D:K = 0, Plan-Kosten 0).</summary>
        public static void StandardHelperRows(SheetSpec sheet)
        {
            sheet.Row(4, Root, "SAE410", "-", 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, Root, "Amato", "Freigegeben", null, "62D1001I_DTS10 Produktentwicklung", 0.0, 15733.48, -15733.48, 0.0);
            sheet.Set("O4", "62D1001I_DTS10 Produktentwicklung ", Cv.FormulaString);
            sheet.Row(5, Step1, "SAE410", "-", 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, Root, "Amato", "Freigegeben", null, "6D1010ITI_DTS10 Produktentwicklung Step1", 0.0, 83333.31, -83333.31, 0.0);
            sheet.Set("O5", "62D1001I_DTS10 Produktentwicklung ", Cv.FormulaString);
        }

        public static void AddParentSheet(WorkbookBuilder wb)
        {
            var sheet = wb.Sheet("SAP#1PL", true);
            sheet.Row(1, ParentHeaders.Cast<object>().ToArray());
            sheet.Row(5, "DD - DTS (1500410)", "62D1001I_DTS10 Produktentwicklung (1500410A24RD001)", "Claudio Amato", "Freigegeben", 45292.0, 46387.0);
        }

        public static string TempDbPath()
        {
            var dir = Path.Combine(Path.GetTempPath(), "TimeTrackTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            return Path.Combine(dir, "test.db");
        }

        public static DateTime ExcelDate(double serial)
        {
            return new DateTime(1899, 12, 30).AddDays(serial);
        }
    }
}
