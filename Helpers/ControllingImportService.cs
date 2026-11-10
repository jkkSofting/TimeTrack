// Target: .NET Framework + C# 7.3
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;

namespace Zeitmanagement.Helpers
{
    /// <summary>Eine Zeile der Importvorschau: ein passendes PSP-Element samt den lokalen Projekten, die es enthalten.</summary>
    public sealed class ControllingPreviewRow
    {
        public string CarrierKey { get; set; }
        public string CarrierText { get; set; }
        public string PspName { get; set; }
        public string ExternalParentId { get; set; }
        public string ExternalProjectName { get; set; }
        public string ProfitCenter { get; set; }
        public decimal? PlannedCost { get; set; }
        public decimal? ActualCost { get; set; }
        public string SourceSheet { get; set; }
        public int SourceRow { get; set; }
        public bool HasNegativeAmount { get; set; }
        public List<LocalProjectInfo> LinkedProjects { get; } = new List<LocalProjectInfo>();

        public string LinkedProjectNames => string.Join(", ", LinkedProjects.Select(p => p.Name));
        public decimal? RemainingCost => CostMath.Remaining(PlannedCost, ActualCost);
        public decimal? Consumption => CostMath.Consumption(PlannedCost, ActualCost);

        /// <summary>Letzter vergleichbarer Stand dieses PSP-Elements (gleicher Währungskontext, nicht neuer als dieser Import).</summary>
        public ControllingObservation Previous { get; set; }
        public decimal? PlanDelta => Previous == null ? null : CostMath.Delta(Previous.PlannedCost, PlannedCost);
        public decimal? ActualDelta => Previous == null ? null : CostMath.Delta(Previous.ActualCost, ActualCost);
        /// <summary>Gleicher Quellstand wie ein bereits importierter: Das ist eine Korrektur, kein neuer Stand.</summary>
        public bool IsCorrection { get; set; }

        public string PlannedText => CostMath.FormatMoney(PlannedCost);
        public string ActualText => CostMath.FormatMoney(ActualCost);
        public string RemainingText => CostMath.FormatMoney(RemainingCost);

        public string ComparisonText
        {
            get
            {
                if (Previous == null) return "Erster Stand";
                var label = IsCorrection ? "Korrektur zu Stand " : "Seit Stand ";
                label += Previous.SourceTimestamp.ToString("dd.MM.yyyy HH:mm", CostMath.German) + ": ";
                return label + "Plan " + CostMath.FormatSignedMoney(PlanDelta) + ", Ist " + CostMath.FormatSignedMoney(ActualDelta);
            }
        }
    }

    /// <summary>Ergebnis des Einlesens einer Datei, bevor etwas gespeichert wird.</summary>
    public sealed class ControllingImportPreview
    {
        public string FileName { get; set; }
        public string FileHash { get; set; }
        public long FileSize { get; set; }
        public string AllowlistHash { get; set; }
        public string PayloadHash { get; set; }
        public ControllingReadResult Read { get; set; }
        public List<ImportDiagnostic> Diagnostics { get; } = new List<ImportDiagnostic>();
        public List<ControllingPreviewRow> Rows { get; } = new List<ControllingPreviewRow>();

        /// <summary>Lokale Kostenträger, in denen kein PSP-Element der Datei vorkommt (Anzeigetext der ersten Schreibweise).</summary>
        public List<string> MissingCarriers { get; } = new List<string>();

        public int ScannedCount => Read == null ? 0 : Read.ScannedValidCount;
        public int IgnoredCount => Read == null ? 0 : Read.IgnoredCount;
        public int MatchedCount => Rows.Count;
        public int LinkedProjectCount { get; set; }
        public int WarningCount => Diagnostics.Count(d => d.IsWarning);
        public bool HasBlockingErrors => Diagnostics.Any(d => d.IsError);

        public DateTime? WorkbookTimestamp => Read == null || Read.PrimaryTimestamp == null ? (DateTime?)null : Read.PrimaryTimestamp.Value;
        public bool HasTimestampConflict => Read != null && Read.HasTimestampConflict;
        public bool HasNoTimestamp => Read != null && Read.Timestamps.Count == 0;

