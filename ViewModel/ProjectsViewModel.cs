using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Windows.Data;
using Zeitmanagement.Helpers;
using Zeitmanagement.MVVM;

namespace Zeitmanagement.ViewModel
{
    internal sealed class ProjectsViewModel : BaseViewModel
    {
        // Daten
        public ObservableCollection<ProjectItemVM> Projects { get; } = new ObservableCollection<ProjectItemVM>();

        public ICollectionView ProjectsView { get; }

        // Suche
        private string _searchText;

        public string SearchText
        {
            get => _searchText;
            set
            {
                SetProperty(ref _searchText, value);
                UpdateView();
            }
        }

        private bool _showInactive;

        private bool _showRoundedHours;

        /// <summary>
        /// false = Stunden netto (tatsächlich gebucht), true = jede Buchung auf die Rundung des Projekts aufgerundet.
        /// Gilt nur für die Anzeige und wird nur für die Sitzung gehalten.
        /// </summary>
        public bool ShowRoundedHours
        {
            get => _showRoundedHours;
            set
            {
                if (!SetProperty(ref _showRoundedHours, value)) return;

                foreach (var item in Projects)
                    item.ZeigeGerundet = value;

                RaisePropertyChanged(nameof(IsNettoSelected));
                UpdateView();
            }
        }

        /// <summary>Segment „Netto“ im Umschalter (Gegenstück zu <see cref="ShowRoundedHours"/>).</summary>
        public bool IsNettoSelected
        {
            get => !_showRoundedHours;
            set
            {
                if (value) ShowRoundedHours = false;
            }
        }

        /// <summary>Zeigt auch inaktive Projekte (Schalter „Aktiv“ aus) an; standardmäßig sind sie ausgeblendet.</summary>
        public bool ShowInactive
        {
            get => _showInactive;
            set
            {
                if (SetProperty(ref _showInactive, value))
                    UpdateView();
            }
        }

        // --- Filter-Panel (Typ, Kunde, Rundung, nur mit Buchungen) ---

        /// <summary>Filterleiste auf der Seite ein-/ausgeklappt (nur für die Sitzung).</summary>
        private bool _isFilterPanelOpen;
        public bool IsFilterPanelOpen { get => _isFilterPanelOpen; set => SetProperty(ref _isFilterPanelOpen, value); }

        private ProjectFilterCriteria _criteria = new ProjectFilterCriteria();   // Feld-Initialisierer, damit FilterProject vor dem ersten UpdateView nicht null sieht

        // Typ (Segment)
        private ProjectTypeFilter _typeFilter = ProjectTypeFilter.Alle;
        public bool IsTypeAll    { get => _typeFilter == ProjectTypeFilter.Alle;   set { if (value) SetTypeFilter(ProjectTypeFilter.Alle); } }
        public bool IsTypeIntern { get => _typeFilter == ProjectTypeFilter.Intern; set { if (value) SetTypeFilter(ProjectTypeFilter.Intern); } }
        public bool IsTypeExtern { get => _typeFilter == ProjectTypeFilter.Extern; set { if (value) SetTypeFilter(ProjectTypeFilter.Extern); } }

        private void SetTypeFilter(ProjectTypeFilter t)
        {
            if (_typeFilter == t) return;
            _typeFilter = t;
            RaisePropertyChanged(nameof(IsTypeAll));
            RaisePropertyChanged(nameof(IsTypeIntern));
            RaisePropertyChanged(nameof(IsTypeExtern));
            UpdateView();
        }

        // Kunde
        public ObservableCollection<string> Customers { get; } = new ObservableCollection<string>();

        private string _customerFilter = ProjectFilter.AllCustomers;

        public string CustomerFilter
        {
            get => _customerFilter;
            set
            {
                if (value == null) return;   // ComboBox schreibt null, wenn Customers neu aufgebaut wird; ignorieren, RebuildCustomers stellt die Auswahl wieder her
                if (SetProperty(ref _customerFilter, value)) UpdateView();
            }
        }

        // Rundung
        public ProjectRoundingChoice[] RoundingFilterChoices => ProjectFilter.RoundingChoices;

        private ProjectRoundingChoice _selectedRoundingFilter = ProjectFilter.RoundingChoices[0];

