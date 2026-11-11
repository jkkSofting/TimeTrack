using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Data;
using Zeitmanagement.Helpers;
using Zeitmanagement.MVVM;

namespace Zeitmanagement.ViewModel
{
    /// <summary>
    /// Seite "Tätigkeitsnachweis": Zeitraum und Projekte wählen, sehen was exportiert wird (Kennzahlen, Warnungen, Zeilen) und als
    /// PDF oder CSV speichern. Die Datenbank wird nur auf dem UI-Thread gelesen; Aufbau und Rendern laufen mit fertigen Daten im
    /// Hintergrund.
    /// </summary>
    internal sealed class ExportViewModel : BaseViewModel
    {
        private const string AllCustomers = "Alle Kunden";

        private readonly string _appVersion;
        private int _suspend;

        private List<TimeTracker.ProjectRow> _projects = new List<TimeTracker.ProjectRow>();
        private List<TimeTracker.TimeEntryRow> _entries = new List<TimeTracker.TimeEntryRow>();
        private ExportReport _report;
        private readonly HashSet<string> _selectedNames = new HashSet<string>(StringComparer.Ordinal);

        public ExportViewModel()
        {
            _appVersion = Assembly.GetExecutingAssembly().GetName().Version.ToString();

            Presets = ExportPeriod.Keys.Select(k => new ExportPresetItem(k, ExportPeriod.Text(k))).ToList();
            RoundingChoices = new List<ExportRoundingItem>
            {
                new ExportRoundingItem(0, "Nicht runden"),
                new ExportRoundingItem(5, "Auf 5 Minuten runden"),
                new ExportRoundingItem(10, "Auf 10 Minuten runden"),
                new ExportRoundingItem(15, "Auf 15 Minuten runden"),
                new ExportRoundingItem(30, "Auf 30 Minuten runden")
            };
            _selectedRounding = RoundingChoices[0];

            ProjectsView = CollectionViewSource.GetDefaultView(ProjectItems);
            ProjectsView.Filter = FilterProject;

            _suspend++;
            ApplyPreset(ExportPeriod.ThisMonth);
            _issuerBlock = ReadSetting(() => Properties.Settings.Default.ExportIssuerBlock);
            _suspend--;

            RefreshCommand = new DelegateCommand(_ => Refresh());
            SelectAllVisibleCommand = new DelegateCommand(_ => SetVisibleSelection(true));
            ClearSelectionCommand = new DelegateCommand(_ => ClearSelection());
            ToggleMetaPanelCommand = new DelegateCommand(_ => IsMetaPanelOpen = !IsMetaPanelOpen);
            ExportPdfCommand = new DelegateCommand(async _ => await ExportAsync(true), _ => CanExport);
            ExportCsvCommand = new DelegateCommand(async _ => await ExportAsync(false), _ => CanExport);
            OpenResultCommand = new DelegateCommand(_ => OpenResult(), _ => HasResult);
            ShowResultInFolderCommand = new DelegateCommand(_ => ShowResultInFolder(), _ => HasResult);
        }

        // ---------- Zeitraum ----------

        public IReadOnlyList<ExportPresetItem> Presets { get; }

        private ExportPresetItem _selectedPreset;
        public ExportPresetItem SelectedPreset
        {
            get => _selectedPreset;
            set
            {
                if (value == null || !SetProperty(ref _selectedPreset, value)) return;
                if (value.Key != ExportPeriod.Custom) ApplyPreset(value.Key);
            }
        }

        private bool _applyingPreset;
        private DateTime? _from;
        private DateTime? _to;

        public DateTime? From
        {
            get => _from;
            set
            {
                if (!SetProperty(ref _from, value)) return;
                OnRangeEdited();
            }
        }

        public DateTime? To
        {
            get => _to;
            set
            {
                if (!SetProperty(ref _to, value)) return;
                OnRangeEdited();
            }
        }

