// Target: .NET Framework + C# 7.3
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading;
using System.Xml;
using DocumentFormat.OpenXml.Packaging;

namespace Zeitmanagement.Helpers
{
    public static class ControllingParser
    {
        /// <summary>Version von Layout-Annahmen und Auswertungsregeln; Teil der Import-Identität.</summary>
        public const int Version = 1;
    }

    /// <summary>
    /// Liest den SAP-Projektcontrolling-Export (.xlsx) ohne Excel: nur gespeicherte Zellwerte, keine Formeln,
    /// keine Makros, keine externen Verknüpfungen. Die Hauptquelle ist das (meist ausgeblendete) Blatt
    /// "SAP#2PL"; "_PrjListe" (Kontext) und "SAP#1PL" (Projektdaten) sind optional.
    ///
    /// Der Reader kennt die lokalen Kostenträger nur über <c>isEligible</c>: Zeilen ohne passenden Kostenträger
    /// werden nur gezählt, ihre Inhalte werden weder geparst noch behalten.
    /// </summary>
    public static class ProjectControllingExcelReader
    {
        public const string PrimarySheetName = "SAP#2PL";
        public const string ParentSheetName = "SAP#1PL";
        public const string HelperSheetName = "_PrjListe";

        private static readonly string[] PrimaryHeaders =
        {
            "Profitcenter",
            "Externe Projekt-ID",
            "Externe Projekt-ID",
            "Externe PSP-Element-ID",
            "Externe PSP-Element-ID",
            "Planbetrag in Buchungskreiswährung",
            "Istbetrag in Buchungskreiswährung"
        };

        private static readonly string[] ParentHeaders =
        {
            "Verantwortliche Kostenstelle",
            "Projekt",
            "Projektmanager",
            "Ausführungsstatus",
            "Geplanter Start",
            "Geplantes Ende"
        };

        private static readonly string[] HelperHeaders =
        {
            "PSP-Element", "Profitcenter", "Kunde",
            "Auftragswert Gesamt", "Abgerechnet Gesamt", "Offen Gesamt", "Abg % Gesamt", "Prj FG Gesamt", "WIP (offen) Gesamt",
            "Auftragswert lfd. Jahr", "Offen lfd. Jahr",
            "Projekt-ID", "PL", "Prj-Status", "Projekt", "PSP-Name",
            "Plan-Kosten", "Ist-Kosten", "Kosten offen", "FG"
        };

        private static readonly HashSet<string> PlaceholderKeys =
            new HashSet<string>(new[] { "Gesamtsumme", "Gesamtergebnis", "-", "(Leer)", "0" }, StringComparer.OrdinalIgnoreCase);

        private static readonly string[] SheetNamespaces =
        {
            "http://schemas.openxmlformats.org/spreadsheetml/2006/main",
            "http://purl.oclc.org/ooxml/spreadsheetml/main"
        };

        private const int MaxDetailDiagnosticsPerKind = 10;

        // ---------- Einstieg ----------

        /// <summary>
        /// Liest die Arbeitsmappe. Wirft <see cref="ControllingImportException"/>, wenn die Datei ungültig ist oder
        /// Grenzen überschreitet; Layoutprobleme und Zellfehler stehen dagegen in <see cref="ControllingReadResult.Diagnostics"/>.
        /// </summary>
        public static ControllingReadResult Read(
            Stream xlsx,
            Func<string, bool> isEligible,
            ReaderLimits limits = null,
            CancellationToken ct = default(CancellationToken))
        {
            if (xlsx == null) throw new ArgumentNullException(nameof(xlsx));
            if (isEligible == null) throw new ArgumentNullException(nameof(isEligible));
            limits = limits ?? new ReaderLimits();

            var result = new ControllingReadResult();
            var context = new ReadContext(limits, new ByteBudget(limits.MaxUncompressedBytes), ct, result);

            PrecheckPackage(xlsx, limits);
            xlsx.Position = 0;

            using (var document = OpenDocument(xlsx))
            {
                var workbook = document.WorkbookPart;
                if (workbook == null || workbook.Workbook == null)
                    throw new ControllingImportException("Die Datei enthält keine Arbeitsmappe.");

                var props = workbook.Workbook.WorkbookProperties;
                var date1904Flag = props == null ? null : props.Date1904;
                context.Date1904 = date1904Flag != null && date1904Flag.HasValue && date1904Flag.Value;

                var sheets = ResolveSheets(workbook);
                context.SharedStrings = new Lazy<List<string>>(() => ReadSharedStrings(workbook, context));

                ReadTimestamps(sheets, context);

                WorksheetPart primary;
                if (!sheets.TryGetValue(SheetKey(PrimarySheetName), out primary))
                {
                    result.Diagnostics.Add(new ImportDiagnostic(ImportSeverity.Error,
                        "Nicht unterstütztes Layout: Das Blatt „" + PrimarySheetName + "“ fehlt. Es wird kein Ersatz aus den Pivot-Blättern gebildet."));
                    return result;
                }

                var matched = ReadPrimary(primary, isEligible, context);
                if (result.HasErrors) return result;

                var enrichment = ReadHelperSheet(sheets, matched, context);
                var metadata = ReadParentMetadata(sheets, matched, context);
                BuildParents(matched, enrichment, metadata, context);
            }

            return result;
        }

        // ---------- Paket prüfen und öffnen ----------