        public ProjectRoundingChoice SelectedRoundingFilter
        {
            get => _selectedRoundingFilter;
            set { if (value != null && SetProperty(ref _selectedRoundingFilter, value)) UpdateView(); }
        }

        // Nur mit Buchungen
        private bool _onlyWithBookings;

        public bool OnlyWithBookings
        {
            get => _onlyWithBookings;
            set { if (SetProperty(ref _onlyWithBookings, value)) UpdateView(); }
        }

        // Abgeleitet (nur lesen)
        public int ActiveFilterCount => ProjectFilter.CountActive(_criteria);
        public bool HasActiveFilters => ActiveFilterCount > 0;
        public string FilterButtonText => ActiveFilterCount > 0 ? "Filter (" + ActiveFilterCount + ")" : "Filter";

        /// <summary>Statuszeile unter dem Titel, z. B. „4 von 12 Projekten · 3 inaktive Projekte ausgeblendet“; null wenn nichts zu sagen ist.</summary>
        public string ListStatusText { get; private set; }

        /// <summary>Leerzustand der Tabelle, wenn Projekte da sind, aber alle Filter sie ausschließen; sonst null.</summary>
        public string EmptyText { get; private set; }

        public DelegateCommand ResetFilterPanelCommand { get; }

        // Sortierung (Dropdown; setzt ProjectsView.SortDescriptions, bleibt mit der Spaltenkopf-Sortierung synchron)
        public ProjectSortOption[] SortChoices => ProjectSort.Choices;

        private ProjectSortOption _selectedSort = ProjectSort.Choices[0];

        public ProjectSortOption SelectedSort
        {
            get => _selectedSort;
            set { if (value != null && SetProperty(ref _selectedSort, value) && !_syncingSort) ApplySort(); }
        }

        private bool _sortDescending;

        public bool IsSortAscending  { get => !_sortDescending; set { if (value) SetSortDescending(false); } }
        public bool IsSortDescending { get => _sortDescending;  set { if (value) SetSortDescending(true); } }

        private void SetSortDescending(bool descending)
        {
            if (_sortDescending == descending) return;
            _sortDescending = descending;
            RaisePropertyChanged(nameof(IsSortAscending));
            RaisePropertyChanged(nameof(IsSortDescending));
            if (!_syncingSort) ApplySort();
        }

        // true, solange Dropdown/Richtung und View-SortDescriptions sich gegenseitig nachziehen (verhindert Rückkopplung)
        private bool _syncingSort;

        /// <summary>Spaltenkopf-Klick (DataGrid ändert SortDescriptions): Dropdown und Richtung nachziehen.</summary>
        private void OnViewSortDescriptionsChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            if (_syncingSort) return;
            var descriptions = ProjectsView.SortDescriptions;
            // Clear/Add des DataGrids: Zwischenzustand ohne Beschreibung ignorieren, der folgende Add setzt den Endstand
            if (descriptions.Count == 0) return;

            _syncingSort = true;
            try
            {
                var first = descriptions[0];
                var choice = ProjectSort.ChoiceFromProperty(first.PropertyName);
                SelectedSort = ProjectSort.Choices.First(c => c.Value == choice);
                SetSortDescending(ProjectSort.IsDescending(first.Direction));
            }
            finally { _syncingSort = false; }
        }

        private void ApplySort()
        {
            var descriptions = ProjectsView.SortDescriptions;
            _syncingSort = true;
            try
            {
                descriptions.Clear();

                var property = ProjectSort.PropertyName(_selectedSort.Value);
                if (property == null) return;

                descriptions.Add(new SortDescription(property,
                    _sortDescending ? ListSortDirection.Descending : ListSortDirection.Ascending));

                // Sekundaer nach Name, damit gleiche Werte in stabiler Reihenfolge erscheinen.
                if (property != "Projektname")
                    descriptions.Add(new SortDescription("Projektname", ListSortDirection.Ascending));
            }
            finally { _syncingSort = false; }
        }

        /// <summary>Anzeige der globalen Standardrundung, z. B. „Standardrundung: 15 Minuten“.</summary>
        public string GlobalRoundingText => "Standardrundung: " + RoundingOptions.Label(_globalRoundingMinutes);

