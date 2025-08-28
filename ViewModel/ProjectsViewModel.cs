// ProjectsViewModel.cs
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Windows.Data;
using System.Windows.Input;
using Zeitmanagement.MVVM;

namespace Zeitmanagement.ViewModel
{
    internal sealed class ProjectsViewModel : BaseViewModel
    {
        public ObservableCollection<ProjectItemVM> Projects { get; } = new ObservableCollection<ProjectItemVM>();

        // Gefilterte Sicht
        public ICollectionView ProjectsView { get; }

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

        private bool _isAddPanelOpen;
        public bool IsAddPanelOpen { get => _isAddPanelOpen; set => SetProperty(ref _isAddPanelOpen, value); }

        // Add-Form Felder
        public string NewProjektname { get => _newProjektname; set => SetProperty(ref _newProjektname, value); }
        public string NewKunde { get => _newKunde; set => SetProperty(ref _newKunde, value); }
        public string NewKostentraeger { get => _newKostentraeger; set => SetProperty(ref _newKostentraeger, value); }
        private string _newProjektname, _newKunde, _newKostentraeger;

        // UI-Text
        private string _headerSubtitle = "Verwalte Projekte, Buchungen & Stunden";
        public string HeaderSubtitle { get => _headerSubtitle; set => SetProperty(ref _headerSubtitle, value); }

        // Commands
        public DelegateCommand RefreshCommand { get; }
        public DelegateCommand ToggleAddPanelCommand { get; }
        public DelegateCommand AddProjectCommand { get; }
        public DelegateCommand DeleteProjectCommand { get; }

        public ProjectsViewModel()
        {
            ProjectsView = CollectionViewSource.GetDefaultView(Projects);
            ProjectsView.Filter = FilterProject;

            RefreshCommand = new DelegateCommand(_ => Refresh());
            ToggleAddPanelCommand = new DelegateCommand(_ => IsAddPanelOpen = !IsAddPanelOpen);
            AddProjectCommand = new DelegateCommand(_ => AddProject(), _ => CanAddProject());
            DeleteProjectCommand = new DelegateCommand(p => DeleteProject(p as ProjectItemVM), p => p is ProjectItemVM);

            Refresh();
        }

        public override void Refresh()
        {
            Projects.Clear();

            var db = MainViewModel.DbInstance;
            var projects = db.GetProjects().ToList();

            foreach (var p in projects)
            {
                // Hole Buchungen für dieses Projekt
                var entries = db.GetTimeEntriesForProject(p.Projektname);
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
            if (string.IsNullOrWhiteSpace(SearchText)) return true;
            var it = obj as ProjectItemVM;
            if (it == null) return false;

            var q = SearchText.Trim();
            return (it.Projektname?.IndexOf(q, StringComparison.OrdinalIgnoreCase) ?? -1) >= 0
                || (it.Kunde?.IndexOf(q, StringComparison.OrdinalIgnoreCase) ?? -1) >= 0
                || (it.Kostentraeger?.IndexOf(q, StringComparison.OrdinalIgnoreCase) ?? -1) >= 0;
        }

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

            // DB: Add or ensure
            MainViewModel.DbInstance.AddOrEnsureProject(name, kunde, kt);

            // UI aufräumen
            NewProjektname = NewKunde = NewKostentraeger = string.Empty;
            IsAddPanelOpen = false;

            Refresh();
        }

        private void DeleteProject(ProjectItemVM item)
        {
            if (item == null) return;

            // Falls deine DB-API anders heißt (RemoveProject o.ä.), hier anpassen:
            // Optional: Confirmation vorab (MessageBox etc.) – mache ich MVVM-konform bewusst nicht hier.
            MainViewModel.DbInstance.DeleteProject(item.Projektname);

            Refresh();
        }
    }

    internal sealed class ProjectItemVM:BindableBase
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
