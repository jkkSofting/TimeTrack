// Target: .NET Framework + C# 7.3
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using MigraDoc.DocumentObjectModel;
using MigraDoc.DocumentObjectModel.Tables;

namespace Zeitmanagement.Helpers
{
    public enum ExportOrientation
    {
        Portrait,
        Landscape
    }

    /// <summary>Alles, was im Nachweis steht und nicht aus den Buchungen kommt: Absender, Empfänger, Nummer, Bemerkung, Optik.</summary>
    public sealed class ExportMeta
    {
        public string Title { get; set; }
        /// <summary>Auftragnehmer, mehrzeilig (erste Zeile fett): Name, Firma, Anschrift, Kontakt.</summary>
        public string IssuerBlock { get; set; }
        /// <summary>Auftraggeber, erste Zeile fett (meist der Kunde des Projekts).</summary>
        public string ClientName { get; set; }
        /// <summary>Weitere Zeilen zum Auftraggeber: Anschrift, Ansprechpartner.</summary>
        public string ClientBlock { get; set; }
        /// <summary>Auftrags- oder Bestellnummer.</summary>
        public string ReferenceNumber { get; set; }
        public string Note { get; set; }
        public string LogoPath { get; set; }
        /// <summary>"Erstellt am … mit TimeTrack …" in der Fußzeile.</summary>
        public bool ShowBranding { get; set; }
        public DateTime CreatedAt { get; set; }
        public string AppVersion { get; set; }
        public ExportOrientation Orientation { get; set; }

        public ExportMeta()
        {
            ShowBranding = true;
            CreatedAt = DateTime.Now;
        }

        /// <summary>Erste nicht leere Zeile des Auftragnehmer-Blocks (der Name), sonst leer.</summary>
        public string IssuerName
        {
            get
            {
                return (IssuerBlock ?? string.Empty)
                    .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(l => l.Trim())
                    .FirstOrDefault(l => l.Length > 0) ?? string.Empty;
            }
        }
    }

    /// <summary>
    /// Baut den Tätigkeitsnachweis als MigraDoc-Dokument (A4, ruhiges Geschäftsdokument). Das Dokumentmodell ist unabhängig vom
    /// Rendern prüfbar; <see cref="PdfExporter"/> macht daraus die PDF-Datei.
    ///
    /// Aufbau: Kopf mit Titel und Zeitraum · Auftragnehmer/Auftraggeber · Eckdaten · Zusammenfassung (· Stunden je Projekt) ·
    /// Einzelnachweis mit Zwischen- und Gesamtsumme · Hinweise · Bemerkung. Die Fußzeile steht auf jeder Seite.
    /// </summary>
    public static class PdfReportLayout
    {
        private static readonly Color Ink = Color.FromRgb(0x1B, 0x24, 0x30);
        private static readonly Color Muted = Color.FromRgb(0x5B, 0x66, 0x75);
        private static readonly Color Accent = Color.FromRgb(0x1F, 0x3A, 0x5F);
        private static readonly Color Soft = Color.FromRgb(0xF1, 0xF4, 0xF8);
        private static readonly Color Hair = Color.FromRgb(0xD9, 0xDE, 0xE5);
        private static readonly Color White = Color.FromRgb(0xFF, 0xFF, 0xFF);

        private const double MarginSideMm = 20;
        private const double MarginTopMm = 20;
        private const double MarginBottomMm = 22;

        private static string _fontName;