        private int _globalRoundingMinutes;

        public string StartDate { get => _startDate; set => SetProperty(ref _startDate, value); }
        private string _startDate;

        public string EndDate { get => _endDate; set => SetProperty(ref _endDate, value); }
        private string _endDate;

        private bool _isFiltering;

        // UI-Text
        private string _headerSubtitle = "Verwalte Projekte, Buchungen & Stunden";

        public string HeaderSubtitle { get => _headerSubtitle; set => SetProperty(ref _headerSubtitle, value); }

        // Commands
        public DelegateCommand RefreshCommand { get; }

        /// <summary>Öffnet das Overlay „Neues Projekt“.</summary>
        public DelegateCommand NewProjectCommand { get; }

        /// <summary>Öffnet das Overlay „Standardrundung“.</summary>
        public DelegateCommand EditGlobalRoundingCommand { get; }

        public DelegateCommand DeleteProjectCommand { get; }

        public DelegateCommand FilterProjectsCommand { get; }
        public DelegateCommand ResetProjectsFilterCommand { get; }

        /// <summary>Öffnet „Projektcontrolling“ mit dem Kostenträger des Projekts.</summary>
        public DelegateCommand OpenControllingCommand { get; }

        /// <summary>Öffnet den „Tätigkeitsnachweis“ mit diesem Projekt gewählt.</summary>
        public DelegateCommand OpenExportCommand { get; }

        /// <summary>Öffnet das Overlay „Projekt bearbeiten“ für die Zeile.</summary>
        public DelegateCommand StartEditProjectCommand { get; }

        /// <summary>
        /// Called whenever the set of projects (or which of them are enabled for Quick Switch)
        /// changed, so the Quick Switch window can pick it up without a restart.
        /// </summary>
        private readonly Action _projectsChanged;

        private readonly Action<string> _openControlling;
        private readonly Action<string> _openExport;

        public ProjectsViewModel(Action projectsChanged = null, Action<string> openControlling = null, Action<string> openExport = null)
        {
            _projectsChanged = projectsChanged;
            _openControlling = openControlling;
            _openExport = openExport;

            ProjectsView = CollectionViewSource.GetDefaultView(Projects);
            ProjectsView.Filter = FilterProject;
            ((INotifyCollectionChanged)ProjectsView.SortDescriptions).CollectionChanged += OnViewSortDescriptionsChanged;

            RefreshCommand = new DelegateCommand(_ => Refresh());
            NewProjectCommand = new DelegateCommand(_ => StartNew());
            EditGlobalRoundingCommand = new DelegateCommand(_ => EditGlobalRounding());
            DeleteProjectCommand = new DelegateCommand(p => DeleteProject(p as ProjectItemVM), p => p is ProjectItemVM);

            OpenControllingCommand = new DelegateCommand(
                p => _openControlling?.Invoke(((ProjectItemVM)p).Kostentraeger),
                p => p is ProjectItemVM && _openControlling != null);

            OpenExportCommand = new DelegateCommand(
                p => _openExport?.Invoke(((ProjectItemVM)p).Projektname),
                p => p is ProjectItemVM && _openExport != null);

            StartEditProjectCommand = new DelegateCommand(p => StartEdit(p as ProjectItemVM), p => p is ProjectItemVM);

            FilterProjectsCommand = new DelegateCommand(_ => FilterProjectsCommandExecute(null));
            ResetProjectsFilterCommand = new DelegateCommand(_ => ResetProjectsCommandExecute(null));
            ResetFilterPanelCommand = new DelegateCommand(_ => ResetFilterPanel(), _ => HasActiveFilters);

            Refresh();
        }

        private static readonly string[] AllowedTimeFormats = { @"h\:mm", @"hh\:mm", "Hmm", "HHmm" };

        private static bool TryParseTimeSpan(string input, out TimeSpan time)
        {
            return TimeSpan.TryParseExact(input, AllowedTimeFormats, CultureInfo.InvariantCulture, out time);
        }

