// Target: .NET Framework + C# 7.3
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;

namespace Zeitmanagement.Tests
{
    /// <summary>Wie ein Zellwert in die Datei geschrieben wird.</summary>
    public enum Cv
    {
        Shared,        // Text über die Shared-Strings-Tabelle
        Inline,        // Text als inlineStr
        Rich,          // Rich-Text-Eintrag in den Shared Strings (mehrere Läufe, Phonetik)
        Number,        // numerischer Wert
        FormulaString, // gespeicherter Formeltext (t="str") mit Formel
        FormulaNumber, // gespeicherte Formelzahl mit Formel
        Error,         // Fehlerwert (t="e")
        Blank          // nur formatierte, leere Zelle
    }

    public sealed class CellSpec
    {
        public string Ref;
        public object Value;
        public Cv Kind;
    }

    public sealed class SheetSpec
    {
        public string Name;
        public bool Hidden;
        public string RelationshipId;
        public readonly List<CellSpec> Cells = new List<CellSpec>();

        public SheetSpec Set(string reference, object value, Cv kind = Cv.Shared)
        {
            Cells.RemoveAll(c => c.Ref == reference); // eine Zelle nur einmal: spätere Angabe ersetzt die frühere
            Cells.Add(new CellSpec { Ref = reference, Value = value, Kind = kind });
            return this;
        }

        /// <summary>Setzt mehrere Zellen einer Zeile ab Spalte A (null = Zelle auslassen). Text als Shared String, Zahlen numerisch.</summary>
        public SheetSpec Row(int row, params object[] values)
        {
            for (var i = 0; i < values.Length; i++)
            {
                if (values[i] == null) continue;
                var kind = values[i] is string ? Cv.Shared : Cv.Number;
                Set(ColumnName(i + 1) + row, values[i], kind);
            }
            return this;
        }

        public static string ColumnName(int col)
        {
            var sb = new StringBuilder();
            while (col > 0)
            {
                col--;
                sb.Insert(0, (char)('A' + col % 26));
                col /= 26;
            }
            return sb.ToString();
        }
    }

    /// <summary>Erzeugt kleine .xlsx-Dateien mit dem Open-XML-SDK; kein Excel, keine Originaldatei.</summary>
    public sealed class WorkbookBuilder
    {
        public bool Date1904;
        public bool ReverseSheetOrder;
        public readonly List<SheetSpec> Sheets = new List<SheetSpec>();

        public SheetSpec Sheet(string name, bool hidden = false)
        {
            var s = new SheetSpec { Name = name, Hidden = hidden };
            Sheets.Add(s);
            return s;
        }

        public byte[] Build()
        {
            using (var ms = new MemoryStream())
            {
                using (var doc = SpreadsheetDocument.Create(ms, SpreadsheetDocumentType.Workbook))
                {
                    var workbookPart = doc.AddWorkbookPart();
                    workbookPart.Workbook = new Workbook();
                    if (Date1904) workbookPart.Workbook.WorkbookProperties = new WorkbookProperties { Date1904 = true };

                    var sharedPart = workbookPart.AddNewPart<SharedStringTablePart>();
                    var sharedItems = new List<SharedStringItem>();

                    Func<string, int> addShared = text =>
                    {
                        sharedItems.Add(new SharedStringItem(new Text(text) { Space = SpaceProcessingModeValues.Preserve }));
                        return sharedItems.Count - 1;
                    };
                    Func<string, int> addRich = text =>
                    {
                        // Rich-Text-Lauf aus zwei Teilen plus Phonetik, die nicht zum Text gehört
                        var half = text.Length / 2;
                        var item = new SharedStringItem(
                            new Run(new Text(text.Substring(0, half)) { Space = SpaceProcessingModeValues.Preserve }),
                            new Run(new RunProperties(new Bold()), new Text(text.Substring(half)) { Space = SpaceProcessingModeValues.Preserve }),
                            new PhoneticRun(new Text("PHONETIK-NICHT-ANZEIGEN")) { BaseTextStartIndex = 0, EndingBaseIndex = 1 });
                        sharedItems.Add(item);
                        return sharedItems.Count - 1;
                    };

                    var sheetEntries = new List<DocumentFormat.OpenXml.Spreadsheet.Sheet>();
                    uint sheetId = 1;
                    foreach (var spec in Sheets)
                    {
                        var relId = spec.RelationshipId ?? ("rIdSheet" + sheetId);
                        var part = workbookPart.AddNewPart<WorksheetPart>(relId);
                        part.Worksheet = new Worksheet(BuildSheetData(spec, addShared, addRich));
                        part.Worksheet.Save();

                        var entry = new DocumentFormat.OpenXml.Spreadsheet.Sheet { Name = spec.Name, SheetId = sheetId, Id = relId };
                        if (spec.Hidden) entry.State = SheetStateValues.Hidden;
                        sheetEntries.Add(entry);
                        sheetId++;
                    }

                    if (ReverseSheetOrder) sheetEntries.Reverse();
                    workbookPart.Workbook.AppendChild(new Sheets(sheetEntries.Cast<OpenXmlElement>().ToArray()));

                    sharedPart.SharedStringTable = new SharedStringTable(sharedItems.Cast<OpenXmlElement>().ToArray());
                    sharedPart.SharedStringTable.Save();
                    workbookPart.Workbook.Save();
                }
                return ms.ToArray();
            }
        }