        /// <summary>Warum der Zeitraum nicht benutzt werden kann; leer, wenn er in Ordnung ist.</summary>
        public string RangeError
        {
            get
            {
                if (!_from.HasValue || !_to.HasValue) return "Bitte „Von“ und „Bis“ wählen.";
                if (_from.Value.Date > _to.Value.Date) return "„Von“ liegt nach „Bis“.";
                return string.Empty;
            }
        }

        public bool HasRangeError => RangeError.Length > 0;

        private void ApplyPreset(string key)
        {
            DateTime from, to;
            if (!ExportPeriod.TryResolve(key, DateTime.Today, out from, out to)) return;

            _applyingPreset = true;
            try
            {
                _selectedPreset = Presets.First(p => p.Key == key);
                RaisePropertyChanged(nameof(SelectedPreset));
                _suspend++;
                From = from;
                To = to;
                _suspend--;
            }
            finally
            {
                _applyingPreset = false;
            }

            OnRangeChanged();
        }

        private void OnRangeEdited()
        {
            if (_applyingPreset) return;

            // Wer die Daten selbst ändert, wählt "Benutzerdefiniert".
            var custom = Presets.First(p => p.Key == ExportPeriod.Custom);
            if (!ReferenceEquals(_selectedPreset, custom))
            {
                _selectedPreset = custom;
                RaisePropertyChanged(nameof(SelectedPreset));
            }
            OnRangeChanged();
        }

        private void OnRangeChanged()
        {
            RaisePropertyChanged(nameof(RangeError));
            RaisePropertyChanged(nameof(HasRangeError));
            if (_suspend > 0) return;
            LoadEntries();
            Rebuild();
        }

        // ---------- Projekte ----------

        public ObservableCollection<ExportProjectItemVM> ProjectItems { get; } = new ObservableCollection<ExportProjectItemVM>();
        public ICollectionView ProjectsView { get; }
        public ObservableCollection<string> Customers { get; } = new ObservableCollection<string>();

        private string _searchText;
        public string SearchText
        {
            get => _searchText;
            set
            {
                if (SetProperty(ref _searchText, value)) ProjectsView?.Refresh();
            }
        }

        private string _customerFilter = AllCustomers;
        public string CustomerFilter
        {
            get => _customerFilter;
            set
            {
                if (SetProperty(ref _customerFilter, value ?? AllCustomers)) ProjectsView?.Refresh();
            }
        }

        public string ProjectSelectionText
        {
            get
            {
                var n = ProjectItems.Count(p => p.IsSelected);
                return ProjectItems.Count == 0 ? "Noch keine Projekte angelegt" : n + " von " + ProjectItems.Count + " Projekten gewählt";
            }
        }

        public bool HasProjects => ProjectItems.Count > 0;

        private bool FilterProject(object o)
        {
            var item = o as ExportProjectItemVM;
            if (item == null) return false;
            if (!string.IsNullOrEmpty(_customerFilter) && _customerFilter != AllCustomers &&
                !string.Equals(item.Customer, _customerFilter, StringComparison.Ordinal)) return false;
            if (string.IsNullOrWhiteSpace(_searchText)) return true;

            var q = _searchText.Trim();
            return Contains(item.Name, q) || Contains(item.Customer, q) || Contains(item.Carrier, q);
        }

        private static bool Contains(string text, string query)
        {
            return text != null && text.IndexOf(query, StringComparison.CurrentCultureIgnoreCase) >= 0;
        }

        private void SetVisibleSelection(bool selected)
        {
            _suspend++;
            foreach (var item in ProjectsView.Cast<ExportProjectItemVM>().ToList()) item.IsSelected = selected;
            _suspend--;
            OnSelectionChanged();
        }

        private void ClearSelection()
        {
            _suspend++;
            foreach (var item in ProjectItems) item.IsSelected = false;
            _suspend--;
            OnSelectionChanged();
        }

