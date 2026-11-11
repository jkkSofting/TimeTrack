using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Data;
using System.Windows.Interop;
using Zeitmanagement.Helpers;
using Zeitmanagement.MVVM;

namespace Zeitmanagement.ViewModel
{
    /// <summary>Beschriftung und Wert für die Detailanzeige.</summary>
    internal sealed class DetailItem
    {
        public DetailItem(string label, string value, string tooltip = null)
        {
            Label = label;
            Value = string.IsNullOrWhiteSpace(value) ? "–" : value;
            Tooltip = tooltip;
        }

        public string Label { get; }
        public string Value { get; }
        public string Tooltip { get; }
    }

    /// <summary>Lokales Projekt, das im Stundenverlauf ein- oder ausgeblendet werden kann.</summary>
    internal sealed class HoursProjectFilter : BindableBase
    {
        private bool _isSelected = true;

        public int Id { get; set; }
        public string Name { get; set; }
        public bool IsSelected { get => _isSelected; set => SetProperty(ref _isSelected, value); }
    }

    /// <summary>
    /// Ein lokales Projekt in den Details samt seinem Stundensatz. Der Satz gehört zum Projekt (nicht global) und gilt für die
    /// Währung des gewählten Imports; er dient nur der Umrechnung von Geld in rechnerische Stunden.
    /// </summary>
    internal sealed class ProjectRateItem : BindableBase
    {
        private string _rateText;
        private string _error;

        public ProjectRateItem(LocalProjectInfo project, string initialRateText)
        {
            Id = project.Id;
            Name = project.Name;
            Customer = project.Customer;
            _rateText = initialRateText ?? string.Empty;
        }

        public int Id { get; }
        public string Name { get; }
        public string Customer { get; }

        public string RateText
        {
            get => _rateText;
            set { if (SetProperty(ref _rateText, value)) RateEdited?.Invoke(this); }
        }

        public string Error
        {
            get => _error;
            set { if (SetProperty(ref _error, value)) RaisePropertyChanged(nameof(HasError)); }
        }

        public bool HasError => !string.IsNullOrEmpty(_error);

        /// <summary>Wird nach jeder Änderung des Textes ausgelöst (nicht beim Setzen des Anfangswerts).</summary>
        public Action<ProjectRateItem> RateEdited { get; set; }
    }

    /// <summary>Ein Kostenträger in der Auswahlliste des Diagramms: Häkchen = im Diagramm sichtbar.</summary>
    internal sealed class ChartCarrierItem : BindableBase
    {
        private bool _isShown;

        public ChartCarrierItem(string key, string text, string detail, bool isShown)
        {
            Key = key;
            Text = text;
            Detail = detail;
            _isShown = isShown;
        }

        public string Key { get; }
        public string Text { get; }
        public string Detail { get; }

        public bool IsShown
        {
            get => _isShown;
            set { if (SetProperty(ref _isShown, value)) Changed?.Invoke(this); }
        }

        /// <summary>Wird bei einem Klick auf das Häkchen ausgelöst (nicht bei Sammelaktionen).</summary>
        public Action<ChartCarrierItem> Changed { get; set; }

        /// <summary>Setzt das Häkchen ohne Rückruf; für „Alle ein-/ausblenden“, das danach einmal neu zeichnet.</summary>
        public void SetSilently(bool value)
        {
            if (_isShown == value) return;
            _isShown = value;
            RaisePropertyChanged(nameof(IsShown));
        }
    }

    /// <summary>Eine Zeile des Kostenverlaufs: ein Importstand eines PSP-Elements mit Differenz zum vorigen Stand.</summary>
    internal sealed class CostHistoryItem
    {
        public ControllingObservation Observation { get; set; }
        public string SourceText { get; set; }
        public string PlannedText { get; set; }
        public string ActualText { get; set; }
        public string RemainingText { get; set; }
        public string ConsumptionText { get; set; }
        public string PlannedHoursText { get; set; }
        public string ActualHoursText { get; set; }
        public string RemainingHoursText { get; set; }
        public string PlanDeltaText { get; set; }
        public string ActualDeltaText { get; set; }
        public string FileName { get; set; }
        public string ImportedText { get; set; }
        public string RevisionText { get; set; }
    }

    /// <summary>Eine Revision (Import) desselben Quellstands.</summary>
    internal sealed class RevisionItem
    {
        public string ImportText { get; set; }
        public string ImportedText { get; set; }
        public string FileName { get; set; }
        public string PlannedText { get; set; }
        public string ActualText { get; set; }
        public string StateText { get; set; }
    }

    /// <summary>Auswahl eines Standes, wenn die Datei zwei unterschiedliche nennt.</summary>
    internal sealed class TimestampChoice
    {
        public DateTime Value { get; set; }
        public string Label { get; set; }
        public override string ToString() => Label;
    }

    /// <summary>
    /// Seite „Projektcontrolling“: Import der SAP-Kosten (nur lesend) für bestehende Kostenträger, Übersicht, Kostenverlauf
    /// aus den Importständen und – davon getrennt – der lokal erfasste Stundenverlauf.
    /// Geldbeträge und Stunden werden nie vermischt oder ineinander umgerechnet.
    /// </summary>
    internal sealed class ProjectControllingViewModel : BaseViewModel
    {
        private const string AllFilter = "Alle";
        private static readonly CultureInfo German = CostMath.German;

        private readonly Action<DateTime> _openDay;
        private CancellationTokenSource _cancellation;
        private string _previewPath;
        private bool _loading;

        private List<LocalProjectInfo> _projects = new List<LocalProjectInfo>();
        // Verläufe (aktive Beobachtungen je PSP-Element) des gewählten Währungskontexts, für das Diagramm
        private Dictionary<string, List<ControllingObservation>> _histories = new Dictionary<string, List<ControllingObservation>>(StringComparer.Ordinal);
        private List<ControllingOverviewRow> _currentRows = new List<ControllingOverviewRow>();
        private List<ControllingOverviewRow> _lastKnownRows = new List<ControllingOverviewRow>();
        private readonly Dictionary<string, ParentContext> _contextCache = new Dictionary<string, ParentContext>(StringComparer.Ordinal);

        public ProjectControllingViewModel(Action<DateTime> openDay = null)
        {
            _openDay = openDay;

            RowsView = CollectionViewSource.GetDefaultView(Rows);
            RowsView.Filter = FilterRow;

            RefreshCommand = new DelegateCommand(_ => Refresh());
            ImportCommand = new DelegateCommand(_ => StartImport(), _ => !IsBusy);
            ConfirmImportCommand = new DelegateCommand(_ => ConfirmImport(), _ => CanConfirmImport);
            CancelPreviewCommand = new DelegateCommand(_ => ClosePreview(), _ => !IsBusy);
            CancelBusyCommand = new DelegateCommand(_ => _cancellation?.Cancel(), _ => IsBusy);
            DeleteImportCommand = new DelegateCommand(_ => DeleteSelectedImport(), _ => SelectedImport != null && !IsBusy);
            ResetFiltersCommand = new DelegateCommand(_ => ResetFilters());
            ToggleContextCommand = new DelegateCommand(_ => IsContextExpanded = !IsContextExpanded);
            CopyCarrierCommand = new DelegateCommand(CopyCarrier, p => p is ControllingOverviewRow || SelectedRow != null);
            OpenDayCommand = new DelegateCommand(OpenDay, p => p is HoursBucket && _openDay != null);
            DismissMessageCommand = new DelegateCommand(_ => StatusMessage = null);
            SelectSeriesCommand = new DelegateCommand(SelectSeries, p => p is string);
            ChartShowAllCommand = new DelegateCommand(_ => SetListedCarriers(true));
            ChartHideAllCommand = new DelegateCommand(_ => SetListedCarriers(false));

            ChartCarriersView = CollectionViewSource.GetDefaultView(ChartCarriers);
            ChartCarriersView.Filter = FilterChartCarrier;
            ResetHoursRangeCommand = new DelegateCommand(_ => { HoursStartText = HoursEndText = string.Empty; });
        }

        // ===== Befehle =====

        public DelegateCommand RefreshCommand { get; }
        public DelegateCommand ImportCommand { get; }
        public DelegateCommand ConfirmImportCommand { get; }
        public DelegateCommand CancelPreviewCommand { get; }
        public DelegateCommand CancelBusyCommand { get; }
        public DelegateCommand DeleteImportCommand { get; }
        public DelegateCommand ResetFiltersCommand { get; }
        public DelegateCommand ToggleContextCommand { get; }
        public DelegateCommand CopyCarrierCommand { get; }
        public DelegateCommand OpenDayCommand { get; }
        public DelegateCommand DismissMessageCommand { get; }
        /// <summary>Wählt die Zeile zu einer angeklickten Linie des Diagramms (Kostenträger-Schlüssel als Parameter).</summary>
        public DelegateCommand SelectSeriesCommand { get; }
        /// <summary>Blendet alle (in der Liste gerade angezeigten) Kostenträger im Diagramm ein.</summary>
        public DelegateCommand ChartShowAllCommand { get; }
        /// <summary>Blendet alle (in der Liste gerade angezeigten) Kostenträger im Diagramm aus.</summary>
        public DelegateCommand ChartHideAllCommand { get; }
        public DelegateCommand ResetHoursRangeCommand { get; }

        // ===== Meldungen =====

