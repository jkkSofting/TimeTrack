// Target: .NET Framework + C# 7.3
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;

namespace Zeitmanagement.Helpers
{
    /// <summary>Eine Zeile der Controlling-Übersicht: ein PSP-Element mit seinen Beträgen und den lokalen Projekten, die es enthalten.</summary>
    public sealed class ControllingOverviewRow : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;

        public const string AmbiguousRateText = "Satz uneinheitlich";

        /// <summary>
        /// Der für dieses PSP-Element geltende Stundensatz: der Satz der verknüpften Projekte, sofern sie sich einig sind.
        /// <c>null</c> = kein Projekt hat einen Satz eingetragen, dann wird nichts umgerechnet.
        /// </summary>
        public decimal? HourlyRate { get; private set; }

        /// <summary>Die verknüpften Projekte haben verschiedene Sätze: Es wird nicht geraten, sondern nichts umgerechnet.</summary>
        public bool IsRateAmbiguous { get; private set; }

        /// <summary>Ein Satz oder ein Widerspruch liegt vor (die Stundenspalten haben dann etwas zu zeigen).</summary>
        public bool HasRate => HourlyRate.HasValue || IsRateAmbiguous;

        public void SetRate(RateResolution resolution)
        {
            resolution = resolution ?? RateResolution.None;
            if (HourlyRate == resolution.Rate && IsRateAmbiguous == resolution.IsAmbiguous) return;

            HourlyRate = resolution.Rate;
            IsRateAmbiguous = resolution.IsAmbiguous;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty)); // alles neu lesen
        }

        /// <summary>Rechnerische Stunden (Betrag ÷ Stundensatz) für einen Betrag dieser Zeile - keine gebuchte Zeit.</summary>
        public string FormatHours(decimal? amount)
        {
            return IsRateAmbiguous ? AmbiguousRateText : CostMath.FormatHours(amount, HourlyRate);
        }

        public string PlannedHoursText => FormatHours(PlannedCost);
        public string ActualHoursText => FormatHours(ActualCost);
        public string RemainingHoursText => FormatHours(RemainingCost);

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

        /// <summary>Der Budgetverbrauch ist definiert (bekannter, streng positiver Plan und bekanntes Ist).</summary>
        public bool HasConsumption => Consumption.HasValue;

        /// <summary>Verbrauch als Anteil für den Fortschrittsbalken, auf 0 bis 1 begrenzt (eine Überschreitung füllt ihn ganz, der Text nennt den echten Wert).</summary>
        public double ProgressValue => Consumption.HasValue ? (double)Math.Max(0m, Math.Min(1m, Consumption.Value)) : 0d;

        /// <summary>Stand in Prozent ("42,0 %"), ohne definierten Verbrauch "–" (die Begründung steht in <see cref="ConsumptionText"/>).</summary>
        public string ConsumptionPercentText => Consumption.HasValue ? CostMath.FormatPercent(Consumption) : "–";

        /// <summary>Ab 90 % bis zum Plan: Der Balken warnt, der Status bleibt "Im Budget".</summary>
        public bool IsNearLimit => Consumption.HasValue && Consumption.Value >= 0.9m && !IsOverrun;

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

    /// <summary>Ergebnis der Satzauflösung für ein PSP-Element.</summary>
    public sealed class RateResolution
    {
        public static readonly RateResolution None = new RateResolution(null, false);

        public RateResolution(decimal? rate, bool isAmbiguous)
        {
            Rate = rate;
            IsAmbiguous = isAmbiguous;
        }

        public decimal? Rate { get; }
        public bool IsAmbiguous { get; }
    }

    /// <summary>Stundensatz je Projekt: Bestimmt den Satz, der für ein PSP-Element gilt.</summary>
    public static class ControllingRates
    {
        /// <summary>
        /// Teilen sich mehrere Projekte einen Kostenträger, gilt der Satz, den alle eingetragenen Sätze gemeinsam haben.
        /// Projekte ohne Satz bleiben außen vor; weichen die eingetragenen Sätze voneinander ab, ist das Ergebnis
        /// "uneinheitlich" (keine Umrechnung, kein Mittelwert, kein erstes oder letztes Projekt).
        /// </summary>
        public static RateResolution Resolve(IEnumerable<LocalProjectInfo> linkedProjects, IDictionary<int, decimal> ratesByProject)
        {
            if (linkedProjects == null || ratesByProject == null) return RateResolution.None;

            var rates = new List<decimal>();
            foreach (var project in linkedProjects)
            {
                decimal rate;
                if (ratesByProject.TryGetValue(project.Id, out rate) && rate > 0m && !rates.Contains(rate)) rates.Add(rate);
            }

            if (rates.Count == 0) return RateResolution.None;
            return rates.Count == 1 ? new RateResolution(rates[0], false) : new RateResolution(null, true);
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
