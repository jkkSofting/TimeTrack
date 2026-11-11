// Target: .NET Framework + C# 7.3
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using MigraDoc.DocumentObjectModel;
using MigraDoc.DocumentObjectModel.Fields;
using MigraDoc.DocumentObjectModel.Tables;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using Xunit;
using Zeitmanagement.Helpers;

namespace Zeitmanagement.Tests
{
    /// <summary>
    /// Layout (über das Dokumentmodell) und Rendern (über die erzeugte Datei) des Tätigkeitsnachweises. Wie der Nachweis aussieht,
    /// prüft kein Test: Dafür schreibt <see cref="Writes_sample_documents_for_manual_review"/> Beispieldateien in den Temp-Ordner.
    /// </summary>
    public class PdfExporterTests
    {
        private static readonly DateTime Created = new DateTime(2026, 11, 11, 9, 30, 0);

        private static TimeTracker.TimeEntryRow E(int id, string date, string start, string end, string project, string description = "Konzept erstellt")
        {
            return new TimeTracker.TimeEntryRow
            {
                Id = id,
                Datum = DateTime.ParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture),
                Startzeit = start,
                Endzeit = end,
                Projektname = project,
                Beschreibung = description
            };
        }

        private static readonly TimeTracker.ProjectRow[] Projects =
        {
            new TimeTracker.ProjectRow { Projektname = "Projekt Alpha", Kunde = "Muster GmbH", Kostentraeger = "1500410A24RD001.01", IstExtern = true },
            new TimeTracker.ProjectRow { Projektname = "Projekt Beta", Kunde = "Muster GmbH", Kostentraeger = "1500410A24RD001.02", IstExtern = true }
        };

        private static ExportOptions Options()
        {
            return new ExportOptions { From = new DateTime(2026, 10, 1), To = new DateTime(2026, 10, 31) };
        }

        private static ExportReport Report(ExportOptions options, params TimeTracker.TimeEntryRow[] entries)
        {
            return ExportReportBuilder.Build(entries, Projects, options);
        }

        private static ExportMeta Meta()
        {
            return new ExportMeta
            {
                IssuerBlock = "Max Beispiel\nBeispielstraße 1\n70173 Stuttgart",
                ClientName = "Muster GmbH",
                ClientBlock = "Musterweg 2\n80331 München",
                ReferenceNumber = "PO-4711",
                CreatedAt = Created,
                AppVersion = "2026.46.1"
            };
        }

        private static TimeTracker.TimeEntryRow[] SomeEntries()
        {
            return new[]
            {
                E(1, "2026-10-05", "09:00", "10:30", "Projekt Alpha"),
                E(2, "2026-10-05", "11:00", "12:00", "Projekt Alpha", "Abstimmung mit dem Kunden"),
                E(3, "2026-10-06", "09:00", "11:15", "Projekt Beta", "Umsetzung der Schnittstelle")
            };
        }

        // ---------- Dokumentmodell lesen ----------

        private static void Collect(DocumentObject o, StringBuilder sb)
        {
            if (o is Text) { sb.Append(((Text)o).Content); return; }
            if (o is Character)
            {
                var c = (Character)o;
                if (c.SymbolName == SymbolName.LineBreak) sb.Append('\n');
                else if (c.SymbolName == SymbolName.Tab) sb.Append('\t');
                return;
            }
            if (o is PageField) { sb.Append("{PAGE}"); return; }
            if (o is NumPagesField) { sb.Append("{NUMPAGES}"); return; }
            if (o is FormattedText) { foreach (DocumentObject e in ((FormattedText)o).Elements) Collect(e, sb); return; }
            if (o is Paragraph) { foreach (DocumentObject e in ((Paragraph)o).Elements) Collect(e, sb); sb.Append('\n'); return; }
            if (o is Cell) { foreach (DocumentObject e in ((Cell)o).Elements) Collect(e, sb); return; }
        }

        private static string CellText(Cell cell)
        {
            var sb = new StringBuilder();
            Collect(cell, sb);
            return sb.ToString().Trim('\n').Replace("\n", " ");
        }