        private string _statusMessage;
        /// <summary>Kurze Rückmeldung (Import fertig, Hinweis zur Navigation); leer = ausgeblendet.</summary>
        public string StatusMessage
        {
            get => _statusMessage;
            set { if (SetProperty(ref _statusMessage, value)) RaisePropertyChanged(nameof(HasStatusMessage)); }
        }

        public bool HasStatusMessage => !string.IsNullOrEmpty(_statusMessage);

        // ===== Importe (Chargen) und Übersicht =====

        public ObservableCollection<ControllingImportInfo> Imports { get; } = new ObservableCollection<ControllingImportInfo>();
        public ObservableCollection<ControllingOverviewRow> Rows { get; } = new ObservableCollection<ControllingOverviewRow>();
        public ICollectionView RowsView { get; }

        private ControllingImportInfo _selectedImport;
        public ControllingImportInfo SelectedImport
        {
            get => _selectedImport;
            set
            {
                if (!SetProperty(ref _selectedImport, value)) return;
                DeleteImportCommand.RaiseCanExecuteChanged();
                if (!_loading) LoadRows();
                RaiseHeaderChanged();
            }
        }

        public bool HasImports => Imports.Count > 0;
        public bool HasNoImports => Imports.Count == 0;

        private bool _showLastKnown;
        public bool IsBatchMode
        {
            get => !_showLastKnown;
            set { if (value && _showLastKnown) SetMode(false); }
        }

        public bool IsLastKnownMode
        {
            get => _showLastKnown;
            set { if (value && !_showLastKnown) SetMode(true); }
        }

        private void SetMode(bool lastKnown)
        {
            _showLastKnown = lastKnown;
            RaisePropertyChanged(nameof(IsBatchMode));
            RaisePropertyChanged(nameof(IsLastKnownMode));
            ApplyRows();
        }

        public string LastKnownModeText => "Letzter bekannter Stand (" + _lastKnownRows.Count.ToString(CultureInfo.InvariantCulture) + ")";
        public string BatchModeText => "Importstand (" + _currentRows.Count.ToString(CultureInfo.InvariantCulture) + ")";

        // Kopf und Kennzahlen: beziehen sich immer auf den gewählten Importstand, nie auf eine Mischung mehrerer Stände.
        public string SourceText => SelectedImport == null ? "Kein Import" : "SAP-Stand " + SelectedImport.SourceTimestamp.ToString("dd.MM.yyyy HH:mm", German);
        public string FileNameText => SelectedImport == null ? string.Empty : SelectedImport.FileName;

        public string FreshnessText
        {
            get
            {
                if (SelectedImport == null) return string.Empty;
                var days = (int)Math.Floor((DateTime.Now - SelectedImport.SourceTimestamp).TotalDays);
                if (days <= 0) return "Stand von heute";
                if (days == 1) return "Stand von gestern";
                return "Stand vor " + days.ToString(CultureInfo.InvariantCulture) + " Tagen" + (IsStale ? " – veraltet" : string.Empty);
            }
        }

        /// <summary>Stand älter als 31 Tage; die Aktualität richtet sich nach dem SAP-Stand, nicht nach dem Importzeitpunkt.</summary>
        public bool IsStale => SelectedImport != null && (DateTime.Now - SelectedImport.SourceTimestamp).TotalDays > 31;

        public string MatchedCountText => _currentRows.Count.ToString(CultureInfo.InvariantCulture);
        public string LinkedProjectCountText => _currentRows.SelectMany(r => r.LinkedProjects).Select(p => p.Id).Distinct().Count().ToString(CultureInfo.InvariantCulture);
        public string WarningCountText => SelectedImport == null ? "0" : SelectedImport.WarningCount.ToString(CultureInfo.InvariantCulture);
        public string CurrencyText => SelectedImport == null ? string.Empty : SelectedImport.CurrencyText;

        public bool HasRows => Rows.Count > 0;
        public bool ShowEmptyRowsHint => HasImports && Rows.Count == 0;

        public string EmptyRowsText
        {
            get
            {
                if (!HasImports) return string.Empty;
                if (_showLastKnown) return "Für alle aktuell zugeordneten Kostenträger liegt in diesem Importstand ein Wert vor; ein älterer Stand wird nicht gebraucht.";
                if (_currentRows.Count == 0)
                    return "Kein PSP-Element dieses Imports ist aktuell in einem Projekt-Kostenträger enthalten. Die importierten Daten bleiben gespeichert.";
                return "Keine Zeile passt zu den Filtern.";
            }
        }

        public string VisibleCountText
        {
            get
            {
                var visible = RowsView.Cast<object>().Count();
                return visible.ToString(CultureInfo.InvariantCulture) + " von " + Rows.Count.ToString(CultureInfo.InvariantCulture) + " Zeilen";
            }
        }

        /// <summary>Hinweis bei „Letzter bekannter Stand“: Zeilen haben ein eigenes Datum und zählen nicht zum Importstand.</summary>
        public string ModeHint => _showLastKnown
            ? "Kostenträger, die im gewählten Import fehlen: letzter bekannter Stand mit eigenem, älteren SAP-Datum. Diese Zeilen zählen nicht zu den Kennzahlen des Importstands."
            : "Genau die Zeilen dieses Imports für aktuell zugeordnete Kostenträger. Fehlende Beträge sind „" + CostMath.MissingText + "“, nicht null.";

        private void RaiseHeaderChanged()
        {
            RaisePropertyChanged(nameof(SourceText));
            RaisePropertyChanged(nameof(FileNameText));
            RaisePropertyChanged(nameof(FreshnessText));
            RaisePropertyChanged(nameof(IsStale));
            RaisePropertyChanged(nameof(MatchedCountText));
            RaisePropertyChanged(nameof(LinkedProjectCountText));
            RaisePropertyChanged(nameof(WarningCountText));
            RaisePropertyChanged(nameof(CurrencyText));
            RaisePropertyChanged(nameof(HasImports));
            RaisePropertyChanged(nameof(HasNoImports));
            RaisePropertyChanged(nameof(LastKnownModeText));
            RaisePropertyChanged(nameof(BatchModeText));
            RaisePropertyChanged(nameof(HasRows));
            RaisePropertyChanged(nameof(ShowEmptyRowsHint));
            RaisePropertyChanged(nameof(ShowTableEmptyHint));
            RaisePropertyChanged(nameof(EmptyRowsText));
            RaisePropertyChanged(nameof(ModeHint));
            RaisePropertyChanged(nameof(VisibleCountText));
        }

        // ===== Ansicht: Tabelle oder Diagramm =====

        private bool _isChartView;

        /// <summary>Die Übersicht als Tabelle (bisherige Ansicht).</summary>
        public bool IsTableView
        {
            get => !_isChartView;
            set { if (value) SetChartView(false); }
        }

        /// <summary>Die Übersicht als X-Y-Diagramm (Stand über die SAP-Daten).</summary>
        public bool IsChartView
        {
            get => _isChartView;
            set { if (value) SetChartView(true); }
        }

        private ChartMode _chartMode = ChartMode.Percent;

        /// <summary>Diagramm relativ: Ist ÷ Plan in Prozent.</summary>
        public bool IsChartPercent
        {
            get => _chartMode == ChartMode.Percent;
            set { if (value) SetChartMode(ChartMode.Percent); }
        }

        /// <summary>Diagramm in Stunden: aus Geld umgerechnet (Stundensatz des Projekts) und lokal gebucht.</summary>
        public bool IsChartHours
        {
            get => _chartMode == ChartMode.Hours;
            set { if (value) SetChartMode(ChartMode.Hours); }
        }

        private ChartData _chart;
        public ChartData Chart { get => _chart; private set => SetProperty(ref _chart, value); }

        public string ChartNoteText => _chart == null ? string.Empty : string.Join("\n", _chart.Notes);
        public bool HasChartNotes => _chart != null && _chart.Notes.Count > 0;

        /// <summary>Hinweis der leeren Tabelle gilt nur in der Tabellenansicht (das Diagramm erklärt sich selbst).</summary>
        public bool ShowTableEmptyHint => ShowEmptyRowsHint && !_isChartView;

        private void SetChartView(bool chart)
        {
            if (_isChartView == chart) return;
            _isChartView = chart;
            RaisePropertyChanged(nameof(IsTableView));
            RaisePropertyChanged(nameof(IsChartView));
            RaisePropertyChanged(nameof(ShowTableEmptyHint));
            RebuildChart();
        }

        private void SetChartMode(ChartMode mode)
        {
            if (_chartMode == mode) return;
            _chartMode = mode;
            RaisePropertyChanged(nameof(IsChartPercent));
            RaisePropertyChanged(nameof(IsChartHours));
            RebuildChart();
        }

        /// <summary>
        /// Baut das Diagramm aus den Zeilen, die die Tabelle gerade zeigt (nach Suche und Filtern), und – für die gewählte Zeile –
        /// ihren lokal gebuchten Stunden. Läuft nur, solange das Diagramm angezeigt wird.
        /// </summary>
        private void RebuildChart()
        {
            if (!_isChartView) return;

            var visible = RowsView.Cast<ControllingOverviewRow>().ToList();
            SyncChartCarriers(visible);

            var shown = visible.Where(r => !_chartHidden.Contains(r.CarrierKey)).ToList();
            var inputs = shown.Select(row =>
            {
                List<ControllingObservation> history;
                _histories.TryGetValue(row.CarrierKey, out history);
                return new ChartRowInput(row, history);
            }).ToList();

            var local = ControllingChartBuilder.LocalCumulative(SelectedRow == null ? null : _hoursEntries);

            // Hat der Benutzer Kostenträger gezielt aus- oder eingeblendet, dürfen es mehr Linien sein als in der Voreinstellung.
            var maxSeries = _chartHidden.Count > 0 ? 16 : ControllingChartBuilder.MaxSeries;
            Chart = ControllingChartBuilder.Build(inputs, SelectedRow == null ? null : SelectedRow.CarrierKey, _chartMode, _showHoursColumns, local, maxSeries, visible.Count - shown.Count);
            RaisePropertyChanged(nameof(ChartNoteText));
            RaisePropertyChanged(nameof(HasChartNotes));
        }