        /// <summary>Genügt für den Import, sobald ein Stand feststeht: keine Fehler und mindestens ein passendes PSP-Element.</summary>
        public bool IsImportable => !HasBlockingErrors && Rows.Count > 0;

        /// <summary>Baut den Entwurf zum Speichern. Der Stand wird von außen bestimmt (aus der Datei oder vom Benutzer).</summary>
        public ControllingImportDraft BuildDraft(DateTime sourceTimestamp, TimestampOrigin origin, string currencyCode)
        {
            if (!IsImportable) throw new InvalidOperationException("Die Vorschau enthält Fehler oder keine passenden PSP-Elemente.");

            var draft = new ControllingImportDraft
            {
                FileName = FileName,
                FileHash = FileHash,
                SourceTimestamp = DateTime.SpecifyKind(new DateTime(sourceTimestamp.Year, sourceTimestamp.Month, sourceTimestamp.Day, sourceTimestamp.Hour, sourceTimestamp.Minute, sourceTimestamp.Second), DateTimeKind.Unspecified),
                TimestampOrigin = origin,
                CurrencyCode = string.IsNullOrWhiteSpace(currencyCode) ? null : currencyCode.Trim().ToUpperInvariant(),
                AllowlistHash = AllowlistHash,
                PayloadHash = PayloadHash,
                ScannedCount = ScannedCount,
                IgnoredCount = IgnoredCount,
                WarningCount = WarningCount
            };

            foreach (var d in Diagnostics.Where(x => x.IsWarning).Take(200))
                draft.Warnings.Add(d.ToString());

            foreach (var r in Rows)
            {
                draft.Rows.Add(new ControllingDraftRow
                {
                    CarrierKey = r.CarrierKey,
                    CarrierText = r.CarrierText,
                    SourceSheet = r.SourceSheet,
                    SourceRow = r.SourceRow,
                    ExternalParentId = r.ExternalParentId,
                    ParentKey = CarrierKeys.NormalizeOrNull(r.ExternalParentId),
                    ParentName = r.ExternalProjectName,
                    PspName = r.PspName,
                    ProfitCenter = r.ProfitCenter,
                    PlannedCost = r.PlannedCost,
                    ActualCost = r.ActualCost
                });
            }

            var usedParents = new HashSet<string>(draft.Rows.Select(r => r.ParentKey).Where(k => k != null), StringComparer.Ordinal);
            foreach (var parent in Read.Parents.Values.Where(p => usedParents.Contains(p.ParentKey)).OrderBy(p => p.ParentKey, StringComparer.Ordinal))
                draft.Parents.Add(parent);

            return draft;
        }
    }

    /// <summary>Optionen, die ein Benutzer in der Vorschau wählt.</summary>
    public sealed class ControllingImportOptions
    {
        /// <summary>Optionaler ISO-Währungscode; ohne Angabe bleibt die Währung "nicht angegeben".</summary>
        public string CurrencyCode { get; set; }
    }

    public static class ControllingImportService
    {
        /// <summary>
        /// Liest die Datei unverändert in den Speicher (nur lesend, ohne sie zu sperren) und gibt Bytes samt SHA-256 zurück.
        /// Genau diese Bytes werden danach ausgewertet und gehasht.
        /// </summary>
        public static byte[] LoadFile(string path, ReaderLimits limits = null)
        {
            limits = limits ?? new ReaderLimits();
            if (string.IsNullOrWhiteSpace(path)) throw new ControllingImportException("Es wurde keine Datei ausgewählt.");

            if (!string.Equals(Path.GetExtension(path), ".xlsx", StringComparison.OrdinalIgnoreCase))
                throw new ControllingImportException("Nur Excel-Arbeitsmappen im Format .xlsx werden unterstützt.");

            try
            {
                using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                {
                    if (stream.Length > limits.MaxCompressedBytes)
                    {
                        throw new ControllingImportException(
                            "Die Datei ist zu groß (höchstens " + (limits.MaxCompressedBytes / (1024 * 1024)).ToString(CultureInfo.InvariantCulture) + " MiB erlaubt).");
                    }

                    var buffer = new byte[(int)stream.Length];
                    var offset = 0;
                    while (offset < buffer.Length)
                    {
                        var read = stream.Read(buffer, offset, buffer.Length - offset);
                        if (read <= 0) break;
                        offset += read;
                    }
                    if (offset != buffer.Length) Array.Resize(ref buffer, offset);
                    return buffer;
                }
            }
            catch (FileNotFoundException ex)
            {
                throw new ControllingImportException("Die Datei wurde nicht gefunden.", ex);
            }
            catch (DirectoryNotFoundException ex)
            {
                throw new ControllingImportException("Der Ordner der Datei wurde nicht gefunden.", ex);
            }
            catch (UnauthorizedAccessException ex)
            {
                throw new ControllingImportException("Kein Zugriff auf die Datei: " + ex.Message, ex);
            }
            catch (IOException ex)
            {
                throw new ControllingImportException("Die Datei kann nicht gelesen werden (ist sie in Gebrauch oder beschädigt?): " + ex.Message, ex);
            }
        }

