using System;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Zeitmanagement.MVVM;

namespace Zeitmanagement.ViewModel
{
    internal sealed class EntriesViewModel : BaseViewModel
    {
        // --- Tabellen-Daten ---
        public ObservableCollection<EntryItemVM> Entries { get; } = new ObservableCollection<EntryItemVM>();

        public ObservableCollection<string> ProjectNames { get; } = new ObservableCollection<string>();

        // --- Sidebar-Übersichten ---
        public ObservableCollection<SummaryItem> SumByProject { get; } = new ObservableCollection<SummaryItem>();

        public ObservableCollection<SummaryItem> SumByKTR { get; } = new ObservableCollection<SummaryItem>();

        // --- Datumsauswahl ---
        private string _selectedDate = DateTime.Today.ToString("dd.MM.yyyy");

        public string SelectedDate
        {
            get => _selectedDate;
            set
            {
                SetProperty(ref _selectedDate, value);
                Refresh();
            }
        }

        // --- Header-Text ---
        private string _headerSubtitle = "Buchungen pro Tag verwalten (hinzufügen, bearbeiten, löschen)";

        public string HeaderSubtitle
        {
            get => _headerSubtitle;
            set => SetProperty(ref _headerSubtitle, value);
        }

        // --- HEUTE KPIs ---
        private double _todayTotalHours;

        private int _todayEntryCount;

        /// <summary>Gesamtstunden aller Buchungen am heutigen Datum.</summary>
        public double TodayTotalHours
        {
            get => _todayTotalHours;
            private set => SetProperty(ref _todayTotalHours, value);
        }

        /// <summary>Anzahl der heutigen Buchungen.</summary>
        public int TodayEntryCount
        {
            get => _todayEntryCount;
            private set => SetProperty(ref _todayEntryCount, value);
        }

        // --- Add-Form ---
        private string _newProjektname, _newStart, _newEnd, _newBeschreibung;

        public string NewProjektname { get => _newProjektname; set => SetProperty(ref _newProjektname, value); }
        public string NewStart { get => _newStart; set => SetProperty(ref _newStart, value); }
        public string NewEnd { get => _newEnd; set => SetProperty(ref _newEnd, value); }
        public string NewBeschreibung { get => _newBeschreibung; set => SetProperty(ref _newBeschreibung, value); }

        // --- Edit-Form ---
        private bool _isEditPanelOpen;

        private int _editId;
        private string _editProjektname, _editStart, _editEnd, _editBeschreibung;

        public bool IsEditPanelOpen { get => _isEditPanelOpen; set => SetProperty(ref _isEditPanelOpen, value); }
        public string EditProjektname { get => _editProjektname; set => SetProperty(ref _editProjektname, value); }
        public string EditStart { get => _editStart; set => SetProperty(ref _editStart, value); }
        public string EditEnd { get => _editEnd; set => SetProperty(ref _editEnd, value); }
        public string EditBeschreibung { get => _editBeschreibung; set => SetProperty(ref _editBeschreibung, value); }

        // --- Commands ---
        public DelegateCommand RefreshCommand { get; }

        public DelegateCommand TodayCommand { get; }
        public DelegateCommand AddEntryCommand { get; }
        public DelegateCommand ResetNewFormCommand { get; }
        public DelegateCommand EditEntryCommand { get; }
        public DelegateCommand SaveEditCommand { get; }
        public DelegateCommand CancelEditCommand { get; }
        public DelegateCommand DeleteEntryCommand { get; }
        public DelegateCommand GoToNextDayCommand { get; }
        public DelegateCommand GoToLastDayCommand { get; }