        // ----- Kostenträger-Filter des Diagramms -----

        // Schlüssel der Kostenträger, die der Benutzer im Diagramm ausgeblendet hat (bleibt über Tabellenfilter hinweg erhalten)
        private readonly HashSet<string> _chartHidden = new HashSet<string>(StringComparer.Ordinal);

        /// <summary>Auswahlliste: die Zeilen, die die Tabelle gerade zeigt, mit Häkchen für „im Diagramm sichtbar“.</summary>
        public ObservableCollection<ChartCarrierItem> ChartCarriers { get; } = new ObservableCollection<ChartCarrierItem>();
        public ICollectionView ChartCarriersView { get; }

        private bool _isChartCarrierPanelOpen;
        public bool IsChartCarrierPanelOpen { get => _isChartCarrierPanelOpen; set => SetProperty(ref _isChartCarrierPanelOpen, value); }

        private string _chartCarrierSearch;
        public string ChartCarrierSearch
        {
            get => _chartCarrierSearch;
            set { if (SetProperty(ref _chartCarrierSearch, value)) ChartCarriersView.Refresh(); }
        }

        /// <summary>Beschriftung des Filterknopfs, z. B. „Kostenträger 5/12“.</summary>
        public string ChartCarrierSummary
        {
            get
            {
                var shown = ChartCarriers.Count(c => c.IsShown);
                return "Kostenträger " + shown.ToString(CultureInfo.InvariantCulture) + "/" + ChartCarriers.Count.ToString(CultureInfo.InvariantCulture);
            }
        }

        private bool FilterChartCarrier(object item)
        {
            var c = item as ChartCarrierItem;
            if (c == null) return false;
            if (string.IsNullOrWhiteSpace(_chartCarrierSearch)) return true;
            var q = _chartCarrierSearch.Trim();
            return Has(c.Text, q) || Has(c.Detail, q);
        }

        /// <summary>Gleicht die Liste mit den Zeilen der Tabelle ab; vorhandene Auswahl bleibt erhalten.</summary>
        private void SyncChartCarriers(IReadOnlyList<ControllingOverviewRow> visibleRows)
        {
            if (ChartCarriers.Select(c => c.Key).SequenceEqual(visibleRows.Select(r => r.CarrierKey))) return;

            ChartCarriers.Clear();
            foreach (var row in visibleRows)
            {
                ChartCarriers.Add(new ChartCarrierItem(row.CarrierKey, row.CarrierText, row.PspName, !_chartHidden.Contains(row.CarrierKey))
                {
                    Changed = OnChartCarrierToggled
                });
            }
            RaisePropertyChanged(nameof(ChartCarrierSummary));
        }

        private void OnChartCarrierToggled(ChartCarrierItem item)
        {
            if (item.IsShown) _chartHidden.Remove(item.Key);
            else _chartHidden.Add(item.Key);

            RaisePropertyChanged(nameof(ChartCarrierSummary));
            RebuildChart();
        }

        private void SetListedCarriers(bool show)
        {
            // gilt für die Einträge, die die Suche in der Liste gerade zeigt
            foreach (var item in ChartCarriersView.Cast<ChartCarrierItem>().ToList())
            {
                if (show) _chartHidden.Remove(item.Key);
                else _chartHidden.Add(item.Key);
                item.SetSilently(show);
            }

            RaisePropertyChanged(nameof(ChartCarrierSummary));
            RebuildChart();
        }

        private void SelectSeries(object parameter)
        {
            var key = parameter as string;
            if (key == null) return;

            var row = Rows.FirstOrDefault(r => r.CarrierKey == key);
            if (row != null) SelectedRow = row;
        }

        // ===== Stundensatz je Projekt: rechnerische Stunden aus Geld =====

        // Projekt-ID → Stundensatz für den Währungskontext des gewählten Imports
        private Dictionary<int, decimal> _projectRates = new Dictionary<int, decimal>();

        /// <summary>Mindestens ein PSP-Element hat einen Satz (oder widersprüchliche Sätze): Dann lassen sich Stundenspalten zeigen.</summary>
        public bool HasHourlyRate => _currentRows.Concat(_lastKnownRows).Any(r => r.HasRate);

        private bool _showHoursColumns = LoadShowHoursColumns();

        /// <summary>
        /// Berechnete Stunden (aus Geld) in den Tabellen anzeigen. Wer sie nicht sehen will, schaltet sie hier aus; die Wahl bleibt gespeichert.
        /// </summary>
        public bool ShowHoursColumns
        {
            get => _showHoursColumns;
            set
            {
                if (!SetProperty(ref _showHoursColumns, value)) return;
                SaveShowHoursColumns(value);
                RaiseHoursVisibilityChanged();
                UpdateHoursComparison();
                RebuildChart();
            }
        }

        /// <summary>Die berechneten Stundenspalten sind sichtbar: ein Satz ist eingetragen und der Schalter steht auf „an“.</summary>
        public bool HoursColumnsVisible => HasHourlyRate && _showHoursColumns;

        /// <summary>Der Detailbereich zeigt die Stunden der gewählten Zeile (Satz oder Widerspruch vorhanden, Schalter an).</summary>
        public bool ShowDetailHours => _showHoursColumns && _selectedRow != null && _selectedRow.HasRate;

        private void RaiseHoursVisibilityChanged()
        {
            RaisePropertyChanged(nameof(HasHourlyRate));
            RaisePropertyChanged(nameof(HoursColumnsVisible));
            RaisePropertyChanged(nameof(ShowDetailHours));
        }

        private static bool LoadShowHoursColumns()
        {
            try { return Properties.Settings.Default.ControllingShowHoursColumns; }
            catch (Exception) { return true; }
        }

        private static void SaveShowHoursColumns(bool value)
        {
            try
            {
                Properties.Settings.Default.ControllingShowHoursColumns = value;
                Properties.Settings.Default.Save();
            }
            catch (Exception)
            {
                // Die Wahl gilt trotzdem für diese Sitzung.
            }
        }

        public string ConversionNote =>
            HasHourlyRate
                ? "Stunden (h) in den Tabellen sind aus Geld umgerechnet: Betrag ÷ Stundensatz des Projekts (in den Details je Projekt eingetragen, " + CurrencyText +
                  "). Teilen sich Projekte einen Kostenträger mit unterschiedlichen Sätzen, steht „" + ControllingOverviewRow.AmbiguousRateText +
                  "“. Das ist eine Schätzung aus Geld, keine gebuchte Zeit und nicht mit den lokal erfassten Stunden abgestimmt."
                : string.Empty;

        /// <summary>Liest die Sätze zum Währungskontext des gewählten Imports und wendet sie auf alle Zeilen an.</summary>
        private void LoadProjectRates()
        {
            _projectRates = SelectedImport == null
                ? new Dictionary<int, decimal>()
                : MainViewModel.DbInstance.GetControllingProjectRates(SelectedImport.CurrencyContext);

            ApplyRates();

            // Die Sätze gelten je Währungskontext: Beim Wechsel des Imports zeigen die Projektzeilen die Sätze des neuen Kontexts.
            if (SelectedRow != null)
            {
                DetailProjects.Clear();
                foreach (var p in SelectedRow.LinkedProjects) DetailProjects.Add(CreateRateItem(p));
            }
        }

        /// <summary>Bestimmt je PSP-Element den geltenden Satz (siehe <see cref="ControllingRates.Resolve"/>) und frischt die Anzeige auf.</summary>
        private void ApplyRates()
        {
            foreach (var row in _currentRows.Concat(_lastKnownRows))
                row.SetRate(ControllingRates.Resolve(row.LinkedProjects, _projectRates));

            RaiseHoursVisibilityChanged();
            RaisePropertyChanged(nameof(ConversionNote));

            if (SelectedRow != null)
            {
                CostHistory.Clear();
                LoadCostHistory(MainViewModel.DbInstance, SelectedRow);
                UpdateHoursComparison();
            }

            RebuildChart();
        }

        private ProjectRateItem CreateRateItem(LocalProjectInfo project)
        {
            decimal rate;
            var item = new ProjectRateItem(project, _projectRates.TryGetValue(project.Id, out rate) ? CostMath.FormatRate(rate) : string.Empty);
            item.RateEdited = OnProjectRateEdited;
            return item;
        }

        private void OnProjectRateEdited(ProjectRateItem item)
        {
            decimal? rate;
            if (!CostMath.TryParseHourlyRate(item.RateText, out rate))
            {
                item.Error = "Bitte einen Stundensatz größer 0 eingeben, z. B. 95,50.";
                return;
            }

            if (SelectedImport == null) return;

            try
            {
                MainViewModel.DbInstance.SetControllingProjectRate(item.Id, SelectedImport.CurrencyContext, rate);
            }
            catch (Exception ex)
            {
                item.Error = "Der Stundensatz konnte nicht gespeichert werden: " + ex.Message;
                return;
            }

            item.Error = null;
            if (rate.HasValue) _projectRates[item.Id] = rate.Value;
            else _projectRates.Remove(item.Id);

            ApplyRates();
        }

