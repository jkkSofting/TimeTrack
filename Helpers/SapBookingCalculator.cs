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
        InternalCoveredBySurplus
    }

    /// <summary>Ein Abschnitt einer Buchung mit seinem Verrechnungsstatus (für die Zeitleiste).</summary>
    internal sealed class SapSegment
    {
        public string Project { get; set; }
        public int Lane { get; set; }
        public double StartMin { get; set; }
        public double EndMin { get; set; }
        public SapSegmentKind Kind { get; set; }
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
    }

    internal sealed class SapDayResult
    {
        public DateTime Date { get; set; }
        public int FirstStartMin { get; set; }
        public int LastEndMin { get; set; }
        /// <summary>Arbeitszeit: erste Buchung bis Ende letzte Buchung.</summary>
        public double SpanMin => LastEndMin - FirstStartMin;
        /// <summary>Summe aller Buchungen (parallele Zeit zählt mehrfach).</summary>
        public double GrossMin { get; set; }
        public double ExternalMin { get; set; }
        public double InternalGrossMin { get; set; }
        public double InternalBookedMin { get; set; }
        public double CoveredByExternalMin { get; set; }
        public double CoveredBySurplusMin { get; set; }
        public double SurplusGeneratedMin { get; set; }
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
    /// Der Speicher wirkt nur auf später liegende Abschnitte (auch an späteren Tagen). Am Monatsende
    /// verfällt der Rest und zählt als Überstunden. Übergeben werden darf daher nur ein Kalendermonat.
    /// </summary>
    internal static class SapBookingCalculator
    {
        private const double Eps = 1e-9;

        public static SapMonthResult Calculate(IEnumerable<SapBookingInput> inputs)
        {
            var result = new SapMonthResult();
            var monthProjects = new Dictionary<string, SapProjectResult>(StringComparer.OrdinalIgnoreCase);
            double pool = 0;

            var days = inputs
                .Where(x => x != null && x.EndMin > x.StartMin)
                .GroupBy(x => x.Date.Date)
                .OrderBy(g => g.Key);

            foreach (var group in days)
            {
                var entries = group.OrderBy(x => x.StartMin).ThenBy(x => x.EndMin).ToList();
                var day = new SapDayResult
                {
                    Date = group.Key,
                    FirstStartMin = entries.Min(x => x.StartMin),
                    LastEndMin = entries.Max(x => x.EndMin),
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
                SapProjectResult ProjectOf(SapBookingInput e)
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
                        day.Segments.Add(Segment(entries[k], laneOf[k], a, z, SapSegmentKind.External));
                    }

                    if (ext.Count >= 1)
                    {
                        foreach (var k in inte)
                        {
                            ProjectOf(entries[k]).CoveredByExternalMin += len;
                            day.CoveredByExternalMin += len;
                            day.Segments.Add(Segment(entries[k], laneOf[k], a, z, SapSegmentKind.InternalCoveredByExternal));
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
                                day.Segments.Add(Segment(entries[k], laneOf[k], a, split, SapSegmentKind.InternalCoveredBySurplus));
                            if (len - perEntry > Eps)
                                day.Segments.Add(Segment(entries[k], laneOf[k], split, z, SapSegmentKind.InternalBooked));
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
                    mp.BookedMin += p.BookedMin;
                    mp.CoveredByExternalMin += p.CoveredByExternalMin;
                    mp.CoveredBySurplusMin += p.CoveredBySurplusMin;
                }
            }

            result.LeftoverMin = pool;
            result.Projects.AddRange(monthProjects.Values
                .OrderByDescending(p => p.IsExternal)
                .ThenBy(p => p.Project, StringComparer.CurrentCultureIgnoreCase));
            return result;
        }

        private static SapSegment Segment(SapBookingInput e, int lane, double start, double end, SapSegmentKind kind)
        {
            return new SapSegment { Project = e.Project, Lane = lane, StartMin = start, EndMin = end, Kind = kind };
        }
    }
}