        private static List<Table> Tables(Document document)
        {
            return document.Sections.Cast<Section>().SelectMany(s => s.Elements.Cast<DocumentObject>()).OfType<Table>().ToList();
        }

        private static List<string> RowTexts(Table table)
        {
            // Zusammengeführte Zellen zählen einmal: die von einer Zelle überdeckten Nachbarn werden übersprungen.
            var rows = new List<string>();
            foreach (Row row in table.Rows)
            {
                var texts = new List<string>();
                for (var i = 0; i < row.Cells.Count; i++)
                {
                    var cell = row.Cells[i];
                    texts.Add(CellText(cell));
                    i += cell.MergeRight;
                }
                rows.Add(string.Join(" | ", texts));
            }
            return rows;
        }

        private static Table Details(Document document)
        {
            return Tables(document).Single(t => CellText(t.Rows[0].Cells[0]) == "Datum");
        }

        private static string BodyText(Document document)
        {
            var sb = new StringBuilder();
            foreach (Section section in document.Sections)
            {
                foreach (DocumentObject e in section.Elements)
                {
                    var table = e as Table;
                    if (table != null) sb.AppendLine(string.Join("\n", RowTexts(table)));
                    else Collect(e, sb);
                }
            }
            return sb.ToString();
        }

        private static string FooterText(Document document)
        {
            var sb = new StringBuilder();
            foreach (DocumentObject e in document.Sections.Cast<Section>().First().Footers.Primary.Elements) Collect(e, sb);
            return sb.ToString();
        }

        // ---------- Layout ----------

        [Fact]
        public void Document_has_title_period_parties_and_facts()
        {
            var document = PdfReportLayout.Build(Report(Options(), SomeEntries()), Meta());

            var body = BodyText(document);
            Assert.Contains("Tätigkeitsnachweis", body);
            Assert.Contains("01.10.2026 – 31.10.2026", body);
            Assert.Contains("AUFTRAGNEHMER", body);
            Assert.Contains("Max Beispiel", body);
            Assert.Contains("Beispielstraße 1", body);
            Assert.Contains("AUFTRAGGEBER", body);
            Assert.Contains("Muster GmbH", body);
            Assert.Contains("Musterweg 2", body);
            Assert.Contains("PO-4711", body);
            Assert.Contains("11.11.2026", body);
            Assert.Contains("1500410A24RD001.01, 1500410A24RD001.02", body);
        }

        [Fact]
        public void Document_info_names_the_issuer_and_not_the_windows_user()
        {
            var document = PdfReportLayout.Build(Report(Options(), SomeEntries()), Meta());

            Assert.Equal("Max Beispiel", document.Info.Author);
            Assert.Equal("Tätigkeitsnachweis 01.10.2026 – 31.10.2026", document.Info.Title);
            Assert.Equal("Projekt Alpha, Projekt Beta", document.Info.Subject);
        }

        [Fact]
        public void Summary_shows_total_bookings_work_days_and_average()
        {
            // 90 + 60 + 135 Minuten = 285 = 4,75 h an 2 Tagen, Durchschnitt 143 Minuten (2,38 h).
            var document = PdfReportLayout.Build(Report(Options(), SomeEntries()), Meta());

            var kpi = BodyText(document);
            Assert.Contains("GESAMTZEIT", kpi);
            Assert.Contains("4,75 h", kpi);
            Assert.Contains("4:45 h", kpi);
            Assert.Contains("BUCHUNGEN", kpi);
            Assert.Contains("ARBEITSTAGE", kpi);
            Assert.Contains("2,38 h", kpi);
        }

        [Fact]
        public void Details_table_repeats_its_header_row_on_every_page()
        {
            var table = Details(PdfReportLayout.Build(Report(Options(), SomeEntries()), Meta()));

            Assert.True(table.Rows[0].HeadingFormat);
            Assert.Equal("Datum | Zeit | Dauer (h) | Projekt | Tätigkeit", RowTexts(table)[0]);
        }