        private void OnItemSelectionChanged(ExportProjectItemVM item)
        {
            if (item.IsSelected) _selectedNames.Add(item.Name); else _selectedNames.Remove(item.Name);
            if (_suspend > 0) return;
            OnSelectionChanged();
        }

        private void OnSelectionChanged()
        {
            _selectedNames.Clear();
            foreach (var p in ProjectItems.Where(p => p.IsSelected)) _selectedNames.Add(p.Name);
            RaisePropertyChanged(nameof(ProjectSelectionText));
            Rebuild();
        }

        /// <summary>Öffnet die Seite mit genau diesem Projekt gewählt (Knopf in der Projektliste).</summary>
        public void SelectOnly(string projectName)
        {
            _selectedNames.Clear();
            if (!string.IsNullOrEmpty(projectName)) _selectedNames.Add(projectName);
            _searchText = string.Empty;
            _customerFilter = AllCustomers;
            RaisePropertyChanged(nameof(SearchText));
            RaisePropertyChanged(nameof(CustomerFilter));
        }

        // ---------- Optionen, die den Bericht ändern ----------

        private ExportProjectKind _kind = ExportProjectKind.All;
        public bool IsKindAll { get => _kind == ExportProjectKind.All; set { if (value) SetKind(ExportProjectKind.All); } }
        public bool IsKindExternal { get => _kind == ExportProjectKind.ExternalOnly; set { if (value) SetKind(ExportProjectKind.ExternalOnly); } }
        public bool IsKindInternal { get => _kind == ExportProjectKind.InternalOnly; set { if (value) SetKind(ExportProjectKind.InternalOnly); } }

        private void SetKind(ExportProjectKind kind)
        {
            if (_kind == kind) return;
            _kind = kind;
            RaisePropertyChanged(nameof(IsKindAll));
            RaisePropertyChanged(nameof(IsKindExternal));
            RaisePropertyChanged(nameof(IsKindInternal));
            Rebuild();
        }

        private ExportGrouping _grouping = ExportGrouping.ByDay;
        public bool IsGroupByDay { get => _grouping == ExportGrouping.ByDay; set { if (value) SetGrouping(ExportGrouping.ByDay); } }
        public bool IsGroupByProject { get => _grouping == ExportGrouping.ByProject; set { if (value) SetGrouping(ExportGrouping.ByProject); } }
        public bool IsGroupByWeek { get => _grouping == ExportGrouping.ByWeek; set { if (value) SetGrouping(ExportGrouping.ByWeek); } }

        private void SetGrouping(ExportGrouping grouping)
        {
            if (_grouping == grouping) return;
            _grouping = grouping;
            RaisePropertyChanged(nameof(IsGroupByDay));
            RaisePropertyChanged(nameof(IsGroupByProject));
            RaisePropertyChanged(nameof(IsGroupByWeek));
            Rebuild();
        }

        private ExportDurationFormat _durationFormat = ExportDurationFormat.Decimal;
        public bool IsDurationDecimal { get => _durationFormat == ExportDurationFormat.Decimal; set { if (value) SetDuration(ExportDurationFormat.Decimal); } }
        public bool IsDurationClock { get => _durationFormat == ExportDurationFormat.Clock; set { if (value) SetDuration(ExportDurationFormat.Clock); } }
        public bool IsDurationBoth { get => _durationFormat == ExportDurationFormat.Both; set { if (value) SetDuration(ExportDurationFormat.Both); } }

        private void SetDuration(ExportDurationFormat format)
        {
            if (_durationFormat == format) return;
            _durationFormat = format;
            RaisePropertyChanged(nameof(IsDurationDecimal));
            RaisePropertyChanged(nameof(IsDurationClock));
            RaisePropertyChanged(nameof(IsDurationBoth));
            Rebuild();
        }

        private bool _showTimes = true;
        public bool ShowTimes { get => _showTimes; set { if (SetProperty(ref _showTimes, value)) Rebuild(); } }

