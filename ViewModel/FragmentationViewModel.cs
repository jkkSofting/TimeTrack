using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using Zeitmanagement.MVVM;

namespace Zeitmanagement.ViewModel
{
    internal sealed class FragmentationViewModel : BaseViewModel
    {
        // ── Palette for projects (deterministic by name) ──
        private static readonly Color[] Palette =
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

        // ── View modes ──
        public ObservableCollection<string> ViewModes { get; } =
            new ObservableCollection<string> { "Tag", "Woche", "Monat" };

        private string _selectedMode = "Woche";
        public string SelectedMode
        {
            get => _selectedMode;
            set { SetProperty(ref _selectedMode, value); Refresh(); }
        }

        private DateTime _referenceDate = DateTime.Today;
        public DateTime ReferenceDate
        {
            get => _referenceDate;
            set { SetProperty(ref _referenceDate, value); Refresh(); }
        }

        private string _rangeLabel;
        public string RangeLabel
        {
            get => _rangeLabel;
            set => SetProperty(ref _rangeLabel, value);
        }

        public ObservableCollection<DayRow> Days { get; } = new ObservableCollection<DayRow>();
        public ObservableCollection<LegendItem> Legend { get; } = new ObservableCollection<LegendItem>();

        public DelegateCommand PreviousCommand { get; }
        public DelegateCommand NextCommand { get; }

        public FragmentationViewModel()
        {
            PreviousCommand = new DelegateCommand(_ => Navigate(-1));
            NextCommand = new DelegateCommand(_ => Navigate(1));
        }

        private void Navigate(int direction)
        {
            switch (_selectedMode)
            {
                case "Tag":   ReferenceDate = _referenceDate.AddDays(direction); break;
                case "Woche": ReferenceDate = _referenceDate.AddDays(7 * direction); break;
                case "Monat": ReferenceDate = _referenceDate.AddMonths(direction); break;
            }
        }

        public override void Refresh()
        {
            var db = MainViewModel.DbInstance;
            DateTime start, end;

            switch (_selectedMode)
            {
                case "Tag":
                    start = _referenceDate.Date;
                    end = start;
                    RangeLabel = start.ToString("dd.MM.yyyy");
                    break;
                case "Monat":
                    start = new DateTime(_referenceDate.Year, _referenceDate.Month, 1);
                    end = start.AddMonths(1).AddDays(-1);
                    RangeLabel = start.ToString("MMMM yyyy");
                    break;
                default: // Woche
                    int diff = ((int)_referenceDate.DayOfWeek + 6) % 7; // Monday=0
                    start = _referenceDate.Date.AddDays(-diff);
                    end = start.AddDays(6);
                    RangeLabel = $"{start:dd.MM} – {end:dd.MM.yyyy}";
                    break;
            }

            Days.Clear();
            Legend.Clear();

            var projectColors = new Dictionary<string, SolidColorBrush>();
            int colorIndex = 0;

            for (var d = start; d <= end; d = d.AddDays(1))
            {
                var entries = db.GetTimeEntriesForDate(d).ToList();
                var rawSegments = new List<(string Project, double Mins)>();
                double totalMinutes = 0;

                foreach (var e in entries)
                {
                    var timeFormats = new[] { @"hh\:mm", @"h\:mm" };
                    if (!TimeSpan.TryParseExact(e.Startzeit, timeFormats, CultureInfo.InvariantCulture, out var s)) continue;
                    if (!TimeSpan.TryParseExact(e.Endzeit, timeFormats, CultureInfo.InvariantCulture, out var en)) continue;
                    var mins = (en - s).TotalMinutes;
                    if (mins <= 0) continue;

                    if (!projectColors.ContainsKey(e.Projektname))
                    {
                        var c = Palette[colorIndex % Palette.Length];
                        projectColors[e.Projektname] = new SolidColorBrush(c);
                        projectColors[e.Projektname].Freeze();
                        colorIndex++;
                    }

                    rawSegments.Add((e.Projektname, mins));
                    totalMinutes += mins;
                }

                // Build segments with Canvas offsets relative to total worked time (1000 units = full bar)
                var segments = new ObservableCollection<Segment>();
                double cursor = 0;
                foreach (var (project, mins) in rawSegments)
                {
                    double width = totalMinutes > 0 ? mins / totalMinutes * 1000.0 : 0;
                    segments.Add(new Segment
                    {
                        ProjectName = project,
                        StartMinute = cursor,
                        Minutes = width,
                        Brush = projectColors[project]
                    });
                    cursor += width;
                }

                int switchCount = 0;
                for (int i = 1; i < segments.Count; i++)
                    if (segments[i].ProjectName != segments[i - 1].ProjectName)
                        switchCount++;

                Days.Add(new DayRow
                {
                    DateLabel = d.ToString("ddd dd.MM"),
                    Segments = segments,
                    SwitchCount = switchCount,
                    TotalHours = totalMinutes / 60.0,
                    HasEntries = segments.Count > 0
                });
            }

            foreach (var kv in projectColors)
                Legend.Add(new LegendItem { ProjectName = kv.Key, Brush = kv.Value });
        }

        // ── Inner types ──

        internal class Segment
        {
            public string ProjectName { get; set; }
            public double StartMinute { get; set; }
            public double Minutes { get; set; }
            public SolidColorBrush Brush { get; set; }
        }

        internal class DayRow
        {
            public string DateLabel { get; set; }
            public ObservableCollection<Segment> Segments { get; set; }
            public int SwitchCount { get; set; }
            public double TotalHours { get; set; }
            public bool HasEntries { get; set; }
        }

        internal class LegendItem
        {
            public string ProjectName { get; set; }
            public SolidColorBrush Brush { get; set; }
        }
    }
}
