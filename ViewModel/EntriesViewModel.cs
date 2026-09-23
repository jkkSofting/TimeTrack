using System;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using Zeitmanagement.MVVM;

namespace Zeitmanagement.ViewModel
{
    internal sealed class EntriesViewModel : BaseViewModel
    {
        // --- Tabellen-Daten ---
        public ObservableCollection<EntryItemVM> Entries { get; } = new ObservableCollection<EntryItemVM>();

        /// <summary>Buchungen des ausgewählten Tages, nacheinander eingeplant (Überschneidungen aufgelöst).</summary>
        public ObservableCollection<SequencedEntryVM> MergedEntries { get; } = new ObservableCollection<SequencedEntryVM>();

        public ObservableCollection<string> ProjectNames { get; } = new ObservableCollection<string>();

        // --- Sidebar-Übersichten ---
        public ObservableCollection<SummaryItem> SumByProject { get; } = new ObservableCollection<SummaryItem>();

        public ObservableCollection<SummaryItem> SumByKTR { get; } = new ObservableCollection<SummaryItem>();

        // --- Git-Graph-Timeline (visuelle Zusammenfassung des ausgewählten Tages) ---
        public ObservableCollection<GraphEdge> GraphEdges { get; } = new ObservableCollection<GraphEdge>();
        public ObservableCollection<GraphNode> GraphNodes { get; } = new ObservableCollection<GraphNode>();
        public ObservableCollection<GraphLabel> GraphLabels { get; } = new ObservableCollection<GraphLabel>();

        private double _graphWidth;
        public double GraphWidth { get => _graphWidth; private set => SetProperty(ref _graphWidth, value); }

        private double _graphHeight;
        public double GraphHeight { get => _graphHeight; private set => SetProperty(ref _graphHeight, value); }

        private bool _hasGraphEntries;

        /// <summary>True, wenn der ausgewählte Tag Buchungen hat (steuert Graph- vs. Leer-Anzeige).</summary>
        public bool HasGraphEntries
        {
            get => _hasGraphEntries;
            private set
            {
                if (SetProperty(ref _hasGraphEntries, value))
                    RaisePropertyChanged(nameof(HasNoGraphEntries));
            }
        }

        public bool HasNoGraphEntries => !HasGraphEntries;

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

        // --- Zusammenfassen-Toggle (tatsächliche Arbeitszeit statt Summe aller Buchungen) ---
        private bool _isMergedView;

        /// <summary>
        /// True, wenn überschneidende Buchungen nacheinander eingeplant angezeigt werden sollen
        /// (Überschneidungen werden aufgelöst, indem die spätere Buchung nach hinten verschoben wird),
        /// damit sichtbar wird, wie lange der Arbeitstag tatsächlich gedauert hat.
        /// </summary>
        public bool IsMergedView
        {
            get => _isMergedView;
            set
            {
                if (SetProperty(ref _isMergedView, value))
                {
                    RaisePropertyChanged(nameof(IsRawView));
                    RaisePropertyChanged(nameof(ShowMandatoryBreakPill));
                    RaisePropertyChanged(nameof(ShowOverworkWarningPill));
                }
            }
        }

        /// <summary>Komplement zu <see cref="IsMergedView"/>, praktisch für Visibility-Bindings.</summary>
        public bool IsRawView => !IsMergedView;

        private double _actualWorkedHours;

        /// <summary>
        /// Tatsächliche Dauer des Arbeitstages (erste Buchung bis letztes, ggf. verschobenes Ende), nachdem
        /// überschneidende Buchungen nacheinander statt gleichzeitig eingeplant wurden.
        /// </summary>
        public double ActualWorkedHours
        {
            get => _actualWorkedHours;
            private set => SetProperty(ref _actualWorkedHours, value);
        }

        private double _totalMergedBreakHours;

        /// <summary>Summe der echten Lücken (ohne Buchung) zwischen den nacheinander eingeplanten Buchungen.</summary>
        public double TotalMergedBreakHours
        {
            get => _totalMergedBreakHours;
            private set => SetProperty(ref _totalMergedBreakHours, value);
        }

        private int _mandatoryBreakMinutes;