        private bool _alwaysShowProjectColumn;
        public bool AlwaysShowProjectColumn { get => _alwaysShowProjectColumn; set { if (SetProperty(ref _alwaysShowProjectColumn, value)) Rebuild(); } }

        private bool _showCarrierColumn;
        public bool ShowCarrierColumn { get => _showCarrierColumn; set { if (SetProperty(ref _showCarrierColumn, value)) Rebuild(); } }

        public IReadOnlyList<ExportRoundingItem> RoundingChoices { get; }
        private ExportRoundingItem _selectedRounding;
        public ExportRoundingItem SelectedRounding
        {
            get => _selectedRounding;
            set { if (value != null && SetProperty(ref _selectedRounding, value)) Rebuild(); }
        }

        // ---------- Angaben, die nur das Dokument betreffen ----------

        private string _title = "Tätigkeitsnachweis";
        public string Title { get => _title; set => SetProperty(ref _title, value); }

        private string _note;
        public string Note { get => _note; set => SetProperty(ref _note, value); }

        private bool _landscape;
        public bool Landscape { get => _landscape; set => SetProperty(ref _landscape, value); }

        private bool _showSignature = true;
        public bool ShowSignature { get => _showSignature; set => SetProperty(ref _showSignature, value); }

        private bool _showBranding = true;
        public bool ShowBranding { get => _showBranding; set => SetProperty(ref _showBranding, value); }

        private bool _csvInternational;
        public bool CsvInternational { get => _csvInternational; set => SetProperty(ref _csvInternational, value); }

        private bool _csvIsoDates;
        public bool CsvIsoDates { get => _csvIsoDates; set => SetProperty(ref _csvIsoDates, value); }

        private bool _csvTotalRow;
        public bool CsvTotalRow { get => _csvTotalRow; set => SetProperty(ref _csvTotalRow, value); }

        private bool _isMetaPanelOpen;
        public bool IsMetaPanelOpen { get => _isMetaPanelOpen; set => SetProperty(ref _isMetaPanelOpen, value); }

        private string _issuerBlock;
        /// <summary>Auftragnehmer (Name, Anschrift, Kontakt); bleibt für den nächsten Nachweis gespeichert.</summary>
        public string IssuerBlock
        {
            get => _issuerBlock;
            set
            {
                if (!SetProperty(ref _issuerBlock, value)) return;
                WriteSetting(() => Properties.Settings.Default.ExportIssuerBlock = value ?? string.Empty);
            }
        }

        private string _clientName;
        private bool _clientNameEdited;
        /// <summary>Auftraggeber; ohne eigene Eingabe der Kunde der gewählten Projekte.</summary>
        public string ClientName
        {
            get => _clientName;
            set
            {
                if (!SetProperty(ref _clientName, value)) return;
                _clientNameEdited = !string.IsNullOrWhiteSpace(value);
            }
        }

        private string _clientBlock;
        public string ClientBlock { get => _clientBlock; set => SetProperty(ref _clientBlock, value); }

        private string _referenceNumber;
        public string ReferenceNumber { get => _referenceNumber; set => SetProperty(ref _referenceNumber, value); }

        // ---------- Vorschau ----------

        private IReadOnlyList<ExportPreviewRow> _previewRows = new ExportPreviewRow[0];
        public IReadOnlyList<ExportPreviewRow> PreviewRows { get => _previewRows; private set => SetProperty(ref _previewRows, value); }

        private IReadOnlyList<ExportWarningVM> _warnings = new ExportWarningVM[0];
        public IReadOnlyList<ExportWarningVM> Warnings { get => _warnings; private set => SetProperty(ref _warnings, value); }
        public bool HasWarnings => _warnings.Count > 0;

        private string _previewHint = string.Empty;
        /// <summary>Erklärt, warum die Vorschau leer ist; leer, wenn Buchungen angezeigt werden.</summary>
        public string PreviewHint { get => _previewHint; private set { SetProperty(ref _previewHint, value); RaisePropertyChanged(nameof(HasPreviewHint)); } }
        public bool HasPreviewHint => !string.IsNullOrEmpty(_previewHint);