        public static Document Build(ExportReport report, ExportMeta meta)
        {
            if (report == null) throw new ArgumentNullException(nameof(report));
            meta = meta ?? new ExportMeta();
            var options = report.Options ?? new ExportOptions();
            var font = FontName();

            var document = new Document();
            document.Info.Title = (string.IsNullOrWhiteSpace(meta.Title) ? "Tätigkeitsnachweis" : meta.Title.Trim()) + " " + Period(report);
            document.Info.Author = meta.IssuerName;
            document.Info.Subject = ProjectsText(report);
            document.Info.Keywords = "Tätigkeitsnachweis";

            var normal = document.Styles["Normal"];
            normal.Font.Name = font;
            normal.Font.Size = 9;
            normal.Font.Color = Ink;
            normal.ParagraphFormat.SpaceAfter = 0;

            var section = document.AddSection();
            var page = section.PageSetup;
            page.PageFormat = PageFormat.A4;
            page.Orientation = meta.Orientation == ExportOrientation.Landscape ? Orientation.Landscape : Orientation.Portrait;
            page.LeftMargin = Unit.FromMillimeter(MarginSideMm);
            page.RightMargin = Unit.FromMillimeter(MarginSideMm);
            page.TopMargin = Unit.FromMillimeter(MarginTopMm);
            page.BottomMargin = Unit.FromMillimeter(MarginBottomMm);
            page.FooterDistance = Unit.FromMillimeter(10);

            var width = (meta.Orientation == ExportOrientation.Landscape ? 297.0 : 210.0) - 2 * MarginSideMm;

            AddFooter(section, report, meta, width);
            AddTitle(section, report, meta, width);
            AddParties(section, meta, width);
            AddFacts(section, report, meta, width);
            if (options.ShowSummary) AddSummary(section, report, options, width);
            if (report.Projects.Count > 1) AddProjectTable(section, report, options, width);
            AddDetails(section, report, options, width);
            AddNotes(section, report);
            AddRemark(section, meta);
            return document;
        }

        // ---------- Kopf, Parteien, Eckdaten ----------

        private static void AddTitle(Section section, ExportReport report, ExportMeta meta, double width)
        {
            var hasLogo = !string.IsNullOrWhiteSpace(meta.LogoPath) && File.Exists(meta.LogoPath);
            var table = section.AddTable();
            table.Borders.Visible = false;
            table.AddColumn(Unit.FromMillimeter(hasLogo ? width - 50 : width));
            if (hasLogo) table.AddColumn(Unit.FromMillimeter(50));

            var row = table.AddRow();
            row.VerticalAlignment = VerticalAlignment.Bottom;
            row.Borders.Bottom.Width = 1.5;
            row.Borders.Bottom.Color = Accent;

            var title = row.Cells[0].AddParagraph(string.IsNullOrWhiteSpace(meta.Title) ? "Tätigkeitsnachweis" : Clean(meta.Title).Trim());
            title.Format.Font.Size = 22;
            title.Format.Font.Bold = true;
            title.Format.Font.Color = Accent;
            title.Format.SpaceAfter = 2;
            var period = row.Cells[0].AddParagraph(Period(report));
            period.Format.Font.Size = 11;
            period.Format.Font.Color = Muted;
            period.Format.SpaceAfter = 6;

            if (hasLogo)
            {
                var logo = row.Cells[1].AddParagraph();
                logo.Format.Alignment = ParagraphAlignment.Right;
                logo.Format.SpaceAfter = 6;
                var image = logo.AddImage(meta.LogoPath);
                image.LockAspectRatio = true;
                image.Height = Unit.FromMillimeter(14);
            }

            Spacer(section, 10);
        }

        private static void AddParties(Section section, ExportMeta meta, double width)
        {
            var client = JoinLines(meta.ClientName, meta.ClientBlock);
            var issuer = Clean(meta.IssuerBlock).Trim();
            if (issuer.Length == 0 && client.Length == 0) return;

            var half = (width - 10) / 2;
            var table = section.AddTable();
            table.Borders.Visible = false;
            table.AddColumn(Unit.FromMillimeter(half));
            table.AddColumn(Unit.FromMillimeter(10));
            table.AddColumn(Unit.FromMillimeter(half));
            var row = table.AddRow();
            row.VerticalAlignment = VerticalAlignment.Top;
            FillParty(row.Cells[0], "AUFTRAGNEHMER", issuer);
            FillParty(row.Cells[2], "AUFTRAGGEBER", client);
            Spacer(section, 12);
        }

        private static void FillParty(Cell cell, string label, string block)
        {
            if (block.Length == 0) return;
            var caption = cell.AddParagraph(label);
            caption.Format.Font.Size = 7;
            caption.Format.Font.Bold = true;
            caption.Format.Font.Color = Muted;
            caption.Format.SpaceAfter = 3;

            var lines = block.Split('\n');
            for (var i = 0; i < lines.Length; i++)
            {
                var p = cell.AddParagraph(lines[i].TrimEnd());
                p.Format.Font.Bold = i == 0;
                p.Format.SpaceAfter = 1;
            }
        }