        public override void Refresh()
        {
            Projects.Clear();

            var db = MainViewModel.DbInstance;
            var projects = db.GetProjects().ToList();
            _globalRoundingMinutes = db.GetGlobalRoundingMinutes();
            RaisePropertyChanged(nameof(GlobalRoundingText));

            foreach (var p in projects)
            {
                try
                {
                    var entries = MainViewModel.DbInstance.GetTimeEntriesForProject(p.Projektname);
                    int count = 0;
                    double hours = 0;
                    var entryMinutes = new List<int>();

                    foreach (var entry in entries)
                    {
                        if (!TryParseTimeSpan(entry.Startzeit, out var start) ||
                            !TryParseTimeSpan(entry.Endzeit, out var end))
                        {
                            Console.WriteLine($"Ungültige Zeit in Eintrag {count + 1}: '{entry.Startzeit}' -> '{entry.Endzeit}'");
                            continue;
                        }

                        if (_isFiltering)
                        {
                            var endDate = DateTime.TryParseExact(EndDate, "dd.MM.yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var endDateParsed)
                                ? endDateParsed
                                : DateTime.MaxValue; // Fallback: Heute

                            var startDate = DateTime.TryParseExact(StartDate, "dd.MM.yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var startDateParsed)
                                ? startDateParsed
                                : DateTime.MinValue; // Fallback: Heute

                            if (entry.Datum < startDate || entry.Datum > endDate)
                            {
                                continue; // Eintrag liegt außerhalb des Filterzeitraums
                            }
                        }

                        var duration = end - start;
                        if (duration.TotalHours < 0) // über Mitternacht
                            duration = duration.Add(TimeSpan.FromDays(1));

                        hours += duration.TotalHours;
                        entryMinutes.Add(ProjectHours.ToMinutes(duration));
                        count++;
                    }

                    if (_isFiltering && count < 1)
                    {
                        continue;
                    }

                    var item = new ProjectItemVM
                    {
                        Projektname = p.Projektname,
                        Kunde = p.Kunde,
                        Kostentraeger = p.Kostentraeger,
                        IstExtern = p.IstExtern,
                        GlobalRundungMinuten = _globalRoundingMinutes,
                        RundungMinuten = p.RundungMinuten,
                        RundungEigenMinuten = p.RundungEigenMinuten,
                        RundungGlobal = p.RundungGlobal,
                        QuickSelectAktiv = p.QuickSelectAktiv,
                        Buchungen = count,
                        Stunden = Math.Round(hours, 2),
                        StundenGerundet = Math.Round(ProjectHours.RoundedHours(entryMinutes, p.RundungMinuten), 2),
                        ZeigeGerundet = _showRoundedHours
                    };

                    // Subscribed only after the initializer, so loading doesn't write back to the DB.
                    item.PropertyChanged += OnProjectItemChanged;
                    Projects.Add(item);
                }
                catch (Exception e)
                {
                    Console.WriteLine(e.Message);
                }
                // Hole Buchungen für dieses Projekt (falls du sie in der DB nicht schon aggregierst)
            }

            RebuildCustomers();
            UpdateView();
        }

        /// <summary>Wendet Suche, Inaktiv- und Panel-Filter neu an und aktualisiert Statuszeile, Leerzustand und Filter-Zähler.</summary>
        private void UpdateView()
        {
            _criteria = BuildCriteria();
            ProjectsView.Refresh();

            var counts = ProjectFilter.Count(Projects, _criteria);
            ListStatusText = ProjectFilter.StatusText(counts);
            EmptyText = counts.Shown == 0 && Projects.Count > 0 ? "Keine Projekte passen zu den aktuellen Filtern." : null;

            RaisePropertyChanged(nameof(ListStatusText));
            RaisePropertyChanged(nameof(EmptyText));
            RaisePropertyChanged(nameof(ActiveFilterCount));
            RaisePropertyChanged(nameof(HasActiveFilters));
            RaisePropertyChanged(nameof(FilterButtonText));
            ResetFilterPanelCommand?.RaiseCanExecuteChanged();
        }

        private void OnProjectItemChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName != nameof(ProjectItemVM.QuickSelectAktiv)) return;

            var item = (ProjectItemVM)sender;
            MainViewModel.DbInstance.SetProjectQuickSelectActive(item.Projektname, item.QuickSelectAktiv);
            UpdateView();
            _projectsChanged?.Invoke();
        }