        [Fact]
        public void Details_by_day_show_the_date_once_and_a_subtotal_only_for_days_with_several_bookings()
        {
            var rows = RowTexts(Details(PdfReportLayout.Build(Report(Options(), SomeEntries()), Meta())));

            Assert.Equal("Mo 05.10.2026 | 09:00 – 10:30 | 1,50 | Projekt Alpha | Konzept erstellt", rows[1]);
            Assert.Equal(" | 11:00 – 12:00 | 1,00 | Projekt Alpha | Abstimmung mit dem Kunden", rows[2]);
            Assert.Equal("Tagessumme | 2,50 |", rows[3].TrimEnd());
            Assert.StartsWith("Di 06.10.2026 | 09:00 – 11:15 | 2,25 | Projekt Beta", rows[4]);
            Assert.StartsWith("Gesamt | 4,75", rows[5]);
            Assert.Equal(6, rows.Count);
        }

        [Fact]
        public void Duration_format_follows_the_option()
        {
            var options = Options();
            options.DurationFormat = ExportDurationFormat.Both;
            var both = RowTexts(Details(PdfReportLayout.Build(Report(options, SomeEntries()), Meta())));
            Assert.Contains("1,50 (1:30)", both[1]);

            options.DurationFormat = ExportDurationFormat.Clock;
            var clock = RowTexts(Details(PdfReportLayout.Build(Report(options, SomeEntries()), Meta())));
            Assert.Equal("Datum | Zeit | Dauer (h:mm) | Projekt | Tätigkeit", clock[0]);
            Assert.Contains("| 1:30 |", clock[1]);
        }

        [Fact]
        public void Hiding_the_times_leaves_only_the_duration()
        {
            var options = Options();
            options.ShowTimes = false;

            var rows = RowTexts(Details(PdfReportLayout.Build(Report(options, SomeEntries()), Meta())));

            Assert.Equal("Datum | Dauer (h) | Projekt | Tätigkeit", rows[0]);
            Assert.DoesNotContain("09:00", string.Join("\n", rows));
        }

        [Fact]
        public void Project_column_is_left_out_for_a_single_project_and_carrier_is_optional()
        {
            var single = Report(Options(), E(1, "2026-10-05", "09:00", "10:00", "Projekt Alpha"));
            Assert.Equal("Datum | Zeit | Dauer (h) | Tätigkeit", RowTexts(Details(PdfReportLayout.Build(single, Meta())))[0]);

            var options = Options();
            options.ShowCarrierColumn = true;
            var withCarrier = Report(options, E(1, "2026-10-05", "09:00", "10:00", "Projekt Alpha"));
            Assert.Equal("Datum | Zeit | Dauer (h) | Kostenträger | Tätigkeit", RowTexts(Details(PdfReportLayout.Build(withCarrier, Meta())))[0]);
        }

        [Fact]
        public void Hours_per_project_table_appears_only_with_several_projects()
        {
            var several = PdfReportLayout.Build(Report(Options(), SomeEntries()), Meta());
            var one = PdfReportLayout.Build(Report(Options(), E(1, "2026-10-05", "09:00", "10:00", "Projekt Alpha")), Meta());

            var perProject = Tables(several).Where(t => CellText(t.Rows[0].Cells[0]) == "Projekt").ToList();
            Assert.Single(perProject);
            var rows = RowTexts(perProject[0]);
            Assert.Equal("Projekt | Buchungen | Stunden | Anteil", rows[0]);
            Assert.Equal("Projekt Alpha | 2 | 2,50 | 53 %", rows[1]);
            Assert.Equal("Projekt Beta | 1 | 2,25 | 47 %", rows[2]);
            Assert.Equal("Gesamt | 3 | 4,75 | 100 %", rows[3]);
            Assert.DoesNotContain("Stunden je Projekt", BodyText(one));
        }