        private static void AddFacts(Section section, ExportReport report, ExportMeta meta, double width)
        {
            var facts = new List<KeyValuePair<string, string>>();
            AddFact(facts, report.Projects.Count > 1 ? "Projekte" : "Projekt", ProjectsText(report));
            AddFact(facts, "Kostenträger", string.Join(", ", report.Projects.Select(p => p.Carrier).Where(c => !string.IsNullOrWhiteSpace(c)).Distinct()));
            AddFact(facts, "Auftrags-/Bestellnummer", Clean(meta.ReferenceNumber).Trim());
            AddFact(facts, "Leistungszeitraum", Period(report));
            AddFact(facts, "Erstellt am", ExportFormat.Date(meta.CreatedAt));
            if (facts.Count == 0) return;

            var table = section.AddTable();
            table.Borders.Visible = false;
            table.AddColumn(Unit.FromMillimeter(46));
            table.AddColumn(Unit.FromMillimeter(width - 46));
            foreach (var fact in facts)
            {
                var row = table.AddRow();
                row.Borders.Bottom.Width = 0.5;
                row.Borders.Bottom.Color = Hair;
                row.Cells[0].AddParagraph(fact.Key).Format.Font.Color = Muted;
                row.Cells[1].AddParagraph(fact.Value);
            }
            table.TopPadding = 3;
            table.BottomPadding = 3;
            Spacer(section, 12);
        }

        private static void AddFact(List<KeyValuePair<string, string>> facts, string key, string value)
        {
            if (!string.IsNullOrWhiteSpace(value)) facts.Add(new KeyValuePair<string, string>(key, Clean(value).Replace('\n', ' ')));
        }

        // ---------- Zusammenfassung ----------

        private static void AddSummary(Section section, ExportReport report, ExportOptions options, double width)
        {
            Heading(section, "Zusammenfassung", 0);

            var table = section.AddTable();
            table.Borders.Visible = false;
            for (var i = 0; i < 4; i++) table.AddColumn(Unit.FromMillimeter(width / 4));
            var row = table.AddRow();
            row.Shading.Color = Soft;
            var total = report.TotalMinutes;
            var average = report.AverageMinutesPerWorkDay;
            var clockPrimary = options.DurationFormat == ExportDurationFormat.Clock;

            Kpi(row.Cells[0], "GESAMTZEIT", clockPrimary ? ExportFormat.Clock(total) + " h" : ExportFormat.Decimal(total) + " h",
                clockPrimary ? ExportFormat.Decimal(total) + " h" : ExportFormat.Clock(total) + " h");
            Kpi(row.Cells[1], "BUCHUNGEN", report.EntryCount.ToString(CultureInfo.InvariantCulture), null);
            Kpi(row.Cells[2], "ARBEITSTAGE", report.WorkDays.ToString(CultureInfo.InvariantCulture), null);
            Kpi(row.Cells[3], "Ø PRO ARBEITSTAG", clockPrimary ? ExportFormat.Clock(average) + " h" : ExportFormat.Decimal(average) + " h",
                clockPrimary ? ExportFormat.Decimal(average) + " h" : ExportFormat.Clock(average) + " h");
            for (var i = 1; i < 4; i++)
            {
                row.Cells[i].Borders.Left.Width = 3;
                row.Cells[i].Borders.Left.Color = White;
            }
            table.TopPadding = 6;
            table.BottomPadding = 6;
            table.LeftPadding = 8;
            Spacer(section, 12);
        }

        private static void Kpi(Cell cell, string label, string value, string secondary)
        {
            var l = cell.AddParagraph(label);
            l.Format.Font.Size = 7;
            l.Format.Font.Bold = true;
            l.Format.Font.Color = Muted;
            l.Format.SpaceAfter = 2;
            var v = cell.AddParagraph(value);
            v.Format.Font.Size = 15;
            v.Format.Font.Bold = true;
            v.Format.Font.Color = Accent;
            if (secondary != null)
            {
                var s = cell.AddParagraph(secondary);
                s.Format.Font.Size = 8;
                s.Format.Font.Color = Muted;
            }
        }

