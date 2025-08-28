using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Zeitmanagement.MVVM;

namespace Zeitmanagement.ViewModel
{
    internal sealed class DashboardViewModel : BaseViewModel
    {
        // Hauptwerte (numerisch!)
        private int _projectsCounter;
        public int ProjectsCounter { get => _projectsCounter; set => SetProperty(ref _projectsCounter, value); }

        private int _bookingsCounter;
        public int BookingsCounter { get => _bookingsCounter; set => SetProperty(ref _bookingsCounter, value); }

        private double _hoursCounter;
        public double HoursCounter { get => _hoursCounter; set => SetProperty(ref _hoursCounter, value); }

        // Zusatzwerte
        private double _todayHours;
        public double TodayHours { get => _todayHours; set => SetProperty(ref _todayHours, value); }

        private double _avgHoursLast5Days;
        public double AvgHoursLast5Days { get => _avgHoursLast5Days; set => SetProperty(ref _avgHoursLast5Days, value); }

        private string _topProjectName;
        public string TopProjectName { get => _topProjectName; set => SetProperty(ref _topProjectName, value); }

        private double _topProjectHours;
        public double TopProjectHours { get => _topProjectHours; set => SetProperty(ref _topProjectHours, value); }

        // Header/Foot
        private string _headerSubtitle;
        public string HeaderSubtitle { get => _headerSubtitle; set => SetProperty(ref _headerSubtitle, value); }

        private string _nowStamp;
        public string NowStamp { get => _nowStamp; set => SetProperty(ref _nowStamp, value); }

        // Karten-VMs für das DataTemplate
        public StatCardVM StatProjects { get; } = new StatCardVM
        {
            Caption = "Projekte",
            FormatString = "0",
            IconGeometry = "M3,10 L21,10 21,21 3,21 z M7,3 L17,3 17,10 7,10 z" // simple folder-ish icon
        };

        public StatCardVM StatBookings { get; } = new StatCardVM
        {
            Caption = "Buchungen",
            FormatString = "0",
            IconGeometry = "M3,4 L21,4 21,6 3,6 z M3,9 L21,9 21,20 3,20 z" // list icon
        };

        public StatCardVM StatHours { get; } = new StatCardVM
        {
            Caption = "Stunden gearbeitet",
            FormatString = "0.##",
            IconGeometry = "M12,3 A9,9 0 1,0 12,21 A9,9 0 1,0 12,3 z M11,7 L13,7 13,12 17,14 16,15 11,12 z" // clock
        };

        public DashboardViewModel()
        {
            HeaderSubtitle = "Übersicht deiner produktiven Selbstgeißelung";
            NowStamp = DateTime.Now.ToString("dd.MM.yyyy HH:mm");
        }

        public override void Refresh()
        {
            var db = MainViewModel.DbInstance;

            var projects = db.GetProjects().ToList();
            ProjectsCounter = projects.Count;

            double totalHours = 0d;
            int entryCounter = 0;

            // Für Top-Projekt der letzten 30 Tage sammeln
            var cutoff30 = DateTime.Today.AddDays(-30);
            var projectHours30 = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);

            foreach (var p in projects)
            {
                var entries = db.GetTimeEntriesForProject(p.Projektname);
                foreach (var e in entries)
                {
                    entryCounter++;

                    var fmt = "HH:mm";
                    var culture = CultureInfo.InvariantCulture;
                    var start = DateTime.ParseExact(e.Startzeit, fmt, culture);
                    var end = DateTime.ParseExact(e.Endzeit, fmt, culture);
                    var diffH = (end - start).TotalHours;

                    totalHours += diffH;

                    // Datumsfeld annehmen: e.Datum (DateTime). Falls String – anpassen.
                    DateTime date = e.Datum; // <- falls dein Modell string hat: DateTime.Parse(e.Datum, …)

                    if (date >= cutoff30)
                    {
                        double cur;
                        projectHours30.TryGetValue(p.Projektname, out cur);
                        projectHours30[p.Projektname] = cur + diffH;
                    }
                }
            }

            BookingsCounter = entryCounter;
            HoursCounter = Math.Round(totalHours, 2);

            // Heute
            TodayHours = Math.Round(SumHoursForDate(DateTime.Today), 2);

            // Ø letzte 7 Tage
            double sum7 = 0;
            double sumWorkdays = 0;
            int countedDays = 0;
            DateTime current = DateTime.Today;

            while (countedDays < 5)
            {
                // Nur Montag–Freitag nehmen
                if (current.DayOfWeek != DayOfWeek.Saturday &&
                    current.DayOfWeek != DayOfWeek.Sunday)
                {
                    sumWorkdays += SumHoursForDate(current);
                    countedDays++;
                }

                current = current.AddDays(-1);
            }

            AvgHoursLast5Days = Math.Round(sumWorkdays / 5.0, 2);


            // Top-Projekt (30 Tage)
            if (projectHours30.Count > 0)
            {
                var top = projectHours30.OrderByDescending(kv => kv.Value).First();
                TopProjectName = top.Key;
                TopProjectHours = Math.Round(top.Value, 2);
            }
            else
            {
                TopProjectName = "–";
                TopProjectHours = 0;
            }

            // Karten aktualisieren (für animierte Values)
            StatProjects.Value = ProjectsCounter;
            StatBookings.Value = BookingsCounter;
            StatHours.Value = HoursCounter;

            NowStamp = DateTime.Now.ToString("dd.MM.yyyy HH:mm");
        }

        private double SumHoursForDate(DateTime date)
        {
            var db = MainViewModel.DbInstance;
            var all = db.GetProjects().ToList();
            double sum = 0d;

            foreach (var p in all)
            {
                var entries = db.GetTimeEntriesForProject(p.Projektname);
                foreach (var e in entries)
                {
                    if (e.Datum.Date != date.Date) continue;

                    var fmt = "HH:mm";
                    var culture = CultureInfo.InvariantCulture;
                    var start = DateTime.ParseExact(e.Startzeit, fmt, culture);
                    var end = DateTime.ParseExact(e.Endzeit, fmt, culture);
                    sum += (end - start).TotalHours;
                }
            }
            return sum;
        }
    }

    internal sealed class StatCardVM : BindableBase
    {
        private string _caption;
        public string Caption { get => _caption; set => SetProperty(ref _caption, value); }

        private string _formatString;
        public string FormatString { get => _formatString; set => SetProperty(ref _formatString, value); }

        private string _iconGeometry;
        public string IconGeometry { get => _iconGeometry; set => SetProperty(ref _iconGeometry, value); }

        private double _value;
        public double Value { get => _value; set => SetProperty(ref _value, value); }

        private string _subnote;
        public string Subnote { get => _subnote; set => SetProperty(ref _subnote, value); }
    }
}
