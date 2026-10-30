using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Windows.Media;
using Zeitmanagement.Helpers;
using Zeitmanagement.MVVM;

namespace Zeitmanagement.ViewModel
{
    /// <summary>
    /// Zeigt pro Monat, welche Zeiten im SAP tatsächlich gebucht werden müssen: externe Projekte decken
    /// parallele interne Projekte ab, überschüssige externe Zeit wird bis Monatsende gespeichert und mit
    /// späteren freien internen Zeiten verrechnet. Der Rest sind Überstunden. Die Rechenregeln stehen
    /// in <see cref="SapBookingCalculator"/>.
    /// </summary>
    internal sealed class SapBookingViewModel : BaseViewModel
    {
        private const double TimelineWidth = 1000.0;
        private const double LaneHeight = 18.0;
        private const double SurplusTrackHeight = 8.0;

        // Farben aus der Kategoriepalette des aktuellen Themes; die Legende der View nutzt
        // dieselben Schlüssel (Theme.Data0..4, Theme.DataNeutral).
        private static Brush ExternalBrush => ThemeManager.GetBrush("Theme.Data0");
        private static Brush InternalBookedBrush => ThemeManager.GetBrush("Theme.Data1");
        private static Brush CoveredExternalBrush => ThemeManager.GetBrush("Theme.Data2");
        private static Brush CoveredSurplusBrush => ThemeManager.GetBrush("Theme.Data3");
        private static Brush SurplusBrush => ThemeManager.GetBrush("Theme.Data4");
        private static Brush DiscardedBrush => ThemeManager.GetBrush("Theme.DataNeutral");

        // Neutrale Markierung (Gesamtwerte) in der Textfarbe des aktuellen Themes.
        private static Brush TextBrush => ThemeManager.GetBrush("Theme.Text");

        private DateTime _month = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);

        public DateTime ReferenceDate
        {
            get => _month;
            set
            {
                var m = new DateTime(value.Year, value.Month, 1);
                if (m == _month) return;
                _month = m;
                RaisePropertyChanged();
                Refresh();
            }
        }

        private string _rangeLabel;
        public string RangeLabel { get => _rangeLabel; private set => SetProperty(ref _rangeLabel, value); }

        private string _statusNote;
        /// <summary>Hinweis unter den Kennzahlen (leer, wenn nichts zu sagen ist).</summary>
        public string StatusNote { get => _statusNote; private set => SetProperty(ref _statusNote, value); }

        private bool _hasDays;
        public bool HasDays { get => _hasDays; private set => SetProperty(ref _hasDays, value); }

        private bool _noExternalProjects;
        /// <summary>True, wenn im Monat gebucht wurde, aber kein Projekt als extern markiert ist.</summary>
        public bool NoExternalProjects { get => _noExternalProjects; private set => SetProperty(ref _noExternalProjects, value); }

        public ObservableCollection<SapDayVM> Days { get; } = new ObservableCollection<SapDayVM>();
        public ObservableCollection<SapKpiVM> Kpis { get; } = new ObservableCollection<SapKpiVM>();
        public ObservableCollection<SapProjectRowVM> MonthProjects { get; } = new ObservableCollection<SapProjectRowVM>();

        public DelegateCommand PreviousCommand { get; }
        public DelegateCommand NextCommand { get; }
        public DelegateCommand TodayCommand { get; }

        public SapBookingViewModel()
        {
            PreviousCommand = new DelegateCommand(_ => ReferenceDate = _month.AddMonths(-1));
            NextCommand = new DelegateCommand(_ => ReferenceDate = _month.AddMonths(1));
            TodayCommand = new DelegateCommand(_ => ReferenceDate = DateTime.Today);

            // Balkenfarben kommen aus dem Theme; erst neu zeichnen, wenn die Seite schon gezeigt wurde.
            ThemeManager.ThemeChanged += (s, e) =>
            {
                if (Days.Count > 0)
                    Refresh();
            };
        }

