using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using Zeitmanagement.Helpers;

namespace Zeitmanagement.Views
{
    /// <summary>
    /// X-Y-Diagramm für den Stand je PSP-Element über die SAP-Daten (Zeit auf X, Prozent oder Stunden auf Y).
    /// Gezeichnet wird direkt (keine Fremdbibliothek) mit den Farben des aktiven Themes; die Werte und Reihen bestimmt
    /// <see cref="ControllingChartBuilder"/>.
    ///
    /// Beim Überfahren erscheint der aktuelle Stand: Direkt an einem Punkt dessen Wert samt Herkunft, daneben am nächsten
    /// SAP-Datum eine Senkrechte mit den Werten aller Linien zu diesem Datum. Ein Klick auf einen Punkt oder die Legende
    /// wählt die zugehörige Zeile.
    /// </summary>
    public sealed class XyChart : FrameworkElement
    {
        public static readonly DependencyProperty DataProperty = DependencyProperty.Register(
            nameof(Data), typeof(ChartData), typeof(XyChart),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, (d, e) => ((XyChart)d).OnDataChanged()));

        /// <summary>Wird mit dem Kostenträger-Schlüssel (string) der angeklickten Reihe ausgeführt.</summary>
        public static readonly DependencyProperty SeriesClickedCommandProperty = DependencyProperty.Register(
            nameof(SeriesClickedCommand), typeof(ICommand), typeof(XyChart), new PropertyMetadata(null));

        public ChartData Data
        {
            get { return (ChartData)GetValue(DataProperty); }
            set { SetValue(DataProperty, value); }
        }

        public ICommand SeriesClickedCommand
        {
            get { return (ICommand)GetValue(SeriesClickedCommandProperty); }
            set { SetValue(SeriesClickedCommandProperty, value); }
        }

        private static readonly CultureInfo German = CultureInfo.GetCultureInfo("de-DE");
        private const double PointHitRadius = 14;
        private const double ColumnHitDistance = 28;
        private const int MaxLegendRows = 3;
        private const int MaxTooltipLines = 14;

        private sealed class Hit
        {
            public ChartSeries Series;
            public ChartPoint Point;
            public Point Position;
        }

        private sealed class LegendHit
        {
            public ChartSeries Series;
            public Rect Bounds;
        }

        private readonly List<Hit> _hits = new List<Hit>();
        private readonly List<LegendHit> _legendHits = new List<LegendHit>();
        private Rect _plot = Rect.Empty;
        private Hit _hoverHit;
        private DateTime? _hoverColumn;
        private readonly ToolTip _tip = new ToolTip { Placement = PlacementMode.Relative, StaysOpen = true, IsHitTestVisible = false };
        private double _pixelsPerDip = 1.0;

        public XyChart()
        {
            Focusable = false;
            SnapsToDevicePixels = true;
            ClipToBounds = true;
            Loaded += (s, e) => ThemeManager.ThemeChanged += OnThemeChanged;
            Unloaded += (s, e) =>
            {
                ThemeManager.ThemeChanged -= OnThemeChanged;
                CloseTip();
            };
        }

        private void OnThemeChanged(object sender, EventArgs e)
        {
            Dispatcher.BeginInvoke(new Action(InvalidateVisual));
        }

        private void OnDataChanged()
        {
            _hoverHit = null;
            _hoverColumn = null;
            CloseTip();
        }

        // ---------- Farben und Text ----------

        private Brush Res(string key)
        {
            return TryFindResource(key) as Brush ?? Brushes.Gray;
        }

        private Brush SeriesBrush(ChartSeries series)
        {
            return series.ColorIndex >= 0
                ? Res("Theme.Data" + (series.ColorIndex % ControllingChartBuilder.PaletteSize).ToString(CultureInfo.InvariantCulture))
                : Res("Theme.DataNeutral");
        }

