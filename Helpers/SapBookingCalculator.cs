using System;
using System.Collections.Generic;
using System.Linq;

namespace Zeitmanagement.Helpers
{
    /// <summary>Eine Buchung als Eingabe für die SAP-Verrechnung (Zeiten in Minuten seit 00:00).</summary>
    internal sealed class SapBookingInput
    {
        public DateTime Date { get; set; }
        public int StartMin { get; set; }
        public int EndMin { get; set; }
        public string Project { get; set; }
        public bool IsExternal { get; set; }
        /// <summary>
        /// Rundungseinheit des Projekts in Minuten (0 = Aus; sonst 5, 10, 15, 60 - siehe <see cref="RoundingOptions"/>).
        /// Die Buchung gilt für die gesamte Verrechnung als auf das nächste volle Vielfache aufgerundet; die
        /// Aufrundung wird ans ENDE der Buchung gehängt (Start bleibt, Ende verlängert sich).
        /// </summary>
        public int RoundingMinutes { get; set; }
    }

    internal enum SapSegmentKind
    {
        /// <summary>Externe Zeit: wird im SAP voll gebucht.</summary>
        External,
        /// <summary>Interne Zeit, die im SAP gebucht werden muss.</summary>
        InternalBooked,
        /// <summary>Interne Zeit, die parallel zu externer Arbeit lief und deshalb entfällt.</summary>
        InternalCoveredByExternal,
        /// <summary>Interne Zeit, die mit gespeichertem Überschuss verrechnet wurde und deshalb entfällt.</summary>
        InternalCoveredBySurplus,
        /// <summary>Interne Zeit, die parallel zu einem anderen internen Projekt mit weniger Tagesstunden lief: wird verworfen.</summary>
        InternalDiscarded
    }

    /// <summary>Ein Abschnitt einer Buchung mit seinem Verrechnungsstatus (für die Zeitleiste).</summary>
    internal sealed class SapSegment
    {
        public string Project { get; set; }
        public int Lane { get; set; }
        public double StartMin { get; set; }
        public double EndMin { get; set; }
        /// <summary>Verrechnungsstatus des Abschnitts (gilt auch für aufgerundete Zeit).</summary>
        public SapSegmentKind Kind { get; set; }
        /// <summary>
        /// true = dieser Abschnitt liegt in der Aufrundung am Ende der Buchung (also hinter dem realen Ende).
        /// <see cref="Kind"/> bleibt dabei erhalten (die aufgerundete Zeit wird wie reale Zeit verrechnet);
        /// die Darstellung färbt solche Abschnitte mit der Rundungsfarbe. Ein Abschnitt ist nie teils real,
        /// teils aufgerundet: der Rechner teilt am realen Ende.
        /// </summary>
        public bool IsRounding { get; set; }
    }

    /// <summary>Zeitraum, in dem Überschuss entsteht (mehrere externe Projekte laufen parallel).</summary>
    internal sealed class SapSurplusSpan
    {
        public double StartMin { get; set; }
        public double EndMin { get; set; }
        public double Minutes { get; set; }
    }

    internal sealed class SapProjectResult
    {
        public string Project { get; set; }
        public bool IsExternal { get; set; }
        public double GrossMin { get; set; }
        public double BookedMin { get; set; }
        public double CoveredByExternalMin { get; set; }
        public double CoveredBySurplusMin { get; set; }
        public double DiscardedMin { get; set; }
        /// <summary>Durch Aufrundung hinzugekommene Minuten (bereits in GrossMin enthalten).</summary>
        public double RoundedUpMin { get; set; }
    }

