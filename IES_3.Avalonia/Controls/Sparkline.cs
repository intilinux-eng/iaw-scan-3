using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using IES_2.Session;

namespace IES_2.Avalonia.Controls
{
    /// <summary>A small line of a trace's recent history, for dashboard tiles. Repaints when Stamp changes.</summary>
    public class Sparkline : Control
    {
        public static readonly StyledProperty<SeriesBuffer?> BufferProperty =
            AvaloniaProperty.Register<Sparkline, SeriesBuffer?>(nameof(Buffer));
        public static readonly StyledProperty<long> StampProperty =
            AvaloniaProperty.Register<Sparkline, long>(nameof(Stamp));
        public static readonly StyledProperty<double> SpanSecondsProperty =
            AvaloniaProperty.Register<Sparkline, double>(nameof(SpanSeconds), 60);
        public static readonly StyledProperty<IBrush?> StrokeProperty =
            AvaloniaProperty.Register<Sparkline, IBrush?>(nameof(Stroke));

        private readonly List<ChartPoint> _points = new();

        static Sparkline()
        {
            AffectsRender<Sparkline>(BufferProperty, StampProperty, SpanSecondsProperty, StrokeProperty);
        }

        public SeriesBuffer? Buffer { get => GetValue(BufferProperty); set => SetValue(BufferProperty, value); }
        public long Stamp { get => GetValue(StampProperty); set => SetValue(StampProperty, value); }
        public double SpanSeconds { get => GetValue(SpanSecondsProperty); set => SetValue(SpanSecondsProperty, value); }
        public IBrush? Stroke { get => GetValue(StrokeProperty); set => SetValue(StrokeProperty, value); }

        public override void Render(DrawingContext context)
        {
            var buffer = Buffer;
            var stroke = Stroke;
            var width = Bounds.Width;
            var height = Bounds.Height;
            if (buffer == null || stroke == null || width < 4 || height < 4) return;
            if (buffer.LastTime is not { } end) return;

            var start = end - SpanSeconds;
            buffer.Downsample(start, end, (int)Math.Max(8, width / 3), _points);

            double min = double.PositiveInfinity, max = double.NegativeInfinity;
            foreach (var p in _points)
            {
                if (p.IsGap || p.T < start) continue;
                if (p.Min < min) min = p.Min;
                if (p.Max > max) max = p.Max;
            }
            if (double.IsInfinity(min)) return;
            if (max - min < 1e-9)
            {
                min -= 1;
                max += 1;
            }

            double X(double t) => (t - start) / SpanSeconds * width;
            double Y(double v) => height - 1 - (v - min) / (max - min) * (height - 2);

            var geometry = new StreamGeometry();
            using (var g = geometry.Open())
            {
                var open = false;
                foreach (var p in _points)
                {
                    if (p.IsGap)
                    {
                        if (open) g.EndFigure(false);
                        open = false;
                        continue;
                    }
                    var x = Math.Max(0, X(p.T));
                    if (!open)
                    {
                        g.BeginFigure(new Point(x, Y(p.First)), false);
                        open = true;
                    }
                    else
                    {
                        g.LineTo(new Point(x, Y(p.First)));
                    }
                    if (p.Max > p.Min)
                    {
                        g.LineTo(new Point(x, Y(p.Min)));
                        g.LineTo(new Point(x, Y(p.Max)));
                        g.LineTo(new Point(x, Y(p.Last)));
                    }
                }
                if (open) g.EndFigure(false);
            }
            context.DrawGeometry(null, new Pen(stroke, 1.5, lineJoin: PenLineJoin.Round), geometry);
        }
    }
}