        public override void Refresh()
        {
            var db = MainViewModel.DbInstance;
            var first = _month;
            var last = first.AddMonths(1).AddDays(-1);
            RangeLabel = first.ToString("MMMM yyyy", CultureInfo.CurrentCulture);

            var inputs = new List<SapBookingInput>();
            foreach (var e in db.GetTimeEntriesBetween(first, last))
            {
                if (!TryParseMinutes(e.Startzeit, out var s) || !TryParseMinutes(e.Endzeit, out var en) || en <= s)
                    continue;
                inputs.Add(new SapBookingInput
                {
                    Date = e.Datum,
                    StartMin = s,
                    EndMin = en,
                    Project = e.Projektname,
                    IsExternal = e.IstExtern
                });
            }

            var result = SapBookingCalculator.Calculate(inputs);

            _projectToKtr.Clear();
            foreach (var p in db.GetProjects())
                _projectToKtr[p.Projektname] = p.Kostentraeger;

            HasDays = result.Days.Count > 0;
            NoExternalProjects = HasDays && result.Projects.All(p => !p.IsExternal);

            BuildKpis(result, last);
            BuildDays(result);

            MonthProjects.Clear();
            foreach (var row in ToKtrRows(result.Projects))
                MonthProjects.Add(row);
        }

        private void BuildKpis(SapMonthResult r, DateTime lastDayOfMonth)
        {
            bool monthOpen = DateTime.Today <= lastDayOfMonth;
            Kpis.Clear();
            Kpis.Add(new SapKpiVM("Arbeitszeit", Fmt(r.SpanMin), "erste bis letzte Buchung, je Tag", TextBrush));
            Kpis.Add(new SapKpiVM("Extern", Fmt(r.ExternalMin), "vom Kunden bezahlt, wird voll gebucht", ExternalBrush));
            Kpis.Add(new SapKpiVM("Intern (Buchungen)", Fmt(r.InternalGrossMin), "alle internen Buchungen brutto", InternalBookedBrush));
            Kpis.Add(new SapKpiVM("Gedeckt durch Extern", Fmt(r.CoveredByExternalMin), "intern, parallel zu extern: entfällt", CoveredExternalBrush));
            Kpis.Add(new SapKpiVM("Gedeckt durch Speicher", Fmt(r.CoveredBySurplusMin), "intern, mit Überschuss verrechnet: entfällt", CoveredSurplusBrush));
            if (r.DiscardedMin > 0.5)
                Kpis.Add(new SapKpiVM("Verworfen", Fmt(r.DiscardedMin), "intern, parallel zu internem Projekt mit weniger Stunden: zählt nicht", DiscardedBrush));
            Kpis.Add(new SapKpiVM("Intern im SAP buchen", Fmt(r.InternalBookedMin), "Rest der internen Zeit", InternalBookedBrush));
            Kpis.Add(new SapKpiVM("Im SAP gesamt", Fmt(r.SapTotalMin), "extern + interner Rest", TextBrush));
            Kpis.Add(new SapKpiVM(monthOpen ? "Überschuss offen" : "Überstunden", Fmt(r.LeftoverMin),
                monthOpen ? "noch verrechenbar bis Monatsende" : "nicht mehr verrechenbar", SurplusBrush));

            if (!HasDays)
                StatusNote = "In diesem Monat gibt es keine Buchungen.";
            else if (r.LeftoverMin < 0.5)
                StatusNote = "Der gesamte Überschuss konnte mit internen Zeiten verrechnet werden.";
            else if (monthOpen)
                StatusNote = $"{Fmt(r.LeftoverMin)} Überschuss sind noch gespeichert. Er wird mit weiteren internen Zeiten verrechnet; " +
                             "was am Monatsende übrig bleibt, sind Überstunden.";
            else
                StatusNote = $"{Fmt(r.LeftoverMin)} Überschuss ließen sich bis zum Monatsende nicht mehr mit internen Zeiten verrechnen " +
                             "und sind tatsächliche Überstunden.";
        }