        private static void AddProjectTable(Section section, ExportReport report, ExportOptions options, double width)
        {
            Heading(section, "Stunden je Projekt", 0);

            // Der Kostenträger steht hier immer: Bei mehreren Projekten nennen die Eckdaten oben nur die Liste der Kostenträger,
            // erst diese Tabelle ordnet jedem Projekt seinen Kostenträger zu.
            var table = NewTable(section);
            const double CarrierMm = 34;
            table.AddColumn(Unit.FromMillimeter(width - (CarrierMm + 22 + 26 + 18)));
            table.AddColumn(Unit.FromMillimeter(CarrierMm));
            table.AddColumn(Unit.FromMillimeter(22));
            table.AddColumn(Unit.FromMillimeter(26));
            table.AddColumn(Unit.FromMillimeter(18));

            HeaderRow(table.AddRow(), new List<string> { "Projekt", "Kostenträger", "Buchungen", "Stunden", "Anteil" }, 2);

            foreach (var p in report.Projects)
            {
                var row = table.AddRow();
                row.Borders.Bottom.Width = 0.5;
                row.Borders.Bottom.Color = Hair;
                row.Cells[0].AddParagraph(Clean(p.Project));
                row.Cells[1].AddParagraph(Clean(p.Carrier));
                Right(row.Cells[2].AddParagraph(p.Count.ToString(CultureInfo.InvariantCulture)));
                Right(row.Cells[3].AddParagraph(ExportFormat.Duration(p.Minutes, options.DurationFormat)));
                Right(row.Cells[4].AddParagraph(p.SharePercent.ToString("0", ExportFormat.German) + " %"));
            }

            var sum = table.AddRow();
            sum.Shading.Color = Soft;
            sum.Format.Font.Bold = true;
            sum.Cells[0].AddParagraph("Gesamt");
            Right(sum.Cells[2].AddParagraph(report.EntryCount.ToString(CultureInfo.InvariantCulture)));
            Right(sum.Cells[3].AddParagraph(ExportFormat.Duration(report.TotalMinutes, options.DurationFormat)));
            Right(sum.Cells[4].AddParagraph(report.TotalMinutes > 0 ? "100 %" : "–"));
            Spacer(section, 12);
        }

        // ---------- Einzelnachweis ----------

        private sealed class Col
        {
            public string Key;
            public string Header;
            public double Mm;
            public bool Right;
        }