        public bool HasEntries => _report != null && !_report.IsEmpty;

        private string _totalText = "–", _entryCountText = "–", _workDaysText = "–", _averageText = "–";
        public string TotalText { get => _totalText; private set => SetProperty(ref _totalText, value); }
        public string EntryCountText { get => _entryCountText; private set => SetProperty(ref _entryCountText, value); }
        public string WorkDaysText { get => _workDaysText; private set => SetProperty(ref _workDaysText, value); }
        public string AverageText { get => _averageText; private set => SetProperty(ref _averageText, value); }

        // ---------- Laden und Aufbauen ----------

        public override void Refresh()
        {
            _suspend++;
            try
            {
                LoadProjects();
                LoadEntries();
            }
            finally
            {
                _suspend--;
            }
            Rebuild();
        }

        private void LoadProjects()
        {
            try
            {
                _projects = MainViewModel.DbInstance.GetProjects().ToList();
            }
            catch (Exception ex)
            {
                _projects = new List<TimeTracker.ProjectRow>();
                Debug.WriteLine("Projekte konnten nicht gelesen werden: " + ex.Message);
            }

            // Gewählte Projekte, die es nicht mehr gibt (gelöscht, umbenannt), fallen aus der Auswahl.
            _selectedNames.IntersectWith(_projects.Select(p => p.Projektname));

            foreach (var old in ProjectItems) old.SelectionChanged = null;
            ProjectItems.Clear();
            foreach (var p in _projects.OrderBy(p => p.Projektname, StringComparer.Create(ExportFormat.German, true)))
            {
                var item = new ExportProjectItemVM(p.Projektname, p.Kunde, p.Kostentraeger, p.IstExtern)
                {
                    IsSelected = _selectedNames.Contains(p.Projektname)
                };
                item.SelectionChanged = OnItemSelectionChanged;
                ProjectItems.Add(item);
            }

            var customers = _projects.Select(p => p.Kunde).Where(k => !string.IsNullOrWhiteSpace(k)).Distinct(StringComparer.Ordinal)
                .OrderBy(k => k, StringComparer.Create(ExportFormat.German, true)).ToList();
            var wanted = new[] { AllCustomers }.Concat(customers).ToList();
            if (!Customers.SequenceEqual(wanted))
            {
                // Leeren und neu füllen setzt die Auswahl der ComboBox zurück und schreibt dabei null in den Filter:
                // Den Filter merken und danach wiederherstellen, sonst ginge er bei jedem Seitenwechsel verloren.
                var keep = _customerFilter;
                Customers.Clear();
                foreach (var c in wanted) Customers.Add(c);
                _customerFilter = Customers.Contains(keep) ? keep : AllCustomers;
                RaisePropertyChanged(nameof(CustomerFilter));
            }

            ProjectsView.Refresh();
            RaisePropertyChanged(nameof(ProjectSelectionText));
            RaisePropertyChanged(nameof(HasProjects));
        }

        private void LoadEntries()
        {
            _entries = new List<TimeTracker.TimeEntryRow>();
            if (HasRangeError) return;

            try
            {
                _entries = MainViewModel.DbInstance.GetTimeEntriesBetween(_from.Value.Date, _to.Value.Date);
            }
            catch (Exception ex)
            {
                Debug.WriteLine("Buchungen konnten nicht gelesen werden: " + ex.Message);
            }

            // Je Projekt zeigen: wie viele Buchungen und Stunden im Zeitraum liegen, damit man sieht, wo sich ein Export lohnt.
            var stats = _entries.GroupBy(e => e.Projektname ?? string.Empty).ToDictionary(g => g.Key, g =>
            {
                var minutes = 0;
                foreach (var e in g)
                {
                    int m;
                    if (LocalHoursCalculator.TryGetDurationMinutes(e.Startzeit, e.Endzeit, out m)) minutes += m;
                }
                return new KeyValuePair<int, int>(g.Count(), minutes);
            });
            foreach (var item in ProjectItems)
            {
                KeyValuePair<int, int> s;
                item.Stats = stats.TryGetValue(item.Name, out s)
                    ? ExportFormat.Plural(s.Key, "Buchung", "Buchungen") + " · " + ExportFormat.Decimal(s.Value) + " h"
                    : "keine Buchungen";
            }
        }