        /// <summary>
        /// Gesetzlich notwendige Mittagspause anhand der Gesamtarbeitszeit: bis 6h keine, &gt;6h bis 9h
        /// 30 Minuten, über 9h weitere 15 Minuten (also 45 insgesamt).
        /// </summary>
        public int MandatoryBreakMinutes
        {
            get => _mandatoryBreakMinutes;
            private set
            {
                if (SetProperty(ref _mandatoryBreakMinutes, value))
                {
                    RaisePropertyChanged(nameof(HasMandatoryBreak));
                    RaisePropertyChanged(nameof(ShowMandatoryBreakPill));
                }
            }
        }

        public bool HasMandatoryBreak => MandatoryBreakMinutes > 0;

        /// <summary>Steuert die Sichtbarkeit der Mittagspausen-Kachel (nur in der zusammengefassten Ansicht).</summary>
        public bool ShowMandatoryBreakPill => IsMergedView && HasMandatoryBreak;

        private bool _showOverworkWarning;

        /// <summary>True, wenn die tatsächliche Tagesdauer 10h + notwendige Mittagspause überschreitet.</summary>
        public bool ShowOverworkWarning
        {
            get => _showOverworkWarning;
            private set
            {
                if (SetProperty(ref _showOverworkWarning, value))
                    RaisePropertyChanged(nameof(ShowOverworkWarningPill));
            }
        }

        /// <summary>Steuert die Sichtbarkeit der Überlastungs-Warnung (nur in der zusammengefassten Ansicht).</summary>
        public bool ShowOverworkWarningPill => IsMergedView && ShowOverworkWarning;

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
        public DelegateCommand DeleteEditingEntryCommand { get; }
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
            DeleteEditingEntryCommand = new DelegateCommand(_ => DeleteEditingEntry(), _ => IsEditPanelOpen);

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

            RebuildSequencedView();
            BuildGraph();

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