        private static void AddDetails(Section section, ExportReport report, ExportOptions options, double width)
        {
            Heading(section, "Einzelnachweis", 0);

            if (report.IsEmpty)
            {
                var none = section.AddParagraph("Im gewählten Zeitraum liegen keine Buchungen vor.");
                none.Format.Font.Color = Muted;
                none.Format.SpaceAfter = 10;
                return;
            }

            var byProject = options.Grouping == ExportGrouping.ByProject;
            var columns = new List<Col> { new Col { Key = "date", Header = "Datum", Mm = 27 } };
            if (options.ShowTimes) columns.Add(new Col { Key = "time", Header = "Zeit", Mm = 26 });
            columns.Add(new Col
            {
                Key = "dur",
                Header = options.DurationFormat == ExportDurationFormat.Clock ? "Dauer (h:mm)" : "Dauer (h)",
                Mm = options.DurationFormat == ExportDurationFormat.Both ? 30 : 22,
                Right = true
            });
            if (report.ShowProjectColumn && !byProject) columns.Add(new Col { Key = "project", Header = "Projekt", Mm = 32 });
            if (options.ShowCarrierColumn) columns.Add(new Col { Key = "carrier", Header = "Kostenträger", Mm = 30 });
            // Die Tätigkeit bekommt den Rest, mindestens aber 40 mm. Wird es dafür zu eng (Hochformat mit allen Spalten), werden nur
            // Projekt und Kostenträger schmaler (Namen dürfen umbrechen); Datum, Zeit und Dauer behalten ihre Breite, damit sie nie
            // umbrechen. So ragt die Tabelle nie über den Satzspiegel hinaus.
            const double MinTextMm = 40;
            const double MinFlexMm = 20;
            var flexible = columns.Where(c => c.Key == "project" || c.Key == "carrier").ToList();
            var fixedMm = columns.Where(c => !flexible.Contains(c)).Sum(c => c.Mm);
            var flexMm = flexible.Sum(c => c.Mm);
            if (flexMm > 0 && width - fixedMm - flexMm < MinTextMm)
            {
                var target = Math.Max(MinFlexMm * flexible.Count, width - fixedMm - MinTextMm);
                var factor = target / flexMm;
                foreach (var c in flexible) c.Mm *= factor;
                flexMm = flexible.Sum(c => c.Mm);
            }
            columns.Add(new Col { Key = "text", Header = "Tätigkeit", Mm = Math.Max(MinTextMm, width - fixedMm - flexMm) });

            var table = NewTable(section);
            foreach (var c in columns) table.AddColumn(Unit.FromMillimeter(c.Mm));
            var durIndex = columns.FindIndex(c => c.Key == "dur");

            var header = table.AddRow();
            HeaderRow(header, columns.Select(c => c.Header).ToList(), durIndex);

            Row last = null;
            foreach (var group in report.Groups)
            {
                if (options.Grouping != ExportGrouping.ByDay)
                {
                    var groupRow = table.AddRow();
                    groupRow.Shading.Color = Soft;
                    groupRow.KeepWith = 1;
                    groupRow.Cells[0].MergeRight = columns.Count - 1;
                    var p = groupRow.Cells[0].AddParagraph();
                    p.Format.Font.Bold = true;
                    p.AddText(Clean(group.Title));
                    if (!string.IsNullOrWhiteSpace(group.Subtitle))
                    {
                        var sub = p.AddFormattedText("   " + Clean(group.Subtitle));
                        sub.Bold = false;
                        sub.Color = Muted;
                    }
                    last = groupRow;
                }

                for (var i = 0; i < group.Lines.Count; i++)
                {
                    var line = group.Lines[i];
                    var row = table.AddRow();
                    row.Borders.Bottom.Width = 0.5;
                    row.Borders.Bottom.Color = Hair;
                    // Das Datum steht in jeder Zeile: Bei "Nach Tag" in den Folgezeilen eines Tages gedämpft, damit auch nach einem
                    // Seitenumbruch klar ist, zu welchem Tag eine Zeile gehört.
                    var dimDate = options.Grouping == ExportGrouping.ByDay && i > 0;
                    for (var c = 0; c < columns.Count; c++)
                    {
                        FillCell(row.Cells[c], columns[c], line, dimDate, options);
                    }
                    last = row;
                }

                // Bei "Nach Tag" ist die Tagessumme wählbar und steht dann unter jedem Tag, auch bei nur einer Buchung: Sonst läse sich
                // die Summe unter einem Tag mit mehreren Buchungen wie eine Summe über alle Zeilen darüber, also auch über Tage ohne
                // eigene Summe. Die Zwischensummen je Projekt und je Woche gehören zur jeweiligen Gliederung.
                var withSubtotal = options.Grouping != ExportGrouping.ByDay || options.ShowDaySubtotals;
                if (withSubtotal)
                {
                    if (last != null) last.KeepWith = 1;
                    var label = options.Grouping == ExportGrouping.ByDay ? "Tagessumme" : "Summe " + Clean(group.Title);
                    var shortLabel = options.Grouping == ExportGrouping.ByDay ? "Tagessumme" : "Summe";   // wenn die Beschriftung nur eine Spalte hat
                    last = SumRow(table, columns.Count, durIndex, label, shortLabel, ExportFormat.Duration(group.Minutes, options.DurationFormat), false);
                }
            }

            if (last != null) last.KeepWith = 1;
            SumRow(table, columns.Count, durIndex, "Gesamt", "Gesamt", ExportFormat.Duration(report.TotalMinutes, options.DurationFormat), true);
            Spacer(section, 10);
        }

        private static void FillCell(Cell cell, Col column, ExportLine line, bool dimDate, ExportOptions options)
        {
            switch (column.Key)
            {
                case "date":
                    var date = cell.AddParagraph(line.WeekdayText + " " + line.DateText);
                    if (dimDate) date.Format.Font.Color = Muted;
                    break;
                case "time":
                    cell.AddParagraph(line.Start + " – " + line.End);
                    break;
                case "dur":
                    Right(cell.AddParagraph(ExportFormat.Duration(line.Minutes, options.DurationFormat)));
                    break;
                case "project":
                    cell.AddParagraph(Clean(line.Project));
                    break;
                case "carrier":
                    cell.AddParagraph(Clean(line.Carrier));
                    break;
                default:
                    var text = cell.AddParagraph();
                    var description = Clean(line.Description).Trim();
                    if (description.Length == 0)
                    {
                        text.Format.Font.Color = Muted;
                        text.AddText("–");
                    }
                    else
                    {
                        AddLines(text, description);
                    }
                    break;
            }
        }