    internal sealed class SapDayResult
    {
        public DateTime Date { get; set; }
        public int FirstStartMin { get; set; }
        /// <summary>Ende der letzten Buchung (REAL, ohne Aufrundung).</summary>
        public int LastEndMin { get; set; }
        /// <summary>Spätestes Ende inkl. Aufrundung (für die Zeitleiste); ohne Rundung = LastEndMin.</summary>
        public int DisplayEndMin { get; set; }
        /// <summary>Arbeitszeit: erste Buchung bis Ende letzte Buchung (real, ohne Aufrundung).</summary>
        public double SpanMin => LastEndMin - FirstStartMin;
        /// <summary>Summe aller Buchungen inkl. Aufrundung (parallele Zeit zählt mehrfach).</summary>
        public double GrossMin { get; set; }
        /// <summary>Durch Aufrundung hinzugekommene Minuten (bereits in GrossMin enthalten).</summary>
        public double RoundedUpMin { get; set; }
        public double ExternalMin { get; set; }
        public double InternalGrossMin { get; set; }
        public double InternalBookedMin { get; set; }
        public double CoveredByExternalMin { get; set; }
        public double CoveredBySurplusMin { get; set; }
        public double SurplusGeneratedMin { get; set; }
        public double DiscardedMin { get; set; }
        public double PoolStartMin { get; set; }
        public double PoolEndMin { get; set; }
        public int LaneCount { get; set; }
        public double SapTotalMin => ExternalMin + InternalBookedMin;
        public List<SapSegment> Segments { get; } = new List<SapSegment>();
        public List<SapSurplusSpan> SurplusSpans { get; } = new List<SapSurplusSpan>();
        public List<SapProjectResult> Projects { get; } = new List<SapProjectResult>();
    }

    internal sealed class SapMonthResult
    {
        public List<SapDayResult> Days { get; } = new List<SapDayResult>();
        public List<SapProjectResult> Projects { get; } = new List<SapProjectResult>();
        public double SpanMin => Days.Sum(d => d.SpanMin);
        public double GrossMin => Days.Sum(d => d.GrossMin);
        public double ExternalMin => Days.Sum(d => d.ExternalMin);
        public double InternalGrossMin => Days.Sum(d => d.InternalGrossMin);
        public double InternalBookedMin => Days.Sum(d => d.InternalBookedMin);
        public double CoveredByExternalMin => Days.Sum(d => d.CoveredByExternalMin);
        public double CoveredBySurplusMin => Days.Sum(d => d.CoveredBySurplusMin);
        public double SurplusGeneratedMin => Days.Sum(d => d.SurplusGeneratedMin);
        public double DiscardedMin => Days.Sum(d => d.DiscardedMin);
        /// <summary>Durch Aufrundung hinzugekommene Minuten (bereits in GrossMin enthalten).</summary>
        public double RoundedUpMin => Days.Sum(d => d.RoundedUpMin);
        public double SapTotalMin => ExternalMin + InternalBookedMin;
        /// <summary>Am Monatsende nicht verrechneter Überschuss = tatsächliche Überstunden.</summary>
        public double LeftoverMin { get; set; }
    }

    /// <summary>
    /// Berechnet, welche Zeiten im SAP gebucht werden müssen, wenn parallel auf mehreren Projekten
    /// gebucht wird. Die Zeit wird an jeder Stelle in Abschnitte zerlegt, in denen dieselben Projekte
    /// gleichzeitig laufen. Pro Abschnitt der Länge L mit e externen und i internen Projekten gilt:
    /// <list type="bullet">
    /// <item>e ≥ 1: Die reale Zeit L wird vom Kunden bezahlt. Alle internen Projekte im Abschnitt entfallen
    /// im SAP. Von den e externen Buchungen trägt eine die reale Zeit, die übrigen (e−1)·L sind Überschuss
    /// und wandern in den Speicher.</item>
    /// <item>e = 0, i ≥ 1: Interne Zeit ist "frei". Sie wird mit dem gespeicherten Überschuss verrechnet
    /// (entfällt im SAP), soweit der Speicher reicht; der Rest wird gebucht.</item>
    /// </list>
    /// Laufen ohne externes Projekt mehrere interne Projekte parallel, zählt nur das Projekt, das an diesem
    /// Kalendertag insgesamt weniger Stunden gebucht hat (bei Gleichstand das alphabetisch erste). Die
    /// parallele Zeit der anderen internen Projekte wird verworfen, als hätte sie nie existiert.
    /// Der Speicher wirkt nur auf später liegende Abschnitte (auch an späteren Tagen). Am Monatsende
    /// verfällt der Rest und zählt als Überstunden. Übergeben werden darf daher nur ein Kalendermonat.
    /// <para>Rundung: Hat ein Projekt eine Rundungseinheit (<see cref="SapBookingInput.RoundingMinutes"/>), wird jede
    /// Buchung vor allen Berechnungen am Ende auf das nächste volle Vielfache verlängert. Die Aufrundung gilt als
    /// echte, abrechenbare Zeit (Abdeckung, Überschuss, Verwerfen, GrossMin usw.). Abschnitte hinter dem realen Ende
    /// tragen <see cref="SapSegment.IsRounding"/>. Arbeitszeit (Span, FirstStart/LastEnd) bleibt real.</para>
    /// </summary>
    internal static class SapBookingCalculator
    {
        private const double Eps = 1e-9;

