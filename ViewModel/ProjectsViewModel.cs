using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Windows.Data;
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
                ProjectsView.Refresh();
            }
        }

        // Add-Panel
        private bool _isAddPanelOpen;
        public bool IsAddPanelOpen { get => _isAddPanelOpen; set => SetProperty(ref _isAddPanelOpen, value); }

        public string NewProjektname { get => _newProjektname; set { SetProperty(ref _newProjektname, value); AddProjectCommand.RaiseCanExecuteChanged(); } }
        public string NewKunde { get => _newKunde; set { SetProperty(ref _newKunde, value); AddProjectCommand.RaiseCanExecuteChanged(); } }
        public string NewKostentraeger { get => _newKostentraeger; set { SetProperty(ref _newKostentraeger, value); AddProjectCommand.RaiseCanExecuteChanged(); } }
        private string _newProjektname, _newKunde, _newKostentraeger;

        // Edit-Panel
        private bool _isEditPanelOpen;
        public bool IsEditPanelOpen { get => _isEditPanelOpen; set => SetProperty(ref _isEditPanelOpen, value); }

        private string _editProjektnameOriginal; // zum Identifizieren in der DB
        public string EditProjektname { get => _editProjektname; set { SetProperty(ref _editProjektname, value); SaveEditProjectCommand.RaiseCanExecuteChanged(); } }
        public string EditKunde { get => _editKunde; set { SetProperty(ref _editKunde, value); SaveEditProjectCommand.RaiseCanExecuteChanged(); } }
        public string EditKostentraeger { get => _editKostentraeger; set { SetProperty(ref _editKostentraeger, value); SaveEditProjectCommand.RaiseCanExecuteChanged(); } }
        private string _editProjektname, _editKunde, _editKostentraeger;

        // UI-Text
        private string _headerSubtitle = "Verwalte Projekte, Buchungen & Stunden";
        public string HeaderSubtitle { get => _headerSubtitle; set => SetProperty(ref _headerSubtitle, value); }

        // Commands
        public DelegateCommand RefreshCommand { get; }
        public DelegateCommand ToggleAddPanelCommand { get; }
        public DelegateCommand AddProjectCommand { get; }
        public DelegateCommand DeleteProjectCommand { get; }

        public DelegateCommand StartEditProjectCommand { get; }
        public DelegateCommand SaveEditProjectCommand { get; }
        public DelegateCommand CancelEditProjectCommand { get; }

        public ProjectsViewModel()
        {
            ProjectsView = CollectionViewSource.GetDefaultView(Projects);
            ProjectsView.Filter = FilterProject;

            RefreshCommand = new DelegateCommand(_ => Refresh());
            ToggleAddPanelCommand = new DelegateCommand(_ => IsAddPanelOpen = !IsAddPanelOpen);
            AddProjectCommand = new DelegateCommand(_ => AddProject(), _ => CanAddProject());
            DeleteProjectCommand = new DelegateCommand(p => DeleteProject(p as ProjectItemVM), p => p is ProjectItemVM);

            StartEditProjectCommand = new DelegateCommand(p => StartEdit(p as ProjectItemVM), p => p is ProjectItemVM);
            SaveEditProjectCommand = new DelegateCommand(_ => SaveEdit(), _ => CanSaveEdit());
            CancelEditProjectCommand = new DelegateCommand(_ => CancelEdit());

            Refresh();
        }

        public override void Refresh()
        {
            Projects.Clear();

            var db = MainViewModel.DbInstance;
            var projects = db.GetProjects().ToList();

            foreach (var p in projects)
            {
                // Hole Buchungen für dieses Projekt (falls du sie in der DB nicht schon aggregierst)
                var entries = MainViewModel.DbInstance.GetTimeEntriesForProject(p.Projektname);
                int count = 0;
                double hours = 0;

                foreach (var e in entries)
                {
                    count++;
                    var fmt = "HH:mm";
                    var cult = CultureInfo.InvariantCulture;
                    var start = DateTime.ParseExact(e.Startzeit, fmt, cult);
                    var end = DateTime.ParseExact(e.Endzeit, fmt, cult);
                    hours += (end - start).TotalHours;
                }

                Projects.Add(new ProjectItemVM
                {
                    Projektname = p.Projektname,
                    Kunde = p.Kunde,
                    Kostentraeger = p.Kostentraeger,
                    Buchungen = count,
                    Stunden = Math.Round(hours, 2)
                });
            }

            ProjectsView.Refresh();
        }

        private bool FilterProject(object obj)
        {
            if (string.IsNullOrWhiteSpace(SearchText))
                return true;

            var it = obj as ProjectItemVM;
            if (it == null)
                return false;

            var q = SearchText.Trim();
            return (it.Projektname?.IndexOf(q, StringComparison.OrdinalIgnoreCase) ?? -1) >= 0
                   || (it.Kunde?.IndexOf(q, StringComparison.OrdinalIgnoreCase) ?? -1) >= 0
                   || (it.Kostentraeger?.IndexOf(q, StringComparison.OrdinalIgnoreCase) ?? -1) >= 0;
        }


        // --- Add ---
        private bool CanAddProject()
        {
            return !string.IsNullOrWhiteSpace(NewProjektname)
                && !string.IsNullOrWhiteSpace(NewKunde)
                && !string.IsNullOrWhiteSpace(NewKostentraeger);
        }

        private void AddProject()
        {
            var name = NewProjektname.Trim();
            var kunde = NewKunde.Trim();
            var kt = NewKostentraeger.Trim();

            MainViewModel.DbInstance.AddOrEnsureProject(name, kunde, kt);

            NewProjektname = NewKunde = NewKostentraeger = string.Empty;
            IsAddPanelOpen = false;

            Refresh();
        }

        // --- Delete ---
        private void DeleteProject(ProjectItemVM item)
        {
            if (item == null) return;

            MainViewModel.DbInstance.DeleteProject(item.Projektname);
            Refresh();
        }

        // --- Edit ---
        private void StartEdit(ProjectItemVM item)
        {
            if (item == null) return;

            _editProjektnameOriginal = item.Projektname; // zum WHERE-Vergleich in der DB
            EditProjektname = item.Projektname;
            EditKunde = item.Kunde;
            EditKostentraeger = item.Kostentraeger;

            // Falls Add-Panel offen ist: zumachen, wir sind nicht im Basar
            IsAddPanelOpen = false;
            IsEditPanelOpen = true;
        }

        private bool CanSaveEdit()
        {
            return !string.IsNullOrWhiteSpace(EditProjektname)
                && !string.IsNullOrWhiteSpace(EditKunde)
                && !string.IsNullOrWhiteSpace(EditKostentraeger)
                && !string.IsNullOrWhiteSpace(_editProjektnameOriginal);
        }

        private void SaveEdit()
        {
            var newName = EditProjektname.Trim();
            var newKunde = EditKunde.Trim();
            var newKt = EditKostentraeger.Trim();

            MainViewModel.DbInstance.UpdateProject(
                originalProjektname: _editProjektnameOriginal,
                newProjektname: newName,
                kunde: newKunde,
                kostentraeger: newKt);

            CancelEdit(); // leert Felder & schließt Panel
            Refresh();
        }

        private void CancelEdit()
        {
            _editProjektnameOriginal = null;
            EditProjektname = string.Empty;
            EditKunde = string.Empty;
            EditKostentraeger = string.Empty;
            IsEditPanelOpen = false;
        }
    }

    internal sealed class ProjectItemVM : BindableBase
    {
        public string Projektname { get => _projektname; set => SetProperty(ref _projektname, value); }
        public string Kunde { get => _kunde; set => SetProperty(ref _kunde, value); }
        public string Kostentraeger { get => _kostentraeger; set => SetProperty(ref _kostentraeger, value); }
        public int Buchungen { get => _buchungen; set => SetProperty(ref _buchungen, value); }
        public double Stunden { get => _stunden; set => SetProperty(ref _stunden, value); }

        private string _projektname, _kunde, _kostentraeger;
        private int _buchungen;
        private double _stunden;
    }
}