        /// <summary>Zwischen- oder Gesamtsumme: Beschriftung rechtsbündig über den Spalten vor der Dauer, die Dauer in ihrer Spalte.</summary>
        private static Row SumRow(Table table, int columnCount, int durIndex, string label, string shortLabel, string duration, bool grand)
        {
            var row = table.AddRow();
            row.Format.Font.Bold = true;
            if (grand)
            {
                row.Shading.Color = Accent;
                row.Format.Font.Color = White;
            }
            else
            {
                row.Shading.Color = Soft;
                row.Borders.Bottom.Width = 0.5;
                row.Borders.Bottom.Color = Hair;
            }

            // Ohne Uhrzeiten-Spalte hat die Beschriftung nur die schmale Datumsspalte: dort passt nur die Kurzform.
            var labelCell = row.Cells[0];
            if (durIndex > 1)
            {
                labelCell.MergeRight = durIndex - 1;
                labelCell.Format.Alignment = ParagraphAlignment.Right;
            }
            labelCell.AddParagraph(durIndex > 1 ? label : shortLabel);
            Right(row.Cells[durIndex].AddParagraph(duration));
            if (durIndex < columnCount - 1) row.Cells[durIndex + 1].MergeRight = columnCount - durIndex - 2;
            return row;
        }

        // ---------- Hinweise und Bemerkung ----------

        private static void AddNotes(Section section, ExportReport report)
        {
            if (report.Notes.Count == 0) return;
            var title = section.AddParagraph("Hinweise");
            title.Format.Font.Size = 8;
            title.Format.Font.Bold = true;
            title.Format.Font.Color = Muted;
            title.Format.SpaceAfter = 2;
            title.Format.KeepWithNext = true;
            foreach (var note in report.Notes)
            {
                var p = section.AddParagraph("• " + Clean(note));
                p.Format.Font.Size = 8;
                p.Format.Font.Color = Muted;
                p.Format.LeftIndent = Unit.FromMillimeter(3);
                p.Format.FirstLineIndent = Unit.FromMillimeter(-3);
                p.Format.SpaceAfter = 1;
            }
            Spacer(section, 8);
        }

        private static void AddRemark(Section section, ExportMeta meta)
        {
            var note = Clean(meta.Note).Trim();
            if (note.Length == 0) return;
            var title = section.AddParagraph("Bemerkung");
            title.Format.Font.Size = 8;
            title.Format.Font.Bold = true;
            title.Format.Font.Color = Muted;
            title.Format.SpaceAfter = 2;
            title.Format.KeepWithNext = true;
            var p = section.AddParagraph();
            AddLines(p, note);
            Spacer(section, 8);
        }

        private static void AddFooter(Section section, ExportReport report, ExportMeta meta, double width)
        {
            var footer = section.Footers.Primary;
            var line = footer.AddParagraph();
            line.Format.Font.Size = 8;
            line.Format.Font.Color = Muted;
            line.Format.Borders.Top.Width = 0.5;
            line.Format.Borders.Top.Color = Hair;
            line.Format.TabStops.AddTabStop(Unit.FromMillimeter(width), TabAlignment.Right);
            var left = new[] { meta.IssuerName, (string.IsNullOrWhiteSpace(meta.Title) ? "Tätigkeitsnachweis" : Clean(meta.Title).Trim()) + " " + Period(report) }
                .Where(s => !string.IsNullOrWhiteSpace(s));
            line.AddText(string.Join(" · ", left));
            line.AddTab();
            line.AddText("Seite ");
            line.AddPageField();
            line.AddText(" von ");
            line.AddNumPagesField();

            if (meta.ShowBranding)
            {
                var brand = footer.AddParagraph("Erstellt am " + ExportFormat.Date(meta.CreatedAt) + " mit TimeTrack" +
                                                (string.IsNullOrWhiteSpace(meta.AppVersion) ? string.Empty : " " + meta.AppVersion.Trim()));
                brand.Format.Font.Size = 7;
                brand.Format.Font.Color = Muted;
            }
        }

        // ---------- Bausteine ----------