        private static void PrecheckPackage(Stream stream, ReaderLimits limits)
        {
            if (!stream.CanSeek) throw new ControllingImportException("Die Datei kann nicht gelesen werden.");

            if (stream.Length > limits.MaxCompressedBytes)
            {
                throw new ControllingImportException(
                    "Die Datei ist zu groß (höchstens " + (limits.MaxCompressedBytes / (1024 * 1024)).ToString(CultureInfo.InvariantCulture) + " MiB erlaubt).");
            }

            stream.Position = 0;
            var head = new byte[8];
            var read = stream.Read(head, 0, head.Length);
            stream.Position = 0;
            if (read >= 8 && head[0] == 0xD0 && head[1] == 0xCF && head[2] == 0x11 && head[3] == 0xE0 &&
                head[4] == 0xA1 && head[5] == 0xB1 && head[6] == 0x1A && head[7] == 0xE1)
            {
                throw new ControllingImportException(
                    "Die Datei ist kennwortgeschützt/verschlüsselt oder eine alte .xls-Datei. Bitte als unverschlüsselte .xlsx speichern.");
            }

            try
            {
                using (var zip = new ZipArchive(stream, ZipArchiveMode.Read, true))
                {
                    long total = 0;
                    foreach (var entry in zip.Entries)
                    {
                        total += entry.Length;
                        if (total > limits.MaxUncompressedBytes)
                        {
                            throw new ControllingImportException(
                                "Die Datei ist entpackt zu groß (höchstens " + (limits.MaxUncompressedBytes / (1024 * 1024)).ToString(CultureInfo.InvariantCulture) + " MiB erlaubt).");
                        }
                    }
                }
            }
            catch (InvalidDataException ex)
            {
                throw new ControllingImportException("Die Datei ist keine gültige Excel-Arbeitsmappe (.xlsx).", ex);
            }
        }

        private static SpreadsheetDocument OpenDocument(Stream stream)
        {
            try
            {
                return SpreadsheetDocument.Open(stream, false, new OpenSettings { AutoSave = false, MaxCharactersInPart = 50000000 });
            }
            catch (Exception ex) when (!(ex is ControllingImportException) && !(ex is OperationCanceledException))
            {
                throw new ControllingImportException("Die Datei ist keine gültige Excel-Arbeitsmappe (.xlsx).", ex);
            }
        }

        /// <summary>
        /// Löst Blattnamen über die Beziehungen der Arbeitsmappe auf (nicht über Reihenfolge oder Dateinamen im Paket).
        /// Ausgeblendete Blätter bleiben lesbar.
        /// </summary>
        private static Dictionary<string, WorksheetPart> ResolveSheets(WorkbookPart workbook)
        {
            var map = new Dictionary<string, WorksheetPart>(StringComparer.Ordinal);
            var sheets = workbook.Workbook.Sheets;
            if (sheets == null) return map;

            foreach (var sheet in sheets.Elements<DocumentFormat.OpenXml.Spreadsheet.Sheet>())
            {
                var name = sheet.Name == null ? null : sheet.Name.Value;
                var relId = sheet.Id == null ? null : sheet.Id.Value;
                if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(relId)) continue;

                try
                {
                    var part = workbook.GetPartById(relId) as WorksheetPart;
                    if (part != null) map[SheetKey(name)] = part;
                }
                catch (Exception ex) when (ex is ArgumentException || ex is InvalidOperationException || ex is KeyNotFoundException || ex is IOException)
                {
                    // Beziehung zeigt ins Leere oder aus dem Paket heraus: Blatt gilt als nicht vorhanden.
                }
            }

            return map;
        }

        private static string SheetKey(string name)
        {
            return (name ?? string.Empty).Trim().Normalize(NormalizationForm.FormC).ToUpperInvariant();
        }

        // ---------- Zellen und Zeilen ----------

        private enum CellKind
        {
            Empty,
            Text,
            Number,
            Bool,
            Error,
            Invalid
        }

        private sealed class CellData
        {
            public CellKind Kind;
            public string Text;     // Text, Fehlerkürzel oder Rohtext einer ungültigen Zahl
            public decimal Number;
            public int Col;
            public int Row;

            public string Ref => ColumnName(Col) + Row.ToString(CultureInfo.InvariantCulture);

            public bool IsBlank => Kind == CellKind.Empty || (Kind == CellKind.Text && string.IsNullOrWhiteSpace(Text));

            /// <summary>Inhalt als Text (Zahlen in unveränderlicher Schreibweise), <c>null</c> bei leerer Zelle.</summary>
            public string TextValue
            {
                get
                {
                    switch (Kind)
                    {
                        case CellKind.Text: return string.IsNullOrWhiteSpace(Text) ? null : Text.Trim();
                        case CellKind.Number: return Number.ToString(CultureInfo.InvariantCulture);
                        case CellKind.Bool: return Number != 0m ? "TRUE" : "FALSE";
                        default: return null;
                    }
                }
            }
        }

        private static string ColumnName(int col)
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

        private static bool TryParseCellRef(string reference, out int col, out int row)
        {
            col = 0;
            row = 0;
            if (string.IsNullOrEmpty(reference)) return false;

            var i = 0;
            while (i < reference.Length && char.IsLetter(reference[i]))
            {
                var c = char.ToUpperInvariant(reference[i]);
                if (c < 'A' || c > 'Z') return false;
                col = col * 26 + (c - 'A' + 1);
                i++;
                if (col > 20000) return false;
            }

            if (i == 0 || i == reference.Length) return false;
            return int.TryParse(reference.Substring(i), NumberStyles.None, CultureInfo.InvariantCulture, out row);
        }

        private sealed class ReadContext
        {
            public ReadContext(ReaderLimits limits, ByteBudget budget, CancellationToken ct, ControllingReadResult result)
            {
                Limits = limits;
                Budget = budget;
                Cancellation = ct;
                Result = result;
            }

            public ReaderLimits Limits { get; }
            public ByteBudget Budget { get; }
            public CancellationToken Cancellation { get; }
            public ControllingReadResult Result { get; }
            public bool Date1904;
            public Lazy<List<string>> SharedStrings;
            private readonly Dictionary<string, int> _kindCounts = new Dictionary<string, int>(StringComparer.Ordinal);

            /// <summary>Fügt eine Meldung hinzu, bei vielen gleichartigen nur die ersten und später eine Sammelzeile.</summary>
            public void Add(string kind, ImportDiagnostic diagnostic)
            {
                int count;
                _kindCounts.TryGetValue(kind, out count);
                _kindCounts[kind] = count + 1;
                if (count < MaxDetailDiagnosticsPerKind || diagnostic.IsError)
                    Result.Diagnostics.Add(diagnostic);
            }

