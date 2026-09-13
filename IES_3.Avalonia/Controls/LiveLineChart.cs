using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using IES_2.Avalonia.Models;

namespace IES_2.Avalonia.Controls
{
    /// <summary>
    /// Minimal self-drawn rolling line chart for live ECU traces - a lightweight, dependency-free
    /// replacement for the WinForms build's ZedGraph control. Pull-based: the view model owns the
    /// data and calls InvalidateVisual() (indirectly, via GraphsViewModel.Redraw) when new samples
    /// arrive; this control just reads whatever is currently in Series each time it paints.
    /// </summary>
    public class LiveLineChart : Control
    {
        public static readonly StyledProperty<IReadOnlyList<ChartSeries>?> SeriesProperty =
            AvaloniaProperty.Register<LiveLineChart, IReadOnlyList<ChartSeries>?>(nameof(Series));

        public static readonly StyledProperty<double> WindowSecondsProperty =
            AvaloniaProperty.Register<LiveLineChart, double>(nameof(WindowSeconds), 15.0);

        public IReadOnlyList<ChartSeries>? Series
        {
            get => GetValue(SeriesProperty);
            set => SetValue(SeriesProperty, value);
        }

        public double WindowSeconds
        {
            get => GetValue(WindowSecondsProperty);
            set => SetValue(WindowSecondsProperty, value);
        }

        private static readonly IBrush GridBrush = new SolidColorBrush(Color.FromArgb(0x30, 0x80, 0x80, 0x80));
        private static readonly IBrush AxisTextBrush = new SolidColorBrush(Color.FromArgb(0xA0, 0x80, 0x80, 0x80));
        private static readonly Typeface AxisTypeface = new("Segoe UI");

        public override void Render(DrawingContext context)
        {
            base.Render(context);

            var bounds = Bounds;
            const double leftMargin = 48, rightMargin = 12, topMargin = 12, bottomMargin = 22;
            var plotRect = new Rect(leftMargin, topMargin,
                Math.Max(0, bounds.Width - leftMargin - rightMargin),
                Math.Max(0, bounds.Height - topMargin - bottomMargin));

            var series = Series;
            if (series == null || series.Count == 0 || plotRect.Width <= 0 || plotRect.Height <= 0)
            {
                DrawPlaceholder(context, bounds, "Nessun tracciato selezionato.");
                return;
            }

            double maxX = 0;
            bool any = false;
            foreach (var s in series)
                foreach (var p in s.Points)
                {
                    if (p.X > maxX) maxX = p.X;
                    any = true;
                }

            if (!any)
            {
                DrawPlaceholder(context, bounds, "In attesa di dati...");
                return;
            }

            double window = WindowSeconds;
            double minX = Math.Max(0, maxX - window);

            double minY = double.MaxValue, maxY = double.MinValue;
            foreach (var s in series)
                foreach (var p in s.Points)
                {
                    if (p.X < minX) continue;
                    if (p.Y < minY) minY = p.Y;
                    if (p.Y > maxY) maxY = p.Y;
                }
            if (minY > maxY) { minY = 0; maxY = 1; }
            if (Math.Abs(maxY - minY) < 0.0001) { minY -= 1; maxY += 1; }
            var pad = (maxY - minY) * 0.08;
            minY -= pad; maxY += pad;

            // Grid + Y axis labels (5 bands).
            for (int i = 0; i <= 4; i++)
            {
                double t = i / 4.0;
                double y = plotRect.Bottom - t * plotRect.Height;
                context.DrawLine(new Pen(GridBrush), new Point(plotRect.Left, y), new Point(plotRect.Right, y));
                double value = minY + t * (maxY - minY);
                var text = new FormattedText(value.ToString("0.##", CultureInfo.InvariantCulture),
                    CultureInfo.InvariantCulture, FlowDirection.LeftToRight, AxisTypeface, 10, AxisTextBrush);
                context.DrawText(text, new Point(2, y - text.Height / 2));
            }

            // X axis labels (seconds).
            for (int i = 0; i <= 3; i++)
            {
                double t = i / 3.0;
                double x = plotRect.Left + t * plotRect.Width;
                double seconds = minX + t * window;
                var text = new FormattedText(seconds.ToString("0", CultureInfo.InvariantCulture) + "s",
                    CultureInfo.InvariantCulture, FlowDirection.LeftToRight, AxisTypeface, 10, AxisTextBrush);
                context.DrawText(text, new Point(x - text.Width / 2, plotRect.Bottom + 4));
            }

            double PX(double x) => plotRect.Left + (x - minX) / window * plotRect.Width;
            double PY(double y) => plotRect.Bottom - (y - minY) / (maxY - minY) * plotRect.Height;

            using (context.PushClip(plotRect))
            {
                foreach (var s in series)
                {
                    if (s.Points.Count < 2) continue;
                    var geometry = new StreamGeometry();
                    using (var gc = geometry.Open())
                    {
                        bool started = false;
                        foreach (var p in s.Points)
                        {
                            if (p.X < minX - window) continue; // cheap skip well outside window
                            var pt = new Point(PX(p.X), PY(p.Y));
                            if (!started) { gc.BeginFigure(pt, false); started = true; }
                            else gc.LineTo(pt);
                        }
                    }
                    context.DrawGeometry(null, new Pen(new SolidColorBrush(s.Color), 2), geometry);
                }
            }

            // Legend.
            double lx = plotRect.Left + 6, ly = plotRect.Top + 6;
            foreach (var s in series)
            {
                context.FillRectangle(new SolidColorBrush(s.Color), new Rect(lx, ly, 10, 10));
                var text = new FormattedText(s.Name, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                    AxisTypeface, 11, AxisTextBrush);
                context.DrawText(text, new Point(lx + 14, ly - 1));
                ly += 16;
            }
        }

        private static void DrawPlaceholder(DrawingContext context, Rect bounds, string message)
        {
            var text = new FormattedText(message, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                AxisTypeface, 13, AxisTextBrush);
            context.DrawText(text, new Point((bounds.Width - text.Width) / 2, (bounds.Height - text.Height) / 2));
        }
    }
}