        [Fact]
        public void Grouping_by_project_adds_group_rows_and_summed_blocks()
        {
            var options = Options();
            options.Grouping = ExportGrouping.ByProject;

            var rows = RowTexts(Details(PdfReportLayout.Build(Report(options, SomeEntries()), Meta())));

            Assert.Equal("Datum | Zeit | Dauer (h) | Tätigkeit", rows[0]);   // Projekt-Spalte entfällt: steht in der Gruppenzeile
            Assert.StartsWith("Projekt Alpha", rows[1]);
            Assert.Contains("Muster GmbH · 1500410A24RD001.01", rows[1]);
            Assert.StartsWith("Summe Projekt Alpha | 2,50", rows[4]);
            Assert.StartsWith("Projekt Beta", rows[5]);
            Assert.StartsWith("Gesamt | 4,75", rows.Last());
        }

        [Fact]
        public void Grouping_by_week_adds_week_blocks_with_their_date_range()
        {
            var options = Options();
            options.Grouping = ExportGrouping.ByWeek;

            var rows = RowTexts(Details(PdfReportLayout.Build(Report(options, SomeEntries()), Meta())));

            Assert.StartsWith("KW 41 / 2026", rows[1]);
            Assert.Contains("05.10.2026 – 11.10.2026", rows[1]);
            Assert.StartsWith("Summe KW 41 / 2026 | 4,75", rows[5]);
            Assert.StartsWith("Gesamt | 4,75", rows[6]);
        }

        [Fact]
        public void Empty_description_shows_a_dash_and_line_breaks_become_line_breaks()
        {
            var entries = new[]
            {
                E(1, "2026-10-05", "09:00", "10:00", "Projekt Alpha", null),
                E(2, "2026-10-05", "10:00", "11:00", "Projekt Alpha", "Zeile 1\r\nZeile 2\tTab")
            };

            var rows = RowTexts(Details(PdfReportLayout.Build(Report(Options(), entries), Meta())));

            Assert.EndsWith("| –", rows[1]);
            Assert.EndsWith("| Zeile 1 Zeile 2 Tab", rows[2]);
        }

        [Fact]
        public void Empty_report_says_so_instead_of_printing_an_empty_table()
        {
            var document = PdfReportLayout.Build(Report(Options()), Meta());

            Assert.Contains("Im gewählten Zeitraum liegen keine Buchungen vor.", BodyText(document));
            Assert.DoesNotContain(Tables(document), t => CellText(t.Rows[0].Cells[0]) == "Datum");
        }

        [Fact]
        public void Notes_and_remark_are_printed_and_signature_is_optional()
        {
            var options = Options();
            options.RoundingMinutes = 15;
            var meta = Meta();
            meta.Note = "Abnahme am 12.11.";

            var with = BodyText(PdfReportLayout.Build(Report(options, SomeEntries()), meta));
            Assert.Contains("Hinweise", with);
            Assert.Contains("auf 15 Minuten gerundet", with);
            Assert.Contains("Bemerkung", with);
            Assert.Contains("Abnahme am 12.11.", with);
            Assert.Contains("Leistungsbestätigung", with);
            Assert.Contains("Ort, Datum, Unterschrift Auftraggeber", with);
            Assert.Contains("Ort, Datum, Unterschrift Auftragnehmer", with);

            meta.ShowSignature = false;
            var without = BodyText(PdfReportLayout.Build(Report(options, SomeEntries()), meta));
            Assert.DoesNotContain("Leistungsbestätigung", without);
        }

        [Fact]
        public void Footer_has_page_numbers_and_the_branding_can_be_turned_off()
        {
            var report = Report(Options(), SomeEntries());
            var meta = Meta();

            var footer = FooterText(PdfReportLayout.Build(report, meta));
            Assert.Contains("Max Beispiel · Tätigkeitsnachweis 01.10.2026 – 31.10.2026", footer);
            Assert.Contains("Seite {PAGE} von {NUMPAGES}", footer);
            Assert.Contains("Erstellt am 11.11.2026 mit TimeTrack 2026.46.1", footer);

            meta.ShowBranding = false;
            Assert.DoesNotContain("TimeTrack", FooterText(PdfReportLayout.Build(report, meta)));
        }

        [Fact]
        public void Clean_unifies_line_breaks_and_replaces_other_control_characters()
        {
            Assert.Equal("a\nb\nc d", PdfReportLayout.Clean("a\r\nb\rc\u0007d"));
            Assert.Equal(string.Empty, PdfReportLayout.Clean(null));
        }