        /// <summary>Buchung mit bereits aufgerundetem Ende (interne Arbeitsform).</summary>
        private sealed class Entry
        {
            public string Project;
            public bool IsExternal;
            public int StartMin;
            /// <summary>Ende inkl. Aufrundung.</summary>
            public int EndMin;
            /// <summary>Reales Ende (Beginn der Aufrundung).</summary>
            public int RealEndMin;
            public int Padding => EndMin - RealEndMin;
        }

        public static SapMonthResult Calculate(IEnumerable<SapBookingInput> inputs)
        {
            var result = new SapMonthResult();
            var monthProjects = new Dictionary<string, SapProjectResult>(StringComparer.OrdinalIgnoreCase);
            double pool = 0;

            var days = inputs
                .Where(x => x != null && x.EndMin > x.StartMin)
                .Select(x => new { x.Date, Entry = ToEntry(x) })
                .GroupBy(x => x.Date.Date)
                .OrderBy(g => g.Key);

            foreach (var group in days)
            {
                var entries = group.Select(x => x.Entry).OrderBy(x => x.StartMin).ThenBy(x => x.EndMin).ToList();
                var day = new SapDayResult
                {
                    Date = group.Key,
                    FirstStartMin = entries.Min(x => x.StartMin),
                    LastEndMin = entries.Max(x => x.RealEndMin),
                    DisplayEndMin = entries.Max(x => x.EndMin),
                    PoolStartMin = pool
                };

                // Spuren (Lanes) für die Darstellung parallel laufender Buchungen
                var laneOf = new int[entries.Count];
                var laneEnds = new List<int>();
                for (int k = 0; k < entries.Count; k++)
                {
                    int lane = laneEnds.FindIndex(end => end <= entries[k].StartMin);
                    if (lane < 0) { laneEnds.Add(0); lane = laneEnds.Count - 1; }
                    laneEnds[lane] = entries[k].EndMin;
                    laneOf[k] = lane;
                }
                day.LaneCount = laneEnds.Count;

                var projects = new Dictionary<string, SapProjectResult>(StringComparer.OrdinalIgnoreCase);
                SapProjectResult ProjectOf(Entry e)
                {
                    if (!projects.TryGetValue(e.Project, out var p))
                    {
                        p = new SapProjectResult { Project = e.Project, IsExternal = e.IsExternal };
                        projects[e.Project] = p;
                    }
                    return p;
                }

                foreach (var e in entries)
                {
                    double len = e.EndMin - e.StartMin;
                    ProjectOf(e).GrossMin += len;
                    ProjectOf(e).RoundedUpMin += e.Padding;
                    day.RoundedUpMin += e.Padding;
                    day.GrossMin += len;
                    if (e.IsExternal) day.ExternalMin += len; else day.InternalGrossMin += len;
                }

                var bounds = entries.SelectMany(x => new[] { x.StartMin, x.EndMin }).Distinct().OrderBy(x => x).ToList();
                for (int b = 0; b + 1 < bounds.Count; b++)
                {
                    int a = bounds[b], z = bounds[b + 1];
                    double len = z - a;
                    var active = new List<int>();
                    for (int k = 0; k < entries.Count; k++)
                        if (entries[k].StartMin <= a && entries[k].EndMin >= z) active.Add(k);
                    if (active.Count == 0) continue; // Pause

                    var ext = active.Where(k => entries[k].IsExternal).ToList();
                    var inte = active.Where(k => !entries[k].IsExternal).ToList();

                    foreach (var k in ext)
                    {
                        ProjectOf(entries[k]).BookedMin += len;
                        AddSegment(day, entries[k], laneOf[k], a, z, SapSegmentKind.External);
                    }

                    if (ext.Count >= 1)
                    {
                        foreach (var k in inte)
                        {
                            ProjectOf(entries[k]).CoveredByExternalMin += len;
                            day.CoveredByExternalMin += len;
                            AddSegment(day, entries[k], laneOf[k], a, z, SapSegmentKind.InternalCoveredByExternal);
                        }

                        double surplus = (ext.Count - 1) * len;
                        if (surplus > Eps)
                        {
                            pool += surplus;
                            day.SurplusGeneratedMin += surplus;
                            day.SurplusSpans.Add(new SapSurplusSpan { StartMin = a, EndMin = z, Minutes = surplus });
                        }
                    }
                    else if (inte.Count >= 1)
                    {
                        if (inte.Count > 1)
                        {
                            // Nur das Projekt mit den wenigsten Tagesstunden bleibt, die anderen entfallen
                            var keep = inte
                                .OrderBy(k => projects[entries[k].Project].GrossMin)
                                .ThenBy(k => entries[k].Project, StringComparer.CurrentCultureIgnoreCase)
                                .First();
                            foreach (var k in inte.Where(k => k != keep))
                            {
                                var dp = ProjectOf(entries[k]);
                                dp.DiscardedMin += len;
                                day.DiscardedMin += len;
                                AddSegment(day, entries[k], laneOf[k], a, z, SapSegmentKind.InternalDiscarded);
                            }
                            inte = new List<int> { keep };
                        }

                        double demand = inte.Count * len;
                        double absorbed = Math.Min(pool, demand);
                        pool -= absorbed;
                        double perEntry = absorbed / inte.Count;       // Minuten pro interner Buchung
                        double split = a + perEntry;                    // Darstellung: erst verrechnet, dann gebucht

                        foreach (var k in inte)
                        {
                            var p = ProjectOf(entries[k]);
                            p.CoveredBySurplusMin += perEntry;
                            p.BookedMin += len - perEntry;
                            if (perEntry > Eps)
                                AddSegment(day, entries[k], laneOf[k], a, split, SapSegmentKind.InternalCoveredBySurplus);
                            if (len - perEntry > Eps)
                                AddSegment(day, entries[k], laneOf[k], split, z, SapSegmentKind.InternalBooked);
                        }
                        day.CoveredBySurplusMin += absorbed;
                        day.InternalBookedMin += demand - absorbed;
                    }
                }

                day.PoolEndMin = pool;
                day.Projects.AddRange(projects.Values
                    .OrderByDescending(p => p.IsExternal)
                    .ThenBy(p => p.Project, StringComparer.CurrentCultureIgnoreCase));
                result.Days.Add(day);

                foreach (var p in projects.Values)
                {
                    if (!monthProjects.TryGetValue(p.Project, out var mp))
                    {
                        mp = new SapProjectResult { Project = p.Project, IsExternal = p.IsExternal };
                        monthProjects[p.Project] = mp;
                    }
                    mp.GrossMin += p.GrossMin;
                    mp.RoundedUpMin += p.RoundedUpMin;
                    mp.BookedMin += p.BookedMin;
                    mp.CoveredByExternalMin += p.CoveredByExternalMin;
                    mp.CoveredBySurplusMin += p.CoveredBySurplusMin;
                    mp.DiscardedMin += p.DiscardedMin;
                }
            }

            result.LeftoverMin = pool;
            result.Projects.AddRange(monthProjects.Values
                .OrderByDescending(p => p.IsExternal)
                .ThenBy(p => p.Project, StringComparer.CurrentCultureIgnoreCase));
            return result;
        }