        // ===== Suche und Filter (ändern nie, was importiert werden darf) =====

        private string _searchText;
        public string SearchText { get => _searchText; set { if (SetProperty(ref _searchText, value)) RefreshView(); } }

        public ObservableCollection<string> Managers { get; } = new ObservableCollection<string> { AllFilter };
        public ObservableCollection<string> Statuses { get; } = new ObservableCollection<string> { AllFilter };

        private string _managerFilter = AllFilter;
        public string ManagerFilter { get => _managerFilter; set { if (SetProperty(ref _managerFilter, value ?? AllFilter)) RefreshView(); } }

        private string _statusFilter = AllFilter;
        public string StatusFilter { get => _statusFilter; set { if (SetProperty(ref _statusFilter, value ?? AllFilter)) RefreshView(); } }

        private bool _onlyMissingPlan;
        public bool OnlyMissingPlan { get => _onlyMissingPlan; set { if (SetProperty(ref _onlyMissingPlan, value)) RefreshView(); } }

        private bool _onlyOverrun;
        public bool OnlyOverrun { get => _onlyOverrun; set { if (SetProperty(ref _onlyOverrun, value)) RefreshView(); } }

        private void ResetFilters()
        {
            SearchText = string.Empty;
            ManagerFilter = AllFilter;
            StatusFilter = AllFilter;
            OnlyMissingPlan = false;
            OnlyOverrun = false;
        }

        private void RefreshView()
        {
            RowsView.Refresh();
            RaisePropertyChanged(nameof(VisibleCountText));
            RaisePropertyChanged(nameof(EmptyRowsText));
            RebuildChart();
        }

        private bool FilterRow(object item)
        {
            var row = item as ControllingOverviewRow;
            if (row == null) return false;

            if (_managerFilter != AllFilter && !string.Equals(row.Manager, _managerFilter, StringComparison.OrdinalIgnoreCase)) return false;
            if (_statusFilter != AllFilter && !string.Equals(row.ProjectStatus, _statusFilter, StringComparison.OrdinalIgnoreCase)) return false;
            if (_onlyMissingPlan && !row.HasMissingPlan) return false;
            if (_onlyOverrun && !row.IsOverrun) return false;

            if (string.IsNullOrWhiteSpace(_searchText)) return true;
            var q = _searchText.Trim();
            return Has(row.LinkedProjectNames, q) || Has(row.CarrierText, q) || Has(row.ParentName, q) || Has(row.ExternalParentId, q)
                   || Has(row.PspName, q) || Has(row.SapCustomer, q) || Has(row.LinkedCustomers, q) || Has(row.Manager, q);
        }

        private static bool Has(string text, string query)
        {
            return text != null && text.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        // ===== Laden =====

        public override void Refresh()
        {
            var db = MainViewModel.DbInstance;
            var keepId = SelectedImport == null ? 0 : SelectedImport.Id;

            _loading = true;
            try
            {
                var imports = db.GetControllingImports();
                Imports.Clear();
                foreach (var info in imports) Imports.Add(info);

                // Vorgabe: der neueste Stand (nach SAP-Datum, nicht nach Importreihenfolge) – die Liste ist danach sortiert.
                SelectedImport = Imports.FirstOrDefault(i => i.Id == keepId) ?? Imports.FirstOrDefault();
            }
            finally
            {
                _loading = false;
            }

            LoadRows();
            RaiseHeaderChanged();
        }

        private void LoadRows()
        {
            var db = MainViewModel.DbInstance;
            var keepKey = SelectedRow == null ? null : SelectedRow.CarrierKey;

            _contextCache.Clear();
            _projects = db.GetLocalProjectInfos();
            _currentRows = new List<ControllingOverviewRow>();
            _lastKnownRows = new List<ControllingOverviewRow>();

            if (SelectedImport != null)
            {
                var batch = db.GetControllingBatch(SelectedImport.Id);
                _currentRows = ControllingOverview.Current(_projects, batch);
                var latest = db.GetLatestControllingObservations(null, SelectedImport.CurrencyContext).Values;
                _lastKnownRows = ControllingOverview.LastKnown(_projects, batch, latest);

                foreach (var row in _currentRows.Concat(_lastKnownRows))
                    row.Context = LoadContext(db, row.Observation);

                _histories = db.GetControllingHistories(_currentRows.Concat(_lastKnownRows).Select(r => r.CarrierKey), SelectedImport.CurrencyContext);
            }
            else
            {
                _histories = new Dictionary<string, List<ControllingObservation>>(StringComparer.Ordinal);
            }

            ApplyRows(keepKey);
            LoadProjectRates();
        }

        private ParentContext LoadContext(TimeTracker db, ControllingObservation observation)
        {
            if (string.IsNullOrEmpty(observation.ParentKey)) return null;
            var cacheKey = observation.ImportId.ToString(CultureInfo.InvariantCulture) + "|" + observation.ParentKey;
            ParentContext context;
            if (!_contextCache.TryGetValue(cacheKey, out context))
            {
                context = db.GetControllingParentContext(observation.ImportId, observation.ParentKey);
                _contextCache[cacheKey] = context;
            }
            return context;
        }

        private void ApplyRows(string keepKey = null)
        {
            keepKey = keepKey ?? (SelectedRow == null ? null : SelectedRow.CarrierKey);

            Rows.Clear();
            foreach (var row in _showLastKnown ? _lastKnownRows : _currentRows) Rows.Add(row);

            UpdateFilterLists();
            SelectedRow = keepKey == null ? null : Rows.FirstOrDefault(r => r.CarrierKey == keepKey);

            RefreshView();
            RaisePropertyChanged(nameof(HasRows));
            RaisePropertyChanged(nameof(ShowEmptyRowsHint));
            RaisePropertyChanged(nameof(ModeHint));
            RaisePropertyChanged(nameof(LastKnownModeText));
            RaisePropertyChanged(nameof(BatchModeText));
            RaisePropertyChanged(nameof(MatchedCountText));
            RaisePropertyChanged(nameof(LinkedProjectCountText));
        }

        private void UpdateFilterLists()
        {
            Fill(Managers, _currentRows.Concat(_lastKnownRows).Select(r => r.Manager), ref _managerFilter, nameof(ManagerFilter));
            Fill(Statuses, _currentRows.Concat(_lastKnownRows).Select(r => r.ProjectStatus), ref _statusFilter, nameof(StatusFilter));
        }

        private void Fill(ObservableCollection<string> target, IEnumerable<string> values, ref string selected, string property)
        {
            var distinct = values.Where(v => !string.IsNullOrWhiteSpace(v)).Distinct(StringComparer.OrdinalIgnoreCase)
                                 .OrderBy(v => v, StringComparer.CurrentCultureIgnoreCase).ToList();
            target.Clear();
            target.Add(AllFilter);
            foreach (var v in distinct) target.Add(v);

            if (!target.Contains(selected))
            {
                selected = AllFilter;
                RaisePropertyChanged(property);
            }
        }

        /// <summary>
        /// Springt aus der Projektseite hierher: wählt das PSP-Element, das der Kostenträger des Projekts enthält.
        /// </summary>
        public void FocusCarrier(string carrierText)
        {
            var normalized = CarrierKeys.Normalize(carrierText);

            var row = _currentRows.FirstOrDefault(r => CarrierKeys.TextContainsKey(normalized, r.CarrierKey));
            if (row != null)
            {
                if (_showLastKnown) SetMode(false);
            }
            else
            {
                row = _lastKnownRows.FirstOrDefault(r => CarrierKeys.TextContainsKey(normalized, r.CarrierKey));
                if (row != null && !_showLastKnown) SetMode(true);
            }

            if (row == null)
            {
                SelectedRow = null;
                StatusMessage = HasImports
                    ? "Zum Kostenträger „" + carrierText + "“ liegt noch kein importierter Stand vor."
                    : "Es wurde noch nichts importiert. Mit „Excel importieren“ lassen sich SAP-Kosten für bestehende Kostenträger laden.";
                return;
            }

            StatusMessage = null;
            ResetFilters();
            SelectedRow = Rows.FirstOrDefault(r => r.CarrierKey == row.CarrierKey);
        }

        // ===== Auswahl und Details =====

        private ControllingOverviewRow _selectedRow;
        public ControllingOverviewRow SelectedRow
        {
            get => _selectedRow;
            set
            {
                if (!SetProperty(ref _selectedRow, value)) return;
                CopyCarrierCommand.RaiseCanExecuteChanged();
                LoadDetails();
                RaisePropertyChanged(nameof(ShowDetailHours));
                RebuildChart();
                RaisePropertyChanged(nameof(HasSelection));
                RaisePropertyChanged(nameof(HasNoSelection));
            }
        }

        public bool HasSelection => _selectedRow != null;
        public bool HasNoSelection => _selectedRow == null;

        public ObservableCollection<DetailItem> DetailItems { get; } = new ObservableCollection<DetailItem>();
        public ObservableCollection<DetailItem> ContextItems { get; } = new ObservableCollection<DetailItem>();
        public ObservableCollection<ProjectRateItem> DetailProjects { get; } = new ObservableCollection<ProjectRateItem>();
        public ObservableCollection<DetailItem> ImportItems { get; } = new ObservableCollection<DetailItem>();
        public ObservableCollection<string> ImportWarnings { get; } = new ObservableCollection<string>();

        private bool _isContextExpanded;
        public bool IsContextExpanded { get => _isContextExpanded; set => SetProperty(ref _isContextExpanded, value); }

        private bool _hasContext;
        public bool HasContext
        {
            get => _hasContext;
            private set { if (SetProperty(ref _hasContext, value)) RaisePropertyChanged(nameof(HasNoContext)); }
        }

        public bool HasNoContext => !_hasContext;

        public ObservableCollection<CostHistoryItem> CostHistory { get; } = new ObservableCollection<CostHistoryItem>();
        public ObservableCollection<RevisionItem> Revisions { get; } = new ObservableCollection<RevisionItem>();

        private CostHistoryItem _selectedHistoryItem;
        public CostHistoryItem SelectedHistoryItem
        {
            get => _selectedHistoryItem;
            set { if (SetProperty(ref _selectedHistoryItem, value)) LoadRevisions(); }
        }

        private string _costHistoryNote;
        public string CostHistoryNote { get => _costHistoryNote; private set => SetProperty(ref _costHistoryNote, value); }

        public string CostDisclaimer =>
            "Geldbeträge aus den SAP-Importständen (" + (SelectedRow == null ? CostMath.CurrencyText(null) : CostMath.CurrencyText(SelectedRow.Observation.CurrencyCode)) +
            "), keine Stunden. Eine Differenz ist die Veränderung zwischen zwei Importständen – keine Buchung pro Tag oder Monat. Fehlende Werte bleiben „" +
            CostMath.MissingText + "“.";

        private void LoadDetails()
        {
            DetailItems.Clear();
            ContextItems.Clear();
            DetailProjects.Clear();
            ImportItems.Clear();
            ImportWarnings.Clear();
            CostHistory.Clear();
            Revisions.Clear();
            HoursProjects.Clear();
            HasContext = false;
            CostHistoryNote = null;

            var row = SelectedRow;
            if (row == null)
            {
                _hoursEntries = new List<LocalTimeEntry>();
                RecalculateHours();
                RaisePropertyChanged(nameof(CostDisclaimer));
                return;
            }

            var db = MainViewModel.DbInstance;
            var o = row.Observation;

            foreach (var p in row.LinkedProjects) DetailProjects.Add(CreateRateItem(p));

            DetailItems.Add(new DetailItem("Kostenträger (PSP-Element)", o.CarrierText));
            DetailItems.Add(new DetailItem("PSP-Name", o.PspName));
            DetailItems.Add(new DetailItem("SAP-Projekt-ID", o.ExternalParentId));
            DetailItems.Add(new DetailItem("SAP-Projekt", o.ParentName));
            DetailItems.Add(new DetailItem("Profitcenter", o.ProfitCenter));
            DetailItems.Add(new DetailItem("Lokale Projekte", row.LinkedProjectNames));
            DetailItems.Add(new DetailItem("Kunde (lokal)", row.LinkedCustomers));
            DetailItems.Add(new DetailItem("Kunde (SAP)", row.SapCustomer));
            DetailItems.Add(new DetailItem("SAP-Stand", row.SourceText));
            DetailItems.Add(new DetailItem("Importiert am", o.ImportedUtc.ToLocalTime().ToString("dd.MM.yyyy HH:mm", German)));
            DetailItems.Add(new DetailItem("Währung", CostMath.CurrencyText(o.CurrencyCode)));

            LoadContextItems(row);
            LoadCostHistory(db, row);
            LoadImportItems(db, row);

            foreach (var p in row.LinkedProjects)
                HoursProjects.Add(CreateHoursFilter(p));
            LoadHoursSource();

            RaisePropertyChanged(nameof(CostDisclaimer));
        }

        private HoursProjectFilter CreateHoursFilter(LocalProjectInfo project)
        {
            var filter = new HoursProjectFilter { Id = project.Id, Name = project.Name, IsSelected = true };
            filter.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(HoursProjectFilter.IsSelected) && !_loading) LoadHoursSource();
            };
            return filter;
        }