        private FormattedText Txt(string text, Brush brush, double size, bool semiBold = false)
        {
            return new FormattedText(
                text ?? string.Empty, German, FlowDirection.LeftToRight,
                new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, semiBold ? FontWeights.SemiBold : FontWeights.Normal, FontStretches.Normal),
                size, brush, _pixelsPerDip);
        }

        private static string Truncate(string text, int max)
        {
            if (string.IsNullOrEmpty(text) || text.Length <= max) return text;
            return text.Substring(0, max - 1) + "…";
        }

        // ---------- Zeichnen ----------

        protected override void OnRender(DrawingContext dc)
        {
            base.OnRender(dc);

            var width = ActualWidth;
            var height = ActualHeight;
            dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, width, height));   // damit die Maus überall ankommt

            _hits.Clear();
            _legendHits.Clear();
            _plot = Rect.Empty;
            _pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;

            if (width < 180 || height < 120) return;

            var data = Data;
            var muted = Res("Theme.TextMuted");
            var text = Res("Theme.Text");
            var divider = Res("Theme.Divider");

            if (data == null || !data.HasData)
            {
                var message = Txt(data == null || string.IsNullOrEmpty(data.EmptyText) ? "Noch keine Daten zum Zeichnen." : data.EmptyText, muted, 13);
                message.MaxTextWidth = Math.Min(width - 40, 520);
                message.TextAlignment = TextAlignment.Center;
                dc.DrawText(message, new Point((width - message.MaxTextWidth) / 2, Math.Max(10, (height - message.Height) / 2)));
                return;
            }

            // Legende (unten) vorab vermessen, damit die Zeichenfläche den Rest bekommt
            var legendItems = data.Series.Where(s => s.Kind != ChartSeriesKind.Reference)
                                         .Concat(data.Series.Where(s => s.Kind == ChartSeriesKind.Reference)).ToList();
            var legend = LayoutLegend(legendItems, width - 24, text);
            var legendHeight = legend.Rows * 20 + 6;

            // Y-Teilung und Beschriftungen
            var yTicks = ChartAxis.Ticks(data.YMin, data.YMax, data.YStep);
            var yLabels = yTicks.Select(t => Txt(ChartAxis.FormatTick(t, data.YStep, data.Mode), muted, 11)).ToList();
            var labelWidth = yLabels.Count == 0 ? 30 : yLabels.Max(l => l.Width);

            var plot = new Rect(12 + labelWidth + 8, 24, width - (12 + labelWidth + 8) - 18, height - 24 - legendHeight - 28);
            if (plot.Width < 60 || plot.Height < 40) return;
            _plot = plot;

            dc.DrawText(Txt(data.YTitle, muted, 11), new Point(12, 4));

            Func<DateTime, double> mapX = t => plot.Left + (t - data.XMin).Ticks / (double)Math.Max(1, (data.XMax - data.XMin).Ticks) * plot.Width;
            Func<double, double> mapY = v => plot.Bottom - (v - data.YMin) / Math.Max(1e-9, data.YMax - data.YMin) * plot.Height;

            var gridPen = new Pen(divider, 1);
            var axisPen = new Pen(Res("Theme.CardBorder"), 1);
            gridPen.Freeze();
            axisPen.Freeze();

            // waagerechte Gitterlinien mit Beschriftung
            for (var i = 0; i < yTicks.Count; i++)
            {
                var y = Math.Round(mapY(yTicks[i])) + 0.5;
                dc.DrawLine(Math.Abs(yTicks[i]) < 1e-9 ? axisPen : gridPen, new Point(plot.Left, y), new Point(plot.Right, y));
                var label = yLabels[i];
                dc.DrawText(label, new Point(plot.Left - 8 - label.Width, y - label.Height / 2));
            }

            // Zeitachse
            var xTicks = ChartAxis.TimeTicks(data.XMin, data.XMax, Math.Max(2, (int)(plot.Width / 90)));
            var lastLabelRight = double.NegativeInfinity;
            foreach (var tick in xTicks)
            {
                var x = Math.Round(mapX(tick.Value)) + 0.5;
                dc.DrawLine(gridPen, new Point(x, plot.Top), new Point(x, plot.Bottom));
                var label = Txt(tick.Label, muted, 11);
                var left = x - label.Width / 2;
                if (left > lastLabelRight + 6 && left >= 0 && left + label.Width <= width)
                {
                    dc.DrawText(label, new Point(left, plot.Bottom + 5));
                    lastLabelRight = left + label.Width;
                }
            }
            dc.DrawLine(axisPen, new Point(plot.Left, plot.Bottom + 0.5), new Point(plot.Right, plot.Bottom + 0.5));

            // Reihen: Hilfslinien unten, die gewählte Zeile zuoberst
            var drawOrder = data.Series.Where(s => s.Kind == ChartSeriesKind.Reference)
                .Concat(data.Series.Where(s => s.Kind != ChartSeriesKind.Reference && !s.IsSelected))
                .Concat(data.Series.Where(s => s.Kind != ChartSeriesKind.Reference && s.IsSelected)).ToList();

            var outline = Res("Theme.Card");
            foreach (var series in drawOrder)
                DrawSeries(dc, series, mapX, mapY, outline);

            // Führungslinie und Ring des überfahrenen Stands
            if (_hoverColumn.HasValue)
            {
                var x = Math.Round(mapX(_hoverColumn.Value)) + 0.5;
                var guide = new Pen(text, 1) { DashStyle = new DashStyle(new[] { 3.0, 3.0 }, 0) };
                guide.Freeze();
                dc.DrawLine(guide, new Point(x, plot.Top), new Point(x, plot.Bottom));
            }
            if (_hoverHit != null)
            {
                var ringPen = new Pen(text, 2);
                ringPen.Freeze();
                dc.DrawEllipse(null, ringPen, _hoverHit.Position, 8, 8);
            }

            DrawLegend(dc, legend, text, muted);
        }

        private void DrawSeries(DrawingContext dc, ChartSeries series, Func<DateTime, double> mapX, Func<double, double> mapY, Brush outline)
        {
            var brush = SeriesBrush(series);
            var thickness = series.Kind == ChartSeriesKind.Reference ? 1.25 : (series.IsSelected ? 2.75 : 1.75);
            var pen = new Pen(brush, thickness) { LineJoin = PenLineJoin.Round, StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
            if (series.Dashed) pen.DashStyle = new DashStyle(new[] { 4.0, 3.0 }, 0);
            pen.Freeze();

            foreach (var segment in series.Segments)
            {
                if (segment.Count >= 2)
                {
                    var geometry = new StreamGeometry();
                    using (var ctx = geometry.Open())
                    {
                        ctx.BeginFigure(new Point(mapX(segment[0].X), mapY(segment[0].Y)), false, false);
                        for (var i = 1; i < segment.Count; i++)
                            ctx.LineTo(new Point(mapX(segment[i].X), mapY(segment[i].Y)), true, true);
                    }
                    geometry.Freeze();
                    dc.DrawGeometry(null, pen, geometry);
                }
            }

            if (series.Kind == ChartSeriesKind.Reference) return;

            foreach (var segment in series.Segments)
            {
                foreach (var point in segment)
                {
                    var position = new Point(mapX(point.X), mapY(point.Y));
                    _hits.Add(new Hit { Series = series, Point = point, Position = position });
                    if (series.ShowMarkers) DrawMarker(dc, position, series.ColorIndex, series.IsSelected ? 4 : 3.2, brush, outline);
                }
            }
        }

        /// <summary>Marker mit wechselnder Form je Reihe (Kreis, Quadrat, Raute, Dreieck): Die Reihen sind nicht allein an der Farbe zu unterscheiden.</summary>
        private static void DrawMarker(DrawingContext dc, Point c, int colorIndex, double r, Brush fill, Brush outline)
        {
            var pen = new Pen(outline, 1.25);
            pen.Freeze();
            switch (colorIndex < 0 ? 0 : colorIndex % 4)
            {
                case 1:
                    dc.DrawRectangle(fill, pen, new Rect(c.X - r, c.Y - r, 2 * r, 2 * r));
                    break;
                case 2:
                    DrawPolygon(dc, fill, pen, new Point(c.X, c.Y - r * 1.3), new Point(c.X + r * 1.3, c.Y), new Point(c.X, c.Y + r * 1.3), new Point(c.X - r * 1.3, c.Y));
                    break;
                case 3:
                    DrawPolygon(dc, fill, pen, new Point(c.X, c.Y - r * 1.25), new Point(c.X + r * 1.2, c.Y + r), new Point(c.X - r * 1.2, c.Y + r));
                    break;
                default:
                    dc.DrawEllipse(fill, pen, c, r, r);
                    break;
            }
        }

        private static void DrawPolygon(DrawingContext dc, Brush fill, Pen pen, params Point[] points)
        {
            var geometry = new StreamGeometry();
            using (var ctx = geometry.Open())
            {
                ctx.BeginFigure(points[0], true, true);
                for (var i = 1; i < points.Length; i++) ctx.LineTo(points[i], true, false);
            }
            geometry.Freeze();
            dc.DrawGeometry(fill, pen, geometry);
        }

        // ---------- Legende ----------

        private sealed class LegendLayout
        {
            public int Rows;
            public List<Tuple<ChartSeries, FormattedText, Point>> Items = new List<Tuple<ChartSeries, FormattedText, Point>>();
            public int Hidden;
        }

        private LegendLayout LayoutLegend(List<ChartSeries> series, double maxWidth, Brush text)
        {
            var layout = new LegendLayout { Rows = 1 };
            var x = 0.0;
            var row = 0;

            foreach (var s in series)
            {
                var label = Txt(Truncate(s.Name, 38), text, 11, s.IsSelected);
                var itemWidth = 26 + label.Width + 16;

                if (x > 0 && x + itemWidth > maxWidth)
                {
                    row++;
                    x = 0;
                }
                if (row >= MaxLegendRows)
                {
                    layout.Hidden++;
                    continue;
                }

                layout.Items.Add(Tuple.Create(s, label, new Point(x, row)));
                x += itemWidth;
                layout.Rows = Math.Max(layout.Rows, row + 1);
            }

            return layout;
        }

        private void DrawLegend(DrawingContext dc, LegendLayout legend, Brush text, Brush muted)
        {
            var top = ActualHeight - legend.Rows * 20 - 2;
            var outline = Res("Theme.Card");

            foreach (var item in legend.Items)
            {
                var series = item.Item1;
                var label = item.Item2;
                var x = 12 + item.Item3.X;
                var y = top + item.Item3.Y * 20 + 10;

                var brush = SeriesBrush(series);
                var pen = new Pen(brush, series.IsSelected ? 2.75 : 1.75);
                if (series.Dashed) pen.DashStyle = new DashStyle(new[] { 4.0, 3.0 }, 0);
                dc.DrawLine(pen, new Point(x, y), new Point(x + 18, y));
                if (series.ShowMarkers) DrawMarker(dc, new Point(x + 9, y), series.ColorIndex, 3.2, brush, outline);

                dc.DrawText(label, new Point(x + 24, y - label.Height / 2));

                if (series.RowKey != null)
                    _legendHits.Add(new LegendHit { Series = series, Bounds = new Rect(x - 2, y - 10, 24 + label.Width + 6, 20) });
            }

            if (legend.Hidden > 0)
            {
                var more = Txt("+" + legend.Hidden.ToString(CultureInfo.InvariantCulture) + " weitere", muted, 11);
                dc.DrawText(more, new Point(ActualWidth - 12 - more.Width, top + 4));
            }
        }

        // ---------- Maus: aktueller Stand am überfahrenen Punkt ----------

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            UpdateHover(e.GetPosition(this));
        }

        protected override void OnMouseLeave(MouseEventArgs e)
        {
            base.OnMouseLeave(e);
            ClearHover();
        }

        private void ClearHover()
        {
            var changed = _hoverHit != null || _hoverColumn.HasValue;
            _hoverHit = null;
            _hoverColumn = null;
            Cursor = null;
            CloseTip();
            if (changed) InvalidateVisual();
        }

        private void UpdateHover(Point mouse)
        {
            if (Data == null || _plot.IsEmpty)
            {
                ClearHover();
                return;
            }

            // Legende: nur Hand-Cursor, kein Tooltip
            if (_legendHits.Any(l => l.Bounds.Contains(mouse)))
            {
                var hadHover = _hoverHit != null || _hoverColumn.HasValue;
                _hoverHit = null;
                _hoverColumn = null;
                CloseTip();
                Cursor = Cursors.Hand;
                if (hadHover) InvalidateVisual();
                return;
            }

            if (!_plot.Contains(mouse))
            {
                ClearHover();
                return;
            }

            Hit best = null;
            var bestDistance = PointHitRadius * PointHitRadius;
            foreach (var hit in _hits)
            {
                var dx = hit.Position.X - mouse.X;
                var dy = hit.Position.Y - mouse.Y;
                var d = dx * dx + dy * dy;
                if (d < bestDistance || (best != null && Math.Abs(d - bestDistance) < 1e-9 && hit.Series.IsSelected))
                {
                    best = hit;
                    bestDistance = d;
                }
            }

            string tooltip = null;
            DateTime? column = null;

            if (best != null)
            {
                tooltip = best.Point.Tip;
            }
            else
            {
                // Kein Punkt in der Nähe: nächstes SAP-Datum (Stand-Spalte) mit den Werten aller Linien
                var data = Data;
                var xs = data.Series.Where(s => s.Kind == ChartSeriesKind.Actual || s.Kind == ChartSeriesKind.Plan)
                                    .SelectMany(s => s.Points).Select(p => p.X).Distinct().ToList();
                DateTime? nearest = null;
                var nearestDistance = ColumnHitDistance;
                foreach (var x in xs)
                {
                    var px = _plot.Left + (x - data.XMin).Ticks / (double)Math.Max(1, (data.XMax - data.XMin).Ticks) * _plot.Width;
                    var d = Math.Abs(px - mouse.X);
                    if (d < nearestDistance)
                    {
                        nearest = x;
                        nearestDistance = d;
                    }
                }

                if (nearest.HasValue)
                {
                    column = nearest;
                    tooltip = ColumnText(data, nearest.Value);
                }
            }

            var changed = !ReferenceEquals(_hoverHit, best) || _hoverColumn != column;
            _hoverHit = best;
            _hoverColumn = column;
            Cursor = best != null && best.Series.RowKey != null ? Cursors.Hand : null;

            if (tooltip == null)
            {
                CloseTip();
            }
            else
            {
                ShowTip(tooltip, mouse);
            }

            if (changed) InvalidateVisual();
        }

        /// <summary>Werte aller Linien zu einem SAP-Datum; lokal gebuchte Stunden als Stand bis zu diesem Tag.</summary>
        private static string ColumnText(ChartData data, DateTime at)
        {
            var lines = new List<string> { "Stand zum " + at.ToString("dd.MM.yyyy HH:mm", German) };

            foreach (var s in data.Series.Where(s => s.Kind != ChartSeriesKind.Reference))
            {
                ChartPoint point = null;
                if (s.Kind == ChartSeriesKind.LocalBooked)
                    point = s.Points.LastOrDefault(p => p.X.Date <= at.Date);
                else
                    point = s.Points.FirstOrDefault(p => p.X == at);

                if (point == null || string.IsNullOrEmpty(point.ValueText)) continue;
                lines.Add("• " + s.Name + ": " + point.ValueText + (s.Kind == ChartSeriesKind.LocalBooked ? " (gebucht bis zu diesem Tag)" : string.Empty));
            }

            if (lines.Count > MaxTooltipLines + 1)
            {
                var hidden = lines.Count - 1 - MaxTooltipLines;
                lines = lines.Take(MaxTooltipLines + 1).ToList();
                lines.Add("… und " + hidden.ToString(CultureInfo.InvariantCulture) + " weitere");
            }

            return string.Join("\n", lines);
        }

        private void ShowTip(string text, Point mouse)
        {
            _tip.PlacementTarget = this;
            _tip.Content = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, MaxWidth = 420 };
            _tip.HorizontalOffset = mouse.X + 16;
            _tip.VerticalOffset = mouse.Y + 18;
            if (!_tip.IsOpen) _tip.IsOpen = true;
        }

        private void CloseTip()
        {
            if (_tip.IsOpen) _tip.IsOpen = false;
        }

        // ---------- Maus: Klick wählt die Zeile ----------

        protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
        {
            base.OnMouseLeftButtonDown(e);

            var mouse = e.GetPosition(this);
            string key = null;

            var legendHit = _legendHits.FirstOrDefault(l => l.Bounds.Contains(mouse));
            if (legendHit != null) key = legendHit.Series.RowKey;
            else if (_hoverHit != null) key = _hoverHit.Series.RowKey;

            var command = SeriesClickedCommand;
            if (key != null && command != null && command.CanExecute(key))
            {
                command.Execute(key);
                e.Handled = true;
            }
        }
    }
}