        public EntriesViewModel()
        {
            RefreshCommand = new DelegateCommand(_ => Refresh());
            TodayCommand = new DelegateCommand(_ => SelectedDate = DateTime.Today.ToString("dd.MM.yyyy"));

            AddEntryCommand = new DelegateCommand(_ => AddEntry(), _ => CanAdd());
            ResetNewFormCommand = new DelegateCommand(_ => ResetNewForm());

            EditEntryCommand = new DelegateCommand(o => BeginEdit(o as EntryItemVM), o => o is EntryItemVM);
            SaveEditCommand = new DelegateCommand(_ => SaveEdit(), _ => IsEditPanelOpen);
            CancelEditCommand = new DelegateCommand(_ => CancelEdit());
            DeleteEntryCommand = new DelegateCommand(o => DeleteEntry(o as EntryItemVM), o => o is EntryItemVM);

            GoToNextDayCommand = new DelegateCommand(_ =>
            {
                var date = DateTime.TryParseExact(SelectedDate, "dd.MM.yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt)
                    ? dt
                    : DateTime.Today;
                SelectedDate = date.AddDays(1).ToString("dd.MM.yyyy");
            });

            GoToLastDayCommand = new DelegateCommand(_ =>
            {
                var date = DateTime.TryParseExact(SelectedDate, "dd.MM.yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt)
                    ? dt
                    : DateTime.Today;
                SelectedDate = date.AddDays(-1).ToString("dd.MM.yyyy");
            });

            // sinnvolle Defaults fürs Hinzufügen
            NewStart = "09:00";
            NewEnd = "10:00";

            // initial laden
            Refresh();
        }

        public override void Refresh()
        {
            var db = MainViewModel.DbInstance;

            // Projekte neu laden (für Dropdowns)
            ProjectNames.Clear();
            foreach (var p in db.GetProjects())
                ProjectNames.Add(p.Projektname);

            // Einträge des ausgewählten Tages neu laden
            Entries.Clear();

            var date = DateTime.TryParseExact(SelectedDate, "dd.MM.yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt)
                ? dt
                : DateTime.Today;

            foreach (var e in db.GetTimeEntriesForDate(date))
            {
                var dur = CalcHours(e.Startzeit, e.Endzeit);
                Entries.Add(new EntryItemVM
                {
                    Id = e.Id,
                    Projektname = e.Projektname,
                    Startzeit = e.Startzeit,
                    Endzeit = e.Endzeit,
                    Beschreibung = e.Beschreibung,
                    Dauer = Math.Round(dur, 2)
                });
            }

            // Defaults im Add-Panel
            if (string.IsNullOrEmpty(NewProjektname) && ProjectNames.Count > 0)
                NewProjektname = ProjectNames[0];

            // --- Sidebar-Aggregate (pro ausgewähltem Tag) ---
            // Mapping Projekt -> Kostenträger
            var projektToKtr = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var p in db.GetProjects())
                projektToKtr[p.Projektname] = p.Kostentraeger;

            // Summen je Projekt
            SumByProject.Clear();
            foreach (var it in Entries
                .GroupBy(e => e.Projektname)
                .Select(g => new SummaryItem
                {
                    Name = g.Key,
                    Hours = Math.Round(g.Sum(x => x.Dauer), 2),
                    Count = g.Count()
                })
                .OrderByDescending(x => x.Hours))
            {
                SumByProject.Add(it);
            }

            // Summen je Kostenträger
            SumByKTR.Clear();
            foreach (var it in Entries
                .GroupBy(e => projektToKtr.TryGetValue(e.Projektname, out var ktr) ? ktr : "—")
                .Select(g => new SummaryItem
                {
                    Name = g.Key,
                    Hours = Math.Round(g.Sum(x => x.Dauer), 2),
                    Count = g.Count()
                })
                .OrderByDescending(x => x.Hours))
            {
                SumByKTR.Add(it);
            }

            // --- HEUTE-KPIs (unabhängig von SelectedDate) ---

            var todayEntries = db.GetTimeEntriesForDate(date).ToList();
            TodayEntryCount = todayEntries.Count;
            TodayTotalHours = Math.Round(
                todayEntries.Sum(te => CalcHours(te.Startzeit, te.Endzeit)), 2);
        }

        // --- Add ---
        private bool CanAdd()
        {
            return !string.IsNullOrWhiteSpace(NewProjektname)
                   && IsHHmm(NewStart)
                   && IsHHmm(NewEnd);
        }

        private void AddEntry()
        {
            var selectedDate = DateTime.TryParseExact(SelectedDate, "dd.MM.yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt)
                ? dt
                : DateTime.Today;
            MainViewModel.DbInstance.AddTimeEntry(selectedDate, NewStart, NewEnd, NewProjektname, NewBeschreibung);
            ResetNewForm();
            Refresh();
        }

        private void ResetNewForm()
        {
            NewBeschreibung = string.Empty;
            try
            {
                var end = TimeSpan.ParseExact(NewEnd ?? "10:00", @"hh\:mm", CultureInfo.InvariantCulture);
                var nextStart = end;
                var nextEnd = end + TimeSpan.FromMinutes(30);

                NewStart = nextStart.ToString(@"hh\:mm");
                NewEnd = nextEnd.ToString(@"hh\:mm");
            }
            catch
            {
                NewStart = "09:00";
                NewEnd = "10:00";
            }
        }

        // --- Edit ---
        private void BeginEdit(EntryItemVM vm)
        {
            if (vm == null) return;
            _editId = vm.Id;
            EditProjektname = vm.Projektname;
            EditStart = vm.Startzeit;
            EditEnd = vm.Endzeit;
            EditBeschreibung = vm.Beschreibung;
            IsEditPanelOpen = true;
        }

        private void SaveEdit()
        {
            var selectedDate = DateTime.TryParseExact(SelectedDate, "dd.MM.yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt)
                ? dt
                : DateTime.Today;

            MainViewModel.DbInstance.UpdateTimeEntry(_editId, selectedDate, EditStart, EditEnd, EditProjektname, EditBeschreibung);
            IsEditPanelOpen = false;
            Refresh();
        }

        private void CancelEdit()
        {
            IsEditPanelOpen = false;
        }

        // --- Delete ---
        private void DeleteEntry(EntryItemVM vm)
        {
            if (vm == null) return;
            MainViewModel.DbInstance.DeleteTimeEntry(vm.Id);
            Refresh();
        }

        // --- Helpers ---
        private static bool IsHHmm(string s)
        {
            return TimeSpan.TryParseExact(
                s,
                new[] { @"hh\:mm", @"h\:mm" },
                CultureInfo.InvariantCulture,
                out _);
        }

        private static double CalcHours(string startHHmm, string endHHmm)
        {
            var formats = new[] { @"h\:mm", @"hh\:mm", "Hmm", "HHmm" };

            var start = TimeSpan.ParseExact(startHHmm, formats, CultureInfo.InvariantCulture);
            var end = TimeSpan.ParseExact(endHHmm, formats, CultureInfo.InvariantCulture);

            return (end - start).TotalHours;
        }
    }

    internal sealed class EntryItemVM : BindableBase
    {
        private int _id;
        private string _projektname, _start, _end, _beschreibung;
        private double _dauer;

        public int Id { get => _id; set => SetProperty(ref _id, value); }
        public string Projektname { get => _projektname; set => SetProperty(ref _projektname, value); }
        public string Startzeit { get => _start; set => SetProperty(ref _start, value); }
        public string Endzeit { get => _end; set => SetProperty(ref _end, value); }
        public string Beschreibung { get => _beschreibung; set => SetProperty(ref _beschreibung, value); }
        public double Dauer { get => _dauer; set => SetProperty(ref _dauer, value); }
    }

    internal sealed class SummaryItem : BindableBase
    {
        private string _name;
        private double _hours;
        private int _count;

        public string Name { get => _name; set => SetProperty(ref _name, value); }
        public double Hours { get => _hours; set => SetProperty(ref _hours, value); }
        public int Count { get => _count; set => SetProperty(ref _count, value); }
    }
}