        /// <summary>Löscht die Buchung, die gerade im Bearbeiten-Panel geöffnet ist.</summary>
        private void DeleteEditingEntry()
        {
            MainViewModel.DbInstance.DeleteTimeEntry(_editId);
            IsEditPanelOpen = false;
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

        /// <summary>
        /// Plant die Buchungen des ausgewählten Tages der Reihe nach ein, um die tatsächliche Dauer des
        /// Arbeitstages zu ermitteln:
        /// 1) Buchungen chronologisch nach ursprünglicher Startzeit hintereinanderlegen.
        /// 2) Überschneidet eine Buchung die vorherige, wird sie (mit unveränderter Dauer) direkt hinter
        ///    das Ende der vorherigen verschoben und entsprechend markiert (<see cref="SequencedEntryVM.IsShifted"/>).
        ///    Jede nachfolgende Buchung hängt sich an diese verschobene Buchung an, weil der "Cursor"
        ///    (Ende der zuletzt eingeplanten Buchung) für den nächsten Vergleich weiterläuft.
        /// 3) Notwendige Mittagspause anhand der Gesamtarbeitszeit ermitteln (bis 6h keine, &gt;6h bis 9h
        ///    30 Min, über 9h weitere 15 Min).
        /// 4) Warnen, wenn die tatsächliche Tagesdauer 10h + Mittagspause überschreitet.
        /// </summary>
        private void RebuildSequencedView()
        {
            MergedEntries.Clear();

            var sorted = new List<(TimeSpan Start, TimeSpan End, EntryItemVM Entry)>();
            foreach (var e in Entries)
            {
                if (TryParseTime(e.Startzeit, out var s) && TryParseTime(e.Endzeit, out var en) && en > s)
                    sorted.Add((s, en, e));
            }
            sorted.Sort((a, b) => a.Start.CompareTo(b.Start));

            TimeSpan? cursor = null;
            TimeSpan firstStart = TimeSpan.Zero;
            bool isFirst = true;
            double naturalGapMinutes = 0;

            foreach (var iv in sorted)
            {
                var duration = iv.End - iv.Start;
                TimeSpan actualStart;
                bool isShifted;

                if (cursor.HasValue && iv.Start < cursor.Value)
                {
                    // Überschneidung: diese Buchung (und damit alle folgenden) hinter die vorherige schieben.
                    actualStart = cursor.Value;
                    isShifted = true;
                }
                else
                {
                    actualStart = iv.Start;
                    isShifted = false;
                    if (cursor.HasValue)
                        naturalGapMinutes += (iv.Start - cursor.Value).TotalMinutes;
                }

                if (isFirst)
                {
                    firstStart = actualStart;
                    isFirst = false;
                }

                var actualEnd = actualStart + duration;
                cursor = actualEnd;

                MergedEntries.Add(new SequencedEntryVM
                {
                    Projektname = iv.Entry.Projektname,
                    Startzeit = actualStart.ToString(@"hh\:mm", CultureInfo.InvariantCulture),
                    Endzeit = actualEnd.ToString(@"hh\:mm", CultureInfo.InvariantCulture),
                    Dauer = Math.Round(duration.TotalHours, 2),
                    Beschreibung = iv.Entry.Beschreibung,
                    IsShifted = isShifted,
                    OriginalStartzeit = iv.Start.ToString(@"hh\:mm", CultureInfo.InvariantCulture)
                });
            }

            double grossWorkHours = MergedEntries.Sum(x => x.Dauer);

            // --- Notwendige Mittagspause anhand der Gesamtarbeitszeit: bis 6h keine, >6h bis 9h 30 Min, ---
            // --- über 9h weitere 15 Min (45 insgesamt).                                                 ---
            int requiredBreakMinutes = grossWorkHours > 9 ? 45 : grossWorkHours > 6 ? 30 : 0;

            double gesamtzeitHours = cursor.HasValue ? (cursor.Value - firstStart).TotalHours : 0;

            TotalMergedBreakHours = Math.Round(naturalGapMinutes / 60.0, 2);
            MandatoryBreakMinutes = requiredBreakMinutes;
            ActualWorkedHours = Math.Round(gesamtzeitHours, 2);

            // --- Warnung: tatsächliche Tagesdauer überschreitet 10h + notwendige Mittagspause ---
            ShowOverworkWarning = gesamtzeitHours > 10.0 + requiredBreakMinutes / 60.0;
        }

        private static string FormatGap(TimeSpan gap)
        {
            int totalMinutes = (int)Math.Round(gap.TotalMinutes);
            if (totalMinutes < 60)
                return totalMinutes + " min";

            return (totalMinutes / 60) + ":" + (totalMinutes % 60).ToString("00") + " h";
        }

        // ── Git-Graph geometry construction ──
        //
        // The trunk (lane 0) represents the passage of time from the first clock-in to the
        // last clock-out of the selected day, top to bottom. Entries that overlap an already
        // active entry are pushed onto a side lane (a parallel "branch") for their duration
        // and merge back into the trunk afterwards. Stretches where nothing at all is booked
        // are drawn as a dashed trunk segment ("Pause"); stretches where the trunk itself is
        // idle but a branch is still running are drawn as a thin neutral connector so the
        // thread stays visually continuous without being mislabelled as a break.
        private const double GraphPixelsPerMinute = 1.4;
        private const double GraphLaneSpacing = 26;
        private const double GraphTrunkX = 22;
        private const double GraphNodeRadius = 5;
        private const double GraphEndpointRadius = 7;
        private const double GraphCurveReach = 14;
        private const double GraphLabelGap = 16;
        private const double GraphLabelColumnWidth = 320;
        private const double GraphMinHeight = 8;
        private const double GraphLabelLineHeight = 16;
        private const double GraphMinNodeSpacing = 20;

        private static readonly Color[] GraphPalette =
        {
            Color.FromRgb(0x5A, 0xC8, 0xFA), // blue
            Color.FromRgb(0x8E, 0x8B, 0xFF), // purple
            Color.FromRgb(0xFF, 0x6B, 0x6B), // red
            Color.FromRgb(0x4E, 0xCA, 0x84), // green
            Color.FromRgb(0xFF, 0xC1, 0x07), // yellow
            Color.FromRgb(0xFF, 0x8A, 0x50), // orange
            Color.FromRgb(0xE0, 0x5A, 0xA8), // pink
            Color.FromRgb(0x00, 0xD9, 0xC0), // teal
            Color.FromRgb(0xB3, 0x88, 0xFF), // lavender
            Color.FromRgb(0x7E, 0xD3, 0x57), // lime
        };

        private static readonly SolidColorBrush GraphBreakBrush = CreateFrozenBrush(Color.FromRgb(0x6B, 0x73, 0x90));
        private static readonly SolidColorBrush GraphEndpointBrush = CreateFrozenBrush(Color.FromRgb(0xF5, 0xF7, 0xFB));
        private static readonly DoubleCollection GraphBreakDashes = CreateFrozenDashes(4, 3);

        private static SolidColorBrush CreateFrozenBrush(Color c)
        {
            var b = new SolidColorBrush(c);
            b.Freeze();
            return b;
        }

        private static DoubleCollection CreateFrozenDashes(params double[] values)
        {
            var d = new DoubleCollection(values);
            d.Freeze();
            return d;
        }

        private static string N(double v) => v.ToString("F1", CultureInfo.InvariantCulture);

        private static string LineData(double x1, double y1, double x2, double y2) =>
            $"M{N(x1)},{N(y1)} L{N(x2)},{N(y2)}";

        private static string FormatGraphDuration(TimeSpan span)
        {
            if (span.TotalHours >= 1)
                return $"{(int)span.TotalHours}h {span.Minutes:00}m";
            return $"{Math.Max(1, (int)span.TotalMinutes)}m";
        }

        private void BuildGraph()
        {
            GraphEdges.Clear();
            GraphNodes.Clear();
            GraphLabels.Clear();

            var intervals = new List<(TimeSpan Start, TimeSpan End, EntryItemVM Source)>();
            foreach (var e in Entries)
            {
                if (TryParseTime(e.Startzeit, out var s) && TryParseTime(e.Endzeit, out var en) && en > s)
                    intervals.Add((s, en, e));
            }
            intervals.Sort((a, b) => a.Start != b.Start ? a.Start.CompareTo(b.Start) : a.End.CompareTo(b.End));

            HasGraphEntries = intervals.Count > 0;
            if (intervals.Count == 0)
            {
                GraphWidth = 0;
                GraphHeight = 0;
                return;
            }

            var dayEnd = intervals.Max(iv => iv.End);

            // Time-to-pixel mapping: proportional to elapsed minutes, but every distinct
            // start/end time point is guaranteed at least GraphMinNodeSpacing pixels from its
            // neighbour. Without this, a cluster of short or overlapping bookings (e.g. several
            // one-minute entries a few minutes apart) would place their nodes almost on top of
            // each other, making them impossible to tell apart or click individually.
            var timePoints = new List<TimeSpan>();
            foreach (var iv in intervals)
            {
                timePoints.Add(iv.Start);
                timePoints.Add(iv.End);
            }
            timePoints = timePoints.Distinct().OrderBy(t => t).ToList();

            var yLookup = new Dictionary<TimeSpan, double>();
            double cursorY = 0;
            for (int ti = 0; ti < timePoints.Count; ti++)
            {
                if (ti == 0)
                {
                    yLookup[timePoints[ti]] = 0;
                    continue;
                }
                double naturalGap = (timePoints[ti] - timePoints[ti - 1]).TotalMinutes * GraphPixelsPerMinute;
                cursorY += Math.Max(GraphMinNodeSpacing, naturalGap);
                yLookup[timePoints[ti]] = cursorY;
            }
            double Y(TimeSpan t) => yLookup[t];

            // Lane assignment via interval-graph coloring (sweep by start time).
            var active = new List<(int Lane, TimeSpan End)>();
            var lanes = new int[intervals.Count];
            int maxLane = 0;
            for (int i = 0; i < intervals.Count; i++)
            {
                var iv = intervals[i];
                active.RemoveAll(a => a.End <= iv.Start);
                int lane = 0;
                while (active.Any(a => a.Lane == lane)) lane++;
                lanes[i] = lane;
                active.Add((lane, iv.End));
                if (lane > maxLane) maxLane = lane;
            }

            var projectColors = new Dictionary<string, SolidColorBrush>();
            int colorIndex = 0;
            SolidColorBrush ColorFor(string project)
            {
                if (!projectColors.TryGetValue(project, out var b))
                {
                    b = CreateFrozenBrush(GraphPalette[colorIndex % GraphPalette.Length]);
                    projectColors[project] = b;
                    colorIndex++;
                }
                return b;
            }

            double labelX = GraphTrunkX + (maxLane + 1) * GraphLaneSpacing + GraphLabelGap;

            // Labels are collected here first and only added to the bound collection after a
            // collision pass (see below): dense clusters of short/overlapping bookings would
            // otherwise place several labels at nearly the same Y and render them on top of
            // each other as unreadable, garbled text.
            var labelBuilder = new List<GraphLabel>();

            // Trunk gaps: stretches between consecutive lane-0 entries (and after the last one,
            // if the overall day end was set by a branch entry) where the trunk carries nothing.
            var lane0 = intervals.Where((iv, idx) => lanes[idx] == 0).OrderBy(iv => iv.Start).ToList();
            var trunkGaps = new List<(TimeSpan GapStart, TimeSpan GapEnd)>();
            for (int i = 1; i < lane0.Count; i++)
                if (lane0[i].Start > lane0[i - 1].End)
                    trunkGaps.Add((lane0[i - 1].End, lane0[i].Start));
            if (lane0.Count > 0 && lane0[lane0.Count - 1].End < dayEnd)
                trunkGaps.Add((lane0[lane0.Count - 1].End, dayEnd));

            foreach (var (gapStart, gapEnd) in trunkGaps)
            {
                bool isTrueBreak = !intervals.Any(iv => iv.Start < gapEnd && iv.End > gapStart);
                double y1 = Y(gapStart), y2 = Y(gapEnd);

                GraphEdges.Add(new GraphEdge
                {
                    Geometry = LineData(GraphTrunkX, y1, GraphTrunkX, y2),
                    Stroke = GraphBreakBrush,
                    StrokeThickness = isTrueBreak ? 2.5 : 1.5,
                    Dashes = isTrueBreak ? GraphBreakDashes : null
                });

                if (isTrueBreak && (gapEnd - gapStart).TotalMinutes >= 2)
                {
                    labelBuilder.Add(new GraphLabel
                    {
                        Left = labelX,
                        Top = (y1 + y2) / 2 - 9,
                        ProjectRun = "Pause",
                        DetailRun = "  " + FormatGraphDuration(gapEnd - gapStart),
                        Brush = GraphBreakBrush
                    });
                }
            }

            // Entries: trunk segments draw straight down; branch segments run straight down in
            // their own lane (exactly between their two nodes) with short diagonal stubs tying
            // them back to the trunk.
            for (int i = 0; i < intervals.Count; i++)
            {
                var iv = intervals[i];
                int lane = lanes[i];
                double x = GraphTrunkX + lane * GraphLaneSpacing;
                double y1 = Y(iv.Start), y2 = Y(iv.End);
                var brush = ColorFor(iv.Source.Projektname);

                if (lane == 0)
                {
                    GraphEdges.Add(new GraphEdge { Geometry = LineData(GraphTrunkX, y1, GraphTrunkX, y2), Stroke = brush, StrokeThickness = 3, Dashes = null });
                }
                else
                {
                    // The branch's own column runs exactly from y1 to y2 - precisely where its
                    // two nodes sit - so the dots always land exactly on the line. Short diagonal
                    // stubs (borrowing space from before/after the entry, never from inside it)
                    // visually tie that column back to the trunk without moving the nodes off it.
                    GraphEdges.Add(new GraphEdge { Geometry = LineData(GraphTrunkX, y1 - GraphCurveReach, x, y1), Stroke = brush, StrokeThickness = 3, Dashes = null });
                    GraphEdges.Add(new GraphEdge { Geometry = LineData(x, y1, x, y2), Stroke = brush, StrokeThickness = 3, Dashes = null });
                    GraphEdges.Add(new GraphEdge { Geometry = LineData(x, y2, GraphTrunkX, y2 + GraphCurveReach), Stroke = brush, StrokeThickness = 3, Dashes = null });
                }

                string tooltip = $"{iv.Source.Projektname}\n{iv.Source.Startzeit} – {iv.Source.Endzeit}";
                GraphNodes.Add(new GraphNode { Left = x - GraphNodeRadius, Top = y1 - GraphNodeRadius, Diameter = GraphNodeRadius * 2, Fill = brush, ToolTip = tooltip, Entry = iv.Source });
                GraphNodes.Add(new GraphNode { Left = x - GraphNodeRadius, Top = y2 - GraphNodeRadius, Diameter = GraphNodeRadius * 2, Fill = brush, ToolTip = tooltip, Entry = iv.Source });

                string detail = $"  {iv.Source.Startzeit}–{iv.Source.Endzeit} · {FormatGraphDuration(iv.End - iv.Start)}";
                if (!string.IsNullOrWhiteSpace(iv.Source.Beschreibung))
                    detail += $" · {iv.Source.Beschreibung}";

                labelBuilder.Add(new GraphLabel
                {
                    Left = labelX,
                    Top = (y1 + y2) / 2 - 9,
                    ProjectRun = iv.Source.Projektname,
                    DetailRun = detail,
                    Brush = brush
                });
            }

            // Collision pass: sort by the vertical position each label would ideally sit at
            // (which also puts them in chronological order), then push any label down that
            // would otherwise overlap the one above it, so dense clusters stack legibly
            // instead of overlapping into unreadable text.
            labelBuilder.Sort((a, b) => a.Top.CompareTo(b.Top));
            double nextFreeTop = double.NegativeInfinity;
            foreach (var label in labelBuilder)
            {
                if (label.Top < nextFreeTop)
                    label.Top = nextFreeTop;
                nextFreeTop = label.Top + GraphLabelLineHeight;
                GraphLabels.Add(label);
            }

            // Endpoint markers reinforce the "thread" framing: first clock-in and last clock-out.
            var firstEntry = intervals[0];
            var lastEntry = intervals.OrderByDescending(iv => iv.End).First();
            GraphNodes.Add(new GraphNode
            {
                Left = GraphTrunkX - GraphEndpointRadius,
                Top = 0 - GraphEndpointRadius,
                Diameter = GraphEndpointRadius * 2,
                Fill = GraphEndpointBrush,
                ToolTip = $"Beginn {firstEntry.Source.Startzeit}"
            });
            GraphNodes.Add(new GraphNode
            {
                Left = GraphTrunkX - GraphEndpointRadius,
                Top = Y(dayEnd) - GraphEndpointRadius,
                Diameter = GraphEndpointRadius * 2,
                Fill = GraphEndpointBrush,
                ToolTip = $"Ende {lastEntry.Source.Endzeit}"
            });

            double labelsBottom = nextFreeTop == double.NegativeInfinity ? 0 : nextFreeTop;
            double bottomPadding = Math.Max(GraphEndpointRadius, GraphCurveReach) + 4;
            GraphWidth = labelX + GraphLabelColumnWidth;
            GraphHeight = Math.Max(Math.Max(GraphMinHeight, Y(dayEnd)) + bottomPadding, labelsBottom);
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

    /// <summary>
    /// Eine Buchung in der "Tatsächliche Arbeitszeit"-Ansicht, nachdem Überschneidungen aufgelöst wurden:
    /// Start/Ende sind ggf. nach hinten verschoben (<see cref="IsShifted"/>), die Dauer bleibt unverändert.
    /// Jede Buchung bleibt eine eigene Zeile — es werden keine Buchungen unterschiedlicher Projekte
    /// zusammengefasst.
    /// </summary>
    internal sealed class SequencedEntryVM : BindableBase
    {
        private string _projektname, _start, _end, _beschreibung, _originalStart;
        private double _dauer;
        private bool _isShifted;

        public string Projektname { get => _projektname; set => SetProperty(ref _projektname, value); }
        public string Startzeit { get => _start; set => SetProperty(ref _start, value); }
        public string Endzeit { get => _end; set => SetProperty(ref _end, value); }
        public double Dauer { get => _dauer; set => SetProperty(ref _dauer, value); }
        public string Beschreibung { get => _beschreibung; set => SetProperty(ref _beschreibung, value); }

        /// <summary>True, wenn diese Buchung wegen einer Überschneidung nach hinten verschoben wurde.</summary>
        public bool IsShifted { get => _isShifted; set => SetProperty(ref _isShifted, value); }

        /// <summary>Ursprünglich gebuchte Startzeit, bevor wegen einer Überschneidung verschoben wurde.</summary>
        public string OriginalStartzeit { get => _originalStart; set => SetProperty(ref _originalStart, value); }
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

    /// <summary>Eine Kante (Trunk-, Branch- oder Pausen-Linie) der Git-Graph-Timeline.</summary>
    internal sealed class GraphEdge
    {
        public string Geometry { get; set; }
        public Brush Stroke { get; set; }
        public double StrokeThickness { get; set; }
        public DoubleCollection Dashes { get; set; }
    }

    /// <summary>Ein Knoten (Buchungsstart/-ende oder Tagesendpunkt) der Git-Graph-Timeline.</summary>
    internal sealed class GraphNode
    {
        public double Left { get; set; }
        public double Top { get; set; }
        public double Diameter { get; set; }
        public Brush Fill { get; set; }
        public string ToolTip { get; set; }

        /// <summary>Die zugehörige Buchung; null bei den Beginn-/Ende-Markern (nicht klickbar).</summary>
        public EntryItemVM Entry { get; set; }
    }

    /// <summary>Ein Text-Label (Projekt + Zeitspanne + Dauer, oder Pausenlänge) neben einem Graph-Segment.</summary>
    internal sealed class GraphLabel
    {
        public double Left { get; set; }
        public double Top { get; set; }
        public string ProjectRun { get; set; }
        public string DetailRun { get; set; }
        public Brush Brush { get; set; }
    }
}