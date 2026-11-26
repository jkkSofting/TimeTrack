using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
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

        /// <summary>Hinweis „3 inaktive Projekte ausgeblendet“; null, wenn nichts ausgeblendet ist.</summary>
        public string HiddenInactiveText
        {
            get
            {
                if (ShowInactive) return null;

                int hidden = Projects.Count(p => !p.QuickSelectAktiv && MatchesSearch(p));
                if (hidden == 0) return null;

                return hidden == 1 ? "1 inaktives Projekt ausgeblendet" : hidden + " inaktive Projekte ausgeblendet";
            }
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

            UpdateView();
        }

        /// <summary>Wendet Suche und Inaktiv-Filter neu an und aktualisiert den Hinweis auf ausgeblendete Projekte.</summary>
        private void UpdateView()
        {
            ProjectsView.Refresh();
            RaisePropertyChanged(nameof(HiddenInactiveText));
        }

        private void OnProjectItemChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName != nameof(ProjectItemVM.QuickSelectAktiv)) return;

            var item = (ProjectItemVM)sender;
            MainViewModel.DbInstance.SetProjectQuickSelectActive(item.Projektname, item.QuickSelectAktiv);
            UpdateView();
            _projectsChanged?.Invoke();
        }

        private bool FilterProject(object obj)
        {
            var it = obj as ProjectItemVM;
            if (it == null)
                return false;

            if (!ShowInactive && !it.QuickSelectAktiv)
                return false;

            return MatchesSearch(it);
        }

        private bool MatchesSearch(ProjectItemVM it)
        {
            if (string.IsNullOrWhiteSpace(SearchText))
                return true;

            var q = SearchText.Trim();
            return (it.Projektname?.IndexOf(q, StringComparison.OrdinalIgnoreCase) ?? -1) >= 0
                   || (it.Kunde?.IndexOf(q, StringComparison.OrdinalIgnoreCase) ?? -1) >= 0
                   || (it.Kostentraeger?.IndexOf(q, StringComparison.OrdinalIgnoreCase) ?? -1) >= 0;
        }

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

    internal sealed class ProjectItemVM : BindableBase
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