        // ---------- Rendern ----------

        [Fact]
        public void Rendered_file_is_a_readable_pdf_with_title_author_and_embedded_fonts()
        {
            var bytes = PdfExporter.ToBytes(Report(Options(), SomeEntries()), Meta());

            Assert.Equal("%PDF-", Encoding.ASCII.GetString(bytes, 0, 5));
            var document = PdfReader.Open(new MemoryStream(bytes), PdfDocumentOpenMode.Import);
            Assert.True(document.PageCount >= 1);
            Assert.Equal("Tätigkeitsnachweis 01.10.2026 – 31.10.2026", document.Info.Title);
            Assert.Equal("Max Beispiel", document.Info.Author);
            Assert.Equal("TimeTrack", document.Info.Creator);
            Assert.Contains("/FontFile2", Encoding.GetEncoding("iso-8859-1").GetString(bytes));   // TrueType-Schrift eingebettet
        }

        [Fact]
        public void Many_entries_run_over_several_pages_with_the_numbering_filled_in()
        {
            var entries = Enumerable.Range(0, 400)
                .Select(i => E(i + 1, new DateTime(2026, 10, 1).AddDays(i % 28).ToString("yyyy-MM-dd"), (6 + i % 12).ToString("00") + ":00", (6 + i % 12).ToString("00") + ":50",
                    i % 2 == 0 ? "Projekt Alpha" : "Projekt Beta", "Arbeitspaket " + i + " bearbeitet und mit dem Kunden abgestimmt, Ergebnisse dokumentiert"))
                .ToArray();

            var bytes = PdfExporter.ToBytes(Report(Options(), entries), Meta());

            var document = PdfReader.Open(new MemoryStream(bytes), PdfDocumentOpenMode.Import);
            Assert.True(document.PageCount > 5, "Seiten: " + document.PageCount);
        }

        [Fact]
        public void Landscape_gives_wider_than_high_pages()
        {
            var meta = Meta();
            meta.Orientation = ExportOrientation.Landscape;

            var document = PdfReader.Open(new MemoryStream(PdfExporter.ToBytes(Report(Options(), SomeEntries()), meta)), PdfDocumentOpenMode.Import);

            Assert.True(document.Pages[0].Width.Point > document.Pages[0].Height.Point);
        }

        [Fact]
        public void Empty_report_still_renders()
        {
            var document = PdfReader.Open(new MemoryStream(PdfExporter.ToBytes(Report(Options()), Meta())), PdfDocumentOpenMode.Import);

            Assert.Equal(1, document.PageCount);
        }

        [Fact]
        public void Unusual_texts_do_not_break_rendering()
        {
            var entries = new[]
            {
                E(1, "2026-10-05", "09:00", "10:00", "Projekt Alpha", "Prüfung der Größe – „fertig“ äöüß ÄÖÜ € 100 % <b>&amp;</b>"),
                E(2, "2026-10-05", "10:00", "11:00", "Projekt Alpha", "Emoji 😀 und 日本語 und Ελληνικά"),
                E(3, "2026-10-05", "11:00", "12:00", "Projekt Alpha", new string('x', 400)),
                E(4, "2026-10-05", "12:00", "13:00", "Projekt Alpha", string.Join(" ", Enumerable.Repeat("sehr langer Text", 120))),
                E(5, "2026-10-05", "13:00", "14:00", "Projekt Alpha", "Zeile 1\nZeile 2\r\nZeile 3\u0007\u0000")
            };
            var meta = Meta();
            meta.Note = "Bemerkung mit\nZeilenumbruch und Umlauten: ÄÖÜ";

            var bytes = PdfExporter.ToBytes(Report(Options(), entries), meta);

            Assert.True(PdfReader.Open(new MemoryStream(bytes), PdfDocumentOpenMode.Import).PageCount >= 1);
        }