        private void LoadContextItems(ControllingOverviewRow row)
        {
            var c = row.Context;
            HasContext = c != null;
            if (c == null) return;

            Func<decimal?, string> money = v => v.HasValue ? v.Value.ToString("N2", German) : "Nicht verfügbar";
            Func<decimal?, string> ratio = v => v.HasValue ? CostMath.FormatPercent(v) : "Nicht verfügbar";
            Func<DateTime?, string> date = v => v.HasValue ? v.Value.ToString("dd.MM.yyyy", German) : "Nicht verfügbar";
            Func<string, string> text = v => string.IsNullOrWhiteSpace(v) ? "Nicht verfügbar" : v;

            ContextItems.Add(new DetailItem("Projekt", text(c.ParentName)));
            ContextItems.Add(new DetailItem("Projektleiter", text(c.Manager)));
            ContextItems.Add(new DetailItem("Projektstatus", text(c.Status)));
            ContextItems.Add(new DetailItem("Verantwortliche Kostenstelle", text(c.CostCenter)));
            ContextItems.Add(new DetailItem("Geplanter Start (Planungsdatum)", date(c.PlannedStart)));
            ContextItems.Add(new DetailItem("Geplantes Ende (Planungsdatum)", date(c.PlannedEnd)));
            ContextItems.Add(new DetailItem("Projekt: Auftragswert gesamt", money(c.OrderValueTotal)));
            ContextItems.Add(new DetailItem("Projekt: abgerechnet gesamt", money(c.InvoicedTotal)));
            ContextItems.Add(new DetailItem("Projekt: offen gesamt", money(c.OpenTotal)));
            ContextItems.Add(new DetailItem("Projekt: abgerechnet in %", ratio(c.InvoicedRatio)));
            ContextItems.Add(new DetailItem("Projekt: Fortschritt (kostenbasiert, nicht Stunden)", ratio(c.ProgressRatio)));
            ContextItems.Add(new DetailItem("Projekt: WIP (offen)", money(c.WipOpen)));
            ContextItems.Add(new DetailItem("Projekt: Auftragswert lfd. Jahr", money(c.OrderValueYear)));
            ContextItems.Add(new DetailItem("Projekt: offen lfd. Jahr", money(c.OpenYear)));
        }

        private void LoadCostHistory(TimeTracker db, ControllingOverviewRow row)
        {
            var history = db.GetControllingCarrierHistory(row.CarrierKey, row.Observation.CurrencyContext);
            ControllingObservation previous = null;

            foreach (var h in history)
            {
                CostHistory.Add(new CostHistoryItem
                {
                    Observation = h,
                    SourceText = h.SourceTimestamp.ToString("dd.MM.yyyy HH:mm", German),
                    PlannedText = CostMath.FormatMoney(h.PlannedCost),
                    ActualText = CostMath.FormatMoney(h.ActualCost),
                    RemainingText = CostMath.FormatMoney(h.RemainingCost),
                    ConsumptionText = h.Consumption.HasValue ? CostMath.FormatPercent(h.Consumption) : CostMath.NoBudgetText,
                    PlannedHoursText = row.FormatHours(h.PlannedCost),
                    ActualHoursText = row.FormatHours(h.ActualCost),
                    RemainingHoursText = row.FormatHours(h.RemainingCost),
                    PlanDeltaText = previous == null ? "–" : CostMath.FormatSignedMoney(CostMath.Delta(previous.PlannedCost, h.PlannedCost)),
                    ActualDeltaText = previous == null ? "–" : CostMath.FormatSignedMoney(CostMath.Delta(previous.ActualCost, h.ActualCost)),
                    FileName = h.FileName,
                    ImportedText = h.ImportedUtc.ToLocalTime().ToString("dd.MM.yyyy HH:mm", German),
                    RevisionText = h.RevisionCount > 1 ? h.RevisionCount.ToString(CultureInfo.InvariantCulture) + " Revisionen (Import #" + h.ImportId.ToString(CultureInfo.InvariantCulture) + " aktiv)" : "Import #" + h.ImportId.ToString(CultureInfo.InvariantCulture)
                });
                previous = h;
            }

            if (CostHistory.Count == 0) CostHistoryNote = "Kein Kostenverlauf vorhanden.";
            else if (CostHistory.Count == 1) CostHistoryNote = "Ein Importstand vorhanden – ein einzelner Punkt, keine rückwirkend erzeugte Linie.";
            else CostHistoryNote = CostHistory.Count.ToString(CultureInfo.InvariantCulture) + " Importstände, nach SAP-Datum geordnet.";

            SelectedHistoryItem = CostHistory.LastOrDefault();
        }

        private void LoadRevisions()
        {
            Revisions.Clear();
            var item = SelectedHistoryItem;
            if (item == null) return;

            var o = item.Observation;
            foreach (var r in MainViewModel.DbInstance.GetControllingCarrierRevisions(o.CarrierKey, o.SourceTimestamp, o.CurrencyContext))
            {
                Revisions.Add(new RevisionItem
                {
                    ImportText = "Import #" + r.ImportId.ToString(CultureInfo.InvariantCulture),
                    ImportedText = r.ImportedUtc.ToLocalTime().ToString("dd.MM.yyyy HH:mm", German),
                    FileName = r.FileName,
                    PlannedText = CostMath.FormatMoney(r.PlannedCost),
                    ActualText = CostMath.FormatMoney(r.ActualCost),
                    StateText = r.IsActive ? "Aktiv" : "Überholt"
                });
            }
        }

