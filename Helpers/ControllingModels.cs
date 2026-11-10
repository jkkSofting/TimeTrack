// Target: .NET Framework + C# 7.3
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Zeitmanagement.Helpers
{
    // Domänen- und Transfertypen des Projektcontrolling-Imports (SAP-Excel).
    // Bewusst frei von WPF/SQLite, damit Parser und Prüflogik ohne UI testbar sind.

    public enum ImportSeverity
    {
        Info = 0,
        Warning = 1,
        Error = 2
    }

    /// <summary>Hinweis, Warnung oder Fehler aus Import oder Vorschau, wenn möglich mit Blatt und Zelle.</summary>
    public sealed class ImportDiagnostic
    {
        public ImportDiagnostic(ImportSeverity severity, string message, string sheet = null, string cell = null)
        {
            Severity = severity;
            Message = message ?? string.Empty;
            Sheet = sheet;
            Cell = cell;
        }

        public ImportSeverity Severity { get; }
        public string Message { get; }
        public string Sheet { get; }
        /// <summary>A1-Zellbezug, z. B. "F12".</summary>
        public string Cell { get; }

        public bool IsError => Severity == ImportSeverity.Error;
        public bool IsWarning => Severity == ImportSeverity.Warning;

        /// <summary>Fundstelle für die Anzeige, z. B. "SAP#2PL!F12"; leer, wenn keine bekannt ist.</summary>
        public string Location
        {
            get
            {
                if (string.IsNullOrEmpty(Sheet)) return string.Empty;
                return string.IsNullOrEmpty(Cell) ? Sheet : Sheet + "!" + Cell;
            }
        }

        public string SeverityText
        {
            get
            {
                switch (Severity)
                {
                    case ImportSeverity.Error: return "Fehler";
                    case ImportSeverity.Warning: return "Warnung";
                    default: return "Hinweis";
                }
            }
        }

        public override string ToString()
        {
            return string.IsNullOrEmpty(Location) ? Message : Message + " (" + Location + ")";
        }
    }

    public enum TimestampOrigin
    {
        /// <summary>Stand stammt aus dem Arbeitsblatt ("SAP-Daten von").</summary>
        Workbook,
        /// <summary>Stand wurde vom Benutzer angegeben bzw. bestätigt.</summary>
        User
    }

    /// <summary>Ein Stand-Zeitstempel, wie ihn das Arbeitsblatt in einer Zelle E1 angibt.</summary>
    public sealed class SourceTimestampCandidate
    {
        public SourceTimestampCandidate(string sheet, DateTime value)
        {
            Sheet = sheet;
            Value = value;
        }

        public string Sheet { get; }
        /// <summary>Lokale Quellzeit ohne Zeitzone (Kind = Unspecified).</summary>
        public DateTime Value { get; }
    }

    /// <summary>
    /// Eine Zeile aus SAP#2PL, deren PSP-Element zu einem Kostenträger passt.
    /// Beträge sind <c>null</c>, wenn die Zelle leer war ("nicht geliefert") - das ist nicht null Euro.
    /// </summary>
    public sealed class PspCostRow
    {
        /// <summary>Normalisierter Schlüssel (siehe <see cref="CarrierKeys"/>).</summary>
        public string CarrierKey { get; set; }
        /// <summary>PSP-Element-ID, wie sie in der Datei steht (nur getrimmt).</summary>
        public string CarrierText { get; set; }
        public string ProfitCenter { get; set; }
        public string ExternalProjectId { get; set; }
        public string ExternalProjectName { get; set; }
        public string PspName { get; set; }
        public decimal? PlannedCost { get; set; }
        public decimal? ActualCost { get; set; }
        public string SourceSheet { get; set; }
        public int SourceRow { get; set; }

        /// <summary>Normalisierter Schlüssel des übergeordneten Projekts (leer, wenn keins angegeben ist).</summary>
        public string ParentKey => CarrierKeys.NormalizeOrNull(ExternalProjectId);

        public decimal? RemainingCost => CostMath.Remaining(PlannedCost, ActualCost);
        public decimal? Consumption => CostMath.Consumption(PlannedCost, ActualCost);
        public bool HasNegativeAmount => (PlannedCost ?? 0m) < 0m || (ActualCost ?? 0m) < 0m;

        /// <summary>Kanonische Darstellung aller Nutzdaten (ohne Zeilennummer) zum Vergleichen und Hashen.</summary>
        public string PayloadText()
        {
            return string.Join("|", new[]
            {
                CarrierKey ?? "",
                ProfitCenter ?? "",
                ExternalProjectId ?? "",
                ExternalProjectName ?? "",
                PspName ?? "",
                CostMath.ToInvariantText(PlannedCost) ?? "~",
                CostMath.ToInvariantText(ActualCost) ?? "~"
            });
        }
    }

    /// <summary>
    /// Kontext eines SAP-Projekts (Elternprojekt) zu mindestens einem übernommenen PSP-Element.
    /// Alle Beträge gelten für das ganze Projekt, nicht für das einzelne PSP-Element, und werden nie über
    /// dessen PSP-Elemente summiert.
    /// </summary>
    public sealed class ParentContext
    {
        public string ParentKey { get; set; }
        public string ExternalParentId { get; set; }
        public string ParentName { get; set; }
        public string Customer { get; set; }
        public string Manager { get; set; }
        public string Status { get; set; }
        public string CostCenter { get; set; }
        public DateTime? PlannedStart { get; set; }
        public DateTime? PlannedEnd { get; set; }

        public decimal? OrderValueTotal { get; set; }
        public decimal? InvoicedTotal { get; set; }
        public decimal? OpenTotal { get; set; }
        public decimal? InvoicedRatio { get; set; }
        public decimal? ProgressRatio { get; set; }
        public decimal? WipOpen { get; set; }
        public decimal? OrderValueYear { get; set; }
        public decimal? OpenYear { get; set; }

        public string PayloadText()
        {
            Func<DateTime?, string> d = v => v.HasValue ? v.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : "~";
            return string.Join("|", new[]
            {
                ParentKey ?? "", ExternalParentId ?? "", ParentName ?? "", Customer ?? "", Manager ?? "", Status ?? "", CostCenter ?? "",
                d(PlannedStart), d(PlannedEnd),
                CostMath.ToInvariantText(OrderValueTotal) ?? "~", CostMath.ToInvariantText(InvoicedTotal) ?? "~",
                CostMath.ToInvariantText(OpenTotal) ?? "~", CostMath.ToInvariantText(InvoicedRatio) ?? "~",
                CostMath.ToInvariantText(ProgressRatio) ?? "~", CostMath.ToInvariantText(WipOpen) ?? "~",
                CostMath.ToInvariantText(OrderValueYear) ?? "~", CostMath.ToInvariantText(OpenYear) ?? "~"
            });
        }
    }

    /// <summary>Ergebnis des Lesens einer Arbeitsmappe. Enthält nur Zeilen, die zu einem Kostenträger passen.</summary>
    public sealed class ControllingReadResult
    {
        /// <summary>Stand-Zeitstempel aus den Arbeitsblättern; erster Eintrag ist maßgeblich, mehrere abweichende = Konflikt.</summary>
        public List<SourceTimestampCandidate> Timestamps { get; } = new List<SourceTimestampCandidate>();

        public SourceTimestampCandidate PrimaryTimestamp => Timestamps.Count > 0 ? Timestamps[0] : null;

        /// <summary>Beide Blätter nennen einen Stand, aber unterschiedliche: vor dem Import muss einer gewählt werden.</summary>
        public bool HasTimestampConflict
        {
            get { return Timestamps.Select(t => t.Value).Distinct().Count() > 1; }
        }

        /// <summary>Übernommene Zeilen, je PSP-Element genau eine, nach Zeile in der Datei sortiert.</summary>
        public List<PspCostRow> Rows { get; } = new List<PspCostRow>();

        /// <summary>Kontext je Elternprojekt (nur für übernommene Zeilen), Schlüssel = normalisierte Projekt-ID.</summary>
        public Dictionary<string, ParentContext> Parents { get; } = new Dictionary<string, ParentContext>(StringComparer.Ordinal);

        /// <summary>
        /// Alle gültigen PSP-Elemente der Datei (normalisiert). Nur im Speicher, um fehlende Kostenträger
        /// zu erkennen; wird nie gespeichert oder angezeigt.
        /// </summary>
        public HashSet<string> ScannedKeys { get; } = new HashSet<string>(StringComparer.Ordinal);

        public int ScannedValidCount => ScannedKeys.Count;

        /// <summary>Gültige PSP-Elemente der Datei, die zu keinem Kostenträger passen (nur die Anzahl).</summary>
        public int IgnoredCount { get; set; }

        public List<ImportDiagnostic> Diagnostics { get; } = new List<ImportDiagnostic>();

        public bool HasErrors => Diagnostics.Any(d => d.IsError);
    }

    /// <summary>Grenzen gegen übergroße oder manipulierte Dateien.</summary>
    public sealed class ReaderLimits
    {
        public long MaxCompressedBytes { get; set; } = 25L * 1024 * 1024;
        public long MaxUncompressedBytes { get; set; } = 100L * 1024 * 1024;
        public int MaxRowsPerSheet { get; set; } = 50000;
    }

    /// <summary>Schwerer Fehler, der den ganzen Import verhindert (ungültige Datei, überschrittene Grenzen).</summary>
    public sealed class ControllingImportException : Exception
    {
        public ControllingImportException(string message) : base(message) { }
        public ControllingImportException(string message, Exception inner) : base(message, inner) { }
    }

    /// <summary>Lokales Projekt als Eingabe für die Zuordnung zu PSP-Elementen.</summary>
    public sealed class LocalProjectInfo
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public string Customer { get; set; }
        public string Carrier { get; set; }
        public bool IsExternal { get; set; }
    }

    // ---------- Speicher (Snapshots) ----------

    /// <summary>Eine Zeile im Entwurf eines Imports, bereit zum Speichern.</summary>
    public sealed class ControllingDraftRow
    {
        public string CarrierKey { get; set; }
        public string CarrierText { get; set; }
        public string SourceSheet { get; set; }
        public int SourceRow { get; set; }
        public string ExternalParentId { get; set; }
        public string ParentKey { get; set; }
        public string ParentName { get; set; }
        public string PspName { get; set; }
        public string ProfitCenter { get; set; }
        public decimal? PlannedCost { get; set; }
        public decimal? ActualCost { get; set; }
    }

    /// <summary>Alles, was eine bestätigte Vorschau in die Datenbank schreibt.</summary>
    public sealed class ControllingImportDraft
    {
        public string FileName { get; set; }
        public string FileHash { get; set; }
        public DateTime SourceTimestamp { get; set; }
        public TimestampOrigin TimestampOrigin { get; set; }
        /// <summary>ISO-Währungscode oder <c>null</c>, wenn nicht angegeben.</summary>
        public string CurrencyCode { get; set; }
        public int ParserVersion { get; set; } = ControllingParser.Version;
        public string AllowlistHash { get; set; }
        public string PayloadHash { get; set; }
        public int ScannedCount { get; set; }
        public int IgnoredCount { get; set; }
        /// <summary>Anzahl aller Warnungen; <see cref="Warnings"/> enthält davon höchstens die ersten 200 als Text.</summary>
        public int WarningCount { get; set; }
        public List<string> Warnings { get; } = new List<string>();
        public List<ControllingDraftRow> Rows { get; } = new List<ControllingDraftRow>();
        public List<ParentContext> Parents { get; } = new List<ParentContext>();

        /// <summary>Währungskontext: eigener Verlauf je Währung; "UNSPECIFIED", solange keine angegeben ist.</summary>
        public string CurrencyContext => CostMath.CurrencyContext(CurrencyCode);

        /// <summary>
        /// Identität des Imports: gleicher Quellstand, Währungskontext, Parserversion, Kostenträger-Hash,
        /// Dateihash und Nutzdaten-Hash. Ein identischer Import wird nicht noch einmal gespeichert.
        /// </summary>
        public string Fingerprint()
        {
            return Hashing.Sha256Hex(string.Join("|", new[]
            {
                SourceTimestamp.ToString(CostMath.TimestampFormat, CultureInfo.InvariantCulture),
                CurrencyContext,
                ParserVersion.ToString(CultureInfo.InvariantCulture),
                AllowlistHash ?? "",
                FileHash ?? "",
                PayloadHash ?? ""
            }));
        }
    }

    public enum ControllingSaveStatus
    {
        Saved,
        AlreadyImported
    }

    public sealed class ControllingSaveResult
    {
        public ControllingSaveStatus Status { get; set; }
        public int ImportId { get; set; }
    }

    /// <summary>Ein gespeicherter Import (Charge).</summary>
    public sealed class ControllingImportInfo
    {
        public int Id { get; set; }
        public string FileName { get; set; }
        public string FileHash { get; set; }
        public DateTime SourceTimestamp { get; set; }
        public TimestampOrigin TimestampOrigin { get; set; }
        public DateTime ImportedUtc { get; set; }
        public string CurrencyCode { get; set; }
        public string CurrencyContext { get; set; }
        public int ParserVersion { get; set; }
        public int ScannedCount { get; set; }
        public int MatchedCount { get; set; }
        public int IgnoredCount { get; set; }
        public int WarningCount { get; set; }
        public List<string> Warnings { get; } = new List<string>();

        public DateTime ImportedLocal => ImportedUtc.ToLocalTime();
        public string CurrencyText => CostMath.CurrencyText(CurrencyCode);

        /// <summary>Kurzbezeichnung für Auswahllisten.</summary>
        public string Label =>
            "Stand " + SourceTimestamp.ToString("dd.MM.yyyy HH:mm", CostMath.German) + " · " + FileName + " (Import #" + Id.ToString(CultureInfo.InvariantCulture) + ")";
    }

    /// <summary>Beobachtung eines PSP-Elements: Beträge zu einem Quellstand, mit Herkunft.</summary>
    public sealed class ControllingObservation
    {
        public int SnapshotId { get; set; }
        public int ImportId { get; set; }
        public string CarrierKey { get; set; }
        public string CarrierText { get; set; }
        public DateTime SourceTimestamp { get; set; }
        public DateTime ImportedUtc { get; set; }
        public string FileName { get; set; }
        public string CurrencyCode { get; set; }
        public string CurrencyContext { get; set; }
        public string SourceSheet { get; set; }
        public int SourceRow { get; set; }
        public string ExternalParentId { get; set; }
        public string ParentKey { get; set; }
        public string ParentName { get; set; }
        public string PspName { get; set; }
        public string ProfitCenter { get; set; }
        public decimal? PlannedCost { get; set; }
        public decimal? ActualCost { get; set; }

        /// <summary>Größte Import-ID für dieses PSP-Element bei diesem Quellstand und Währungskontext.</summary>
        public int ActiveImportId { get; set; }
        /// <summary>Wie viele Importe (Revisionen) es für diesen Quellstand gibt.</summary>
        public int RevisionCount { get; set; }

        public bool IsActive => ImportId == ActiveImportId;
        public decimal? RemainingCost => CostMath.Remaining(PlannedCost, ActualCost);
        public decimal? Consumption => CostMath.Consumption(PlannedCost, ActualCost);
    }

    /// <summary>Gespeicherter Kontext eines SAP-Projekts zu einem Import.</summary>
    public sealed class ControllingParentContextRow
    {
        public int ImportId { get; set; }
        public ParentContext Context { get; set; }
    }

    // ---------- Rechenregeln ----------

    /// <summary>
    /// Rechenregeln für Kosten (Geldbeträge). Fehlende Werte bleiben fehlend:
    /// es wird nie mit 0 aufgefüllt und nichts in Stunden umgerechnet.
    /// </summary>
    public static class CostMath
    {
        public static readonly CultureInfo German = CultureInfo.GetCultureInfo("de-DE");

        /// <summary>Format für Quellstände in der Datenbank: lokale Quellzeit ohne Zeitzone.</summary>
        public const string TimestampFormat = "yyyy-MM-dd'T'HH:mm:ss";
        public const string UtcFormat = "yyyy-MM-dd'T'HH:mm:ss'Z'";

        public const string MissingText = "Nicht geliefert";
        public const string NoBudgetText = "Kein positives Budget";
        public const string UnspecifiedCurrency = "UNSPECIFIED";

        /// <summary>Plan minus Ist; nur wenn beide bekannt sind. Negativ = Überschreitung und wird nicht auf 0 gekappt.</summary>
        public static decimal? Remaining(decimal? plan, decimal? actual)
        {
            if (plan.HasValue && actual.HasValue) return plan.Value - actual.Value;
            return null;
        }

        /// <summary>Budgetverbrauch als Anteil (1 = 100 %); nur bei streng positivem, bekanntem Plan.</summary>
        public static decimal? Consumption(decimal? plan, decimal? actual)
        {
            if (plan.HasValue && actual.HasValue && plan.Value > 0m) return actual.Value / plan.Value;
            return null;
        }

        public static bool IsOverrun(decimal? plan, decimal? actual)
        {
            return plan.HasValue && actual.HasValue && plan.Value > 0m && actual.Value > plan.Value;
        }

        /// <summary>Differenz zweier Beobachtungen (aktuell minus vorher); nur wenn beide bekannt sind.</summary>
        public static decimal? Delta(decimal? previous, decimal? current)
        {
            if (previous.HasValue && current.HasValue) return current.Value - previous.Value;
            return null;
        }

        /// <summary>Exakter, kulturunabhängiger Text für Speicherung und Hash; <c>null</c> bleibt <c>null</c>.</summary>
        public static string ToInvariantText(decimal? value)
        {
            if (!value.HasValue) return null;
            var v = value.Value;
            if (v == 0m) return "0";
            return v.ToString("0.############################", CultureInfo.InvariantCulture);
        }

        public static decimal? ParseInvariant(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;
            decimal v;
            if (decimal.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out v)) return v;
            return null;
        }

        public static string CurrencyContext(string currencyCode)
        {
            return string.IsNullOrWhiteSpace(currencyCode) ? UnspecifiedCurrency : currencyCode.Trim().ToUpperInvariant();
        }

        public static string CurrencyText(string currencyCode)
        {
            return string.IsNullOrWhiteSpace(currencyCode)
                ? "Buchungskreiswährung (nicht angegeben)"
                : currencyCode.Trim().ToUpperInvariant();
        }

        // ----- Umrechnung Geld -> Stunden über einen vom Benutzer eingetragenen Stundensatz -----

        /// <summary>Größter erlaubter Stundensatz (schützt vor Tippfehlern wie 95000).</summary>
        public const decimal MaxHourlyRate = 100000m;

        /// <summary>
        /// Rechnerische Stunden: Betrag ÷ Stundensatz. Nur mit bekanntem Betrag und positivem Satz; ein negativer Betrag
        /// (Überschreitung, Gutschrift) bleibt negativ. Das ist eine Schätzung aus Geld, keine gebuchte Zeit.
        /// </summary>
        public static decimal? ToHours(decimal? amount, decimal? hourlyRate)
        {
            if (!amount.HasValue || !hourlyRate.HasValue || hourlyRate.Value <= 0m) return null;
            return amount.Value / hourlyRate.Value;
        }

        /// <summary>Rechnerische Stunden als Text ("833,33 h"); fehlender Betrag = "Nicht geliefert", ohne Satz "–".</summary>
        public static string FormatHours(decimal? amount, decimal? hourlyRate)
        {
            if (!hourlyRate.HasValue || hourlyRate.Value <= 0m) return "–";
            var hours = ToHours(amount, hourlyRate);
            return hours.HasValue ? hours.Value.ToString("N2", German) + " h" : MissingText;
        }

        public static string FormatRate(decimal? rate)
        {
            return rate.HasValue ? rate.Value.ToString("N2", German) : string.Empty;
        }

        /// <summary>
        /// Liest einen Stundensatz wie "95,50", "95.50" oder "1.234,56". Leer ist gültig und bedeutet "kein Satz" (<paramref name="rate"/> = null).
        /// Liefert <c>false</c> bei Unlesbarem, Null, Negativem oder mehr als <see cref="MaxHourlyRate"/>.
        /// </summary>
        public static bool TryParseHourlyRate(string text, out decimal? rate)
        {
            rate = null;
            var t = (text ?? string.Empty).Replace("\u00A0", string.Empty).Replace(" ", string.Empty).Trim();
            if (t.Length == 0) return true;

            var lastComma = t.LastIndexOf(',');
            var lastDot = t.LastIndexOf('.');
            char? decimalSeparator = null;
            if (lastComma >= 0 && lastDot >= 0) decimalSeparator = lastComma > lastDot ? ',' : '.';
            else if (lastComma >= 0) decimalSeparator = ',';
            else if (lastDot >= 0 && t.IndexOf('.') == lastDot) decimalSeparator = '.';   // genau ein Punkt = Dezimalpunkt

            var normalized = new System.Text.StringBuilder();
            for (var i = 0; i < t.Length; i++)
            {
                var c = t[i];
                if (char.IsDigit(c)) normalized.Append(c);
                else if ((c == ',' || c == '.') && decimalSeparator == c && i == (c == ',' ? lastComma : lastDot)) normalized.Append('.');
                else if ((c == ',' || c == '.') && decimalSeparator != c) continue;   // Tausendertrenner
                else return false;
            }

            decimal value;
            if (!decimal.TryParse(normalized.ToString(), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out value)) return false;
            if (value <= 0m || value > MaxHourlyRate) return false;

            rate = value;
            return true;
        }

        /// <summary>Betrag mit zwei Nachkommastellen; Währung wird nicht angehängt. Fehlend = "Nicht geliefert".</summary>
        public static string FormatMoney(decimal? value)
        {
            return value.HasValue ? value.Value.ToString("N2", German) : MissingText;
        }

        public static string FormatSignedMoney(decimal? value)
        {
            if (!value.HasValue) return "–";
            return (value.Value > 0m ? "+" : "") + value.Value.ToString("N2", German);
        }

        public static string FormatPercent(decimal? ratio)
        {
            return ratio.HasValue ? (ratio.Value * 100m).ToString("N1", German) + " %" : null;
        }
    }
}