        [Fact]
        public void A_logo_is_placed_and_rendered()
        {
            var path = Path.Combine(Path.GetTempPath(), "TimeTrackTests", Guid.NewGuid().ToString("N") + ".png");
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            using (var bitmap = new System.Drawing.Bitmap(120, 40))
            {
                using (var g = System.Drawing.Graphics.FromImage(bitmap)) g.Clear(System.Drawing.Color.SteelBlue);
                bitmap.Save(path, System.Drawing.Imaging.ImageFormat.Png);
            }

            try
            {
                var meta = Meta();
                meta.LogoPath = path;
                var bytes = PdfExporter.ToBytes(Report(Options(), SomeEntries()), meta);

                Assert.True(PdfReader.Open(new MemoryStream(bytes), PdfDocumentOpenMode.Import).PageCount >= 1);
                Assert.Contains("/Subtype/Image", Encoding.GetEncoding("iso-8859-1").GetString(bytes).Replace(" ", string.Empty));
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Fact]
        public void A_missing_logo_file_is_ignored()
        {
            var meta = Meta();
            meta.LogoPath = @"C:\gibt\es\nicht.png";

            var bytes = PdfExporter.ToBytes(Report(Options(), SomeEntries()), meta);

            Assert.True(bytes.Length > 1000);
        }

        [Fact]
        public void WriteFile_creates_the_file_only_after_rendering()
        {
            var dir = Path.Combine(Path.GetTempPath(), "TimeTrackTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, "nachweis.pdf");

            PdfExporter.WriteFile(path, Report(Options(), SomeEntries()), Meta());

            Assert.True(new FileInfo(path).Length > 1000);
            Directory.Delete(dir, true);
        }

        /// <summary>
        /// Schreibt Beispieldateien zum Ansehen von Hand (Temp-Ordner <c>TimeTrackSamples</c>). Prüft nur, dass sie entstehen –
        /// ob das Layout gefällt, entscheidet ein Mensch.
        /// </summary>
        [Fact]
        public void Writes_sample_documents_for_manual_review()
        {
            var dir = Path.Combine(Path.GetTempPath(), "TimeTrackSamples");
            Directory.CreateDirectory(dir);

            var month = Enumerable.Range(0, 60)
                .Select(i => E(100 + i, new DateTime(2026, 10, 1).AddDays(i / 3).ToString("yyyy-MM-dd"), (8 + (i % 3) * 3).ToString("00") + ":00", (8 + (i % 3) * 3 + 2).ToString("00") + ":" + (15 * (i % 3)).ToString("00"),
                    i % 3 == 2 ? "Projekt Beta" : "Projekt Alpha",
                    i % 7 == 0 ? null : "Entwicklung und Test der Schnittstelle, Abstimmung mit dem Kunden, Dokumentation der Ergebnisse (Paket " + i + ")"))
                .ToArray();

            var options = Options();
            options.DurationFormat = ExportDurationFormat.Both;
            options.RoundingMinutes = 15;
            var meta = Meta();
            meta.Note = "Leistungen laut Auftrag PO-4711. Reisezeiten sind nicht enthalten.";
            PdfExporter.WriteFile(Path.Combine(dir, "01-Monat-nach-Tag.pdf"), Report(options, month), meta);

            var byProject = Options();
            byProject.Grouping = ExportGrouping.ByProject;
            byProject.ShowCarrierColumn = true;
            PdfExporter.WriteFile(Path.Combine(dir, "02-nach-Projekt.pdf"), Report(byProject, month), Meta());

            var landscape = Options();
            landscape.Grouping = ExportGrouping.ByWeek;
            var landscapeMeta = Meta();
            landscapeMeta.Orientation = ExportOrientation.Landscape;
            PdfExporter.WriteFile(Path.Combine(dir, "03-nach-Woche-quer.pdf"), Report(landscape, month), landscapeMeta);

            PdfExporter.WriteFile(Path.Combine(dir, "04-kurz.pdf"), Report(Options(), SomeEntries()), Meta());
            CsvExporter.WriteFile(Path.Combine(dir, "05-Monat.csv"), Report(Options(), month), new CsvOptions { IncludeTotalRow = true });

            Assert.True(File.Exists(Path.Combine(dir, "01-Monat-nach-Tag.pdf")));
            Assert.True(File.Exists(Path.Combine(dir, "05-Monat.csv")));
        }
    }
}