        private void LoadImportItems(TimeTracker db, ControllingOverviewRow row)
        {
            var o = row.Observation;
            var info = db.GetControllingImport(o.ImportId);
            if (info == null) return;

            ImportItems.Add(new DetailItem("Datei", info.FileName));
            ImportItems.Add(new DetailItem("Datei-Prüfsumme (SHA-256)", info.FileHash.Length > 16 ? info.FileHash.Substring(0, 16) + "…" : info.FileHash, info.FileHash));
            ImportItems.Add(new DetailItem("SAP-Stand", info.SourceTimestamp.ToString("dd.MM.yyyy HH:mm", German)));
            ImportItems.Add(new DetailItem("Herkunft des Stands", info.TimestampOrigin == TimestampOrigin.Workbook ? "aus der Datei" : "vom Benutzer angegeben"));
            ImportItems.Add(new DetailItem("Importiert am", info.ImportedLocal.ToString("dd.MM.yyyy HH:mm", German)));
            ImportItems.Add(new DetailItem("Währung", info.CurrencyText));
            ImportItems.Add(new DetailItem("Parser-Version", info.ParserVersion.ToString(CultureInfo.InvariantCulture)));
            ImportItems.Add(new DetailItem("Gültige PSP-Elemente in der Datei", info.ScannedCount.ToString(CultureInfo.InvariantCulture)));
            ImportItems.Add(new DetailItem("Übernommen", info.MatchedCount.ToString(CultureInfo.InvariantCulture)));
            ImportItems.Add(new DetailItem("Ignoriert (kein passender Kostenträger)", info.IgnoredCount.ToString(CultureInfo.InvariantCulture)));
            ImportItems.Add(new DetailItem("Quelle dieser Zeile", o.SourceSheet + ", Zeile " + o.SourceRow.ToString(CultureInfo.InvariantCulture) + " (Spalten D, E, F, G)"));
            ImportItems.Add(new DetailItem("Zuordnung", "Der Kostenträger des Projekts enthält die PSP-Element-ID"));

            foreach (var w in info.Warnings) ImportWarnings.Add(w);
        }

        private void CopyCarrier(object parameter)
        {
            var row = parameter as ControllingOverviewRow ?? SelectedRow;
            if (row == null) return;
            try
            {
                Clipboard.SetText(row.CarrierText);
                StatusMessage = "Kostenträger „" + row.CarrierText + "“ kopiert.";
            }
            catch (Exception)
            {
                StatusMessage = "Die Zwischenablage ist gerade nicht verfügbar.";
            }
        }

        // ===== Detailbereich: Tabs =====

        private string _detailTab = "cost";
        public bool IsCostTab { get => _detailTab == "cost"; set { if (value) SetTab("cost"); } }
        public bool IsHoursTab { get => _detailTab == "hours"; set { if (value) SetTab("hours"); } }
        public bool IsImportTab { get => _detailTab == "import"; set { if (value) SetTab("import"); } }

        private void SetTab(string tab)
        {
            if (_detailTab == tab) return;
            _detailTab = tab;
            RaisePropertyChanged(nameof(IsCostTab));
            RaisePropertyChanged(nameof(IsHoursTab));
            RaisePropertyChanged(nameof(IsImportTab));
        }

        // ===== Lokaler Stundenverlauf (aus den Buchungen der App, nicht aus SAP) =====

        public ObservableCollection<HoursProjectFilter> HoursProjects { get; } = new ObservableCollection<HoursProjectFilter>();
        public ObservableCollection<HoursBucket> HoursBuckets { get; } = new ObservableCollection<HoursBucket>();

        private List<LocalTimeEntry> _hoursEntries = new List<LocalTimeEntry>();

        private HoursBucketKind _hoursKind = HoursBucketKind.Week;
        public bool IsHoursDay { get => _hoursKind == HoursBucketKind.Day; set { if (value) SetHoursKind(HoursBucketKind.Day); } }
        public bool IsHoursWeek { get => _hoursKind == HoursBucketKind.Week; set { if (value) SetHoursKind(HoursBucketKind.Week); } }
        public bool IsHoursMonth { get => _hoursKind == HoursBucketKind.Month; set { if (value) SetHoursKind(HoursBucketKind.Month); } }

        private void SetHoursKind(HoursBucketKind kind)
        {
            if (_hoursKind == kind) return;
            _hoursKind = kind;
            RaisePropertyChanged(nameof(IsHoursDay));
            RaisePropertyChanged(nameof(IsHoursWeek));
            RaisePropertyChanged(nameof(IsHoursMonth));
            RecalculateHours();
        }

        private bool _showCumulative = true;
        public bool ShowCumulative { get => _showCumulative; set => SetProperty(ref _showCumulative, value); }

        private string _hoursStartText;
        public string HoursStartText { get => _hoursStartText; set { if (SetProperty(ref _hoursStartText, value)) RecalculateHours(); } }

        private string _hoursEndText;
        public string HoursEndText { get => _hoursEndText; set { if (SetProperty(ref _hoursEndText, value)) RecalculateHours(); } }

        private string _hoursTotalText;
        public string HoursTotalText { get => _hoursTotalText; private set => SetProperty(ref _hoursTotalText, value); }

        private string _hoursMessage;
        /// <summary>Leerzustand oder Hinweis zu ungültigen Buchungen bzw. Datumsangaben.</summary>
        public string HoursMessage { get => _hoursMessage; private set { if (SetProperty(ref _hoursMessage, value)) RaisePropertyChanged(nameof(HasHoursMessage)); } }
        public bool HasHoursMessage => !string.IsNullOrEmpty(_hoursMessage);

        private bool _hasHours;
        public bool HasHours { get => _hasHours; private set => SetProperty(ref _hasHours, value); }

        private string _hoursCompareText;
        /// <summary>Stellt rechnerische Stunden aus den Ist-Kosten und lokal gebuchte Stunden nebeneinander – ohne sie gleichzusetzen.</summary>
        public string HoursCompareText { get => _hoursCompareText; private set { if (SetProperty(ref _hoursCompareText, value)) RaisePropertyChanged(nameof(HasHoursCompare)); } }
        public bool HasHoursCompare => !string.IsNullOrEmpty(_hoursCompareText);

        private void UpdateHoursComparison()
        {
            var row = SelectedRow;
            if (row == null || !_showHoursColumns || !row.HourlyRate.HasValue || !row.ActualCost.HasValue)
            {
                HoursCompareText = null;
                return;
            }

            var booked = LocalHoursCalculator.Calculate(_hoursEntries, HoursBucketKind.Month).TotalMinutes;
            HoursCompareText =
                "Zum Vergleich: Die Ist-Kosten entsprechen bei " + CostMath.FormatRate(row.HourlyRate) + " pro Stunde rechnerisch " +
                row.FormatHours(row.ActualCost) + " (aus Geld umgerechnet, nicht gebucht). Lokal gebucht sind in allen Zeiträumen " +
                LocalHoursCalculator.FormatDecimalHours(booked) + ". Das sind zwei getrennte Größen, keine Abstimmung.";
        }

        public string HoursDisclaimer =>
            "Lokal erfasste Stunden: Summe der Buchungen in dieser App für die verknüpften Projekte – nicht aus SAP und nicht mit SAP abgestimmt. " +
            "Parallele Buchungen zählen mehrfach. Die Zuordnung folgt dem aktuellen Kostenträger der Projekte; frühere Zuordnungen werden nicht rekonstruiert.";

        private void LoadHoursSource()
        {
            var ids = HoursProjects.Where(p => p.IsSelected).Select(p => p.Id).ToList();
            _hoursEntries = ids.Count == 0 ? new List<LocalTimeEntry>() : MainViewModel.DbInstance.GetTimeEntriesForProjectIds(ids);
            RecalculateHours();
            RebuildChart();
        }