            public void Flush()
            {
                foreach (var kv in _kindCounts)
                {
                    if (kv.Value > MaxDetailDiagnosticsPerKind)
                    {
                        Result.Diagnostics.Add(new ImportDiagnostic(ImportSeverity.Warning,
                            "… und " + (kv.Value - MaxDetailDiagnosticsPerKind).ToString(CultureInfo.InvariantCulture) + " weitere Meldungen dieser Art (" + kv.Key + ")."));
                    }
                }
                _kindCounts.Clear();
            }
        }

        private sealed class ByteBudget
        {
            private long _remaining;

            public ByteBudget(long limit)
            {
                _remaining = limit;
            }

            public void Consume(int bytes)
            {
                _remaining -= bytes;
                if (_remaining < 0)
                    throw new ControllingImportException("Die Datei ist entpackt zu groß und wird nicht gelesen.");
            }
        }

        /// <summary>Zählt tatsächlich gelesene (entpackte) Bytes, damit gefälschte Größenangaben im Archiv nichts nützen.</summary>
        private sealed class BoundedStream : Stream
        {
            private readonly Stream _inner;
            private readonly ByteBudget _budget;

            public BoundedStream(Stream inner, ByteBudget budget)
            {
                _inner = inner;
                _budget = budget;
            }

            public override bool CanRead => true;
            public override bool CanSeek => false;
            public override bool CanWrite => false;
            public override long Length => throw new NotSupportedException();
            public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
            public override void Flush() { }

            public override int Read(byte[] buffer, int offset, int count)
            {
                var read = _inner.Read(buffer, offset, count);
                if (read > 0) _budget.Consume(read);
                return read;
            }

            public override long Seek(long offset, SeekOrigin origin) { throw new NotSupportedException(); }
            public override void SetLength(long value) { throw new NotSupportedException(); }
            public override void Write(byte[] buffer, int offset, int count) { throw new NotSupportedException(); }
        }

