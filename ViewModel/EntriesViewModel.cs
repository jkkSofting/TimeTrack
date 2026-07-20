using System;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
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

        // --- Pausen ---
        private double _totalBreakHours;

        /// <summary>Summe aller Pausen (Lücken zwischen aufeinanderfolgenden Buchungen) am ausgewählten Tag.</summary>
        public double TotalBreakHours
        {
            get => _totalBreakHours;
            private set => SetProperty(ref _totalBreakHours, value);
        }

        // --- Warnungen / Überschneidungen ---
        private int _overlapCount;

        private bool _hasOverlaps;

        /// <summary>Anzahl der Zeitüberschneidungen zwischen aufeinanderfolgenden Buchungen am ausgewählten Tag.</summary>
        public int OverlapCount
        {
            get => _overlapCount;
            private set => SetProperty(ref _overlapCount, value);
        }

        /// <summary>True, wenn mindestens eine Überschneidung vorliegt (steuert die Warnanzeige).</summary>
        public bool HasOverlaps
        {
            get => _hasOverlaps;
            private set => SetProperty(ref _hasOverlaps, value);
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
            SaveEditCommand = new DelegateCommand(_ => SaveEdit(), _ => CanSaveEdit());
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

            // --- Pausen (Lücken) zwischen aufeinanderfolgenden Buchungen berechnen ---
            // Entries sind bereits nach Startzeit sortiert (SQL ORDER BY startzeit).
            // Die Lücke wird der jeweils vorherigen Buchung als "GapAfter" zugeordnet,
            // damit sie über die RowDetails direkt zwischen den beiden Zeilen erscheint.
            double totalBreak = 0;
            int overlaps = 0;
            for (int i = 1; i < Entries.Count; i++)
            {
                if (!TryParseTime(Entries[i - 1].Endzeit, out var prevEnd)
                    || !TryParseTime(Entries[i].Startzeit, out var curStart))
                    continue;

                var gap = curStart - prevEnd;
                if (gap > TimeSpan.Zero)
                {
                    Entries[i - 1].GapAfterHours = gap.TotalHours;
                    Entries[i - 1].GapAfterText = FormatGap(gap);
                    Entries[i - 1].HasGapAfter = true;
                    totalBreak += gap.TotalHours;
                }
                else if (gap < TimeSpan.Zero)
                {
                    // Diese Buchung beginnt, bevor die vorherige endet -> Überschneidung.
                    Entries[i - 1].OverlapAfterText = FormatGap(prevEnd - curStart);
                    Entries[i - 1].HasOverlapAfter = true;
                    Entries[i].IsOverlapping = true;
                    overlaps++;
                }
            }
            TotalBreakHours = Math.Round(totalBreak, 2);
            OverlapCount = overlaps;
            HasOverlaps = overlaps > 0;

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
                   && IsHHmm(NewEnd)
                   && IsStartBeforeEnd(NewStart, NewEnd);
        }

        private void AddEntry()
        {
            var selectedDate = DateTime.TryParseExact(SelectedDate, "dd.MM.yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt)
                ? dt
                : DateTime.Today;

            if (!ConfirmIfOverlapping(NewStart, NewEnd, -1))
                return;

            try
            {
                MainViewModel.DbInstance.AddTimeEntry(selectedDate, NewStart, NewEnd, NewProjektname, NewBeschreibung);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Buchung konnte nicht hinzugefügt werden", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
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

        private bool CanSaveEdit()
        {
            return IsEditPanelOpen
                   && IsHHmm(EditStart)
                   && IsHHmm(EditEnd)
                   && IsStartBeforeEnd(EditStart, EditEnd);
        }

        private void SaveEdit()
        {
            var selectedDate = DateTime.TryParseExact(SelectedDate, "dd.MM.yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt)
                ? dt
                : DateTime.Today;

            if (!ConfirmIfOverlapping(EditStart, EditEnd, _editId))
                return;

            try
            {
                MainViewModel.DbInstance.UpdateTimeEntry(_editId, selectedDate, EditStart, EditEnd, EditProjektname, EditBeschreibung);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Buchung konnte nicht gespeichert werden", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
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

        private static bool IsStartBeforeEnd(string startHHmm, string endHHmm)
        {
            var formats = new[] { @"hh\:mm", @"h\:mm" };
            return TimeSpan.TryParseExact(startHHmm, formats, CultureInfo.InvariantCulture, out var start)
                   && TimeSpan.TryParseExact(endHHmm, formats, CultureInfo.InvariantCulture, out var end)
                   && start < end;
        }

        private static double CalcHours(string startHHmm, string endHHmm)
        {
            var formats = new[] { @"h\:mm", @"hh\:mm", "Hmm", "HHmm" };

            var start = TimeSpan.ParseExact(startHHmm, formats, CultureInfo.InvariantCulture);
            var end = TimeSpan.ParseExact(endHHmm, formats, CultureInfo.InvariantCulture);

            return (end - start).TotalHours;
        }

        /// <summary>
        /// Prüft, ob der angegebene Zeitraum sich mit bestehenden Buchungen des Tages überschneidet.
        /// Bei Überschneidung wird eine Rückfrage angezeigt. Gibt true zurück, wenn fortgefahren werden soll.
        /// </summary>
        /// <param name="excludeId">Id der Buchung, die ignoriert werden soll (beim Bearbeiten), sonst -1.</param>
        private bool ConfirmIfOverlapping(string startHHmm, string endHHmm, int excludeId)
        {
            if (!TryParseTime(startHHmm, out var start) || !TryParseTime(endHHmm, out var end))
                return true; // Formatfehler werden separat abgefangen

            var conflicts = new List<string>();
            foreach (var e in Entries)
            {
                if (e.Id == excludeId)
                    continue;

                if (!TryParseTime(e.Startzeit, out var eStart) || !TryParseTime(e.Endzeit, out var eEnd))
                    continue;

                // Überschneidung, wenn sich die Intervalle berühren/überlappen.
                if (start < eEnd && eStart < end)
                    conflicts.Add($"• {e.Projektname}  ({e.Startzeit}–{e.Endzeit})");
            }

            if (conflicts.Count == 0)
                return true;

            var result = MessageBox.Show(
                "Diese Buchung überschneidet sich mit:\n\n"
                + string.Join("\n", conflicts)
                + "\n\nTrotzdem speichern?",
                "Zeitüberschneidung",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            return result == MessageBoxResult.Yes;
        }

        private static bool TryParseTime(string hhmm, out TimeSpan value)
        {
            var formats = new[] { @"h\:mm", @"hh\:mm", "Hmm", "HHmm" };
            return TimeSpan.TryParseExact(hhmm, formats, CultureInfo.InvariantCulture, out value);
        }

        private static string FormatGap(TimeSpan gap)
        {
            int totalMinutes = (int)Math.Round(gap.TotalMinutes);
            if (totalMinutes < 60)
                return totalMinutes + " min";

            return (totalMinutes / 60) + ":" + (totalMinutes % 60).ToString("00") + " h";
        }
    }

    internal sealed class EntryItemVM : BindableBase
    {
        private int _id;
        private string _projektname, _start, _end, _beschreibung;
        private double _dauer;
        private double _gapAfterHours;
        private bool _hasGapAfter;
        private string _gapAfterText;
        private bool _hasOverlapAfter;
        private string _overlapAfterText;
        private bool _isOverlapping;

        public int Id { get => _id; set => SetProperty(ref _id, value); }
        public string Projektname { get => _projektname; set => SetProperty(ref _projektname, value); }
        public string Startzeit { get => _start; set => SetProperty(ref _start, value); }
        public string Endzeit { get => _end; set => SetProperty(ref _end, value); }
        public string Beschreibung { get => _beschreibung; set => SetProperty(ref _beschreibung, value); }
        public double Dauer { get => _dauer; set => SetProperty(ref _dauer, value); }

        /// <summary>Länge der Pause nach dieser Buchung in Stunden (0 = keine Pause).</summary>
        public double GapAfterHours { get => _gapAfterHours; set => SetProperty(ref _gapAfterHours, value); }

        /// <summary>True, wenn es nach dieser Buchung eine Lücke zur nächsten gibt.</summary>
        public bool HasGapAfter { get => _hasGapAfter; set => SetProperty(ref _hasGapAfter, value); }

        /// <summary>Formatierte Pausenlänge, z. B. "15 min" oder "1:30 h".</summary>
        public string GapAfterText { get => _gapAfterText; set => SetProperty(ref _gapAfterText, value); }

        /// <summary>True, wenn die nächste Buchung beginnt, bevor diese endet (Überschneidung).</summary>
        public bool HasOverlapAfter { get => _hasOverlapAfter; set => SetProperty(ref _hasOverlapAfter, value); }

        /// <summary>Formatierte Länge der Überschneidung mit der nächsten Buchung.</summary>
        public string OverlapAfterText { get => _overlapAfterText; set => SetProperty(ref _overlapAfterText, value); }

        /// <summary>True, wenn diese Buchung beginnt, bevor die vorherige endet (wird als Warnung hervorgehoben).</summary>
        public bool IsOverlapping { get => _isOverlapping; set => SetProperty(ref _isOverlapping, value); }
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