        private static bool TryParseDate(string text, out DateTime? value)
        {
            value = null;
            if (string.IsNullOrWhiteSpace(text)) return true;

            DateTime parsed;
            if (!DateTime.TryParseExact(text.Trim(), "dd.MM.yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out parsed)) return false;
            value = parsed;
            return true;
        }

        private void RecalculateHours()
        {
            HoursBuckets.Clear();

            DateTime? from, to;
            var fromOk = TryParseDate(_hoursStartText, out from);
            var toOk = TryParseDate(_hoursEndText, out to);

            if (!fromOk || !toOk)
            {
                HoursTotalText = string.Empty;
                HasHours = false;
                HoursMessage = "Bitte Datumsangaben im Format TT.MM.JJJJ eingeben.";
                return;
            }

            var result = LocalHoursCalculator.Calculate(_hoursEntries, _hoursKind, from, to);
            foreach (var bucket in result.Buckets) HoursBuckets.Add(bucket);

            HasHours = result.EntryCount > 0;
            HoursTotalText = result.EntryCount == 0
                ? string.Empty
                : LocalHoursCalculator.FormatDecimalHours(result.TotalMinutes) + " (" + LocalHoursCalculator.FormatClock(result.TotalMinutes) + ") in " +
                  result.EntryCount.ToString(CultureInfo.InvariantCulture) + " Buchungen – lokal erfasst";

            UpdateHoursComparison();

            if (SelectedRow == null) HoursMessage = null;
            else if (result.EntryCount == 0) HoursMessage = "Keine lokalen Buchungen vorhanden" + (from.HasValue || to.HasValue ? " (im gewählten Zeitraum)." : ".");
            else if (result.InvalidEntries.Count > 0)
                HoursMessage = result.InvalidEntries.Count.ToString(CultureInfo.InvariantCulture) + " Buchung(en) mit nicht lesbaren Zeiten wurden nicht mitgezählt.";
            else HoursMessage = null;
        }

        private void OpenDay(object parameter)
        {
            var bucket = parameter as HoursBucket;
            if (bucket == null || _openDay == null) return;
            _openDay(bucket.Start); // nur Ansicht: öffnet die Stundenbuchung des Tages, ändert nichts
        }

        // ===== Import: Datei wählen, Vorschau, bestätigen =====

        private bool _isBusy;
        public bool IsBusy
        {
            get => _isBusy;
            private set
            {
                if (!SetProperty(ref _isBusy, value)) return;
                ImportCommand.RaiseCanExecuteChanged();
                CancelPreviewCommand.RaiseCanExecuteChanged();
                CancelBusyCommand.RaiseCanExecuteChanged();
                DeleteImportCommand.RaiseCanExecuteChanged();
                ConfirmImportCommand.RaiseCanExecuteChanged();
            }
        }

        private string _busyText;
        public string BusyText { get => _busyText; private set => SetProperty(ref _busyText, value); }

        private bool _isPreviewOpen;
        public bool IsPreviewOpen { get => _isPreviewOpen; private set => SetProperty(ref _isPreviewOpen, value); }

        private ControllingImportPreview _preview;
        public ControllingImportPreview Preview { get => _preview; private set => SetProperty(ref _preview, value); }

        public ObservableCollection<ControllingPreviewRow> PreviewRows { get; } = new ObservableCollection<ControllingPreviewRow>();
        public ObservableCollection<ImportDiagnostic> PreviewDiagnostics { get; } = new ObservableCollection<ImportDiagnostic>();
        public ObservableCollection<TimestampChoice> TimestampChoices { get; } = new ObservableCollection<TimestampChoice>();

        private TimestampChoice _selectedTimestampChoice;
        public TimestampChoice SelectedTimestampChoice
        {
            get => _selectedTimestampChoice;
            set { if (SetProperty(ref _selectedTimestampChoice, value)) OnPreviewInputChanged(); }
        }

        private string _userTimestampText;
        public string UserTimestampText
        {
            get => _userTimestampText;
            set { if (SetProperty(ref _userTimestampText, value)) OnPreviewInputChanged(); }
        }

        private string _currencyCodeText;
        public string CurrencyCodeText
        {
            get => _currencyCodeText;
            set { if (SetProperty(ref _currencyCodeText, value)) OnPreviewInputChanged(); }
        }

        public bool PreviewHasDiagnostics => PreviewDiagnostics.Count > 0;

        public string PreviewFileText => Preview == null ? string.Empty : Preview.FileName + " (" + (Preview.FileSize / 1024).ToString("N0", German) + " KiB)";

        public string PreviewSummaryText
        {
            get
            {
                if (Preview == null) return string.Empty;
                return Preview.ScannedCount.ToString(CultureInfo.InvariantCulture) + " gültige PSP-Elemente gelesen · " +
                       Preview.MatchedCount.ToString(CultureInfo.InvariantCulture) + " passend zu Kostenträgern (" +
                       Preview.LinkedProjectCount.ToString(CultureInfo.InvariantCulture) + " lokale Projekte) · " +
                       Preview.IgnoredCount.ToString(CultureInfo.InvariantCulture) + " ignoriert";
            }
        }

        public string PreviewMissingText
        {
            get
            {
                if (Preview == null || Preview.MissingCarriers.Count == 0) return null;
                return Preview.MissingCarriers.Count.ToString(CultureInfo.InvariantCulture) + " Kostenträger kommen in der Datei nicht vor: " +
                       string.Join(", ", Preview.MissingCarriers.Take(8)) + (Preview.MissingCarriers.Count > 8 ? " …" : string.Empty);
            }
        }

        public bool HasPreviewMissing => !string.IsNullOrEmpty(PreviewMissingText);
        public int PreviewWarningCount => Preview == null ? 0 : Preview.WarningCount;
        public int PreviewErrorCount => Preview == null ? 0 : Preview.Diagnostics.Count(d => d.IsError);
        public bool HasPreviewErrors => PreviewErrorCount > 0;

        /// <summary>Zeigt, welcher SAP-Stand verwendet wird, bzw. was noch fehlt.</summary>
        public string TimestampStatusText
        {
            get
            {
                if (Preview == null || Preview.Read == null) return string.Empty;
                if (NeedsTimestampInput) return "Die Datei nennt keinen SAP-Stand. Bitte Stand angeben und bestätigen" + (FileMonthHint == null ? "." : " – " + FileMonthHint);
                if (NeedsTimestampChoice) return "Die Blätter nennen unterschiedliche Stände. Bitte den maßgeblichen auswählen.";
                return "SAP-Stand aus der Datei: " + Preview.WorkbookTimestamp.Value.ToString("dd.MM.yyyy HH:mm", German);
            }
        }

        public bool NeedsTimestampInput => Preview != null && Preview.HasNoTimestamp;
        public bool NeedsTimestampChoice => Preview != null && !Preview.HasNoTimestamp && Preview.HasTimestampConflict;

        /// <summary>Hinweis aus dem Dateinamen (z. B. „2026-11“) – nur eine Anregung, kein Beleg für Tag und Uhrzeit.</summary>
        public string FileMonthHint
        {
            get
            {
                if (Preview == null) return null;
                var m = Regex.Match(Preview.FileName ?? string.Empty, @"(20\d{2})[-_.]?(0[1-9]|1[0-2])");
                return m.Success ? "der Dateiname deutet auf " + m.Groups[2].Value + "/" + m.Groups[1].Value + " hin (kein Beleg für Tag und Uhrzeit)" : null;
            }
        }

        public string CurrencyHintText
        {
            get
            {
                string code;
                if (!TryGetCurrency(out code)) return "Bitte einen dreistelligen Währungscode eingeben (z. B. EUR) oder das Feld leer lassen.";
                return code == null
                    ? "Ohne Angabe bleibt die Währung „" + CostMath.CurrencyText(null) + "“. Sie wird nicht automatisch als EUR bezeichnet."
                    : "Beträge werden als " + code + " geführt; je Währung gibt es einen eigenen Verlauf.";
            }
        }

        public bool CanConfirmImport
        {
            get
            {
                DateTime ts;
                TimestampOrigin origin;
                string code;
                return !IsBusy && Preview != null && Preview.IsImportable && TryResolveTimestamp(out ts, out origin) && TryGetCurrency(out code);
            }
        }

        private bool TryGetCurrency(out string code)
        {
            code = null;
            var text = _currencyCodeText == null ? string.Empty : _currencyCodeText.Trim();
            if (text.Length == 0) return true;
            if (!Regex.IsMatch(text, "^[A-Za-z]{3}$")) return false;
            code = text.ToUpperInvariant();
            return true;
        }

        private bool TryResolveTimestamp(out DateTime timestamp, out TimestampOrigin origin)
        {
            timestamp = default(DateTime);
            origin = TimestampOrigin.Workbook;
            if (Preview == null) return false;

            if (Preview.HasNoTimestamp)
            {
                DateTime parsed;
                var text = (_userTimestampText ?? string.Empty).Trim();
                if (!DateTime.TryParseExact(text, new[] { "dd.MM.yyyy HH:mm", "dd.MM.yyyy HH:mm:ss" }, CultureInfo.InvariantCulture, DateTimeStyles.None, out parsed))
                    return false;
                timestamp = parsed;
                origin = TimestampOrigin.User;
                return true;
            }

            if (Preview.HasTimestampConflict)
            {
                if (SelectedTimestampChoice == null) return false;
                timestamp = SelectedTimestampChoice.Value;
                return true;
            }

            if (!Preview.WorkbookTimestamp.HasValue) return false;
            timestamp = Preview.WorkbookTimestamp.Value;
            return true;
        }

        private void OnPreviewInputChanged()
        {
            RaisePropertyChanged(nameof(CurrencyHintText));
            RaisePropertyChanged(nameof(CanConfirmImport));
            RaisePropertyChanged(nameof(TimestampStatusText));
            ConfirmImportCommand.RaiseCanExecuteChanged();
            RefreshComparison();
        }

        private async void StartImport()
        {
            var path = ChooseFile();
            if (path == null) return;

            await LoadPreviewAsync(path, true);
        }

        /// <summary>
        /// Dateiauswahl über den Windows-Forms-Dialog: Er zeigt die gewohnte Explorer-Ansicht (anders als der
        /// WPF-Dialog) und hängt am Hauptfenster. Gibt den Pfad oder <c>null</c> bei Abbruch zurück.
        /// </summary>
        private static string ChooseFile()
        {
            using (var dialog = new System.Windows.Forms.OpenFileDialog
            {
                Title = "SAP-Projektcontrolling-Datei wählen",
                Filter = "Excel-Arbeitsmappe (*.xlsx)|*.xlsx",
                CheckFileExists = true,
                Multiselect = false,
                RestoreDirectory = true
            })
            {
                var result = dialog.ShowDialog(new WindowOwner(Application.Current == null ? null : Application.Current.MainWindow));
                return result == System.Windows.Forms.DialogResult.OK ? dialog.FileName : null;
            }
        }

        /// <summary>Macht ein WPF-Fenster zum Besitzer eines Windows-Forms-Dialogs.</summary>
        private sealed class WindowOwner : System.Windows.Forms.IWin32Window
        {
            public WindowOwner(Window window)
            {
                Handle = window == null ? IntPtr.Zero : new WindowInteropHelper(window).Handle;
            }

            public IntPtr Handle { get; }
        }

        /// <summary>
        /// Liest die Datei im Hintergrund und baut die Vorschau. Die Datenbank wird dabei nicht angefasst: Die lokalen
        /// Projekte werden vorher auf diesem (dem besitzenden) Thread gelesen.
        /// </summary>
        private async Task LoadPreviewAsync(string path, bool resetInputs)
        {
            _cancellation?.Cancel();
            var cancellation = new CancellationTokenSource();
            _cancellation = cancellation;

            IsBusy = true;
            BusyText = "Datei wird gelesen …";

            try
            {
                var projects = MainViewModel.DbInstance.GetLocalProjectInfos();
                var preview = await Task.Run(() => ControllingImportService.BuildPreviewFromFile(path, projects, null, cancellation.Token), cancellation.Token);

                if (cancellation.IsCancellationRequested) return;
                ShowPreview(preview, path, resetInputs);
            }
            catch (OperationCanceledException)
            {
                // Abbruch vor dem Speichern: nichts wurde geschrieben.
            }
            catch (Exception ex)
            {
                MessageBox.Show("Die Datei konnte nicht ausgewertet werden:\n" + ex.Message, "Projektcontrolling", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            finally
            {
                if (ReferenceEquals(_cancellation, cancellation)) IsBusy = false;
            }
        }

        private void ShowPreview(ControllingImportPreview preview, string path, bool resetInputs)
        {
            _previewPath = path;
            Preview = preview;

            if (resetInputs)
            {
                _userTimestampText = string.Empty;
                // Die Währung, die der Benutzer beim letzten Import gewählt hat, wird vorbelegt (nie stillschweigend EUR).
                var lastImport = Imports.FirstOrDefault(i => !string.IsNullOrEmpty(i.CurrencyCode));
                _currencyCodeText = lastImport == null ? string.Empty : lastImport.CurrencyCode;
                RaisePropertyChanged(nameof(UserTimestampText));
                RaisePropertyChanged(nameof(CurrencyCodeText));
            }

            TimestampChoices.Clear();
            _selectedTimestampChoice = null;
            RaisePropertyChanged(nameof(SelectedTimestampChoice));
            if (preview.Read != null && preview.HasTimestampConflict)
            {
                foreach (var c in preview.Read.Timestamps)
                    TimestampChoices.Add(new TimestampChoice { Value = c.Value, Label = c.Sheet + ": " + c.Value.ToString("dd.MM.yyyy HH:mm", German) });
            }

            PreviewDiagnostics.Clear();
            foreach (var d in preview.Diagnostics.OrderByDescending(d => d.Severity)) PreviewDiagnostics.Add(d);

            IsPreviewOpen = true;
            RaisePreviewChanged();
            RefreshComparison();
        }

        private void RaisePreviewChanged()
        {
            RaisePropertyChanged(nameof(PreviewHasDiagnostics));
            RaisePropertyChanged(nameof(PreviewFileText));
            RaisePropertyChanged(nameof(PreviewSummaryText));
            RaisePropertyChanged(nameof(PreviewMissingText));
            RaisePropertyChanged(nameof(HasPreviewMissing));
            RaisePropertyChanged(nameof(PreviewWarningCount));
            RaisePropertyChanged(nameof(PreviewErrorCount));
            RaisePropertyChanged(nameof(HasPreviewErrors));
            RaisePropertyChanged(nameof(TimestampStatusText));
            RaisePropertyChanged(nameof(NeedsTimestampInput));
            RaisePropertyChanged(nameof(NeedsTimestampChoice));
            RaisePropertyChanged(nameof(FileMonthHint));
            RaisePropertyChanged(nameof(CurrencyHintText));
            RaisePropertyChanged(nameof(CanConfirmImport));
            ConfirmImportCommand.RaiseCanExecuteChanged();
        }

        /// <summary>Vergleicht mit dem letzten vergleichbaren Stand (gleicher Währungskontext, nicht neuer als dieser Import).</summary>
        private void RefreshComparison()
        {
            PreviewRows.Clear();
            if (Preview == null) return;

            DateTime ts;
            TimestampOrigin origin;
            string code;
            if (TryResolveTimestamp(out ts, out origin) && TryGetCurrency(out code))
            {
                try
                {
                    var previous = MainViewModel.DbInstance.GetLatestControllingObservations(
                        Preview.Rows.Select(r => r.CarrierKey), CostMath.CurrencyContext(code), ts);
                    ControllingImportService.ApplyComparison(Preview, previous, ts);
                }
                catch (Exception)
                {
                    ControllingImportService.ApplyComparison(Preview, null, ts);
                }
            }
            else
            {
                ControllingImportService.ApplyComparison(Preview, null, default(DateTime));
            }

            foreach (var row in Preview.Rows) PreviewRows.Add(row);
        }

        private void ClosePreview()
        {
            _cancellation?.Cancel();
            _previewPath = null;
            Preview = null;
            PreviewRows.Clear();
            PreviewDiagnostics.Clear();
            TimestampChoices.Clear();
            IsPreviewOpen = false;
            IsBusy = false;
            RaisePreviewChanged();
        }

        private async void ConfirmImport()
        {
            if (Preview == null || IsBusy) return;

            DateTime ts;
            TimestampOrigin origin;
            string code;
            if (!TryResolveTimestamp(out ts, out origin) || !TryGetCurrency(out code)) return;

            var db = MainViewModel.DbInstance;

            // Die Berechtigung wird vor dem Speichern neu gelesen: Haben sich die Kostenträger seit der Vorschau geändert,
            // wird die Vorschau neu aufgebaut und muss erneut bestätigt werden.
            var projects = db.GetLocalProjectInfos();
            var currentHash = new CarrierAllowlist(projects.Select(p => p.Carrier)).Hash;
            if (currentHash != Preview.AllowlistHash)
            {
                MessageBox.Show(
                    "Die Projekte bzw. Kostenträger haben sich seit der Vorschau geändert. Die Vorschau wird neu aufgebaut; bitte danach erneut bestätigen.",
                    "Projektcontrolling", MessageBoxButton.OK, MessageBoxImage.Information);
                await LoadPreviewAsync(_previewPath, false);
                return;
            }

            ControllingSaveResult result;
            try
            {
                var draft = Preview.BuildDraft(ts, origin, code);
                result = db.SaveControllingImport(draft);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Der Import ist fehlgeschlagen. Es wurde nichts gespeichert.\n\n" + ex.Message,
                    "Projektcontrolling", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var matched = Preview.MatchedCount;
            ClosePreview();

            // Ab hier ist der Import gespeichert; Fehler beim Aktualisieren der Anzeige machen ihn nicht rückgängig.
            try
            {
                SelectImportAfterCommit(result.ImportId, ts);
                StatusMessage = result.Status == ControllingSaveStatus.AlreadyImported
                    ? "Dieser Stand wurde bereits importiert (Import #" + result.ImportId.ToString(CultureInfo.InvariantCulture) + "); es wurde nichts doppelt gespeichert."
                    : "Import gespeichert: " + matched.ToString(CultureInfo.InvariantCulture) + " Kostenträger, SAP-Stand " + ts.ToString("dd.MM.yyyy HH:mm", German) + ".";
            }
            catch (Exception ex)
            {
                StatusMessage = "Der Import wurde gespeichert, die Anzeige konnte aber nicht aktualisiert werden: " + ex.Message;
            }
        }

        private void SelectImportAfterCommit(int importId, DateTime sourceTimestamp)
        {
            _loading = true;
            try
            {
                Imports.Clear();
                foreach (var info in MainViewModel.DbInstance.GetControllingImports()) Imports.Add(info);
                // Maßgeblich bleibt der neueste Quellstand: Ein nachträglich importierter älterer Stand verdrängt ihn nicht.
                SelectedImport = Imports.FirstOrDefault();
            }
            finally
            {
                _loading = false;
            }

            LoadRows();
            RaiseHeaderChanged();
        }

        private void DeleteSelectedImport()
        {
            var import = SelectedImport;
            if (import == null) return;

            var answer = MessageBox.Show(
                "Import #" + import.Id.ToString(CultureInfo.InvariantCulture) + " (" + import.Label + ") löschen?\n\n" +
                "Frühere Revisionen desselben Stands werden dadurch wieder aktiv. Lokale Projekte und Buchungen bleiben unverändert.",
                "Import löschen", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (answer != MessageBoxResult.Yes) return;

            try
            {
                MainViewModel.DbInstance.DeleteControllingImport(import.Id);
                Refresh();
                StatusMessage = "Import #" + import.Id.ToString(CultureInfo.InvariantCulture) + " wurde gelöscht.";
            }
            catch (Exception ex)
            {
                MessageBox.Show("Der Import konnte nicht gelöscht werden:\n" + ex.Message, "Projektcontrolling", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
    }
}