        private static XmlReader CreateReader(Stream partStream, ReadContext context)
        {
            var settings = new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit, // keine DTDs, keine externen Entitäten
                XmlResolver = null,
                IgnoreComments = true,
                IgnoreProcessingInstructions = true,
                CloseInput = false
            };
            return XmlReader.Create(new BoundedStream(partStream, context.Budget), settings);
        }

        private static bool IsSpreadsheetElement(XmlReader reader, string localName)
        {
            return reader.NodeType == XmlNodeType.Element &&
                   reader.LocalName == localName &&
                   Array.IndexOf(SheetNamespaces, reader.NamespaceURI) >= 0;
        }

        /// <summary>Text eines &lt;si&gt;/&lt;is&gt;-Elements: alle &lt;t&gt; (auch in Rich-Text-Läufen), ohne Phonetik (&lt;rPh&gt;).</summary>
        private static string ReadStringItem(XmlReader reader)
        {
            var sb = new StringBuilder();
            using (var sub = reader.ReadSubtree())
            {
                sub.Read();
                sub.Read();
                while (!sub.EOF)
                {
                    if (sub.NodeType == XmlNodeType.Element && sub.LocalName == "rPh")
                    {
                        sub.Skip();
                    }
                    else if (sub.NodeType == XmlNodeType.Element && sub.LocalName == "t")
                    {
                        sb.Append(sub.ReadElementContentAsString());
                    }
                    else
                    {
                        sub.Read();
                    }
                }
            }
            return DecodeEscapes(sb.ToString());
        }

        /// <summary>Löst OOXML-Maskierungen wie "_x000D_" auf.</summary>
        private static string DecodeEscapes(string s)
        {
            if (s.IndexOf("_x", StringComparison.Ordinal) < 0) return s;

            var sb = new StringBuilder(s.Length);
            for (var i = 0; i < s.Length; i++)
            {
                if (s[i] == '_' && i + 6 < s.Length && s[i + 1] == 'x' && s[i + 6] == '_')
                {
                    int code;
                    if (int.TryParse(s.Substring(i + 2, 4), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out code))
                    {
                        sb.Append((char)code);
                        i += 6;
                        continue;
                    }
                }
                sb.Append(s[i]);
            }
            return sb.ToString();
        }

        private static List<string> ReadSharedStrings(WorkbookPart workbook, ReadContext context)
        {
            var list = new List<string>();
            var part = workbook.SharedStringTablePart;
            if (part == null) return list;

            try
            {
                using (var raw = part.GetStream(FileMode.Open, FileAccess.Read))
                using (var reader = CreateReader(raw, context))
                {
                    while (reader.Read())
                    {
                        if (IsSpreadsheetElement(reader, "si"))
                            list.Add(ReadStringItem(reader));
                    }
                }
            }
            catch (XmlException ex)
            {
                throw new ControllingImportException("Die Arbeitsmappe enthält ungültiges oder unzulässiges XML.", ex);
            }

            return list;
        }

        private static CellData ParseCell(XmlReader cellReader, string typeAttribute, ReadContext context)
        {
            string valueText = null;
            string inlineText = null;

            cellReader.Read(); // auf <c>
            cellReader.Read(); // erstes Kind (bei leerem Element: Ende)
            while (!cellReader.EOF)
            {
                if (cellReader.NodeType == XmlNodeType.Element)
                {
                    if (cellReader.LocalName == "v")
                    {
                        valueText = cellReader.ReadElementContentAsString();
                    }
                    else if (cellReader.LocalName == "is")
                    {
                        inlineText = ReadStringItem(cellReader);
                        cellReader.Read();
                    }
                    else
                    {
                        cellReader.Skip(); // z. B. <f> (Formeltext wird nie ausgewertet)
                    }
                }
                else
                {
                    cellReader.Read();
                }
            }

            var cell = new CellData { Kind = CellKind.Empty };
            switch (typeAttribute)
            {
                case "s":
                    {
                        int index;
                        if (valueText == null) break;
                        var shared = context.SharedStrings.Value;
                        if (int.TryParse(valueText.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out index) && index >= 0 && index < shared.Count)
                        {
                            cell.Kind = CellKind.Text;
                            cell.Text = shared[index];
                        }
                        else
                        {
                            cell.Kind = CellKind.Invalid;
                            cell.Text = valueText;
                        }
                        break;
                    }
                case "str":
                    if (valueText != null)
                    {
                        cell.Kind = CellKind.Text;
                        cell.Text = DecodeEscapes(valueText);
                    }
                    break;
                case "inlineStr":
                    cell.Kind = CellKind.Text;
                    cell.Text = inlineText ?? (valueText == null ? string.Empty : DecodeEscapes(valueText));
                    break;
                case "b":
                    if (valueText != null)
                    {
                        cell.Kind = CellKind.Bool;
                        cell.Number = valueText.Trim() == "1" ? 1m : 0m;
                    }
                    break;
                case "e":
                    cell.Kind = CellKind.Error;
                    cell.Text = valueText ?? "#ERR";
                    break;
                case "d":
                    if (valueText != null)
                    {
                        cell.Kind = CellKind.Text;
                        cell.Text = valueText;
                    }
                    break;
                default:
                    {
                        if (string.IsNullOrWhiteSpace(valueText)) break;
                        decimal number;
                        if (decimal.TryParse(valueText.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out number))
                        {
                            cell.Kind = CellKind.Number;
                            cell.Number = number;
                        }
                        else
                        {
                            cell.Kind = CellKind.Invalid; // z. B. "NaN", "Infinity"
                            cell.Text = valueText;
                        }
                        break;
                    }
            }

            return cell;
        }

        /// <summary>
        /// Liest die Zeilen eines Blatts der Reihe nach und ruft <paramref name="onRow"/> mit den Zellen bis
        /// <paramref name="maxColumn"/> auf (Spalte → Zelle, leere/ausgelassene Zellen fehlen). Gibt der Callback
        /// <c>false</c> zurück, wird das Lesen beendet.
        /// </summary>
        private static void ScanRows(WorksheetPart part, int maxColumn, ReadContext context, Func<int, Dictionary<int, CellData>, bool> onRow)
        {
            try
            {
                using (var raw = part.GetStream(FileMode.Open, FileAccess.Read))
                using (var reader = CreateReader(raw, context))
                {
                    var rowCount = 0;
                    var lastRow = 0;

                    while (reader.Read())
                    {
                        if (!IsSpreadsheetElement(reader, "row")) continue;

                        context.Cancellation.ThrowIfCancellationRequested();

                        rowCount++;
                        if (rowCount > context.Limits.MaxRowsPerSheet)
                        {
                            throw new ControllingImportException(
                                "Ein Arbeitsblatt hat mehr als " + context.Limits.MaxRowsPerSheet.ToString(CultureInfo.InvariantCulture) + " Zeilen und wird nicht gelesen.");
                        }

                        var rowNumber = lastRow + 1;
                        var rowAttribute = reader.GetAttribute("r");
                        int parsedRow;
                        if (rowAttribute != null && int.TryParse(rowAttribute, NumberStyles.None, CultureInfo.InvariantCulture, out parsedRow))
                            rowNumber = parsedRow;
                        lastRow = rowNumber;

                        var cells = new Dictionary<int, CellData>();
                        using (var rowReader = reader.ReadSubtree())
                        {
                            rowReader.Read(); // auf <row>
                            rowReader.Read();
                            var nextColumn = 1;
                            while (!rowReader.EOF)
                            {
                                if (IsSpreadsheetElement(rowReader, "c"))
                                {
                                    var column = nextColumn;
                                    int refColumn, refRow;
                                    if (TryParseCellRef(rowReader.GetAttribute("r"), out refColumn, out refRow)) column = refColumn;
                                    nextColumn = column + 1;

                                    var typeAttribute = rowReader.GetAttribute("t");
                                    CellData cell;
                                    using (var cellReader = rowReader.ReadSubtree())
                                    {
                                        cell = ParseCell(cellReader, typeAttribute, context);
                                    }

                                    if (column <= maxColumn && cell.Kind != CellKind.Empty)
                                    {
                                        cell.Col = column;
                                        cell.Row = rowNumber;
                                        cells[column] = cell;
                                    }
                                    rowReader.Read();
                                }
                                else if (rowReader.NodeType == XmlNodeType.Element)
                                {
                                    rowReader.Skip();
                                }
                                else
                                {
                                    rowReader.Read();
                                }
                            }
                        }

                        if (!onRow(rowNumber, cells)) break;
                    }
                }
            }
            catch (XmlException ex)
            {
                throw new ControllingImportException("Die Arbeitsmappe enthält ungültiges oder unzulässiges XML.", ex);
            }
        }

        private static CellData Get(Dictionary<int, CellData> cells, int column)
        {
            CellData cell;
            return cells.TryGetValue(column, out cell) ? cell : null;
        }

        private static string NormalizeLabel(string text)
        {
            if (text == null) return string.Empty;
            var sb = new StringBuilder();
            var lastWasSpace = true;
            foreach (var ch in text.Normalize(NormalizationForm.FormC))
            {
                if (char.IsWhiteSpace(ch))
                {
                    if (!lastWasSpace) sb.Append(' ');
                    lastWasSpace = true;
                }
                else
                {
                    sb.Append(ch);
                    lastWasSpace = false;
                }
            }
            return sb.ToString().TrimEnd().ToUpperInvariant();
        }

        /// <summary>Prüft Überschrift für Überschrift (Leerraum/Zeilenumbrüche egal); liefert die Abweichungen.</summary>
        private static List<string> CompareHeaders(Dictionary<int, CellData> cells, string[] expected)
        {
            var problems = new List<string>();
            for (var i = 0; i < expected.Length; i++)
            {
                var cell = Get(cells, i + 1);
                var actual = cell == null ? string.Empty : (cell.TextValue ?? string.Empty);
                if (NormalizeLabel(actual) != NormalizeLabel(expected[i]))
                {
                    problems.Add(ColumnName(i + 1) + ": erwartet „" + expected[i] + "“, gefunden „" + (actual.Length == 0 ? "(leer)" : actual) + "“");
                }
            }
            return problems;
        }

        // ---------- Stand-Zeitstempel ----------

        private static void ReadTimestamps(Dictionary<string, WorksheetPart> sheets, ReadContext context)
        {
            // Zuerst "Projekte", nur falls dort nichts steht, ist "Aufträge" maßgeblich; beide stehen sie für denselben SAP-Stand.
            foreach (var name in new[] { "Projekte", "Aufträge", "Auftraege" })
            {
                WorksheetPart part;
                if (!sheets.TryGetValue(SheetKey(name), out part)) continue;

                CellData stamp = null;
                ScanRows(part, 5, context, (row, cells) =>
                {
                    if (row == 1) stamp = Get(cells, 5);
                    return false; // nur Zeile 1 (oder die erste vorhandene) wird gebraucht
                });

                if (stamp == null || stamp.IsBlank) continue;

                DateTime value;
                if (TryCellToDateTime(stamp, context.Date1904, out value))
                {
                    context.Result.Timestamps.Add(new SourceTimestampCandidate(name == "Auftraege" ? "Aufträge" : name, value));
                }
                else
                {
                    context.Result.Diagnostics.Add(new ImportDiagnostic(ImportSeverity.Warning,
                        "Der SAP-Stand in E1 ist kein gültiges Datum.", name, "E1"));
                }
            }

            if (context.Result.Timestamps.Count == 0)
            {
                context.Result.Diagnostics.Add(new ImportDiagnostic(ImportSeverity.Warning,
                    "Die Datei nennt keinen SAP-Stand („SAP-Daten von“). Der Stand muss angegeben werden."));
            }
        }

        private static bool TryCellToDateTime(CellData cell, bool date1904, out DateTime value)
        {
            value = default(DateTime);
            if (cell == null) return false;

            if (cell.Kind == CellKind.Number)
            {
                var serial = (double)cell.Number;
                if (serial < 1 || serial > 2958465) return false;
                var origin = date1904 ? new DateTime(1904, 1, 1) : new DateTime(1899, 12, 30);
                value = DateTime.SpecifyKind(origin.AddSeconds(Math.Round(serial * 86400.0)), DateTimeKind.Unspecified);
                return true;
            }

            if (cell.Kind == CellKind.Text && !string.IsNullOrWhiteSpace(cell.Text))
            {
                DateTime parsed;
                if (DateTime.TryParse(cell.Text.Trim(), CultureInfo.GetCultureInfo("de-DE"), DateTimeStyles.None, out parsed) ||
                    DateTime.TryParse(cell.Text.Trim(), CultureInfo.InvariantCulture, DateTimeStyles.None, out parsed))
                {
                    value = DateTime.SpecifyKind(parsed, DateTimeKind.Unspecified);
                    return true;
                }
            }

            return false;
        }

        // ---------- Hauptquelle SAP#2PL ----------

        private static Dictionary<string, PspCostRow> ReadPrimary(WorksheetPart part, Func<string, bool> isEligible, ReadContext context)
        {
            var result = context.Result;
            var matched = new Dictionary<string, List<PspCostRow>>(StringComparer.Ordinal);
            var matchedKeys = new HashSet<string>(StringComparer.Ordinal);
            var headerChecked = false;
            var headerOk = true;

            ScanRows(part, PrimaryHeaders.Length, context, (rowNumber, cells) =>
            {
                if (!headerChecked)
                {
                    // Die Überschriften müssen in Zeile 1 stehen, Spalte für Spalte (doppelte Überschriften = feste Positionen).
                    headerChecked = true;
                    var problems = rowNumber == 1 ? CompareHeaders(cells, PrimaryHeaders) : new List<string> { "Zeile 1 mit den Überschriften fehlt" };
                    if (problems.Count > 0)
                    {
                        headerOk = false;
                        foreach (var problem in problems)
                        {
                            result.Diagnostics.Add(new ImportDiagnostic(ImportSeverity.Error,
                                "Nicht unterstütztes Layout in „" + PrimarySheetName + "“, Überschrift " + problem + ".", PrimarySheetName));
                        }
                        return false;
                    }
                    return true;
                }

                if (cells.Count == 0) return true; // wirklich leere (nur formatierte) Zeile

                var keyCell = Get(cells, 4);
                string keyText;
                var classification = ClassifyKey(keyCell, out keyText);

                if (classification == KeyClass.Blank || classification == KeyClass.Placeholder) return true;

                var normalizedKey = CarrierKeys.Normalize(keyText);

                if (classification == KeyClass.Malformed)
                {
                    // Ein defekter Schlüssel, der zu einem Kostenträger gehören könnte, wird nie stillschweigend übergangen.
                    var couldBeConfigured = normalizedKey.Length > 0 && isEligible(normalizedKey);
                    var reference = keyCell == null ? "D" + rowNumber.ToString(CultureInfo.InvariantCulture) : keyCell.Ref;
                    if (couldBeConfigured)
                    {
                        result.Diagnostics.Add(new ImportDiagnostic(ImportSeverity.Error,
                            "Die PSP-Element-ID „" + keyText + "“ ist fehlerhaft gespeichert (z. B. als Zahl); führende Nullen könnten fehlen. Sie passt zu einem Kostenträger und blockiert den Import.",
                            PrimarySheetName, reference));
                    }
                    else
                    {
                        context.Add("Ungültige PSP-Element-ID", new ImportDiagnostic(ImportSeverity.Warning,
                            "Zeile mit ungültiger PSP-Element-ID ignoriert.", PrimarySheetName, reference));
                    }
                    return true;
                }

                result.ScannedKeys.Add(normalizedKey);
                if (!isEligible(normalizedKey)) return true; // Inhalte unpassender Zeilen werden weder geparst noch behalten

                matchedKeys.Add(normalizedKey);
                var row = BuildPrimaryRow(rowNumber, cells, keyText, normalizedKey, context);
                List<PspCostRow> list;
                if (!matched.TryGetValue(normalizedKey, out list))
                {
                    list = new List<PspCostRow>();
                    matched[normalizedKey] = list;
                }
                list.Add(row);
                return true;
            });

            context.Flush();

            var accepted = new Dictionary<string, PspCostRow>(StringComparer.Ordinal);
            if (!headerOk) return accepted;

            foreach (var group in matched)
            {
                var first = group.Value[0];
                var conflicting = group.Value.Skip(1).Where(r => r.PayloadText() != first.PayloadText()).ToList();
                if (conflicting.Count > 0)
                {
                    var rows = string.Join(", ", group.Value.Select(r => r.SourceRow.ToString(CultureInfo.InvariantCulture)));
                    result.Diagnostics.Add(new ImportDiagnostic(ImportSeverity.Error,
                        "Widersprüchliche doppelte Zeilen für das PSP-Element „" + first.CarrierText + "“ (Zeilen " + rows + "). Die Datei muss zuerst bereinigt werden.",
                        PrimarySheetName));
                    continue;
                }

                if (group.Value.Count > 1)
                {
                    result.Diagnostics.Add(new ImportDiagnostic(ImportSeverity.Warning,
                        "Das PSP-Element „" + first.CarrierText + "“ steht " + group.Value.Count.ToString(CultureInfo.InvariantCulture) + "-mal mit identischen Werten in der Datei; es wird einmal übernommen.",
                        PrimarySheetName));
                }
                accepted[group.Key] = first;
            }

            foreach (var row in accepted.Values.OrderBy(r => r.SourceRow))
                result.Rows.Add(row);

            result.IgnoredCount = result.ScannedKeys.Count - matchedKeys.Count;
            return accepted;
        }

        private enum KeyClass
        {
            Blank,
            Placeholder,
            Valid,
            Malformed
        }

        private static KeyClass ClassifyKey(CellData cell, out string text)
        {
            text = null;
            if (cell == null || cell.IsBlank) return KeyClass.Blank;

            switch (cell.Kind)
            {
                case CellKind.Text:
                    text = cell.Text.Trim();
                    if (PlaceholderKeys.Contains(text)) return KeyClass.Placeholder;
                    if (text.Any(char.IsControl)) return KeyClass.Malformed;
                    return KeyClass.Valid;
                case CellKind.Number:
                    text = cell.Number.ToString(CultureInfo.InvariantCulture);
                    return PlaceholderKeys.Contains(text) ? KeyClass.Placeholder : KeyClass.Malformed;
                default:
                    text = cell.Text ?? cell.TextValue ?? string.Empty;
                    return KeyClass.Malformed;
            }
        }

        private static PspCostRow BuildPrimaryRow(int rowNumber, Dictionary<int, CellData> cells, string keyText, string normalizedKey, ReadContext context)
        {
            var row = new PspCostRow
            {
                CarrierKey = normalizedKey,
                CarrierText = keyText,
                ProfitCenter = Get(cells, 1)?.TextValue,
                ExternalProjectId = Get(cells, 2)?.TextValue,
                ExternalProjectName = Get(cells, 3)?.TextValue,
                PspName = Get(cells, 5)?.TextValue,
                SourceSheet = PrimarySheetName,
                SourceRow = rowNumber
            };

            row.PlannedCost = ReadRequiredMoney(Get(cells, 6), "F" + rowNumber.ToString(CultureInfo.InvariantCulture), "Planbetrag", context);
            row.ActualCost = ReadRequiredMoney(Get(cells, 7), "G" + rowNumber.ToString(CultureInfo.InvariantCulture), "Istbetrag", context);

            if (row.ActualCost.HasValue && row.ActualCost.Value < 0m)
            {
                context.Result.Diagnostics.Add(new ImportDiagnostic(ImportSeverity.Warning,
                    "Negativer Istbetrag beim PSP-Element „" + keyText + "“ (Gutschrift oder Korrektur); der Wert bleibt erhalten.",
                    PrimarySheetName, "G" + rowNumber.ToString(CultureInfo.InvariantCulture)));
            }
            if (row.PlannedCost.HasValue && row.PlannedCost.Value < 0m)
            {
                context.Result.Diagnostics.Add(new ImportDiagnostic(ImportSeverity.Warning,
                    "Negativer Planbetrag beim PSP-Element „" + keyText + "“; der Wert bleibt erhalten.",
                    PrimarySheetName, "F" + rowNumber.ToString(CultureInfo.InvariantCulture)));
            }

            return row;
        }

        /// <summary>Betrag einer passenden Zeile: leer = nicht geliefert (null), alles andere Nichtnumerische blockiert den Import.</summary>
        private static decimal? ReadRequiredMoney(CellData cell, string reference, string label, ReadContext context)
        {
            if (cell == null || cell.IsBlank) return null;
            if (cell.Kind == CellKind.Number) return cell.Number;

            context.Result.Diagnostics.Add(new ImportDiagnostic(ImportSeverity.Error,
                label + " ist nicht numerisch" + (cell.Kind == CellKind.Error ? " (Excel-Fehlerwert " + cell.Text + ")" : string.Empty) +
                ": „" + (cell.Text ?? cell.TextValue ?? string.Empty) + "“.",
                PrimarySheetName, reference));
            return null;
        }

        // ---------- Optionaler Kontext: _PrjListe ----------

        private sealed class HelperRow
        {
            public string Customer;
            public decimal? OrderValueTotal, InvoicedTotal, OpenTotal, InvoicedRatio, ProgressRatio, WipOpen, OrderValueYear, OpenYear;
            public string ProjectId, Manager, Status, ProjectName, ProfitCenter;
        }

        private static Dictionary<string, HelperRow> ReadHelperSheet(
            Dictionary<string, WorksheetPart> sheets,
            Dictionary<string, PspCostRow> matched,
            ReadContext context)
        {
            var rows = new Dictionary<string, HelperRow>(StringComparer.Ordinal);
            var result = context.Result;

            WorksheetPart part;
            if (!sheets.TryGetValue(SheetKey(HelperSheetName), out part))
            {
                result.Diagnostics.Add(new ImportDiagnostic(ImportSeverity.Warning,
                    "Das optionale Blatt „" + HelperSheetName + "“ fehlt; Projektkontext (Kunde, Auftragswerte, Status) ist nicht verfügbar."));
                return rows;
            }

            var headerOk = true;
            ScanRows(part, HelperHeaders.Length, context, (rowNumber, cells) =>
            {
                if (rowNumber < 3) return true;
                if (rowNumber == 3)
                {
                    var problems = CompareHeaders(cells, HelperHeaders);
                    if (problems.Count > 0)
                    {
                        headerOk = false;
                        result.Diagnostics.Add(new ImportDiagnostic(ImportSeverity.Warning,
                            "Das optionale Blatt „" + HelperSheetName + "“ hat ein unerwartetes Layout (" + string.Join("; ", problems.Take(3)) + "); Projektkontext wird nicht übernommen.",
                            HelperSheetName));
                        return false;
                    }
                    return true;
                }

                var keyCell = Get(cells, 1);
                var key = CarrierKeys.NormalizeOrNull(keyCell == null ? null : keyCell.TextValue);
                if (key == null || !matched.ContainsKey(key)) return true;

                if (rows.ContainsKey(key))
                {
                    context.Add("Doppelter Kontext", new ImportDiagnostic(ImportSeverity.Warning,
                        "Das PSP-Element steht mehrfach in „" + HelperSheetName + "“; die erste Zeile gilt.", HelperSheetName, "A" + rowNumber.ToString(CultureInfo.InvariantCulture)));
                    return true;
                }

                Func<int, string, decimal?> money = (column, label) => ReadCachedDecimal(Get(cells, column), HelperSheetName, label, context);

                rows[key] = new HelperRow
                {
                    ProfitCenter = Get(cells, 2)?.TextValue,
                    Customer = NullIfDash(Get(cells, 3)?.TextValue),
                    OrderValueTotal = money(4, "Auftragswert Gesamt"),
                    InvoicedTotal = money(5, "Abgerechnet Gesamt"),
                    OpenTotal = money(6, "Offen Gesamt"),
                    InvoicedRatio = money(7, "Abg % Gesamt"),
                    ProgressRatio = money(8, "Prj FG Gesamt"),
                    WipOpen = money(9, "WIP (offen) Gesamt"),
                    OrderValueYear = money(10, "Auftragswert lfd. Jahr"),
                    OpenYear = money(11, "Offen lfd. Jahr"),
                    ProjectId = Get(cells, 12)?.TextValue,
                    Manager = NullIfDash(Get(cells, 13)?.TextValue),
                    Status = NullIfDash(Get(cells, 14)?.TextValue),
                    ProjectName = NullIfDash(Get(cells, 15)?.TextValue)
                };
                return true;
            });

            context.Flush();
            return headerOk ? rows : new Dictionary<string, HelperRow>(StringComparer.Ordinal);
        }

        /// <summary>Gespeicherter Formelwert: leer oder fehlerhaft ergibt "nicht verfügbar" (null) mit Warnung, nie einen erfundenen Wert.</summary>
        private static decimal? ReadCachedDecimal(CellData cell, string sheet, string label, ReadContext context)
        {
            if (cell == null || cell.IsBlank) return null;
            if (cell.Kind == CellKind.Number) return cell.Number;

            context.Add("Kontext-Zellen ohne gültigen Wert", new ImportDiagnostic(ImportSeverity.Warning,
                label + " hat keinen gültigen gespeicherten Wert (" + (cell.Text ?? cell.TextValue ?? "leer") + "); das Feld ist nicht verfügbar.",
                sheet, cell.Ref));
            return null;
        }

        private static string NullIfDash(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;
            var t = text.Trim();
            return t == "-" ? null : t;
        }

        // ---------- Optionale Projektdaten: SAP#1PL ----------

        private sealed class ParentMetaRow
        {
            public string Description, CostCenter, Manager, Status;
            public DateTime? Start, End;
        }

        private static Dictionary<string, ParentMetaRow> ReadParentMetadata(
            Dictionary<string, WorksheetPart> sheets,
            Dictionary<string, PspCostRow> matched,
            ReadContext context)
        {
            var rows = new Dictionary<string, ParentMetaRow>(StringComparer.Ordinal);
            var wanted = new HashSet<string>(matched.Values.Select(r => r.ParentKey).Where(k => k != null), StringComparer.Ordinal);
            if (wanted.Count == 0) return rows;

            WorksheetPart part;
            if (!sheets.TryGetValue(SheetKey(ParentSheetName), out part))
            {
                context.Result.Diagnostics.Add(new ImportDiagnostic(ImportSeverity.Warning,
                    "Das optionale Blatt „" + ParentSheetName + "“ fehlt; Projektleiter, Status und Planungsdaten sind nicht verfügbar."));
                return rows;
            }

            var headerOk = true;
            ScanRows(part, ParentHeaders.Length, context, (rowNumber, cells) =>
            {
                if (rowNumber == 1)
                {
                    var problems = CompareHeaders(cells, ParentHeaders);
                    if (problems.Count > 0)
                    {
                        headerOk = false;
                        context.Result.Diagnostics.Add(new ImportDiagnostic(ImportSeverity.Warning,
                            "Das optionale Blatt „" + ParentSheetName + "“ hat ein unerwartetes Layout (" + string.Join("; ", problems.Take(3)) + "); Projektdaten werden nicht übernommen.",
                            ParentSheetName));
                        return false;
                    }
                    return true;
                }

                var projectCell = Get(cells, 2);
                string description, parentId;
                if (projectCell == null || !TrySplitTrailingId(projectCell.TextValue, out description, out parentId)) return true;

                var key = CarrierKeys.NormalizeOrNull(parentId);
                if (key == null || !wanted.Contains(key)) return true;

                if (rows.ContainsKey(key))
                {
                    context.Add("Doppelte Projektdaten", new ImportDiagnostic(ImportSeverity.Warning,
                        "Das Projekt „" + parentId + "“ steht mehrfach in „" + ParentSheetName + "“; die erste Zeile gilt.",
                        ParentSheetName, "B" + rowNumber.ToString(CultureInfo.InvariantCulture)));
                    return true;
                }

                rows[key] = new ParentMetaRow
                {
                    Description = description,
                    CostCenter = Get(cells, 1)?.TextValue,
                    Manager = NullIfDash(Get(cells, 3)?.TextValue),
                    Status = NullIfDash(Get(cells, 4)?.TextValue),
                    Start = ReadPlanDate(Get(cells, 5), "Geplanter Start", context),
                    End = ReadPlanDate(Get(cells, 6), "Geplantes Ende", context)
                };
                return true;
            });

            context.Flush();
            return headerOk ? rows : new Dictionary<string, ParentMetaRow>(StringComparer.Ordinal);
        }

        /// <summary>
        /// Trennt "Beschreibung (ID)" am letzten Klammerpaar am Ende. Klammern in der Beschreibung bleiben unberührt.
        /// </summary>
        public static bool TrySplitTrailingId(string text, out string description, out string id)
        {
            description = null;
            id = null;
            if (string.IsNullOrWhiteSpace(text)) return false;

            var t = text.TrimEnd();
            if (!t.EndsWith(")", StringComparison.Ordinal)) return false;

            var open = t.LastIndexOf('(');
            if (open < 0) return false;

            var inner = t.Substring(open + 1, t.Length - open - 2).Trim();
            if (inner.Length == 0) return false;

            id = inner;
            description = t.Substring(0, open).Trim();
            return true;
        }

        private static DateTime? ReadPlanDate(CellData cell, string label, ReadContext context)
        {
            if (cell == null || cell.IsBlank) return null;

            DateTime value;
            if (TryCellToDateTime(cell, context.Date1904, out value)) return value.Date;

            context.Add("Ungültige Planungsdaten", new ImportDiagnostic(ImportSeverity.Warning,
                label + " ist kein gültiges Datum; das Feld ist nicht verfügbar.", ParentSheetName, cell.Ref));
            return null;
        }

        // ---------- Projektkontext zusammenführen ----------

        private static void BuildParents(
            Dictionary<string, PspCostRow> matched,
            Dictionary<string, HelperRow> helper,
            Dictionary<string, ParentMetaRow> metadata,
            ReadContext context)
        {
            var result = context.Result;

            var byParent = matched.Values
                .Where(r => r.ParentKey != null)
                .GroupBy(r => r.ParentKey, StringComparer.Ordinal);

            foreach (var group in byParent)
            {
                var rows = group.OrderBy(r => r.SourceRow).ToList();
                var helperRows = rows.Select(r => { HelperRow h; return helper.TryGetValue(r.CarrierKey, out h) ? h : null; })
                                     .Where(h => h != null).ToList();
                ParentMetaRow meta;
                metadata.TryGetValue(group.Key, out meta);

                var parentId = rows[0].ExternalProjectId;
                var parent = new ParentContext
                {
                    ParentKey = group.Key,
                    ExternalParentId = parentId,
                    ParentName = rows.Select(r => r.ExternalProjectName).FirstOrDefault(n => !string.IsNullOrWhiteSpace(n)) ?? (meta == null ? null : meta.Description),
                    CostCenter = meta == null ? null : meta.CostCenter,
                    PlannedStart = meta == null ? null : meta.Start,
                    PlannedEnd = meta == null ? null : meta.End,
                    Manager = (meta == null ? null : meta.Manager) ?? Consistent(helperRows, h => h.Manager, "Projektleiter", parentId, result),
                    Status = (meta == null ? null : meta.Status) ?? Consistent(helperRows, h => h.Status, "Projektstatus", parentId, result),
                    Customer = Consistent(helperRows, h => h.Customer, "Kunde", parentId, result),
                    OrderValueTotal = ConsistentValue(helperRows, h => h.OrderValueTotal, "Auftragswert Gesamt", parentId, result),
                    InvoicedTotal = ConsistentValue(helperRows, h => h.InvoicedTotal, "Abgerechnet Gesamt", parentId, result),
                    OpenTotal = ConsistentValue(helperRows, h => h.OpenTotal, "Offen Gesamt", parentId, result),
                    InvoicedRatio = ConsistentValue(helperRows, h => h.InvoicedRatio, "Abg % Gesamt", parentId, result),
                    ProgressRatio = ConsistentValue(helperRows, h => h.ProgressRatio, "Prj FG Gesamt", parentId, result),
                    WipOpen = ConsistentValue(helperRows, h => h.WipOpen, "WIP (offen) Gesamt", parentId, result),
                    OrderValueYear = ConsistentValue(helperRows, h => h.OrderValueYear, "Auftragswert lfd. Jahr", parentId, result),
                    OpenYear = ConsistentValue(helperRows, h => h.OpenYear, "Offen lfd. Jahr", parentId, result)
                };
                result.Parents[group.Key] = parent;
            }
        }

        private static string Consistent(List<HelperRow> rows, Func<HelperRow, string> selector, string field, string parentId, ControllingReadResult result)
        {
            var values = rows.Select(selector).Where(v => !string.IsNullOrWhiteSpace(v)).Select(v => v.Trim()).Distinct(StringComparer.Ordinal).ToList();
            if (values.Count == 1) return values[0];
            if (values.Count > 1)
            {
                result.Diagnostics.Add(new ImportDiagnostic(ImportSeverity.Warning,
                    "Widersprüchliche Werte für „" + field + "“ im Projekt „" + parentId + "“; das Feld ist nicht verfügbar.", HelperSheetName));
            }
            return null;
        }

        /// <summary>
        /// Projektweite Beträge wiederholen sich in allen PSP-Zeilen eines Projekts. Sie werden nie addiert:
        /// ein einheitlicher Wert gilt, widersprüchliche Wiederholungen machen das Feld unverfügbar.
        /// </summary>
        private static decimal? ConsistentValue(List<HelperRow> rows, Func<HelperRow, decimal?> selector, string field, string parentId, ControllingReadResult result)
        {
            var values = rows.Select(selector).Where(v => v.HasValue).Select(v => v.Value).Distinct().ToList();
            if (values.Count == 1) return values[0];
            if (values.Count > 1)
            {
                result.Diagnostics.Add(new ImportDiagnostic(ImportSeverity.Warning,
                    "Widersprüchliche Werte für „" + field + "“ im Projekt „" + parentId + "“; das Feld ist nicht verfügbar.", HelperSheetName));
            }
            return null;
        }
    }
}