        private static SheetData BuildSheetData(SheetSpec spec, Func<string, int> addShared, Func<string, int> addRich)
        {
            var data = new SheetData();
            var rows = new SortedDictionary<int, List<CellSpec>>();
            foreach (var cell in spec.Cells)
            {
                var rowNumber = int.Parse(new string(cell.Ref.Where(char.IsDigit).ToArray()), CultureInfo.InvariantCulture);
                List<CellSpec> list;
                if (!rows.TryGetValue(rowNumber, out list)) rows[rowNumber] = list = new List<CellSpec>();
                list.Add(cell);
            }

            foreach (var kv in rows)
            {
                var row = new Row { RowIndex = (uint)kv.Key };
                // Nur vorhandene Zellen, nach Spalte sortiert: Die dritte Zelle einer Zeile ist nicht unbedingt Spalte C.
                foreach (var cellSpec in kv.Value.OrderBy(c => ColumnIndex(c.Ref)))
                    row.Append(MakeCell(cellSpec, addShared, addRich));
                data.Append(row);
            }
            return data;
        }

        /// <summary>Zahl so wie Excel sie speichert: Doubles mit voller Rundlauf-Genauigkeit (z. B. 2260.3000000000002).</summary>
        private static string NumberText(object value)
        {
            if (value is string) return (string)value;   // roher XML-Text, z. B. "2260.3000000000002"
            if (value is double) return ((double)value).ToString("R", CultureInfo.InvariantCulture);
            return Convert.ToString(value, CultureInfo.InvariantCulture);
        }

        private static int ColumnIndex(string reference)
        {
            var col = 0;
            foreach (var ch in reference.TakeWhile(char.IsLetter)) col = col * 26 + (ch - 'A' + 1);
            return col;
        }

        private static Cell MakeCell(CellSpec spec, Func<string, int> addShared, Func<string, int> addRich)
        {
            var cell = new Cell { CellReference = spec.Ref };
            switch (spec.Kind)
            {
                case Cv.Shared:
                    cell.DataType = CellValues.SharedString;
                    cell.CellValue = new CellValue(addShared((string)spec.Value).ToString(CultureInfo.InvariantCulture));
                    break;
                case Cv.Rich:
                    cell.DataType = CellValues.SharedString;
                    cell.CellValue = new CellValue(addRich((string)spec.Value).ToString(CultureInfo.InvariantCulture));
                    break;
                case Cv.Inline:
                    cell.DataType = CellValues.InlineString;
                    cell.InlineString = new InlineString(new Text((string)spec.Value) { Space = SpaceProcessingModeValues.Preserve });
                    break;
                case Cv.Number:
                    cell.CellValue = new CellValue(NumberText(spec.Value));
                    break;
                case Cv.FormulaString:
                    cell.DataType = CellValues.String;
                    cell.CellFormula = new CellFormula("XLOOKUP(A4,'SAP#2PL'!D:D,'SAP#2PL'!E:E,0,0,1)");
                    cell.CellValue = new CellValue((string)spec.Value);
                    break;
                case Cv.FormulaNumber:
                    cell.CellFormula = new CellFormula("Q4-R4");
                    cell.CellValue = new CellValue(NumberText(spec.Value));
                    break;
                case Cv.Error:
                    cell.DataType = CellValues.Error;
                    cell.CellFormula = new CellFormula("1/0");
                    cell.CellValue = new CellValue((string)spec.Value);
                    break;
                case Cv.Blank:
                    cell.StyleIndex = 0;
                    break;
            }
            return cell;
        }

        /// <summary>Ersetzt den XML-Inhalt eines Pakets-Teils (für Tests mit manipuliertem XML).</summary>
        public static byte[] ReplacePart(byte[] xlsx, Func<string, bool> entryMatches, Func<string, string> transform)
        {
            using (var input = new MemoryStream(xlsx))
            using (var output = new MemoryStream())
            {
                using (var zipIn = new ZipArchive(input, ZipArchiveMode.Read))
                using (var zipOut = new ZipArchive(output, ZipArchiveMode.Create, true))
                {
                    foreach (var entry in zipIn.Entries)
                    {
                        var newEntry = zipOut.CreateEntry(entry.FullName);
                        using (var src = entry.Open())
                        using (var dst = newEntry.Open())
                        {
                            if (entryMatches(entry.FullName))
                            {
                                string text;
                                using (var reader = new StreamReader(src, Encoding.UTF8)) text = reader.ReadToEnd();
                                var bytes = new UTF8Encoding(false).GetBytes(transform(text));
                                dst.Write(bytes, 0, bytes.Length);
                            }
                            else
                            {
                                src.CopyTo(dst);
                            }
                        }
                    }
                }
                return output.ToArray();
            }
        }

        public static List<string> EntryNames(byte[] xlsx)
        {
            using (var ms = new MemoryStream(xlsx))
            using (var zip = new ZipArchive(ms, ZipArchiveMode.Read))
                return zip.Entries.Select(e => e.FullName).ToList();
        }
    }
}