        /// <summary>Wie <see cref="BuildPreview(byte[], string, IReadOnlyList{LocalProjectInfo}, ReaderLimits, CancellationToken)"/>, liest aber zuerst die Datei.</summary>
        public static ControllingImportPreview BuildPreviewFromFile(string path, IReadOnlyList<LocalProjectInfo> projects, ReaderLimits limits = null, CancellationToken ct = default(CancellationToken))
        {
            var fileName = Path.GetFileName(path);
            byte[] bytes;
            try
            {
                bytes = LoadFile(path, limits);
            }
            catch (ControllingImportException ex)
            {
                return FailedPreview(fileName, ex.Message, projects);
            }

            return BuildPreview(bytes, fileName, projects, limits, ct);
        }

        /// <summary>
        /// Wertet die Datei aus und ordnet PSP-Elemente den lokalen Kostenträgern zu. Hängt nicht an Datenbank oder
        /// Oberfläche und darf daher in einem Hintergrund-Thread laufen. Die Projekte müssen vorher auf dem Thread
        /// gelesen worden sein, dem die Datenbankverbindung gehört.
        /// </summary>
        public static ControllingImportPreview BuildPreview(byte[] bytes, string fileName, IReadOnlyList<LocalProjectInfo> projects, ReaderLimits limits = null, CancellationToken ct = default(CancellationToken))
        {
            projects = projects ?? new List<LocalProjectInfo>();
            var allowlist = new CarrierAllowlist(projects.Select(p => p.Carrier));

            var preview = new ControllingImportPreview
            {
                FileName = Path.GetFileName(fileName ?? string.Empty),
                FileHash = Hashing.Sha256Hex(bytes),
                FileSize = bytes.LongLength,
                AllowlistHash = allowlist.Hash
            };

            if (allowlist.NormalizedCarriers.Count == 0)
            {
                preview.Diagnostics.Add(new ImportDiagnostic(ImportSeverity.Error,
                    "Es gibt keine Projekte mit Kostenträger. Es können nur PSP-Elemente importiert werden, die in einem bestehenden Kostenträger enthalten sind."));
                return preview;
            }

            ControllingReadResult read;
            try
            {
                using (var stream = new MemoryStream(bytes, false))
                {
                    read = ProjectControllingExcelReader.Read(stream, allowlist.IsEligible, limits, ct);
                }
            }
            catch (ControllingImportException ex)
            {
                preview.Diagnostics.Add(new ImportDiagnostic(ImportSeverity.Error, ex.Message));
                return preview;
            }

            preview.Read = read;
            preview.Diagnostics.AddRange(read.Diagnostics);

            foreach (var row in read.Rows)
            {
                var previewRow = new ControllingPreviewRow
                {
                    CarrierKey = row.CarrierKey,
                    CarrierText = row.CarrierText,
                    PspName = row.PspName,
                    ExternalParentId = row.ExternalProjectId,
                    ExternalProjectName = row.ExternalProjectName,
                    ProfitCenter = row.ProfitCenter,
                    PlannedCost = row.PlannedCost,
                    ActualCost = row.ActualCost,
                    SourceSheet = row.SourceSheet,
                    SourceRow = row.SourceRow,
                    HasNegativeAmount = row.HasNegativeAmount
                };
                previewRow.LinkedProjects.AddRange(CarrierLinking.LinkedProjects(projects, row.CarrierKey));
                preview.Rows.Add(previewRow);
            }

            preview.LinkedProjectCount = preview.Rows.SelectMany(r => r.LinkedProjects).Select(p => p.Id).Distinct().Count();

            foreach (var carrier in allowlist.NormalizedCarriers)
            {
                if (!read.ScannedKeys.Any(key => CarrierKeys.TextContainsKey(carrier, key)))
                    preview.MissingCarriers.Add(allowlist.SpellingsOf(carrier).FirstOrDefault() ?? carrier);
            }

            foreach (var group in allowlist.AmbiguousSpellings())
            {
                preview.Diagnostics.Add(new ImportDiagnostic(ImportSeverity.Warning,
                    "Kostenträger werden nach Normalisierung (Groß-/Kleinschreibung, Leerraum am Rand) als gleich behandelt: " +
                    string.Join(" | ", group.Value.Select(v => "„" + v + "“")) + ". Die lokalen Projekte bleiben unverändert."));
            }

            if (!read.HasErrors && preview.Rows.Count == 0)
            {
                preview.Diagnostics.Add(new ImportDiagnostic(ImportSeverity.Error,
                    "Kein PSP-Element der Datei ist in einem bestehenden Kostenträger enthalten. Es werden nur PSP-Elemente importiert, die zu einem vorhandenen Kostenträger passen."));
            }

            preview.PayloadHash = ComputePayloadHash(preview.Rows, read);
            return preview;
        }