        private ExportOptions BuildOptions()
        {
            if (HasRangeError) return null;
            return new ExportOptions
            {
                From = _from.Value.Date,
                To = _to.Value.Date,
                ProjectNames = _selectedNames.ToList(),
                Kind = _kind,
                Grouping = _grouping,
                DurationFormat = _durationFormat,
                RoundingMinutes = _selectedRounding.Minutes,
                ShowTimes = _showTimes,
                ShowProjectColumn = _alwaysShowProjectColumn ? true : (bool?)null,
                ShowCarrierColumn = _showCarrierColumn
            };
        }

        private void Rebuild()
        {
            if (_suspend > 0) return;

            var options = BuildOptions();
            _report = options == null ? null : ExportReportBuilder.Build(_entries, _projects, options);

            if (_report == null)
            {
                PreviewRows = new ExportPreviewRow[0];
                Warnings = new ExportWarningVM[0];
                TotalText = EntryCountText = WorkDaysText = AverageText = "–";
                PreviewHint = RangeError;
            }
            else
            {
                var flagged = new HashSet<int>(_report.Warnings.Where(w => w.Severity == ExportWarningSeverity.Warning).SelectMany(w => w.EntryIds));
                PreviewRows = _report.Lines.Select(l => new ExportPreviewRow(l, options.DurationFormat, flagged.Contains(l.Id))).ToList();
                Warnings = _report.Warnings.Select(w => new ExportWarningVM(w)).ToList();

                TotalText = ExportFormat.Decimal(_report.TotalMinutes) + " h";
                EntryCountText = _report.EntryCount.ToString(CultureInfo.InvariantCulture);
                WorkDaysText = _report.WorkDays.ToString(CultureInfo.InvariantCulture);
                AverageText = ExportFormat.Decimal(_report.AverageMinutesPerWorkDay) + " h";

                if (_selectedNames.Count == 0) PreviewHint = "Wähle links mindestens ein Projekt. Dann siehst du hier, was exportiert wird.";
                else if (_report.IsEmpty) PreviewHint = "Für die gewählten Projekte gibt es im Zeitraum keine Buchungen.";
                else PreviewHint = string.Empty;

                UpdateClientDefault();
            }

            RaisePropertyChanged(nameof(HasWarnings));
            RaisePropertyChanged(nameof(HasEntries));
            RaisePropertyChanged(nameof(CanExport));
            ExportPdfCommand.RaiseCanExecuteChanged();
            ExportCsvCommand.RaiseCanExecuteChanged();
        }

        private void UpdateClientDefault()
        {
            if (_clientNameEdited) return;
            var names = _report.Projects.Select(p => p.Customer).Where(c => !string.IsNullOrWhiteSpace(c)).Distinct().ToList();
            var value = string.Join(", ", names);
            if (value == _clientName) return;
            _clientName = value;
            RaisePropertyChanged(nameof(ClientName));
        }

        // ---------- Export ----------

        public DelegateCommand RefreshCommand { get; }
        public DelegateCommand SelectAllVisibleCommand { get; }
        public DelegateCommand ClearSelectionCommand { get; }
        public DelegateCommand ToggleMetaPanelCommand { get; }
        public DelegateCommand ExportPdfCommand { get; }
        public DelegateCommand ExportCsvCommand { get; }
        public DelegateCommand OpenResultCommand { get; }
        public DelegateCommand ShowResultInFolderCommand { get; }

