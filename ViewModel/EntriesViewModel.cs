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

        /// <summary>Summe aller Pausen (Zeiten ohne jede laufende Buchung zwischen erster und letzter Buchung) am ausgewählten Tag.</summary>
        public double TotalBreakHours
        {
            get => _totalBreakHours;
            private set => SetProperty(ref _totalBreakHours, value);
        }

        // --- Warnungen / Überschneidungen ---
        private int _overlapCount;

        private bool _hasOverlaps;

        /// <summary>Anzahl der Buchungen am ausgewählten Tag, die beginnen, während noch eine frühere Buchung läuft.</summary>
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

        public bool IsEditPanelOpen
        {
            get => _isEditPanelOpen;
            set
            {
                SetProperty(ref _isEditPanelOpen, value);
                UpdateGraphSelection();
            }
        }
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

            // --- Pausen und Überschneidungen berechnen ---
            // Entries sind bereits nach Startzeit sortiert (SQL ORDER BY startzeit). Verglichen wird
            // mit dem spätesten Ende aller bisherigen Buchungen, nicht nur der direkt vorherigen:
            // die kann schon vor einer länger laufenden, parallelen Buchung enden. So zählt als
            // Pause nur, wo gar nichts gebucht ist - genau wie im Git-Graph.
            double totalBreak = 0;
            int overlaps = 0;
            TimeSpan? coveredUntil = null;
            foreach (var e in Entries)
            {
                if (!TryParseTime(e.Startzeit, out var curStart) || !TryParseTime(e.Endzeit, out var curEnd))
                    continue;

                if (coveredUntil.HasValue)
                {
                    if (curStart > coveredUntil.Value)
                        totalBreak += (curStart - coveredUntil.Value).TotalHours;
                    else if (curStart < coveredUntil.Value)
                        overlaps++; // beginnt, während noch eine frühere Buchung läuft
                }

                if (!coveredUntil.HasValue || curEnd > coveredUntil.Value)
                    coveredUntil = curEnd;
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

        // ── Git-Graph geometry construction ──
        //
        // The layout is row-based like a git log graph: every booking gets its own row, and its
        // start node and label sit side by side at exactly that row's height. Rows follow each
        // other in time order, spaced by a compressed (square-root) function of the elapsed
        // minutes: long bookings and pauses still read as longer, but no longer push the rest of
        // the day off-screen, and every row is guaranteed enough room for its label, so a label
        // never has to be shifted away from the node it belongs to.
        //
        // The trunk (lane 0) runs from the first clock-in to the last clock-out. A booking that
        // overlaps an already running one moves to a side lane (a parallel "branch"): it forks
        // off the trunk into its own column and merges back when it ends. Stretches where nothing
        // is booked at all are drawn as a dashed trunk segment ("Pause"); stretches where only a
        // branch is running as a thin neutral connector, so the thread stays visually continuous
        // without being mislabelled as a break.
        private const double GraphRowHeight = 24;
        private const double GraphLabelHeight = GraphRowHeight - 2;
        private const double GraphNodeGap = 20;
        private const double GraphPixelsPerSqrtMinute = 8;
        private const double GraphLaneSpacing = 22;
        private const double GraphTrunkX = 12;
        private const double GraphNodeDiameter = 12;
        private const double GraphCapDiameter = 14;
        private const double GraphNodeHitSize = 18;
        private const double GraphCapGap = 22;
        private const double GraphCurveReach = 24;
        private const double GraphLabelGap = 8;
        private const double GraphPauseLabelMinMinutes = 2;

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

        private static readonly SolidColorBrush[] GraphBrushes = GraphPalette.Select(c => CreateFrozenBrush(c)).ToArray();
        private static readonly SolidColorBrush GraphBreakBrush = CreateFrozenBrush(Color.FromRgb(0x6B, 0x73, 0x90));
        private static readonly SolidColorBrush GraphEndpointBrush = CreateFrozenBrush(Color.FromRgb(0xF5, 0xF7, 0xFB));

        // Same colour as the card behind the graph (ColCard): the ring it draws around each node
        // cuts a small gap into the line, which makes nodes on a busy line easier to tell apart.
        private static readonly SolidColorBrush GraphBackgroundBrush = CreateFrozenBrush(Color.FromRgb(0x1A, 0x22, 0x38));
        private static readonly DoubleCollection GraphBreakDashes = CreateFrozenDashes(3, 3);

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

        /// <summary>One path for a branch: curve off the trunk, straight down its lane, curve back in.</summary>
        private static string BranchData(double laneX, double forkY, double y1, double y2, double mergeY)
        {
            double forkMid = (forkY + y1) / 2, mergeMid = (y2 + mergeY) / 2;
            return $"M{N(GraphTrunkX)},{N(forkY)} C{N(GraphTrunkX)},{N(forkMid)} {N(laneX)},{N(forkMid)} {N(laneX)},{N(y1)}"
                 + $" L{N(laneX)},{N(y2)} C{N(laneX)},{N(mergeMid)} {N(GraphTrunkX)},{N(mergeMid)} {N(GraphTrunkX)},{N(mergeY)}";
        }

        private static string FormatClock(TimeSpan t) => t.ToString(@"hh\:mm", CultureInfo.InvariantCulture);

        private static string FormatTimeRange(TimeSpan from, TimeSpan to) => FormatClock(from) + "–" + FormatClock(to);

        private static string FormatGraphDuration(TimeSpan span)
        {
            if (span.TotalHours >= 1)
                return $"{(int)span.TotalHours}h {span.Minutes:00}m";
            return $"{Math.Max(1, (int)span.TotalMinutes)}m";
        }

        /// <summary>Deterministic across runs (unlike string.GetHashCode), so colours stay stable.</summary>
        private static int StableHash(string s)
        {
            unchecked
            {
                int h = 17;
                foreach (char c in s ?? string.Empty)
                    h = h * 31 + c;
                return h & 0x7FFFFFFF;
            }
        }

        private static GraphNode CreateEntryNode(double x, double y, Brush fill, string toolTip, EntryItemVM entry) =>
            new GraphNode
            {
                Left = x - GraphNodeHitSize / 2,
                Top = y - GraphNodeHitSize / 2,
                HitSize = GraphNodeHitSize,
                Diameter = GraphNodeDiameter,
                Fill = fill,
                Stroke = GraphBackgroundBrush,
                ToolTip = toolTip,
                Entry = entry
            };

        private static GraphNode CreateCapNode(double y, string toolTip) =>
            new GraphNode
            {
                Left = GraphTrunkX - GraphNodeHitSize / 2,
                Top = y - GraphNodeHitSize / 2,
                HitSize = GraphNodeHitSize,
                Diameter = GraphCapDiameter,
                Fill = GraphBackgroundBrush,
                Stroke = GraphEndpointBrush,
                ToolTip = toolTip
            };

        private void BuildGraph()
        {
            GraphEdges.Clear();
            GraphNodes.Clear();
            GraphLabels.Clear();

            var parsed = new List<(TimeSpan Start, TimeSpan End, EntryItemVM Source)>();
            foreach (var e in Entries)
            {
                if (TryParseTime(e.Startzeit, out var s) && TryParseTime(e.Endzeit, out var en) && en > s)
                    parsed.Add((s, en, e));
            }

            // Id as last tie-breaker: bookings with identical times (parallel Quick Select slots)
            // keep a stable order instead of swapping rows/lanes on every refresh.
            var intervals = parsed.OrderBy(iv => iv.Start).ThenBy(iv => iv.End).ThenBy(iv => iv.Source.Id).ToList();

            HasGraphEntries = intervals.Count > 0;
            if (intervals.Count == 0)
            {
                GraphWidth = 0;
                GraphHeight = 0;
                return;
            }

            // Lane assignment via interval-graph coloring (sweep by start time). A side lane is
            // only handed out while every lower lane - in particular the trunk - is busy, so a
            // branch always has a running trunk booking to fork off from.
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

            // Row layout. Every distinct start/end time gets a row; bookings starting at the same
            // time each get a row of their own below it (they all carry a label), stacked in lane
            // order. End nodes sit on the first row of their time.
            var timePoints = intervals.SelectMany(iv => new[] { iv.Start, iv.End }).Distinct().OrderBy(t => t).ToList();
            var timeY = new Dictionary<TimeSpan, double>();
            var startY = new double[intervals.Count];
            var rowYs = new List<double>();
            var pauses = new HashSet<(TimeSpan From, TimeSpan To)>();

            double capTopY = GraphCapDiameter / 2 + 1;
            double y = capTopY + GraphCapGap;
            for (int ti = 0; ti < timePoints.Count; ti++)
            {
                var t = timePoints[ti];
                if (ti > 0)
                {
                    var prev = timePoints[ti - 1];
                    double minutes = (t - prev).TotalMinutes;
                    double minGap;
                    if (!intervals.Any(iv => iv.Start < t && iv.End > prev))
                    {
                        // Nothing booked: a pause. A labelled pause needs room for its own label
                        // row between the rows above and below it.
                        pauses.Add((prev, t));
                        minGap = minutes >= GraphPauseLabelMinMinutes ? 2 * GraphRowHeight : GraphNodeGap;
                    }
                    else
                    {
                        minGap = intervals.Any(iv => iv.Start == prev) ? GraphRowHeight : GraphNodeGap;
                    }
                    y += Math.Max(minGap, GraphPixelsPerSqrtMinute * Math.Sqrt(minutes));
                }

                timeY[t] = y;
                rowYs.Add(y);
                bool isFirstStart = true;
                for (int i = 0; i < intervals.Count; i++)
                {
                    if (intervals[i].Start != t) continue;
                    if (!isFirstStart)
                    {
                        y += GraphRowHeight;
                        rowYs.Add(y);
                    }
                    startY[i] = y;
                    isFirstStart = false;
                }
            }

            double RowAbove(double yy)
            {
                double found = double.NaN;
                foreach (var ry in rowYs)
                {
                    if (ry >= yy) break;
                    found = ry;
                }
                return found;
            }

            double RowBelow(double yy)
            {
                foreach (var ry in rowYs)
                    if (ry > yy) return ry;
                return double.NaN;
            }

            var projectBrushes = new Dictionary<string, SolidColorBrush>();
            var usedColors = new HashSet<int>();
            SolidColorBrush BrushFor(string project)
            {
                if (projectBrushes.TryGetValue(project, out var b))
                    return b;

                // The colour is derived from the project name so a project keeps the same colour
                // from day to day; only if another project of the same day already took it, the
                // next free one is used, so two projects shown together never share a colour.
                int idx = StableHash(project) % GraphBrushes.Length;
                for (int n = 0; n < GraphBrushes.Length && usedColors.Contains(idx); n++)
                    idx = (idx + 1) % GraphBrushes.Length;
                usedColors.Add(idx);
                b = GraphBrushes[idx];
                projectBrushes[project] = b;
                return b;
            }

            // Drawing order: neutral trunk pieces first, then branches, then trunk bookings on
            // top, so fork/merge curves tuck in underneath the coloured trunk line.
            var trunkEdges = new List<GraphEdge>();
            var branchEdges = new List<GraphEdge>();
            var entryEdges = new List<GraphEdge>();
            var labels = new List<GraphLabel>();

            // Lowest point at which a branch ending at a given time rejoins the trunk.
            var mergeYByEnd = new Dictionary<TimeSpan, double>();

            // Gaps between two rows (keyed by the upper row) that a fork curve or a merge curve
            // runs through. If a branch merges into the same gap another one forks out of, both
            // curves get half of it, so they meet on the trunk instead of crossing each other.
            var forkGaps = new HashSet<double>();
            var mergeGaps = new HashSet<double>();
            for (int i = 0; i < intervals.Count; i++)
            {
                if (lanes[i] == 0) continue;
                forkGaps.Add(RowAbove(startY[i]));
                mergeGaps.Add(timeY[intervals[i].End]);
            }

            // Nodes where a booking on some lane starts; a booking that ends exactly where the
            // next one on the same lane starts shares that node rather than stacking a second,
            // hidden button underneath it.
            var startSpots = new HashSet<(int Lane, double Y)>();
            for (int i = 0; i < intervals.Count; i++)
                startSpots.Add((lanes[i], startY[i]));

            double lowestY = timeY[timePoints[timePoints.Count - 1]];
            for (int i = 0; i < intervals.Count; i++)
            {
                var iv = intervals[i];
                int lane = lanes[i];
                double x = GraphTrunkX + lane * GraphLaneSpacing;
                double y1 = startY[i], y2 = timeY[iv.End];
                var brush = BrushFor(iv.Source.Projektname);

                if (lane == 0)
                {
                    entryEdges.Add(new GraphEdge { Geometry = LineData(x, y1, x, y2), Stroke = brush, StrokeThickness = 3 });
                }
                else
                {
                    // Fork off the trunk no further up than the row above and merge back no
                    // further down than the row below, so a curve never sweeps past other rows.
                    double above = RowAbove(y1), below = RowBelow(y2);
                    double forkReach = double.IsNaN(above)
                        ? GraphCurveReach
                        : Math.Min(GraphCurveReach, (y1 - above) / (mergeGaps.Contains(above) ? 2 : 1));
                    double mergeReach = double.IsNaN(below)
                        ? GraphCurveReach
                        : Math.Min(GraphCurveReach, (below - y2) / (forkGaps.Contains(y2) ? 2 : 1));
                    double forkY = y1 - forkReach, mergeY = y2 + mergeReach;
                    branchEdges.Add(new GraphEdge { Geometry = BranchData(x, forkY, y1, y2, mergeY), Stroke = brush, StrokeThickness = 3 });
                    lowestY = Math.Max(lowestY, mergeY);
                    mergeYByEnd[iv.End] = mergeYByEnd.TryGetValue(iv.End, out var m) ? Math.Max(m, mergeY) : mergeY;
                }

                string range = FormatTimeRange(iv.Start, iv.End);
                string duration = FormatGraphDuration(iv.End - iv.Start);
                bool hasDescription = !string.IsNullOrWhiteSpace(iv.Source.Beschreibung);
                string tooltip = $"{iv.Source.Projektname}\n{range} · {duration}"
                                 + (hasDescription ? "\n" + iv.Source.Beschreibung : string.Empty)
                                 + "\nKlicken zum Bearbeiten";

                GraphNodes.Add(CreateEntryNode(x, y1, brush, tooltip, iv.Source));
                if (!startSpots.Contains((lane, y2)))
                    GraphNodes.Add(CreateEntryNode(x, y2, brush, tooltip, iv.Source));

                labels.Add(new GraphLabel
                {
                    Top = y1 - GraphLabelHeight / 2,
                    Height = GraphLabelHeight,
                    TimeText = range,
                    Title = iv.Source.Projektname,
                    Detail = hasDescription ? $"{duration} · {iv.Source.Beschreibung}" : duration,
                    Brush = brush,
                    ToolTip = tooltip,
                    Entry = iv.Source
                });
            }

            // Trunk stretches without a trunk booking: dashed where nothing is booked at all (a
            // real pause), thin and solid where only a branch is running. Checked per pair of
            // neighbouring time points, so a pause after a branch ends is still recognised as one.
            for (int ti = 1; ti < timePoints.Count; ti++)
            {
                var from = timePoints[ti - 1];
                var to = timePoints[ti];
                bool trunkBusy = false;
                for (int i = 0; i < intervals.Count && !trunkBusy; i++)
                    trunkBusy = lanes[i] == 0 && intervals[i].Start < to && intervals[i].End > from;
                if (trunkBusy)
                    continue;

                bool isPause = pauses.Contains((from, to));
                double y1 = timeY[from], y2 = timeY[to];

                // A pause only starts to look like one once every branch that ended with it has
                // curved back into the trunk; up to that point the trunk stays a plain connector.
                double dashFromY = y1;
                if (isPause && mergeYByEnd.TryGetValue(from, out var mergedY))
                    dashFromY = Math.Min(Math.Max(y1, mergedY), y2);

                if (dashFromY > y1 || !isPause)
                    trunkEdges.Add(new GraphEdge { Geometry = LineData(GraphTrunkX, y1, GraphTrunkX, isPause ? dashFromY : y2), Stroke = GraphBreakBrush, StrokeThickness = 1.5 });
                if (isPause)
                    trunkEdges.Add(new GraphEdge { Geometry = LineData(GraphTrunkX, dashFromY, GraphTrunkX, y2), Stroke = GraphBreakBrush, StrokeThickness = 2, Dashes = GraphBreakDashes });

                if (isPause && (to - from).TotalMinutes >= GraphPauseLabelMinMinutes)
                {
                    string duration = FormatGraphDuration(to - from);
                    labels.Add(new GraphLabel
                    {
                        Top = (y1 + y2) / 2 - GraphLabelHeight / 2,
                        Height = GraphLabelHeight,
                        TimeText = FormatTimeRange(from, to),
                        Title = "Pause",
                        Detail = duration,
                        Brush = GraphBreakBrush,
                        ToolTip = $"Pause\n{FormatTimeRange(from, to)} · {duration}"
                    });
                }
            }

            // Day start/end caps sit just outside the first and last row, so they never cover
            // (and swallow clicks on) a booking's own node.
            var dayStart = timePoints[0];
            var dayEnd = timePoints[timePoints.Count - 1];
            double capBottomY = lowestY + GraphCapGap;
            trunkEdges.Add(new GraphEdge { Geometry = LineData(GraphTrunkX, capTopY, GraphTrunkX, timeY[dayStart]), Stroke = GraphBreakBrush, StrokeThickness = 1.5 });
            trunkEdges.Add(new GraphEdge { Geometry = LineData(GraphTrunkX, timeY[dayEnd], GraphTrunkX, capBottomY), Stroke = GraphBreakBrush, StrokeThickness = 1.5 });
            GraphNodes.Add(CreateCapNode(capTopY, $"Beginn {FormatClock(dayStart)}"));
            GraphNodes.Add(CreateCapNode(capBottomY, $"Ende {FormatClock(dayEnd)}"));

            foreach (var edge in trunkEdges.Concat(branchEdges).Concat(entryEdges))
                GraphEdges.Add(edge);
            foreach (var label in labels.OrderBy(l => l.Top))
                GraphLabels.Add(label);

            GraphWidth = GraphTrunkX + maxLane * GraphLaneSpacing + GraphNodeHitSize / 2 + GraphLabelGap;
            GraphHeight = capBottomY + GraphNodeHitSize / 2;
            UpdateGraphSelection();
        }

        /// <summary>Hebt Knoten und Label der Buchung hervor, die gerade im Bearbeiten-Panel offen ist.</summary>
        private void UpdateGraphSelection()
        {
            foreach (var node in GraphNodes)
                node.IsSelected = IsEditPanelOpen && node.Entry != null && node.Entry.Id == _editId;
            foreach (var label in GraphLabels)
                label.IsSelected = IsEditPanelOpen && label.Entry != null && label.Entry.Id == _editId;
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
    internal sealed class GraphNode : BindableBase
    {
        private bool _isSelected;

        /// <summary>Linke obere Ecke der (größeren, unsichtbaren) Klickfläche um den Knoten.</summary>
        public double Left { get; set; }
        public double Top { get; set; }
        public double HitSize { get; set; }
        public double Diameter { get; set; }
        public Brush Fill { get; set; }
        public Brush Stroke { get; set; }
        public string ToolTip { get; set; }

        /// <summary>Die zugehörige Buchung; null bei den Beginn-/Ende-Markern (nicht klickbar).</summary>
        public EntryItemVM Entry { get; set; }

        /// <summary>True, solange die zugehörige Buchung im Bearbeiten-Panel offen ist.</summary>
        public bool IsSelected { get => _isSelected; set => SetProperty(ref _isSelected, value); }
    }

    /// <summary>Eine Label-Zeile (Zeitspanne, Projekt, Dauer + Beschreibung bzw. Pausenlänge) neben dem Graph.</summary>
    internal sealed class GraphLabel : BindableBase
    {
        private bool _isSelected;

        public double Top { get; set; }
        public double Height { get; set; }
        public string TimeText { get; set; }
        public string Title { get; set; }
        public string Detail { get; set; }
        public Brush Brush { get; set; }
        public string ToolTip { get; set; }

        /// <summary>Die zugehörige Buchung; null bei Pausen (nicht klickbar).</summary>
        public EntryItemVM Entry { get; set; }

        /// <summary>True, solange die zugehörige Buchung im Bearbeiten-Panel offen ist.</summary>
        public bool IsSelected { get => _isSelected; set => SetProperty(ref _isSelected, value); }
    }
}