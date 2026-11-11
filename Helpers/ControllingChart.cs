// Target: .NET Framework + C# 7.3
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Zeitmanagement.Helpers
{
    // Daten und Rechenlogik des Diagramms im Projektcontrolling. Bewusst ohne WPF: Das Zeichnen übernimmt Views\XyChart.cs,
    // alles, was Werte, Reihenfolge, Lücken und Achsen bestimmt, ist hier und damit testbar.

    public enum ChartMode
    {
        /// <summary>Stand relativ: Ist ÷ Plan in Prozent (Plan = 100 %).</summary>
        Percent,
        /// <summary>Stunden: aus Geld umgerechnete Stunden (Betrag ÷ Stundensatz) und lokal gebuchte Stunden.</summary>
        Hours
    }

    public enum ChartSeriesKind
    {
        /// <summary>Ist der SAP-Stände (kumuliert bis zum jeweiligen SAP-Datum).</summary>
        Actual,
        /// <summary>Plan der SAP-Stände (gestrichelt).</summary>
        Plan,
        /// <summary>Lokal gebuchte Stunden, kumuliert über die Zeit.</summary>
        LocalBooked,
        /// <summary>Hilfslinie, z. B. 100 % des Plans.</summary>
        Reference
    }

    public sealed class ChartPoint
    {
        public ChartPoint(DateTime x, double y, string tip, string valueText)
        {
            X = x;
            Y = y;
            Tip = tip;
            ValueText = valueText;
        }

        public DateTime X { get; }
        public double Y { get; }
        /// <summary>Text, der beim Überfahren des Punkts erscheint (mit Werten und Quelle).</summary>
        public string Tip { get; }
        /// <summary>Kurzer Wert ("42,0 %", "833,33 h") für die Übersicht aller Linien an einem Datum.</summary>
        public string ValueText { get; }
    }

    public sealed class ChartSeries
    {
        public string Name { get; set; }
        public ChartSeriesKind Kind { get; set; }
        /// <summary>Kostenträger-Schlüssel der Zeile, zu der die Reihe gehört (Klick wählt sie aus); <c>null</c> bei Hilfslinien.</summary>
        public string RowKey { get; set; }
        /// <summary>Index in der Datenpalette des Themes; -1 = neutral.</summary>
        public int ColorIndex { get; set; }
        public bool IsSelected { get; set; }
        public bool ShowMarkers { get; set; }
        public bool Dashed { get; set; }

        /// <summary>Zusammenhängende Linienstücke; ein unbekannter Wert (z. B. fehlender Plan) trennt die Linie, er wird nie als 0 gezeichnet.</summary>
        public List<List<ChartPoint>> Segments { get; } = new List<List<ChartPoint>>();

        public IEnumerable<ChartPoint> Points
        {
            get { return Segments.SelectMany(s => s); }
        }
    }

    public sealed class ChartData
    {
        public ChartMode Mode { get; set; }
        public List<ChartSeries> Series { get; } = new List<ChartSeries>();
        public DateTime XMin { get; set; }
        public DateTime XMax { get; set; }
        public double YMin { get; set; }
        public double YMax { get; set; }
        public double YStep { get; set; } = 1;
        public string YTitle { get; set; }
        /// <summary>Hinweise unter dem Diagramm (ausgelassene Zeilen, Herkunft der Werte).</summary>
        public List<string> Notes { get; } = new List<string>();
        /// <summary>Begründung, wenn nichts zu zeichnen ist.</summary>
        public string EmptyText { get; set; }

        /// <summary>Es gibt mindestens eine Reihe mit Punkten (Hilfslinien allein zählen nicht).</summary>
        public bool HasData
        {
            get { return Series.Any(s => s.Kind != ChartSeriesKind.Reference && s.Points.Any()); }
        }
    }

    /// <summary>Eine Zeile mit ihrem Verlauf (aktive Beobachtungen, nach SAP-Datum aufsteigend).</summary>
    public sealed class ChartRowInput
    {
        public ChartRowInput(ControllingOverviewRow row, IReadOnlyList<ControllingObservation> history)
        {
            Row = row;
            History = history != null && history.Count > 0 ? history : new[] { row.Observation };
        }

        public ControllingOverviewRow Row { get; }
        public IReadOnlyList<ControllingObservation> History { get; }
    }

    /// <summary>Lokal gebuchte Stunden bis einschließlich eines Tages (kumuliert).</summary>
    public sealed class LocalHoursPoint
    {
        public LocalHoursPoint(DateTime date, double cumulativeHours)
        {
            Date = date;
            CumulativeHours = cumulativeHours;
        }

        public DateTime Date { get; }
        public double CumulativeHours { get; }
    }

    /// <summary>
    /// Baut die Reihen des Diagramms. Die SAP-Werte sind schon kumuliert (Ist bis zum SAP-Stand); lokal gebuchte Stunden
    /// werden hier über die Zeit aufsummiert. Beides bleibt getrennte Reihen mit eigener Beschriftung: Umgerechnete
    /// Stunden stammen aus Geld, lokale aus Buchungen.
    /// </summary>
    public static class ControllingChartBuilder
    {
        public const int MaxSeries = 10;
        public const int PaletteSize = 8;

        private static readonly CultureInfo German = CostMath.German;

        public static ChartData Build(
            IReadOnlyList<ChartRowInput> rows,
            string selectedKey,
            ChartMode mode,
            bool showComputedHours,
            IReadOnlyList<LocalHoursPoint> localCumulative,
            int maxSeries = MaxSeries,
            int hiddenByFilter = 0)
        {
            rows = rows ?? new List<ChartRowInput>();
            localCumulative = localCumulative ?? new List<LocalHoursPoint>();

            var data = new ChartData
            {
                Mode = mode,
                YTitle = mode == ChartMode.Percent
                    ? "Stand in % des Plans (Ist ÷ Plan je SAP-Stand)"
                    : "Stunden – umgerechnet aus Geld (Betrag ÷ Stundensatz) bzw. lokal gebucht"
            };

            // 1) Kandidaten: je Zeile die Linienstücke ihres Verlaufs
            var candidates = new List<Candidate>();
            var withoutValue = 0;
            var withoutRate = 0;

            foreach (var input in rows)
            {
                var row = input.Row;

                if (mode == ChartMode.Hours && !showComputedHours) continue;   // berechnete Stunden sind ausgeblendet

                if (mode == ChartMode.Hours && !row.HourlyRate.HasValue)
                {
                    withoutRate++;
                    continue;
                }

                var series = mode == ChartMode.Percent ? PercentSeries(input) : HoursSeries(input, row.HourlyRate.Value, ChartSeriesKind.Actual);
                if (!series.Points.Any())
                {
                    withoutValue++;
                    continue;
                }

                candidates.Add(new Candidate(input, series, LatestValue(series)));
            }

            // 2) Reihenfolge: gewählte Zeile zuerst, dann der höchste Stand; begrenzt, damit das Diagramm lesbar bleibt
            var ordered = candidates
                .OrderByDescending(c => c.Input.Row.CarrierKey == selectedKey)
                .ThenByDescending(c => c.Latest)
                .ThenBy(c => c.Input.Row.CarrierKey, StringComparer.Ordinal)
                .ToList();

            var shown = ordered.Take(Math.Max(1, maxSeries)).ToList();
            for (var i = 0; i < shown.Count; i++)
            {
                var series = shown[i].Series;
                series.ColorIndex = i % PaletteSize;
                series.IsSelected = shown[i].Input.Row.CarrierKey == selectedKey;
                data.Series.Add(series);
            }

            // 3) Zur gewählten Zeile: Plan (Stunden) und lokal gebuchte Stunden
            var selected = rows.FirstOrDefault(r => r.Row.CarrierKey == selectedKey);
            var selectedShown = shown.FirstOrDefault(c => c.Input.Row.CarrierKey == selectedKey);

            if (selected != null && mode == ChartMode.Hours && showComputedHours && selected.Row.HourlyRate.HasValue && selectedShown != null)
            {
                var plan = HoursSeries(selected, selected.Row.HourlyRate.Value, ChartSeriesKind.Plan);
                if (plan.Points.Any())
                {
                    plan.ColorIndex = selectedShown.Series.ColorIndex;
                    plan.IsSelected = true;
                    data.Series.Add(plan);
                }
            }

            LocalBookedSeries(data, selected, mode, showComputedHours, localCumulative);

            // 4) Achsen
            FinishAxes(data);

            // 5) Hinweise
            var notes = data.Notes;
            if (candidates.Count > shown.Count)
            {
                notes.Add("Gezeigt werden " + shown.Count.ToString(CultureInfo.InvariantCulture) + " von " + candidates.Count.ToString(CultureInfo.InvariantCulture) +
                          " Zeilen (die mit dem höchsten Stand). Mit Suche, Filtern oder dem Kostenträger-Filter eingrenzen.");
            }
            if (hiddenByFilter > 0)
            {
                notes.Add(hiddenByFilter.ToString(CultureInfo.InvariantCulture) + " Kostenträger sind im Diagramm ausgeblendet (Kostenträger-Filter).");
            }
            if (mode == ChartMode.Percent && withoutValue > 0)
                notes.Add(withoutValue.ToString(CultureInfo.InvariantCulture) + " Zeile(n) ohne definierten Stand (kein positiver Plan oder Ist nicht geliefert) fehlen im Diagramm.");
            if (mode == ChartMode.Hours && showComputedHours && (withoutRate > 0 || withoutValue > 0))
            {
                var skipped = withoutRate + withoutValue;
                notes.Add(skipped.ToString(CultureInfo.InvariantCulture) + " Zeile(n) fehlen: kein eindeutiger Stundensatz (Satz eintragen oder uneinheitlich) oder Ist nicht geliefert.");
            }
            if (mode == ChartMode.Hours && !showComputedHours)
                notes.Add("Berechnete Stunden sind ausgeblendet („Berechnete Stunden (h) zeigen“). Es erscheinen nur lokal gebuchte Stunden der gewählten Zeile.");
            if (mode == ChartMode.Hours && showComputedHours && selected == null)
                notes.Add("Wähle eine Zeile, um zusätzlich ihren Plan in Stunden und ihre lokal gebuchten Stunden zu sehen.");
            if (mode == ChartMode.Percent && selected != null && selected.Row.HourlyRate.HasValue && showComputedHours)
                notes.Add("Die Linie „Lokal gebucht“ setzt die gebuchten Stunden ins Verhältnis zum Plan in Stunden (Plan ÷ Stundensatz des neuesten Stands).");
            notes.Add("Ist und Plan sind SAP-Stände (kumuliert bis zum jeweiligen SAP-Datum); die Punkte sind die importierten Stände, dazwischen verbindet die Linie nur. Umgerechnete Stunden sind eine Schätzung aus Geld, lokale Stunden stammen aus deinen Buchungen.");

            if (!data.HasData)
            {
                data.EmptyText = EmptyReason(rows.Count, mode, showComputedHours, withoutRate, withoutValue, hiddenByFilter);
            }

            return data;
        }

        // ---------- Reihen ----------

        private sealed class Candidate
        {
            public Candidate(ChartRowInput input, ChartSeries series, double latest)
            {
                Input = input;
                Series = series;
                Latest = latest;
            }

            public ChartRowInput Input { get; }
            public ChartSeries Series { get; }
            public double Latest { get; }
        }

        private static double LatestValue(ChartSeries series)
        {
            var last = series.Points.LastOrDefault();
            return last == null ? double.NegativeInfinity : last.Y;
        }

        private static ChartSeries PercentSeries(ChartRowInput input)
        {
            var row = input.Row;
            var series = new ChartSeries
            {
                Name = row.CarrierText,
                Kind = ChartSeriesKind.Actual,
                RowKey = row.CarrierKey,
                ShowMarkers = true
            };

            List<ChartPoint> segment = null;
            foreach (var o in input.History)
            {
                if (!o.Consumption.HasValue)
                {
                    segment = null;   // unbekannt: Linie reißt ab, kein Punkt bei 0
                    continue;
                }

                var y = (double)(o.Consumption.Value * 100m);
                var tip = row.CarrierText + "\n" + o.SourceTimestamp.ToString("dd.MM.yyyy HH:mm", German) + "\nStand " + y.ToString("N1", German) + " % · Ist " +
                          CostMath.FormatMoney(o.ActualCost) + " von Plan " + CostMath.FormatMoney(o.PlannedCost);
                if (segment == null)
                {
                    segment = new List<ChartPoint>();
                    series.Segments.Add(segment);
                }
                segment.Add(new ChartPoint(o.SourceTimestamp, y, tip, y.ToString("N1", German) + " %"));
            }

            return series;
        }

        private static ChartSeries HoursSeries(ChartRowInput input, decimal rate, ChartSeriesKind kind)
        {
            var row = input.Row;
            var isPlan = kind == ChartSeriesKind.Plan;
            var series = new ChartSeries
            {
                Name = row.CarrierText + (isPlan ? " – Plan (h, umger.)" : " – Ist (h, umger.)"),
                Kind = kind,
                RowKey = row.CarrierKey,
                ShowMarkers = true,
                Dashed = isPlan
            };

            List<ChartPoint> segment = null;
            foreach (var o in input.History)
            {
                var amount = isPlan ? o.PlannedCost : o.ActualCost;
                var hours = CostMath.ToHours(amount, rate);
                if (!hours.HasValue)
                {
                    segment = null;
                    continue;
                }

                var y = (double)hours.Value;
                var tip = row.CarrierText + (isPlan ? " – Plan" : " – Ist") + "\n" + o.SourceTimestamp.ToString("dd.MM.yyyy HH:mm", German) + "\n" +
                          y.ToString("N2", German) + " h (umgerechnet: " + CostMath.FormatMoney(amount) + " ÷ " + CostMath.FormatRate(rate) + " pro Stunde)";
                if (segment == null)
                {
                    segment = new List<ChartPoint>();
                    series.Segments.Add(segment);
                }
                segment.Add(new ChartPoint(o.SourceTimestamp, y, tip, y.ToString("N2", German) + " h"));
            }

            return series;
        }

        private static void LocalBookedSeries(ChartData data, ChartRowInput selected, ChartMode mode, bool showComputedHours, IReadOnlyList<LocalHoursPoint> local)
        {
            if (selected == null || local.Count == 0) return;

            double divisor = 1.0;
            string name;
            if (mode == ChartMode.Percent)
            {
                // Relativ: gebuchte Stunden im Verhältnis zum Plan in Stunden (Plan ÷ Stundensatz des neuesten Stands mit Plan)
                var rate = selected.Row.HourlyRate;
                if (!showComputedHours || !rate.HasValue) return;
                var latestPlan = selected.History.LastOrDefault(o => o.PlannedCost.HasValue && o.PlannedCost.Value > 0m);
                if (latestPlan == null) return;
                var planHours = (double)(latestPlan.PlannedCost.Value / rate.Value);
                if (planHours <= 0) return;
                divisor = planHours;
                name = "Lokal gebucht (kumuliert, in % des Plans in Stunden)";
            }
            else
            {
                name = "Lokal gebucht (kumuliert)";
            }

            var series = new ChartSeries
            {
                Name = name,
                Kind = ChartSeriesKind.LocalBooked,
                RowKey = selected.Row.CarrierKey,
                ColorIndex = -1,
                IsSelected = true,
                ShowMarkers = false
            };

            var segment = new List<ChartPoint>();
            foreach (var p in local)
            {
                var y = mode == ChartMode.Percent ? p.CumulativeHours / divisor * 100.0 : p.CumulativeHours;
                var tip = "Lokal gebucht\nbis " + p.Date.ToString("dd.MM.yyyy", German) + "\n" + p.CumulativeHours.ToString("N2", German) + " h" +
                          (mode == ChartMode.Percent ? " = " + y.ToString("N1", German) + " % des Plans in Stunden" : string.Empty) + "\n(aus deinen Buchungen, nicht aus SAP)";
                var valueText = mode == ChartMode.Percent
                    ? y.ToString("N1", German) + " % (" + p.CumulativeHours.ToString("N2", German) + " h)"
                    : p.CumulativeHours.ToString("N2", German) + " h";
                segment.Add(new ChartPoint(p.Date, y, tip, valueText));
            }
            series.Segments.Add(segment);
            data.Series.Add(series);
        }

        // ---------- Achsen ----------

        private static void FinishAxes(ChartData data)
        {
            var points = data.Series.Where(s => s.Kind != ChartSeriesKind.Reference).SelectMany(s => s.Points).ToList();
            if (points.Count == 0)
            {
                data.XMin = DateTime.Today.AddDays(-3);
                data.XMax = DateTime.Today.AddDays(3);
                data.YMin = 0;
                data.YMax = data.Mode == ChartMode.Percent ? 100 : 10;
                data.YStep = data.Mode == ChartMode.Percent ? 20 : 2;
                return;
            }

            var xMin = points.Min(p => p.X);
            var xMax = points.Max(p => p.X);
            if (xMax <= xMin)
            {
                // Ein einzelner Stand: ein einzelner Punkt in der Mitte, keine erfundene Linie davor oder danach
                data.XMin = xMin.AddDays(-4);
                data.XMax = xMax.AddDays(4);
            }
            else
            {
                var pad = TimeSpan.FromTicks((xMax - xMin).Ticks / 25);
                data.XMin = xMin - pad;
                data.XMax = xMax + pad;
            }

            var yMin = Math.Min(0.0, points.Min(p => p.Y));
            var yMax = points.Max(p => p.Y);
            if (data.Mode == ChartMode.Percent) yMax = Math.Max(yMax, 100.0);   // 100 % bleibt immer sichtbar
            if (yMax <= yMin) yMax = yMin + 1;

            double niceMin, niceMax, step;
            ChartAxis.NiceRange(yMin, yMax * 1.04, 5, out niceMin, out niceMax, out step);
            data.YMin = niceMin;
            data.YMax = niceMax;
            data.YStep = step;

            if (data.Mode == ChartMode.Percent)
            {
                var reference = new ChartSeries
                {
                    Name = "Plan = 100 %",
                    Kind = ChartSeriesKind.Reference,
                    ColorIndex = -1,
                    Dashed = true
                };
                reference.Segments.Add(new List<ChartPoint>
                {
                    new ChartPoint(data.XMin, 100, "Plan = 100 %", "100 %"),
                    new ChartPoint(data.XMax, 100, "Plan = 100 %", "100 %")
                });
                data.Series.Insert(0, reference);
            }
        }

        private static string EmptyReason(int rowCount, ChartMode mode, bool showComputedHours, int withoutRate, int withoutValue, int hiddenByFilter)
        {
            if (rowCount == 0 && hiddenByFilter > 0) return "Alle Kostenträger sind im Diagramm ausgeblendet. Im Kostenträger-Filter wieder einblenden.";
            if (rowCount == 0) return "Keine Zeilen zum Zeichnen. Filter zurücksetzen oder einen Import wählen.";
            if (mode == ChartMode.Percent)
                return "Kein Stand zu zeichnen: Für keine Zeile ist der Verbrauch definiert (es braucht einen positiven Plan und ein gemeldetes Ist).";
            if (!showComputedHours)
                return "Berechnete Stunden sind ausgeblendet. Mit „Berechnete Stunden (h) zeigen“ einblenden oder eine Zeile mit lokalen Buchungen wählen.";
            if (withoutRate > 0)
                return "Für Stunden braucht jede Zeile einen eindeutigen Stundensatz. Trage ihn in den Details beim Projekt ein.";
            return "Keine Stunden zu zeichnen: Ist wurde nicht geliefert.";
        }

        // ---------- Lokale Stunden ----------

        /// <summary>
        /// Kumulierte lokal gebuchte Stunden je Tag (bei langen Zeiträumen je Woche). Beginnt mit 0 am Tag vor der ersten Buchung,
        /// jede Buchung zählt genau einmal. Leer, wenn es keine lesbaren Buchungen gibt.
        /// </summary>
        public static List<LocalHoursPoint> LocalCumulative(IEnumerable<LocalTimeEntry> entries)
        {
            var list = (entries ?? Enumerable.Empty<LocalTimeEntry>()).ToList();
            var days = LocalHoursCalculator.Calculate(list, HoursBucketKind.Day);
            if (days.EntryCount == 0) return new List<LocalHoursPoint>();

            var result = LocalHoursCalculator.Calculate(list, days.Buckets.Count > 400 ? HoursBucketKind.Week : HoursBucketKind.Day);
            var points = new List<LocalHoursPoint>();
            var first = result.Buckets.First();
            points.Add(new LocalHoursPoint(first.Start.AddDays(-1), 0));
            foreach (var b in result.Buckets) points.Add(new LocalHoursPoint(b.End, b.CumulativeHours));
            return points;
        }
    }

    /// <summary>Gleichmäßige Achsenteilung ("schöne" Schritte wie 1, 2, 2,5, 5 × 10^n) und Zeitmarken.</summary>
    public static class ChartAxis
    {
        public sealed class TimeTick
        {
            public TimeTick(DateTime value, string label)
            {
                Value = value;
                Label = label;
            }

            public DateTime Value { get; }
            public string Label { get; }
        }

        public static double NiceStep(double rawStep)
        {
            if (double.IsNaN(rawStep) || double.IsInfinity(rawStep) || rawStep <= 0) return 1;

            var exponent = Math.Floor(Math.Log10(rawStep));
            var magnitude = Math.Pow(10, exponent);
            var fraction = rawStep / magnitude;
            double nice;
            if (fraction <= 1.0) nice = 1;
            else if (fraction <= 2.0) nice = 2;
            else if (fraction <= 2.5) nice = 2.5;
            else if (fraction <= 5.0) nice = 5;
            else nice = 10;
            return nice * magnitude;
        }

        public static void NiceRange(double min, double max, int targetTicks, out double niceMin, out double niceMax, out double step)
        {
            if (max <= min) max = min + 1;
            step = NiceStep((max - min) / Math.Max(1, targetTicks));
            niceMin = Math.Floor(min / step + 1e-9) * step;
            niceMax = Math.Ceiling(max / step - 1e-9) * step;
            if (niceMax <= niceMin) niceMax = niceMin + step;
        }

        /// <summary>Teilstriche von <paramref name="min"/> bis <paramref name="max"/>; per Index berechnet, damit sich keine Rundungsfehler aufsummieren.</summary>
        public static List<double> Ticks(double min, double max, double step)
        {
            var ticks = new List<double>();
            if (step <= 0) return ticks;

            var count = (int)Math.Round((max - min) / step);
            for (var i = 0; i <= count && i < 1000; i++) ticks.Add(Math.Round(min + i * step, 10));
            return ticks;
        }

        public static string FormatTick(double value, double step, ChartMode mode)
        {
            var text = step >= 1 ? value.ToString("N0", CostMath.German) : value.ToString("0.0", CostMath.German);
            return mode == ChartMode.Percent ? text + " %" : text + " h";
        }

        /// <summary>
        /// Zeitmarken im Bereich: tageweise, wöchentlich (montags), monatlich, vierteljährlich/halbjährlich oder jährlich –
        /// der feinste Schritt mit höchstens <paramref name="target"/> Marken.
        /// </summary>
        public static List<TimeTick> TimeTicks(DateTime min, DateTime max, int target)
        {
            if (max <= min) return new List<TimeTick> { new TimeTick(min, min.ToString("dd.MM.", CostMath.German)) };

            var steps = new[]
            {
                new TickStep('d', 1), new TickStep('d', 2), new TickStep('w', 1), new TickStep('w', 2),
                new TickStep('m', 1), new TickStep('m', 2), new TickStep('m', 3), new TickStep('m', 6),
                new TickStep('y', 1), new TickStep('y', 2), new TickStep('y', 5), new TickStep('y', 10)
            };

            List<TimeTick> best = null;
            foreach (var step in steps)
            {
                var ticks = Generate(min, max, step);
                best = ticks;
                if (ticks.Count <= Math.Max(2, target)) break;
            }

            if (best == null || best.Count == 0) return new List<TimeTick> { new TimeTick(min, min.ToString("dd.MM.", CostMath.German)) };
            return best;
        }

        private struct TickStep
        {
            public TickStep(char unit, int count)
            {
                Unit = unit;
                Count = count;
            }

            public char Unit { get; }
            public int Count { get; }
        }

        private static List<TimeTick> Generate(DateTime min, DateTime max, TickStep step)
        {
            var ticks = new List<TimeTick>();
            var culture = CostMath.German;

            switch (step.Unit)
            {
                case 'd':
                case 'w':
                    {
                        var start = min.Date < min ? min.Date.AddDays(1) : min.Date;
                        if (step.Unit == 'w')
                        {
                            var shift = ((int)start.DayOfWeek + 6) % 7;            // bis zum nächsten Montag
                            if (shift != 0) start = start.AddDays(7 - shift);
                        }
                        var days = step.Unit == 'w' ? 7 * step.Count : step.Count;
                        for (var t = start; t <= max && ticks.Count < 500; t = t.AddDays(days))
                            ticks.Add(new TimeTick(t, t.ToString("dd.MM.", culture)));
                        break;
                    }
                case 'm':
                    {
                        var t = new DateTime(min.Year, min.Month, 1);
                        if (t < min) t = t.AddMonths(1);
                        while (((t.Year * 12 + t.Month - 1) % step.Count) != 0) t = t.AddMonths(1);   // an Kalendergrenzen ausrichten
                        for (; t <= max && ticks.Count < 500; t = t.AddMonths(step.Count))
                            ticks.Add(new TimeTick(t, t.ToString("MMM yy", culture)));
                        break;
                    }
                default:
                    {
                        var t = new DateTime(min.Year, 1, 1);
                        if (t < min) t = t.AddYears(1);
                        while (t.Year % step.Count != 0) t = t.AddYears(1);
                        for (; t <= max && ticks.Count < 500; t = t.AddYears(step.Count))
                            ticks.Add(new TimeTick(t, t.ToString("yyyy", culture)));
                        break;
                    }
            }

            return ticks;
        }
    }
}
