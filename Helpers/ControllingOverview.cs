// Target: .NET Framework + C# 7.3
using System;
using System.Collections.Generic;
using System.Linq;

namespace Zeitmanagement.Helpers
{
    /// <summary>Eine Zeile der Controlling-Übersicht: ein PSP-Element mit seinen Beträgen und den lokalen Projekten, die es enthalten.</summary>
    public sealed class ControllingOverviewRow
    {
        public ControllingObservation Observation { get; set; }
        public List<LocalProjectInfo> LinkedProjects { get; } = new List<LocalProjectInfo>();

        /// <summary>Letzter bekannter Stand eines Kostenträgers, der im gewählten Import fehlt (eigenes, älteres Quelldatum).</summary>
        public bool IsLastKnown { get; set; }

        /// <summary>
        /// Kontext des SAP-Projekts zum Import dieser Beobachtung (oder <c>null</c>). Alle Beträge darin gelten für das
        /// ganze Projekt, nicht für dieses PSP-Element, und werden nie summiert.
        /// </summary>
        public ParentContext Context { get; set; }

        public string Manager => Context == null ? null : Context.Manager;
        public string ProjectStatus => Context == null ? null : Context.Status;
        public string SapCustomer => Context == null ? null : Context.Customer;

        /// <summary>Diese Zeile wurde durch eine Korrektur mit gleichem Quellstand überholt.</summary>
        public bool IsSuperseded => !Observation.IsActive;

        /// <summary>Hinweis zur Herkunft der Zeile (letzter bekannter Stand bzw. überholte Revision), sonst leer.</summary>
        public string NoteText
        {
            get
            {
                if (IsSuperseded) return "Korrigiert in Import #" + Observation.ActiveImportId.ToString(System.Globalization.CultureInfo.InvariantCulture);
                if (IsLastKnown) return "Letzter bekannter Stand";
                return string.Empty;
            }
        }

        public string CarrierKey => Observation.CarrierKey;
        public string CarrierText => Observation.CarrierText;
        public string PspName => Observation.PspName;
        public string ParentName => Observation.ParentName;
        public string ExternalParentId => Observation.ExternalParentId;
        public string LinkedProjectNames => string.Join(", ", LinkedProjects.Select(p => p.Name));
        public string LinkedCustomers => string.Join(", ", LinkedProjects.Select(p => p.Customer).Where(c => !string.IsNullOrWhiteSpace(c)).Distinct(StringComparer.OrdinalIgnoreCase));
        public DateTime SourceTimestamp => Observation.SourceTimestamp;
        public string SourceText => Observation.SourceTimestamp.ToString("dd.MM.yyyy HH:mm", CostMath.German);

        public decimal? PlannedCost => Observation.PlannedCost;
        public decimal? ActualCost => Observation.ActualCost;
        public decimal? RemainingCost => Observation.RemainingCost;
        public decimal? Consumption => Observation.Consumption;

        public string PlannedText => CostMath.FormatMoney(PlannedCost);
        public string ActualText => CostMath.FormatMoney(ActualCost);
        public string RemainingText => CostMath.FormatMoney(RemainingCost);

        /// <summary>Budgetverbrauch in Prozent; ohne streng positiven, bekannten Plan nicht definiert.</summary>
        public string ConsumptionText
        {
            get
            {
                if (Consumption.HasValue) return CostMath.FormatPercent(Consumption);
                if (PlannedCost.HasValue && PlannedCost.Value > 0m) return CostMath.MissingText; // Plan da, Ist fehlt
                return CostMath.NoBudgetText;
            }
        }

        public bool IsOverrun => CostMath.IsOverrun(PlannedCost, ActualCost);
        public bool HasMissingPlan => !PlannedCost.HasValue;
        public bool HasNegativeAmount => (PlannedCost ?? 0m) < 0m || (ActualCost ?? 0m) < 0m;

        /// <summary>Status als Text, damit er nie nur über eine Farbe vermittelt wird.</summary>
        public string StatusText
        {
            get
            {
                if (IsOverrun) return "Budget überschritten";
                if (!PlannedCost.HasValue) return "Plan nicht geliefert";
                if (PlannedCost.Value <= 0m) return CostMath.NoBudgetText;
                if (!ActualCost.HasValue) return "Ist nicht geliefert";
                return "Im Budget";
            }
        }
    }

    /// <summary>
    /// Baut die Übersichtszeilen aus Beobachtungen und den aktuellen lokalen Projekten. Nur PSP-Elemente, die ein
    /// aktuell konfigurierter Kostenträger enthält, erscheinen; Historie zu nicht mehr konfigurierten bleibt gespeichert,
    /// wird aber nicht angezeigt oder aufsummiert.
    /// </summary>
    public static class ControllingOverview
    {
        /// <summary>Genau der Inhalt der gewählten Charge, gefiltert auf aktuell konfigurierte Kostenträger. Überholte Revisionen sind ausgeblendet.</summary>
        public static List<ControllingOverviewRow> Current(IReadOnlyList<LocalProjectInfo> projects, IEnumerable<ControllingObservation> batch)
        {
            var rows = new List<ControllingOverviewRow>();
            foreach (var observation in batch ?? Enumerable.Empty<ControllingObservation>())
            {
                var row = Create(projects, observation, false);
                if (row != null) rows.Add(row);
            }
            return rows.OrderBy(r => r.CarrierKey, StringComparer.Ordinal).ToList();
        }

        /// <summary>
        /// Letzter bekannter Stand für aktuell konfigurierte Kostenträger, die in der gewählten Charge fehlen.
        /// Jede Zeile trägt ihr eigenes (älteres) Quelldatum und ist nie Teil der Summen des Importstands.
        /// </summary>
        public static List<ControllingOverviewRow> LastKnown(
            IReadOnlyList<LocalProjectInfo> projects,
            IEnumerable<ControllingObservation> batch,
            IEnumerable<ControllingObservation> latestPerCarrier)
        {
            var inBatch = new HashSet<string>((batch ?? Enumerable.Empty<ControllingObservation>()).Select(o => o.CarrierKey), StringComparer.Ordinal);
            var rows = new List<ControllingOverviewRow>();
            foreach (var observation in latestPerCarrier ?? Enumerable.Empty<ControllingObservation>())
            {
                if (inBatch.Contains(observation.CarrierKey)) continue;
                var row = Create(projects, observation, true);
                if (row != null) rows.Add(row);
            }
            return rows.OrderBy(r => r.CarrierKey, StringComparer.Ordinal).ToList();
        }

        private static ControllingOverviewRow Create(IReadOnlyList<LocalProjectInfo> projects, ControllingObservation observation, bool lastKnown)
        {
            var linked = CarrierLinking.LinkedProjects(projects, observation.CarrierKey);
            if (linked.Count == 0) return null; // kein aktueller Kostenträger enthält dieses PSP-Element mehr

            var row = new ControllingOverviewRow { Observation = observation, IsLastKnown = lastKnown };
            row.LinkedProjects.AddRange(linked);
            return row;
        }
    }
}