        private void BuildDays(SapMonthResult r)
        {
            Days.Clear();
            double maxPool = Math.Max(1, r.Days.Count == 0 ? 1 : r.Days.Max(d => Math.Max(d.PoolStartMin, d.PoolEndMin)));

            foreach (var d in r.Days)
            {
                double origin = d.FirstStartMin;
                double span = Math.Max(1, d.SpanMin);
                double X(double min) => (min - origin) / span * TimelineWidth;

                var vm = new SapDayVM
                {
                    DateLabel = d.Date.ToString("ddd dd.MM", CultureInfo.CurrentCulture),
                    SpanText = Fmt(d.SpanMin),
                    RangeText = $"{FmtClock(d.FirstStartMin)} – {FmtClock(d.LastEndMin)}",
                    StartLabel = FmtClock(d.FirstStartMin),
                    EndLabel = FmtClock(d.LastEndMin),
                    TimelineHeight = d.LaneCount * LaneHeight + (d.SurplusSpans.Count > 0 ? SurplusTrackHeight + 2 : 0),
                    SapTotalText = Fmt(d.SapTotalMin),
                    PoolText = Fmt(d.PoolEndMin),
                    PoolBarWidth = d.PoolEndMin / maxPool * 100.0,
                    PoolTip = $"Überschuss-Speicher am Tagesende: {Fmt(d.PoolEndMin)} (Tagesbeginn: {Fmt(d.PoolStartMin)})"
                };

                foreach (var seg in d.Segments)
                {
                    vm.Rects.Add(new SapRectVM
                    {
                        Left = X(seg.StartMin),
                        Top = seg.Lane * LaneHeight,
                        Width = Math.Max(2, X(seg.EndMin) - X(seg.StartMin)),
                        Height = LaneHeight - 2,
                        Brush = BrushOf(seg.Kind),
                        ToolTip = $"{seg.Project}\n{KindText(seg.Kind)}\n{Fmt(seg.EndMin - seg.StartMin)}"
                    });
                }
                foreach (var sp in d.SurplusSpans)
                {
                    vm.Rects.Add(new SapRectVM
                    {
                        Left = X(sp.StartMin),
                        Top = d.LaneCount * LaneHeight + 2,
                        Width = Math.Max(2, X(sp.EndMin) - X(sp.StartMin)),
                        Height = SurplusTrackHeight,
                        Brush = SurplusBrush,
                        ToolTip = $"Überschuss entsteht: {Fmt(sp.Minutes)}\n(mehrere externe Projekte laufen parallel)"
                    });
                }

                foreach (var row in ToKtrRows(d.Projects))
                    vm.Projects.Add(row);

                // Erklärung in Klartext, damit jede Zahl nachvollziehbar ist
                if (d.CoveredByExternalMin > 0.5)
                    vm.Lines.Add(new SapLineVM($"−{Fmt(d.CoveredByExternalMin)} intern parallel zu externer Arbeit: entfällt im SAP", CoveredExternalBrush));
                if (d.SurplusGeneratedMin > 0.5)
                    vm.Lines.Add(new SapLineVM($"+{Fmt(d.SurplusGeneratedMin)} Überschuss, weil mehrere externe Projekte parallel liefen: kommt in den Speicher", SurplusBrush));
                if (d.CoveredBySurplusMin > 0.5)
                    vm.Lines.Add(new SapLineVM($"−{Fmt(d.CoveredBySurplusMin)} freie interne Zeit mit gespeichertem Überschuss verrechnet: entfällt im SAP", CoveredSurplusBrush));
                if (d.DiscardedMin > 0.5)
                    vm.Lines.Add(new SapLineVM($"−{Fmt(d.DiscardedMin)} parallele interne Zeit des Projekts mit mehr Tagesstunden: verworfen, zählt nicht", DiscardedBrush));
                if (d.InternalBookedMin > 0.5)
                    vm.Lines.Add(new SapLineVM($"{Fmt(d.InternalBookedMin)} interne Zeit muss im SAP gebucht werden", InternalBookedBrush));
                if (vm.Lines.Count == 0)
                    vm.Lines.Add(new SapLineVM("Keine Überschneidungen zwischen internen und externen Projekten.", TextBrush));

                Days.Add(vm);
            }
        }

        private readonly Dictionary<string, string> _projectToKtr = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Fasst Projekte mit gleichem Kostenträger (und gleichem Typ) zu einer Zeile zusammen.</summary>
        private IEnumerable<SapProjectRowVM> ToKtrRows(IEnumerable<SapProjectResult> projects)
        {
            return projects
                .GroupBy(p => new
                {
                    Ktr = _projectToKtr.TryGetValue(p.Project, out var k) && !string.IsNullOrWhiteSpace(k) ? k : "—",
                    p.IsExternal
                })
                .Select(g => ToRow(new SapProjectResult
                {
                    Project = g.Key.Ktr,
                    IsExternal = g.Key.IsExternal,
                    GrossMin = g.Sum(p => p.GrossMin),
                    BookedMin = g.Sum(p => p.BookedMin),
                    CoveredByExternalMin = g.Sum(p => p.CoveredByExternalMin),
                    CoveredBySurplusMin = g.Sum(p => p.CoveredBySurplusMin),
                    DiscardedMin = g.Sum(p => p.DiscardedMin)
                }))
                .OrderBy(r => r.Name, StringComparer.OrdinalIgnoreCase);
        }