        private static Table NewTable(Section section)
        {
            var table = section.AddTable();
            table.Borders.Visible = false;
            table.TopPadding = 3;
            table.BottomPadding = 3;
            table.LeftPadding = 4;
            table.RightPadding = 4;
            return table;
        }

        /// <summary>Kopfzeile einer Tabelle; wiederholt sich auf jeder Seite. <paramref name="rightFrom"/>: ab dieser Spalte rechtsbündig.</summary>
        private static void HeaderRow(Row row, IList<string> headers, int rightFrom)
        {
            row.HeadingFormat = true;
            row.Shading.Color = Soft;
            row.Format.Font.Bold = true;
            row.Format.Font.Size = 8;
            row.Borders.Bottom.Width = 0.75;
            row.Borders.Bottom.Color = Ink;
            for (var i = 0; i < headers.Count; i++)
            {
                var p = row.Cells[i].AddParagraph(headers[i]);
                if (i == rightFrom || (rightFrom >= 0 && i > rightFrom && IsNumericHeader(headers[i]))) Right(p);
            }
        }

        private static bool IsNumericHeader(string header)
        {
            return header == "Buchungen" || header == "Stunden" || header == "Anteil";
        }

        private static void Heading(Section section, string text, double spaceBeforePt)
        {
            var p = section.AddParagraph(text);
            p.Format.Font.Size = 11;
            p.Format.Font.Bold = true;
            p.Format.Font.Color = Accent;
            p.Format.SpaceBefore = spaceBeforePt;
            p.Format.SpaceAfter = 5;
            p.Format.KeepWithNext = true;
        }

        private static void Spacer(Section section, double points)
        {
            var p = section.AddParagraph();
            p.Format.Font.Size = 1;
            p.Format.SpaceAfter = points;
        }

        private static void Right(Paragraph paragraph)
        {
            paragraph.Format.Alignment = ParagraphAlignment.Right;
        }

        // ---------- Text ----------

        /// <summary>Schreibt Text mit Zeilenumbrüchen als Zeilenumbruch in den Absatz.</summary>
        private static void AddLines(Paragraph paragraph, string text)
        {
            var lines = Clean(text).Split('\n');
            for (var i = 0; i < lines.Length; i++)
            {
                if (i > 0) paragraph.AddLineBreak();
                paragraph.AddText(lines[i]);
            }
        }

        /// <summary>Vereinheitlicht Zeilenumbrüche zu "\n" und ersetzt sonstige Steuerzeichen durch Leerzeichen.</summary>
        public static string Clean(string text)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;
            var sb = new StringBuilder(text.Length);
            for (var i = 0; i < text.Length; i++)
            {
                var c = text[i];
                if (c == '\r')
                {
                    if (i + 1 < text.Length && text[i + 1] == '\n') i++;
                    sb.Append('\n');
                }
                else if (c == '\n') sb.Append('\n');
                else if (char.IsControl(c)) sb.Append(' ');
                else sb.Append(c);
            }
            return sb.ToString();
        }

        private static string JoinLines(string first, string rest)
        {
            var parts = new[] { Clean(first).Trim(), Clean(rest).Trim() }.Where(s => s.Length > 0);
            return string.Join("\n", parts);
        }

        private static string Period(ExportReport report)
        {
            var o = report.Options;
            return o == null ? string.Empty : ExportFormat.Period(o.From, o.To);
        }

        private static string ProjectsText(ExportReport report)
        {
            var names = report.Projects.Select(p => Clean(p.Project)).Where(n => n.Length > 0).ToList();
            if (names.Count <= 4) return string.Join(", ", names);
            return names.Count + " Projekte (siehe Übersicht)";
        }

        /// <summary>Segoe UI, wenn installiert (Windows-Standard), sonst Arial.</summary>
        private static string FontName()
        {
            if (_fontName != null) return _fontName;
            var name = "Arial";
            try
            {
                using (var fonts = new System.Drawing.Text.InstalledFontCollection())
                {
                    if (fonts.Families.Any(f => string.Equals(f.Name, "Segoe UI", StringComparison.OrdinalIgnoreCase))) name = "Segoe UI";
                }
            }
            catch (Exception)
            {
                // Schriftenliste nicht lesbar: bei Arial bleiben.
            }
            _fontName = name;
            return name;
        }
    }
}
