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
            new DurationFact("aller Ed Sheeran Studio- und Haupt-Tracks am Stück", 10),
            new DurationFact("aller auf Spotify verfügbaren Songs", 5500000),

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
            new DurationFact("des Baus der Chinesischen Mauer (Gesamtbauphase)", 2000.0 * 365 * 24.0),       // über 2.000 Jahre (verschiedene Dynastien)
            new DurationFact("des Baus des Kolosseums in Rom", 10.0 * 365 * 24.0),                          // ~70–80 n. Chr. (10 Jahre)
            new DurationFact("des Baus des Taj Mahal", 22.0 * 365 * 24.0),                                  // 1631–1653 (~22 Jahre)
            new DurationFact("des Baus der Sagrada Família (Stand 2026)", 144.0 * 365 * 24.0),               // 1882–heute (~144 Jahre)
            new DurationFact("des Baus des Burj Khalifa", 6.0 * 365 * 24.0),                                // 2004–2010 (6 Jahre)
            new DurationFact("des Baus der Golden Gate Bridge", 4.3 * 365 * 24.0),                          // Jan 1933 – Mai 1937 (~4,3 Jahre)
            new DurationFact("des Baus des Sydney Opera House", 14.0 * 365 * 24.0),                         // 1959–1973 (14 Jahre)
            new DurationFact("des Baus des Gotthard-Basistunnels", 17.0 * 365 * 24.0),                      // 1999–2016 (17 Jahre)
            new DurationFact("des Baus des Panama-Kanals", 33.0 * 365 * 24.0),                               // 1881–1914 (inkl. französischer & US-Phase)

            // --- Geschichte ---
            new DurationFact("des Arabischen Frühlings", 2 * 365 * 24.0),                  // Dec 2010 – Dec 2012
            new DurationFact("der Französischen Revolution", 10.5 * 365 * 24.0),           // May 1789 – Nov 1799
            new DurationFact("der Amerikanischen Revolution", 18.5 * 365 * 24.0),          // Dec 1773 – Sep 1783
            new DurationFact("der Glorreichen Revolution", 0.5 * 365 * 24.0),              // Nov 1688 – May 1689
            new DurationFact("der Haïtianischen Revolution", 12.3 * 365 * 24.0),           // Aug 1791 – Jan 1804
            new DurationFact("der Mexikanischen Revolution", 10.5 * 365 * 24.0),           // Nov 1910 – May 1921
            new DurationFact("der Russischen Revolution", 0.7 * 365 * 24.0),               // Mar 1917 – Nov 1917
            new DurationFact("der Nelkenrevolution in Portugal", 1 * 365 * 24.0),          // Apr 1974 – Apr 1975
            new DurationFact("der Friedlichen Revolution in der DDR", 1.0 * 365 * 24.0),   // Sep 1989 – Oct 1990

            // Monarchen
            new DurationFact("der Regierungszeit von Queen Elizabeth II.", 70.6 * 365 * 24.0),           // Feb 1952 – Sep 2022
            new DurationFact("der Regierungszeit von König Ludwig XIV.", 72.3 * 365 * 24.0),            // May 1643 – Sep 1715
            new DurationFact("der Regierungszeit von Kaiser Franz Joseph I.", 68.0 * 365 * 24.0),         // Dec 1848 – Nov 1916

            // Deutsche Kanzler & Staatsmänner
            new DurationFact("der Kanzlerschaft von Helmut Kohl", 16.1 * 365 * 24.0),                   // Oct 1982 – Oct 1998
            new DurationFact("der Kanzlerschaft von Angela Merkel", 16.0 * 365 * 24.0),                 // Nov 2005 – Dec 2021
            new DurationFact("der Kanzlerschaft von Konrad Adenauer", 14.1 * 365 * 24.0),               // Sep 1949 – Oct 1963
            new DurationFact("der Amtszeit von Otto von Bismarck als Reichskanzler", 19.2 * 365 * 24.0),  // Mar 1871 – Mar 1890

            // US-Präsidenten
            new DurationFact("der Präsidentschaft von Franklin D. Roosevelt", 12.1 * 365 * 24.0),         // Mar 1933 – Apr 1945
            new DurationFact("einer regulären US-Präsidentschaft (2 Amtszeiten)", 8.0 * 365 * 24.0),     // 8 Jahre (z.B. Obama, Bush, Clinton)
            new DurationFact("der Präsidentschaft von William Henry Harrison", (31.0 / 365.0) * 365 * 24.0), // 31 Tage (Mar 1841 – Apr 1841)

            // Längste & kürzeste Amtszeiten der Moderne
            new DurationFact("des Pontifikats von Pius IX.", 31.7 * 365 * 24.0),           // Jun 1846 – Feb 1878 (längstes historisch nachgewiesenes Pontifikat)
            new DurationFact("des Pontifikats von Johannes Paul II.", 26.4 * 365 * 24.0),    // Oct 1978 – Apr 2005
            new DurationFact("des Pontifikats von Leo XIII.", 25.4 * 365 * 24.0),           // Feb 1878 – Jul 1903
            new DurationFact("des Pontifikats von Johannes Paul I.", (33.0 / 365.0) * 365 * 24.0), // 33 Tage (Aug 1978 – Sep 1978)
            new DurationFact("des Pontifikats von Urban VII.", (13.0 / 365.0) * 365 * 24.0),       // 13 Tage (15. Sep 1590 – 27. Sep 1590; kürzestes Pontifikat der Geschichte)

            // Bekannte Päpste der Neuzeit
            new DurationFact("des Pontifikats von Benedikt XVI.", 7.8 * 365 * 24.0),         // Apr 2005 – Feb 2013
            new DurationFact("des Pontifikats von Franziskus", 13.3 * 365 * 24.0),           // Mar 2013 – Jul 2026

            // --- Alltag ---
            new DurationFact("einer Kaffeepause", 0.25),
            new DurationFact("eines 8-Stunden-Arbeitstags", 8.0),
            new DurationFact("einer guten Nachtruhe", 8.0),
            new DurationFact("der Zersetzung einer Plastiktüte", 15*365*24.0), // ~15 Jahre
            new DurationFact("der Reifedauer eines Parmesan-Laibs", 12*30*24.0), // ~12 Monate
            new DurationFact("der Reifedauer eines jungen Gouda-Laibs", 6*7*24.0), // ~6 Wochen
            new DurationFact("der Reifedauer eines Maasdammer-Laibs", 5*7*24.0), // ~5 Wochen
            new DurationFact("der Reifedauer eines Emmentaler-Laibs", 100*24.0), // ~100 Tage

            // --- Technik ---
            new DurationFact("der Umlaufzeit des Merkur um die Sonne", 88 * 24.0),
            new DurationFact("der Umlaufzeit der Erde um die Sonne", 365 * 24.0),
            new DurationFact("der Umlaufzeit des Pluto um die Sonne", 248 * 365 * 24.0),
            new DurationFact("der Umlaufzeit des Neptun um die Sonne", 164.8 * 365 * 24.0),
            new DurationFact("der Umlaufzeit des Voyager-1-Raumfahrzeugs um die Sonne", 225 * 365 * 24.0), // ~225 Jahre
            new DurationFact("der Umlaufzeit der Venus um die Sonne", 224.7 * 24.0),
            new DurationFact("der Umlaufzeit des Saturn um die Sonne", 29.5 * 365 * 24.0),
            new DurationFact("der Umlaufzeit des Jupiter um die Sonne", 11.86 * 365 * 24.0),
            new DurationFact("der Umlaufzeit des Uranus um die Sonne", 84.0 * 365 * 24.0),
            new DurationFact("der Umlaufzeit des Mars um die Sonne", 1.88 * 365 * 24.0),

            // --- Obst und Gemüse ---
            // Schnelles Gemüse (Wochen)
            new DurationFact("des Wachstums von Radieschen (Aussaat bis Ernte)", (4.0 * 7.0 * 24.0) / 8760.0),      // ca. 3–5 Wochen
            new DurationFact("des Wachstums von Pflpflanzsalat / Kopfsalat", (7.0 * 7.0 * 24.0) / 8760.0),   // ca. 6–8 Wochen
            new DurationFact("des Wachstums von Spinat", (6.0 * 7.0 * 24.0) / 8760.0),                               // ca. 6–8 Wochen

            // Mittleres Gemüse (Monate)
            new DurationFact("des Wachstums von Karotten / Möhren", (12.0 * 7.0 * 24.0) / 8760.0),                   // ca. 10–14 Wochen
            new DurationFact("des Wachstums von Frühkartoffeln", (14.0 * 7.0 * 24.0) / 8760.0),                     // ca. 3–4 Monate (90–110 Tage)
            new DurationFact("des Wachstums von Spätkartoffeln", (20.0 * 7.0 * 24.0) / 8760.0),                     // ca. 4,5–5 Monate (140–160 Tage)
            new DurationFact("des Wachstums von Tomaten (Aussaat bis zur ersten Reife)", 5.0 * 30.0 * 24.0 / 8760.0),// ca. 4–5 Monate (Vorzucht + Auspflanzen)
            new DurationFact("des Wachstums von Speisekürbissen (Hokkaido/Butternut)", 4.0 * 30.0 * 24.0 / 8760.0), // ca. 4 Monate

            // Beeren & Obstgehölze (Bis zur ERSTEN nennenswerten Ernte nach Pflanzung)
            new DurationFact("bis zur ersten Ernte bei einer Erdbeerpflanze", 0.3 * 365 * 24.0),                    // ca. 3–4 Monate (bei Frühjahrspflanzung)
            new DurationFact("bis zur ersten Ernte bei einem Himbeerstrauch", 1.0 * 365 * 24.0),                    // ca. 1 Jahr
            new DurationFact("bis zur ersten Ernte bei einem Apfelbaum (Veredelter Halbstamm)", 3.5 * 365 * 24.0), // ca. 3–4 Jahre
            new DurationFact("bis zur ersten Ernte bei einem Walnussbaum", 8.0 * 365 * 24.0)                        // ca. 8–10 Jahre
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