        private static SapProjectRowVM ToRow(SapProjectResult p)
        {
            var notes = new List<string>();
            if (p.CoveredByExternalMin > 0.5) notes.Add($"−{Fmt(p.CoveredByExternalMin)} durch Extern");
            if (p.CoveredBySurplusMin > 0.5) notes.Add($"−{Fmt(p.CoveredBySurplusMin)} durch Speicher");
            if (p.DiscardedMin > 0.5) notes.Add($"−{Fmt(p.DiscardedMin)} verworfen");
            return new SapProjectRowVM
            {
                Name = p.Project,
                TypeText = p.IsExternal ? "extern" : "intern",
                TypeBrush = p.IsExternal ? ExternalBrush : InternalBookedBrush,
                GrossText = Fmt(p.GrossMin),
                BookedText = Fmt(p.BookedMin),
                NoteText = string.Join(", ", notes)
            };
        }

        private static Brush BrushOf(SapSegmentKind k)
        {
            switch (k)
            {
                case SapSegmentKind.External: return ExternalBrush;
                case SapSegmentKind.InternalCoveredByExternal: return CoveredExternalBrush;
                case SapSegmentKind.InternalCoveredBySurplus: return CoveredSurplusBrush;
                case SapSegmentKind.InternalDiscarded: return DiscardedBrush;
                default: return InternalBookedBrush;
            }
        }

        private static string KindText(SapSegmentKind k)
        {
            switch (k)
            {
                case SapSegmentKind.External: return "extern: wird im SAP gebucht";
                case SapSegmentKind.InternalCoveredByExternal: return "intern, durch parallele externe Arbeit gedeckt: entfällt";
                case SapSegmentKind.InternalCoveredBySurplus: return "intern, mit gespeichertem Überschuss verrechnet: entfällt";
                case SapSegmentKind.InternalDiscarded: return "intern, parallel zu internem Projekt mit weniger Stunden: verworfen";
                default: return "intern: muss im SAP gebucht werden";
            }
        }

        private static bool TryParseMinutes(string hhmm, out int minutes)
        {
            minutes = 0;
            if (!TimeSpan.TryParseExact(hhmm, new[] { @"hh\:mm", @"h\:mm" }, CultureInfo.InvariantCulture, out var ts))
                return false;
            minutes = (int)ts.TotalMinutes;
            return true;
        }

        /// <summary>Minuten als "h:mm".</summary>
        private static string Fmt(double minutes)
        {
            int total = (int)Math.Round(minutes);
            return $"{total / 60}:{total % 60:D2} h";
        }

        private static string FmtClock(double minutes)
        {
            int total = (int)Math.Round(minutes);
            return $"{total / 60:D2}:{total % 60:D2}";
        }
    }

    internal sealed class SapKpiVM
    {
        public SapKpiVM(string title, string value, string hint, Brush accent)
        {
            Title = title; Value = value; Hint = hint; Accent = accent;
        }
        public string Title { get; }
        public string Value { get; }
        public string Hint { get; }
        public Brush Accent { get; }
    }

    internal sealed class SapRectVM
    {
        public double Left { get; set; }
        public double Top { get; set; }
        public double Width { get; set; }
        public double Height { get; set; }
        public Brush Brush { get; set; }
        public string ToolTip { get; set; }
    }

    internal sealed class SapLineVM
    {
        public SapLineVM(string text, Brush brush) { Text = text; Brush = brush; }
        public string Text { get; }
        public Brush Brush { get; }
    }

    internal sealed class SapProjectRowVM
    {
        public string Name { get; set; }
        public string TypeText { get; set; }
        public Brush TypeBrush { get; set; }
        public string GrossText { get; set; }
        public string BookedText { get; set; }
        public string NoteText { get; set; }
    }

    internal sealed class SapDayVM
    {
        public string DateLabel { get; set; }
        public string SpanText { get; set; }
        public string RangeText { get; set; }
        public string StartLabel { get; set; }
        public string EndLabel { get; set; }
        public double TimelineHeight { get; set; }
        public string SapTotalText { get; set; }
        public string PoolText { get; set; }
        public double PoolBarWidth { get; set; }
        public string PoolTip { get; set; }
        public ObservableCollection<SapRectVM> Rects { get; } = new ObservableCollection<SapRectVM>();
        public ObservableCollection<SapLineVM> Lines { get; } = new ObservableCollection<SapLineVM>();
        public ObservableCollection<SapProjectRowVM> Projects { get; } = new ObservableCollection<SapProjectRowVM>();
    }
}