        private static Entry ToEntry(SapBookingInput x)
        {
            int rounded = RoundingOptions.RoundUp(x.EndMin - x.StartMin, x.RoundingMinutes);
            return new Entry
            {
                Project = x.Project,
                IsExternal = x.IsExternal,
                StartMin = x.StartMin,
                RealEndMin = x.EndMin,
                EndMin = x.StartMin + rounded
            };
        }

        /// <summary>
        /// Erzeugt den Abschnitt; reicht er über das reale Ende der Buchung hinaus, wird er dort geteilt und der
        /// hintere Teil als Aufrundung (<see cref="SapSegment.IsRounding"/>) gekennzeichnet.
        /// </summary>
        private static void AddSegment(SapDayResult day, Entry e, int lane, double start, double end, SapSegmentKind kind)
        {
            if (e.Padding > 0 && end > e.RealEndMin + Eps)
            {
                if (start < e.RealEndMin - Eps)
                {
                    day.Segments.Add(new SapSegment { Project = e.Project, Lane = lane, StartMin = start, EndMin = e.RealEndMin, Kind = kind });
                    start = e.RealEndMin;
                }
                day.Segments.Add(new SapSegment { Project = e.Project, Lane = lane, StartMin = start, EndMin = end, Kind = kind, IsRounding = true });
                return;
            }
            day.Segments.Add(new SapSegment { Project = e.Project, Lane = lane, StartMin = start, EndMin = end, Kind = kind });
        }
    }
}