        private bool FilterProject(object obj) => ProjectFilter.Matches(obj as ProjectItemVM, _criteria);

        // --- Delete ---
        private readonly IConfirmService _confirm = new MessageBoxConfirmService();

        private void DeleteProject(ProjectItemVM item)
        {
            if (item == null) return;

            var db = MainViewModel.DbInstance;
            int count = db.CountTimeEntries(item.Projektname);
            if (!_confirm.Confirm(ConfirmTexts.MoveProjectText(item.Projektname, count), ConfirmTexts.MoveProjectTitle))
                return;

            db.MoveProjectToTrash(item.Projektname);
            Refresh();
            _projectsChanged?.Invoke();   // Quick Switch neu laden
        }

        // --- Neu / Bearbeiten / Standardrundung (alles als Overlay) ---
        private void StartNew()
        {
            OverlayService.Instance.Show(ProjectEditOverlayViewModel.ForNew(MainViewModel.DbInstance, OnOverlaySaved));
        }

        private void StartEdit(ProjectItemVM item)
        {
            if (item == null) return;

            OverlayService.Instance.Show(ProjectEditOverlayViewModel.ForEdit(
                MainViewModel.DbInstance,
                item.Projektname, item.Kunde, item.Kostentraeger, item.IstExtern,
                item.RundungGlobal, item.RundungEigenMinuten,
                OnOverlaySaved));
        }

        private void EditGlobalRounding()
        {
            var db = MainViewModel.DbInstance;
            // Aus der DB gezählt (nicht aus der Liste), damit ein aktiver Zeitraumfilter die Zahl nicht verfälscht.
            int inheriting = db.GetProjects().Count(p => p.RundungGlobal);
            OverlayService.Instance.Show(new GlobalRoundingOverlayViewModel(db, inheriting, OnOverlaySaved));
        }

        private void OnOverlaySaved()
        {
            Refresh();
            _projectsChanged?.Invoke();
        }

        // --- Filter-Panel ---
        private void ResetFilterPanel()
        {
            _typeFilter = ProjectTypeFilter.Alle;
            _customerFilter = ProjectFilter.AllCustomers;
            _selectedRoundingFilter = ProjectFilter.RoundingChoices[0];
            _onlyWithBookings = false;
            RaisePropertyChanged(nameof(IsTypeAll));
            RaisePropertyChanged(nameof(IsTypeIntern));
            RaisePropertyChanged(nameof(IsTypeExtern));
            RaisePropertyChanged(nameof(CustomerFilter));
            RaisePropertyChanged(nameof(SelectedRoundingFilter));
            RaisePropertyChanged(nameof(OnlyWithBookings));
            UpdateView();   // nur EIN Refresh
        }

        private void RebuildCustomers()
        {
            var wanted = _customerFilter;
            var list = ProjectFilter.BuildCustomerList(Projects.Select(p => p.Kunde));
            Customers.Clear();
            foreach (var c in list) Customers.Add(c);
            // Auswahl bleibt, wenn der Kunde noch existiert (Zeitraumfilter kann ihn entfernen), sonst "Alle Kunden"
            _customerFilter = list.Contains(wanted) ? wanted : ProjectFilter.AllCustomers;
            RaisePropertyChanged(nameof(CustomerFilter));   // ComboBox neu auf den Wert setzen (Clear hat SelectedItem auf null gesetzt)
        }

        private ProjectFilterCriteria BuildCriteria() => new ProjectFilterCriteria
        {
            SearchText = SearchText,
            ShowInactive = ShowInactive,
            Type = _typeFilter,
            Customer = _customerFilter,
            Rounding = _selectedRoundingFilter.Value,
            OnlyWithBookings = _onlyWithBookings
        };

        // --- Filter ---
        private void ResetProjectsCommandExecute(object _)
        {
            StartDate = EndDate = string.Empty;
            _isFiltering = false;
            Refresh();
        }

