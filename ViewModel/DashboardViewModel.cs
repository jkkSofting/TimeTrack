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

        // ---------- Aktivitäts-Heatmap (GitHub-Style) ----------

        /// <summary>Anzahl der angezeigten Wochen (Spalten). ~1 Jahr.</summary>
        private const int HeatmapWeeks = 53;

        /// <summary>Stunden-Schwellen für die 5 Farbstufen (Level 1..4 Untergrenzen).</summary>
        private static readonly double[] HeatThresholds = { 2.0, 4.0, 6.0, 8.0 };

        /// <summary>Farbverlauf von "keine Aktivität" bis "viel" – passend zur Dashboard-Palette.</summary>
        private static readonly Brush[] HeatLevelBrushes = BuildLevelBrushes();

        public ObservableCollection<HeatColumnVM> HeatColumns { get; } = new ObservableCollection<HeatColumnVM>();

        // Für die Legende ("Weniger … Mehr")
        public Brush HeatLevel0 => HeatLevelBrushes[0];
        public Brush HeatLevel1 => HeatLevelBrushes[1];
        public Brush HeatLevel2 => HeatLevelBrushes[2];
        public Brush HeatLevel3 => HeatLevelBrushes[3];
        public Brush HeatLevel4 => HeatLevelBrushes[4];

        private static Brush[] BuildLevelBrushes()
        {
            var colors = new[]
            {
                (Color)ColorConverter.ConvertFromString("#1E2740"), // 0 – keine Aktivität
                (Color)ColorConverter.ConvertFromString("#264B6E"), // 1
                (Color)ColorConverter.ConvertFromString("#2E77B0"), // 2
                (Color)ColorConverter.ConvertFromString("#3EA0E0"), // 3
                (Color)ColorConverter.ConvertFromString("#5AC8FA"), // 4 – viel
            };

            var brushes = new Brush[colors.Length];
            for (int i = 0; i < colors.Length; i++)
            {
                var b = new SolidColorBrush(colors[i]);
                b.Freeze();
                brushes[i] = b;
            }
            return brushes;
        }

        private static int LevelForHours(double hours)
        {
            if (hours <= 0) return 0;
            int level = 1;
            for (int i = 0; i < HeatThresholds.Length; i++)
            {
                if (hours >= HeatThresholds[i]) level = i + 2;
            }
            return Math.Min(level, HeatLevelBrushes.Length - 1);
        }

        private void BuildHeatmap()
        {
            var today = DateTime.Today;

            // Montag der aktuellen Woche (Wochenstart = Montag)
            int deltaToMonday = ((int)today.DayOfWeek + 6) % 7;
            var currentMonday = today.AddDays(-deltaToMonday);
            var startMonday = currentMonday.AddDays(-7 * (HeatmapWeeks - 1));

            var daily = MainViewModel.DbInstance.GetDailyHours(startMonday, today);

            HeatColumns.Clear();

            int prevMonth = -1;
            for (int col = 0; col < HeatmapWeeks; col++)
            {
                var colMonday = startMonday.AddDays(7 * col);

                // Monatslabel nur beim ersten Auftauchen eines Monats
                string monthLabel = string.Empty;
                if (colMonday.Month != prevMonth)
                {
                    monthLabel = colMonday.ToString("MMM", CultureInfo.CurrentCulture);
                    prevMonth = colMonday.Month;
                }

                var column = new HeatColumnVM { MonthLabel = monthLabel };

                for (int d = 0; d < 7; d++)
                {
                    var date = colMonday.AddDays(d);
                    if (date > today)
                    {
                        // Zukünftige Tage: unsichtbarer Platzhalter, damit das Raster ausgerichtet bleibt.
                        column.Days.Add(new HeatCellVM { IsPlaceholder = true });
                        continue;
                    }

                    double hours;
                    daily.TryGetValue(date.Date, out hours);
                    int level = LevelForHours(hours);

                    string tip = hours > 0
                        ? $"{date:dd.MM.yyyy}: {hours:0.##} h"
                        : $"{date:dd.MM.yyyy}: keine Buchung";

                    column.Days.Add(new HeatCellVM
                    {
                        Date = date,
                        Fill = HeatLevelBrushes[level],
                        Tooltip = tip
                    });
                }

                HeatColumns.Add(column);
            }
        }

        // ==================================================================
        //  Spaß-Vergleich: "0,00× die Dauer von ..."
        //  --> Neue Vergleiche einfach unten in der Liste ergänzen.
        //      Erster Wert = Dauer in STUNDEN, zweiter = Text nach "die Dauer".
        // ==================================================================
        private static readonly DurationFact[] DurationFacts =
        {
            // --- Filme & Serien ---
            new DurationFact("eines Kinofilms", 2.0),
            new DurationFact("der „Herr der Ringe“-Trilogie (Extended)", 11.4),
            new DurationFact("eines „Tatort“-Krimis", 1.5),
            new DurationFact("der ersten Staffel „Stranger Things“", 8.0),
            new DurationFact("aller acht „Harry Potter“-Filme", 19.7),
            new DurationFact("allen kanonischen „Star Wars“ Filmen und Serien", 250),

            // --- Sport ---
            new DurationFact("eines Fußballspiels", 1.5),
            new DurationFact("eines durchschnittlichen Formel-1-Rennens", 1.75),
            new DurationFact("des längsten Tennismatches der Geschichte (Isner–Mahut)", 11.08),
            new DurationFact("der gesamten Tour de France (Fahrzeit)", 87.0),
            new DurationFact("eines Ironman-Weltrekords", 7.0),

            // --- Musik ---
            new DurationFact("von Beethovens 9. Sinfonie", 1.1),
            new DurationFact("aller Beatles-Studioalben am Stück", 10.4),

            // --- Reisen & Raumfahrt ---
            new DurationFact("eines Transatlantikflugs (Frankfurt–New York)", 8.5),
            new DurationFact("einer Erdumrundung der ISS", 1.5),
            new DurationFact("eines Flugs zum Mond", 4*24),
            new DurationFact("der gesamten Apollo-11-Mission", 195.0),

            // --- Bauwerke (Bauzeit) ---
            new DurationFact("des Baus des Empire State Building", 410 * 24.0),          // ~410 Tage
            new DurationFact("des Baus des Eiffelturms", 26 * 30.0 * 24.0),              // ~26 Monate
            new DurationFact("des Baus des Flughafens BER", 14 * 365 * 24.0),            // 2006–2020
            new DurationFact("des Baus der Elbphilharmonie", 9 * 365 * 24.0),            // 2007–2016
            new DurationFact("des Baus der Cheops-Pyramide", 20 * 365 * 24.0),           // ~20 Jahre
            new DurationFact("des Baus des Kölner Doms", 632 * 365 * 24.0),              // 1248–1880

            // --- Alltag ---
            new DurationFact("einer Kaffeepause", 0.25),
            new DurationFact("eines 8-Stunden-Arbeitstags", 8.0),
            new DurationFact("einer guten Nachtruhe", 8.0),
        };

        private static readonly Random ComparisonRng = new Random();

        /// <summary>Zwischengespeicherte Gesamtstunden, damit „Neu würfeln“ ohne DB-Refresh funktioniert.</summary>
        private double _comparisonTotalHours;

        private string _comparisonFactorText = "–";
        public string ComparisonFactorText { get => _comparisonFactorText; set => SetProperty(ref _comparisonFactorText, value); }

        private string _comparisonCaption = string.Empty;
        public string ComparisonCaption { get => _comparisonCaption; set => SetProperty(ref _comparisonCaption, value); }

        /// <summary>Wählt einen neuen zufälligen Vergleich aus (auch per Button "neu würfeln").</summary>
        public DelegateCommand NextComparisonCommand { get; }

        private void PickRandomComparison()
        {
            if (DurationFacts.Length == 0) return;

            var fact = DurationFacts[ComparisonRng.Next(DurationFacts.Length)];
            double factor = fact.Hours > 0 ? _comparisonTotalHours / fact.Hours : 0;

            ComparisonFactorText = FormatFactor(factor) + "×";
            ComparisonCaption = "die Dauer " + fact.Phrase;
        }

        private static string FormatFactor(double f)
        {
            var de = CultureInfo.GetCultureInfo("de-DE");
            if (f >= 100) return f.ToString("#,##0", de);
            if (f >= 10) return f.ToString("0.0", de);
            if (f >= 1) return f.ToString("0.00", de);
            if (f >= 0.01) return f.ToString("0.000", de);
            return f.ToString("0.00000", de);
        }

        /// <summary>Wird aufgerufen, wenn eine Heatmap-Zelle angeklickt wird (öffnet den Tag in der Stundenbuchung).</summary>
        private readonly Action<DateTime> _openDay;

        /// <summary>Öffnet den angeklickten Tag in der Stundenbuchungs-Ansicht.</summary>
        public DelegateCommand OpenDayCommand { get; }

        public DashboardViewModel() : this(null) { }

        public DashboardViewModel(Action<DateTime> openDay)
        {
            _openDay = openDay;
            OpenDayCommand = new DelegateCommand(OnOpenDay, CanOpenDay);
            NextComparisonCommand = new DelegateCommand(_ => PickRandomComparison());

            HeaderSubtitle = "Übersicht deiner produktiven Selbstgeißelung";
            NowStamp = DateTime.Now.ToString("dd.MM.yyyy HH:mm");
        }

        private static bool CanOpenDay(object parameter)
        {
            return parameter is HeatCellVM cell && !cell.IsPlaceholder && cell.Date.HasValue;
        }

        private void OnOpenDay(object parameter)
        {
            if (parameter is HeatCellVM cell && cell.Date.HasValue)
                _openDay?.Invoke(cell.Date.Value);
        }

        private static readonly string[] AllowedTimeFormats = { @"h\:mm", @"hh\:mm", "Hmm", "HHmm" };

        private static bool TryParseTimeSpan(string input, out TimeSpan time)
        {
            return TimeSpan.TryParseExact(input, AllowedTimeFormats, CultureInfo.InvariantCulture, out time);
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
                foreach (var entry in entries)
                {
                    entryCounter++;


                    if (!TryParseTimeSpan(entry.Startzeit, out var start) ||
                        !TryParseTimeSpan(entry.Endzeit, out var end))
                    {
                        Console.WriteLine($"Ungültige Zeit in Eintrag {entryCounter + 1}: '{entry.Startzeit}' -> '{entry.Endzeit}'");
                        continue;
                    }

                    entryCounter++;

                    var fmt = "HH:mm";
                    var culture = CultureInfo.InvariantCulture;
                    var diffH = (end - start).TotalHours;

                    totalHours += diffH;

                    // Datumsfeld annehmen: e.Datum (DateTime). Falls String – anpassen.
                    DateTime date = entry.Datum; // <- falls dein Modell string hat: DateTime.Parse(e.Datum, …)

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

            BuildHeatmap();

            _comparisonTotalHours = HoursCounter;
            PickRandomComparison();

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

                int counter = 0;
                foreach (var entry in entries)
                {
                    counter++;
                    if (!TryParseTimeSpan(entry.Startzeit, out var start) ||
                        !TryParseTimeSpan(entry.Endzeit, out var end))
                    {
                        Console.WriteLine($"Ungültige Zeit in Eintrag {counter + 1}: '{entry.Startzeit}' -> '{entry.Endzeit}'");
                        continue;
                    }

                    if (entry.Datum.Date != date.Date) continue;

                    sum += (end - start).TotalHours;
                }
            }

            


            return sum;
        }
    }

    /// <summary>Eine Spalte (= eine Woche) der Aktivitäts-Heatmap.</summary>
    internal sealed class HeatColumnVM
    {
        public string MonthLabel { get; set; } = string.Empty;
        public ObservableCollection<HeatCellVM> Days { get; } = new ObservableCollection<HeatCellVM>();
    }

    /// <summary>Ein Tag (= eine Zelle) der Aktivitäts-Heatmap.</summary>
    internal sealed class HeatCellVM
    {
        public DateTime? Date { get; set; }
        public Brush Fill { get; set; }
        public string Tooltip { get; set; }

        /// <summary>Zukünftige Tage bleiben unsichtbar, halten aber das Raster ausgerichtet.</summary>
        public bool IsPlaceholder { get; set; }
    }

    /// <summary>Ein „Dauer-Fakt“ für den Spaß-Vergleich auf dem Dashboard.</summary>
    internal sealed class DurationFact
    {
        /// <summary>Text, der hinter „die Dauer …“ steht, z. B. „eines Fußballspiels“.</summary>
        public string Phrase { get; }

        /// <summary>Dauer dieses Ereignisses in Stunden.</summary>
        public double Hours { get; }

        public DurationFact(string phrase, double hours)
        {
            Phrase = phrase;
            Hours = hours;
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