        private bool _isBusy;
        public bool IsBusy
        {
            get => _isBusy;
            private set
            {
                if (!SetProperty(ref _isBusy, value)) return;
                RaisePropertyChanged(nameof(CanExport));
                ExportPdfCommand.RaiseCanExecuteChanged();
                ExportCsvCommand.RaiseCanExecuteChanged();
            }
        }

        public bool CanExport => !_isBusy && HasEntries;

        private string _resultMessage;
        public string ResultMessage { get => _resultMessage; private set => SetProperty(ref _resultMessage, value); }

        private string _lastExportPath;
        public bool HasResult => !string.IsNullOrEmpty(_lastExportPath);

        private ExportMeta BuildMeta()
        {
            return new ExportMeta
            {
                Title = Title,
                IssuerBlock = IssuerBlock,
                ClientName = ClientName,
                ClientBlock = ClientBlock,
                ReferenceNumber = ReferenceNumber,
                Note = Note,
                ShowSignature = ShowSignature,
                ShowBranding = ShowBranding,
                CreatedAt = DateTime.Now,
                AppVersion = _appVersion,
                Orientation = Landscape ? ExportOrientation.Landscape : ExportOrientation.Portrait
            };
        }

        private async Task ExportAsync(bool pdf)
        {
            if (!CanExport) return;

            // Alles, was im Hintergrund gebraucht wird, jetzt auf dem UI-Thread festhalten: Bericht, Angaben, Optionen.
            var report = _report;
            var meta = BuildMeta();
            var csv = new CsvOptions { International = CsvInternational, IsoDates = CsvIsoDates, IncludeTotalRow = CsvTotalRow };
            var extension = pdf ? "pdf" : "csv";

            var path = FileDialogHelper.ChooseSavePath(
                pdf ? "Tätigkeitsnachweis als PDF speichern" : "Buchungen als CSV speichern",
                pdf ? "PDF-Dokument (*.pdf)|*.pdf" : "CSV-Datei (*.csv)|*.csv",
                extension,
                ExportFileName.Suggest(report, Title, extension),
                ReadSetting(() => Properties.Settings.Default.ExportLastFolder));
            if (path == null) return;

            IsBusy = true;
            try
            {
                await Task.Run(() =>
                {
                    if (pdf) PdfExporter.WriteFile(path, report, meta);
                    else CsvExporter.WriteFile(path, report, csv);
                });

                _lastExportPath = path;
                RaisePropertyChanged(nameof(HasResult));
                ResultMessage = (pdf ? "PDF gespeichert: " : "CSV gespeichert: ") + Path.GetFileName(path);
                OpenResultCommand.RaiseCanExecuteChanged();
                ShowResultInFolderCommand.RaiseCanExecuteChanged();
                var folder = Path.GetDirectoryName(path);
                WriteSetting(() => Properties.Settings.Default.ExportLastFolder = folder ?? string.Empty);
            }
            catch (UnauthorizedAccessException)
            {
                ShowError("Auf diesen Speicherort darf nicht geschrieben werden. Wähle bitte einen anderen Ordner.\n\n" + path);
            }
            catch (IOException ex)
            {
                ShowError("Die Datei konnte nicht geschrieben werden. Ist sie noch in " + (pdf ? "einem PDF-Programm" : "Excel") +
                          " geöffnet? Schließe sie und versuche es noch einmal.\n\n" + path + "\n\n" + ex.Message);
            }
            catch (Exception ex)
            {
                ShowError((pdf ? "Das PDF" : "Die CSV-Datei") + " konnte nicht erstellt werden.\n\n" + ex.Message);
            }
            finally
            {
                IsBusy = false;
            }
        }