        private void FilterProjectsCommandExecute(object _)
        {
            _isFiltering = true;
            Refresh();
        }
    }

    internal sealed class ProjectItemVM : BindableBase, IProjectFilterItem
    {
        public string Projektname { get => _projektname; set => SetProperty(ref _projektname, value); }
        public string Kunde { get => _kunde; set => SetProperty(ref _kunde, value); }
        public string Kostentraeger { get => _kostentraeger; set => SetProperty(ref _kostentraeger, value); }
        public bool IstExtern { get => _istExtern; set { SetProperty(ref _istExtern, value); RaisePropertyChanged(nameof(TypText)); } }
        /// <summary>Whether the project is offered in the Quick Switch window.</summary>
        public bool QuickSelectAktiv { get => _quickSelectAktiv; set => SetProperty(ref _quickSelectAktiv, value); }
        public string TypText => _istExtern ? "Extern" : "Intern";
        /// <summary>EFFEKTIVE Aufrundung je Buchung (0 = Aus); zum Sortieren der Spalte.</summary>
        public int RundungMinuten { get => _rundungMinuten; set => SetProperty(ref _rundungMinuten, value); }
        /// <summary>Eigener Rundungswert des Projekts (gilt nur, wenn <see cref="RundungGlobal"/> false ist).</summary>
        public int RundungEigenMinuten { get => _rundungEigenMinuten; set { SetProperty(ref _rundungEigenMinuten, value); RaisePropertyChanged(nameof(RundungText)); } }
        /// <summary>true = das Projekt erbt die Standardrundung.</summary>
        public bool RundungGlobal { get => _rundungGlobal; set { SetProperty(ref _rundungGlobal, value); RaisePropertyChanged(nameof(RundungText)); } }
        /// <summary>Aktuelle Standardrundung (für den Anzeigetext).</summary>
        public int GlobalRundungMinuten { get => _globalRundungMinuten; set { SetProperty(ref _globalRundungMinuten, value); RaisePropertyChanged(nameof(RundungText)); } }
        /// <summary>Anzeige: „Standard (15 Minuten)“, „Aus“, „10 Minuten“.</summary>
        public string RundungText => RoundingOptions.LabelFor(_rundungGlobal, _rundungEigenMinuten, _globalRundungMinuten);
        public int Buchungen { get => _buchungen; set => SetProperty(ref _buchungen, value); }
        /// <summary>Stunden netto: die tatsächlich gebuchte Zeit ohne Rundung.</summary>
        public double Stunden { get => _stunden; set { SetProperty(ref _stunden, value); RaisePropertyChanged(nameof(StundenAnzeige)); } }
        /// <summary>Stunden, wenn jede Buchung auf die Rundung des Projekts aufgerundet wird (SAP-Verrechnung).</summary>
        public double StundenGerundet { get => _stundenGerundet; set { SetProperty(ref _stundenGerundet, value); RaisePropertyChanged(nameof(StundenAnzeige)); RaisePropertyChanged(nameof(StundenTooltip)); } }
        /// <summary>true = <see cref="StundenGerundet"/> anzeigen, sonst <see cref="Stunden"/>. Vom ViewModel für alle Zeilen gesetzt.</summary>
        public bool ZeigeGerundet { get => _zeigeGerundet; set { SetProperty(ref _zeigeGerundet, value); RaisePropertyChanged(nameof(StundenAnzeige)); RaisePropertyChanged(nameof(StundenTooltip)); } }
        /// <summary>Der angezeigte Stundenwert je nach Modus; zum Sortieren der Spalte „Stunden“.</summary>
        public double StundenAnzeige => _zeigeGerundet ? _stundenGerundet : _stunden;
        /// <summary>Tooltip der Stundenzelle: im gerundeten Modus bei eigener Rundung „netto X, gerundet Y“, sonst leer.</summary>
        public string StundenTooltip => _zeigeGerundet && _rundungMinuten > 0
            ? "netto " + _stunden.ToString("0.00") + ", gerundet " + _stundenGerundet.ToString("0.00")
            : null;

        private string _projektname, _kunde, _kostentraeger;
        private int _buchungen, _rundungMinuten, _rundungEigenMinuten, _globalRundungMinuten;
        private bool _rundungGlobal;
        private bool _istExtern;
        private bool _quickSelectAktiv;
        private bool _zeigeGerundet;
        private double _stunden, _stundenGerundet;
    }
}
