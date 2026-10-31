using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace IES_2.Avalonia.Controls
{
    /// <summary>
    /// A 270° arc gauge: the track, the current value, and two small marks at the lowest and highest
    /// values seen this session. Self-drawn so a dashboard full of them stays cheap: it only repaints
    /// when one of its numbers changes.
    /// </summary>
    public class ArcGauge : Control
    {
        private const double StartAngle = 135;
        private const double SweepAngle = 270;

        public static readonly StyledProperty<double> ValueProperty =
            AvaloniaProperty.Register<ArcGauge, double>(nameof(Value));
        public static readonly StyledProperty<double> MinimumProperty =
            AvaloniaProperty.Register<ArcGauge, double>(nameof(Minimum));
        public static readonly StyledProperty<double> MaximumProperty =
            AvaloniaProperty.Register<ArcGauge, double>(nameof(Maximum), 100);
        public static readonly StyledProperty<double> SessionMinProperty =
            AvaloniaProperty.Register<ArcGauge, double>(nameof(SessionMin), double.NaN);
        public static readonly StyledProperty<double> SessionMaxProperty =
            AvaloniaProperty.Register<ArcGauge, double>(nameof(SessionMax), double.NaN);
        public static readonly StyledProperty<double> ThicknessProperty =
            AvaloniaProperty.Register<ArcGauge, double>(nameof(Thickness), 9);
        public static readonly StyledProperty<IBrush?> TrackBrushProperty =
            AvaloniaProperty.Register<ArcGauge, IBrush?>(nameof(TrackBrush));
        public static readonly StyledProperty<IBrush?> FillBrushProperty =
            AvaloniaProperty.Register<ArcGauge, IBrush?>(nameof(FillBrush));
        public static readonly StyledProperty<IBrush?> MarkerBrushProperty =
            AvaloniaProperty.Register<ArcGauge, IBrush?>(nameof(MarkerBrush));

        static ArcGauge()
        {
            AffectsRender<ArcGauge>(ValueProperty, MinimumProperty, MaximumProperty, SessionMinProperty,
                SessionMaxProperty, ThicknessProperty, TrackBrushProperty, FillBrushProperty, MarkerBrushProperty);
        }

        public double Value { get => GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
        public double Minimum { get => GetValue(MinimumProperty); set => SetValue(MinimumProperty, value); }
        public double Maximum { get => GetValue(MaximumProperty); set => SetValue(MaximumProperty, value); }
        public double SessionMin { get => GetValue(SessionMinProperty); set => SetValue(SessionMinProperty, value); }
        public double SessionMax { get => GetValue(SessionMaxProperty); set => SetValue(SessionMaxProperty, value); }
        public double Thickness { get => GetValue(ThicknessProperty); set => SetValue(ThicknessProperty, value); }
        public IBrush? TrackBrush { get => GetValue(TrackBrushProperty); set => SetValue(TrackBrushProperty, value); }
        public IBrush? FillBrush { get => GetValue(FillBrushProperty); set => SetValue(FillBrushProperty, value); }
        public IBrush? MarkerBrush { get => GetValue(MarkerBrushProperty); set => SetValue(MarkerBrushProperty, value); }

        public override void Render(DrawingContext context)
        {
            var size = Math.Min(Bounds.Width, Bounds.Height);
            var radius = size / 2 - Thickness / 2 - 1;
            if (radius <= 2) return;
            var center = new Point(Bounds.Width / 2, Bounds.Height / 2);

            if (TrackBrush != null)
                context.DrawGeometry(null, new Pen(TrackBrush, Thickness, lineCap: PenLineCap.Round), Arc(center, radius, 0, 1));

            var fraction = Fraction(Value);
            if (FillBrush != null && !double.IsNaN(fraction) && fraction > 0.002)
                context.DrawGeometry(null, new Pen(FillBrush, Thickness, lineCap: PenLineCap.Round), Arc(center, radius, 0, fraction));

            if (MarkerBrush != null)
            {
                var pen = new Pen(MarkerBrush, 2, lineCap: PenLineCap.Round);
                DrawMarker(context, pen, center, radius, Fraction(SessionMin));
                DrawMarker(context, pen, center, radius, Fraction(SessionMax));
            }
        }

        private double Fraction(double value)
        {
            if (double.IsNaN(value) || Maximum <= Minimum) return double.NaN;
            return Math.Clamp((value - Minimum) / (Maximum - Minimum), 0, 1);
        }

        private void DrawMarker(DrawingContext context, IPen pen, Point center, double radius, double fraction)
        {
            if (double.IsNaN(fraction)) return;
            var angle = (StartAngle + SweepAngle * fraction) * Math.PI / 180;
            var direction = new Vector(Math.Cos(angle), Math.Sin(angle));
            var inner = radius - Thickness / 2 - 4;
            var outer = radius + Thickness / 2 + 1;
            context.DrawLine(pen, center + direction * inner, center + direction * outer);
        }

        private static StreamGeometry Arc(Point center, double radius, double fromFraction, double toFraction)
        {
            var from = (StartAngle + SweepAngle * fromFraction) * Math.PI / 180;
            var to = (StartAngle + SweepAngle * toFraction) * Math.PI / 180;
            var geometry = new StreamGeometry();
            using var g = geometry.Open();
            g.BeginFigure(center + new Vector(Math.Cos(from), Math.Sin(from)) * radius, false);
            g.ArcTo(
                center + new Vector(Math.Cos(to), Math.Sin(to)) * radius,
                new Size(radius, radius),
                0,
                SweepAngle * (toFraction - fromFraction) > 180,
                SweepDirection.Clockwise);
            g.EndFigure(false);
            return geometry;
        }
    }
}