        private static void ShowError(string message)
        {
            MessageBox.Show(message, "Tätigkeitsnachweis", MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        private void OpenResult()
        {
            if (!File.Exists(_lastExportPath))
            {
                ShowError("Die Datei gibt es nicht mehr:\n\n" + _lastExportPath);
                return;
            }

            try
            {
                Process.Start(new ProcessStartInfo(_lastExportPath) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                ShowError("Die Datei konnte nicht geöffnet werden. Ist ein Programm für diesen Dateityp installiert?\n\n" + ex.Message);
            }
        }

        private void ShowResultInFolder()
        {
            try
            {
                if (File.Exists(_lastExportPath)) Process.Start("explorer.exe", "/select,\"" + _lastExportPath + "\"");
                else Process.Start("explorer.exe", "\"" + Path.GetDirectoryName(_lastExportPath) + "\"");
            }
            catch (Exception ex)
            {
                ShowError("Der Ordner konnte nicht geöffnet werden.\n\n" + ex.Message);
            }
        }

        // ---------- Einstellungen ----------

        private static string ReadSetting(Func<string> read)
        {
            try { return read() ?? string.Empty; }
            catch (Exception) { return string.Empty; }
        }

        private static void WriteSetting(Action write)
        {
            try
            {
                write();
                Properties.Settings.Default.Save();
            }
            catch (Exception ex)
            {
                Debug.WriteLine("Einstellung konnte nicht gespeichert werden: " + ex.Message);
            }
        }
    }

    // ---------- Anzeige-Bausteine ----------

    internal sealed class ExportPresetItem
    {
        public ExportPresetItem(string key, string text) { Key = key; Text = text; }
        public string Key { get; }
        public string Text { get; }
        public override string ToString() => Text;
    }

    internal sealed class ExportRoundingItem
    {
        public ExportRoundingItem(int minutes, string text) { Minutes = minutes; Text = text; }
        public int Minutes { get; }
        public string Text { get; }
        public override string ToString() => Text;
    }

    internal sealed class ExportProjectItemVM : BindableBase
    {
        private bool _isSelected;
        private string _stats = string.Empty;

        public ExportProjectItemVM(string name, string customer, string carrier, bool isExternal)
        {
            Name = name;
            Customer = customer ?? string.Empty;
            Carrier = carrier ?? string.Empty;
            IsExternal = isExternal;
        }

        public string Name { get; }
        public string Customer { get; }
        public string Carrier { get; }
        public bool IsExternal { get; }

        public string Detail => string.Join(" · ", new[] { Customer, Carrier }.Where(s => s.Length > 0));

        /// <summary>Buchungen und Stunden des Projekts im gewählten Zeitraum.</summary>
        public string Stats { get => _stats; set => SetProperty(ref _stats, value); }

        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (SetProperty(ref _isSelected, value)) SelectionChanged?.Invoke(this);
            }
        }

        public Action<ExportProjectItemVM> SelectionChanged { get; set; }
    }

    internal sealed class ExportPreviewRow
    {
        public ExportPreviewRow(ExportLine line, ExportDurationFormat format, bool hasIssue)
        {
            DateText = line.WeekdayText + " " + line.DateText;
            TimeText = line.Start + " – " + line.End;
            DurationText = ExportFormat.Duration(line.Minutes, format);
            Project = line.Project;
            Description = line.Description.Replace("\r\n", " ").Replace('\n', ' ').Replace('\r', ' ');
            HasIssue = hasIssue;
        }

        public string DateText { get; }
        public string TimeText { get; }
        public string DurationText { get; }
        public string Project { get; }
        public string Description { get; }
        /// <summary>Die Buchung steht in einer Warnung (z. B. ohne Beschreibung) und wird hervorgehoben.</summary>
        public bool HasIssue { get; }
    }

    internal sealed class ExportWarningVM
    {
        public ExportWarningVM(ExportWarning warning)
        {
            Message = warning.Message;
            IsWarning = warning.Severity == ExportWarningSeverity.Warning;
        }

        public string Message { get; }
        public bool IsWarning { get; }
    }
}