        private static ControllingImportPreview FailedPreview(string fileName, string message, IReadOnlyList<LocalProjectInfo> projects)
        {
            var preview = new ControllingImportPreview
            {
                FileName = fileName,
                AllowlistHash = new CarrierAllowlist((projects ?? new List<LocalProjectInfo>()).Select(p => p.Carrier)).Hash
            };
            preview.Diagnostics.Add(new ImportDiagnostic(ImportSeverity.Error, message));
            return preview;
        }

        /// <summary>Hash der übernommenen Nutzdaten: gleiche Werte ergeben unabhängig von Zeilenlage und Dateihash dasselbe Ergebnis.</summary>
        private static string ComputePayloadHash(List<ControllingPreviewRow> rows, ControllingReadResult read)
        {
            var lines = rows
                .OrderBy(r => r.CarrierKey, StringComparer.Ordinal)
                .Select(r => new PspCostRow
                {
                    CarrierKey = r.CarrierKey,
                    ProfitCenter = r.ProfitCenter,
                    ExternalProjectId = r.ExternalParentId,
                    ExternalProjectName = r.ExternalProjectName,
                    PspName = r.PspName,
                    PlannedCost = r.PlannedCost,
                    ActualCost = r.ActualCost
                }.PayloadText())
                .ToList();

            var usedParents = new HashSet<string>(rows.Select(r => CarrierKeys.NormalizeOrNull(r.ExternalParentId)).Where(k => k != null), StringComparer.Ordinal);
            lines.Add("#");
            lines.AddRange(read.Parents.Values.Where(p => usedParents.Contains(p.ParentKey)).OrderBy(p => p.ParentKey, StringComparer.Ordinal).Select(p => p.PayloadText()));
            return Hashing.Sha256Hex(string.Join("\n", lines));
        }

        /// <summary>
        /// Ergänzt die Vorschau um den Vergleich mit dem jeweils letzten vergleichbaren Stand
        /// (gleicher Währungskontext, Quellstand nicht neuer als der dieses Imports).
        /// </summary>
        public static void ApplyComparison(ControllingImportPreview preview, IDictionary<string, ControllingObservation> previousByKey, DateTime sourceTimestamp)
        {
            foreach (var row in preview.Rows)
            {
                ControllingObservation previous = null;
                if (previousByKey != null) previousByKey.TryGetValue(row.CarrierKey, out previous);
                row.Previous = previous;
                row.IsCorrection = previous != null && previous.SourceTimestamp == sourceTimestamp;
            }
        }
    }
}
