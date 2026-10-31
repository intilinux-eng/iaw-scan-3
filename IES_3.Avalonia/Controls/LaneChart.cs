using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Input;
using Avalonia.Media;
using IES_2.Session;

namespace IES_2.Avalonia.Controls
{
    /// <summary>One line on a <see cref="LaneChart"/>, with an optional second session to compare against.</summary>
    public sealed class ChartTrace
    {
        public ChartTrace(string name, string unit, Color color, SeriesBuffer data, SeriesBuffer? compare = null)
        {
            Name = name;
            Unit = unit;
            Color = color;
            Data = data;
            Compare = compare;
            Pen = new Pen(new SolidColorBrush(color), 1.6, lineJoin: PenLineJoin.Round);
            ComparePen = new Pen(new SolidColorBrush(color, 0.45), 1.2, new DashStyle(new double[] { 3, 2 }, 0), lineJoin: PenLineJoin.Round);
            Dot = new SolidColorBrush(color);
        }

        public string Name { get; }
        public string Unit { get; }
        public Color Color { get; }
        public SeriesBuffer Data { get; }
        public SeriesBuffer? Compare { get; }
        internal IPen Pen { get; }
        internal IPen ComparePen { get; }
        internal IBrush Dot { get; }
    }

    /// <summary>
    /// The chart for live data and recorded sessions. Every trace gets its own lane with its own Y scale,
    /// so 6000 rpm doesn't flatten 14 V (or, in overlay mode, all share the plot, each still on its own
    /// scale, to compare shapes). Wheel zooms, dragging pans, the pointer reads values, double-click
    /// goes back to following the data. Drawing cost depends on the plot's width, not on how much
    /// data there is: buffers hand over at most a screenful of points, keeping spikes.
    /// </summary>
    public class LaneChart : Control
    {
        private const double MarginLeft = 56;
        private const double MarginRight = 92;
        private const double MarginTop = 4;
        private const double MarginBottom = 22;
        private const double MinWindowSeconds = 2;

        private static readonly double[] TickSteps = { 1, 2, 5, 10, 15, 30, 60, 120, 300, 600, 900, 1800, 3600, 7200 };

        public static readonly StyledProperty<IReadOnlyList<ChartTrace>?> TracesProperty =
            AvaloniaProperty.Register<LaneChart, IReadOnlyList<ChartTrace>?>(nameof(Traces));
        public static readonly StyledProperty<bool> OverlayProperty =
            AvaloniaProperty.Register<LaneChart, bool>(nameof(Overlay));
        public static readonly StyledProperty<bool> FollowProperty =
            AvaloniaProperty.Register<LaneChart, bool>(nameof(Follow), true, defaultBindingMode: global::Avalonia.Data.BindingMode.TwoWay);
        public static readonly StyledProperty<double> WindowSecondsProperty =
            AvaloniaProperty.Register<LaneChart, double>(nameof(WindowSeconds), 60, defaultBindingMode: global::Avalonia.Data.BindingMode.TwoWay);
        public static readonly StyledProperty<double> ViewEndProperty =
            AvaloniaProperty.Register<LaneChart, double>(nameof(ViewEnd), defaultBindingMode: global::Avalonia.Data.BindingMode.TwoWay);
        public static readonly StyledProperty<string?> PlaceholderProperty =
            AvaloniaProperty.Register<LaneChart, string?>(nameof(Placeholder));

        private readonly List<ChartPoint> _points = new();
        private Point? _pointer;
        private Point? _dragFrom;
        private double _dragViewEnd;
        private IBrush? _gridBrush;
        private IBrush? _laneBrush;
        private IBrush? _textBrush;
        private IBrush? _faintBrush;
        private IBrush? _cursorBrush;
        private IBrush? _labelBrush;
        private double _plotWidth;
        private double _viewStart;
        private double _viewEnd;

        static LaneChart()
        {
            AffectsRender<LaneChart>(TracesProperty, OverlayProperty, FollowProperty, WindowSecondsProperty, ViewEndProperty, PlaceholderProperty);
        }

        public LaneChart()
        {
            ClipToBounds = true;
            ActualThemeVariantChanged += (_, _) =>
            {
                _gridBrush = null;
                InvalidateVisual();
            };
        }

        public IReadOnlyList<ChartTrace>? Traces { get => GetValue(TracesProperty); set => SetValue(TracesProperty, value); }
        public bool Overlay { get => GetValue(OverlayProperty); set => SetValue(OverlayProperty, value); }
        public bool Follow { get => GetValue(FollowProperty); set => SetValue(FollowProperty, value); }
        public double WindowSeconds { get => GetValue(WindowSecondsProperty); set => SetValue(WindowSecondsProperty, value); }
        public double ViewEnd { get => GetValue(ViewEndProperty); set => SetValue(ViewEndProperty, value); }
        public string? Placeholder { get => GetValue(PlaceholderProperty); set => SetValue(PlaceholderProperty, value); }

        private IBrush Resource(string key, IBrush fallback) =>
            this.TryFindResource(key, ActualThemeVariant, out var value) && value is IBrush brush ? brush : fallback;

        private void EnsureBrushes()
        {
            if (_gridBrush != null) return;
            _gridBrush = Resource("ChartGridBrush", Brushes.Gray);
            _laneBrush = Resource("ChartLaneBrush", Brushes.Transparent);
            _textBrush = Resource("TextBrush", Brushes.White);
            _faintBrush = Resource("TextMutedBrush", Brushes.Gray);
            _cursorBrush = Resource("ChartCursorBrush", Brushes.White);
            _labelBrush = Resource("ChartLabelBrush", Brushes.Transparent);
        }

        private double DataEnd()
        {
            var end = 0.0;
            var traces = Traces;
            if (traces == null) return end;
            foreach (var trace in traces)
            {
                if (trace.Data.LastTime is { } last && last > end) end = last;
                if (trace.Compare?.LastTime is { } other && other > end) end = other;
            }
            return end;
        }

        private FormattedText Text(string text, double size, IBrush brush, FontWeight weight = FontWeight.Normal)
        {
            var typeface = new Typeface(TextElement.GetFontFamily(this), FontStyle.Normal, weight);
            return new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, typeface, size, brush);
        }

        public override void Render(DrawingContext context)
        {
            EnsureBrushes();
            var width = Bounds.Width;
            var height = Bounds.Height;
            // Something has to be painted everywhere for the pointer to hit this control.
            context.FillRectangle(Brushes.Transparent, new Rect(0, 0, width, height));

            var traces = Traces;
            var dataEnd = DataEnd();
            if (traces == null || traces.Count == 0 || dataEnd <= 0)
            {
                if (!string.IsNullOrEmpty(Placeholder))
                {
                    var message = Text(Placeholder!, 14, _faintBrush!);
                    context.DrawText(message, new Point((width - message.Width) / 2, (height - message.Height) / 2));
                }
                return;
            }

            var plot = new Rect(MarginLeft, MarginTop,
                Math.Max(10, width - MarginLeft - MarginRight),
                Math.Max(10, height - MarginTop - MarginBottom));
            _plotWidth = plot.Width;

            var window = Math.Max(MinWindowSeconds, WindowSeconds);
            var end = Follow ? Math.Max(dataEnd, window) : Math.Max(ViewEnd, window);
            var start = end - window;
            _viewStart = start;
            _viewEnd = end;

            DrawTimeAxis(context, plot, start, window);

            double? cursor = null;
            if (_pointer is { } pointer && pointer.X >= plot.Left && pointer.X <= plot.Right)
                cursor = start + (pointer.X - plot.Left) / plot.Width * window;

            var overlay = Overlay;
            var laneCount = overlay ? 1 : traces.Count;
            var laneHeight = plot.Height / laneCount;

            for (int i = 0; i < traces.Count; i++)
            {
                var laneIndex = overlay ? 0 : i;
                var lane = new Rect(plot.Left, plot.Top + laneIndex * laneHeight, plot.Width, laneHeight);
                if (!overlay && i % 2 == 0)
                    context.FillRectangle(_laneBrush!, lane);
                DrawTrace(context, traces[i], lane, start, end, window, overlay, i, cursor);
            }

            if (cursor is { } cursorT)
            {
                var x = plot.Left + (cursorT - start) / window * plot.Width;
                context.DrawLine(new Pen(_cursorBrush!, 1), new Point(x, plot.Top), new Point(x, plot.Bottom));
                var label = Text(FormatTime(cursorT, showTenths: true), 11, _textBrush!, FontWeight.SemiBold);
                var labelX = Math.Clamp(x - label.Width / 2, 0, width - label.Width);
                var box = new Rect(labelX - 4, plot.Bottom + 2, label.Width + 8, label.Height + 2);
                context.FillRectangle(Resource("SurfaceAltBrush", Brushes.Black), box, 4);
                context.DrawText(label, new Point(labelX, plot.Bottom + 3));
            }
        }

        private void DrawTimeAxis(DrawingContext context, Rect plot, double start, double window)
        {
            var gridPen = new Pen(_gridBrush!, 1);
            var maxTicks = Math.Max(2, plot.Width / 80);
            var step = TickSteps[^1];
            foreach (var candidate in TickSteps)
            {
                if (window / candidate > maxTicks) continue;
                step = candidate;
                break;
            }

            var first = Math.Ceiling(start / step) * step;
            for (var t = first; t <= start + window; t += step)
            {
                var x = plot.Left + (t - start) / window * plot.Width;
                context.DrawLine(gridPen, new Point(x, plot.Top), new Point(x, plot.Bottom));
                var label = Text(FormatTime(t, showTenths: false), 10, _faintBrush!);
                context.DrawText(label, new Point(x - label.Width / 2, plot.Bottom + 4));
            }
        }

        private void DrawTrace(DrawingContext context, ChartTrace trace, Rect lane, double start, double end, double window, bool overlay, int index, double? cursor)
        {
            var (min, max) = Range(trace, start, end);

            double Y(double v) => lane.Bottom - 3 - (v - min) / (max - min) * (lane.Height - 6);

            using (context.PushClip(lane))
            {
                if (trace.Compare != null) DrawLine(context, trace.Compare, trace.ComparePen, lane, start, end, window, Y);
                DrawLine(context, trace.Data, trace.Pen, lane, start, end, window, Y);
            }

            var value = cursor is { } t ? trace.Data.ValueAt(t) : trace.Data.LastTime is { } last ? trace.Data.ValueAt(last) : double.NaN;
            var valueText = double.IsNaN(value) ? "–" : FormatValue(value);

            if (overlay)
            {
                var y = lane.Top + 4 + index * 16;
                context.FillRectangle(trace.Dot, new Rect(lane.Left + 6, y + 3, 8, 8), 2);
                var legend = Text($"{trace.Name}   {valueText} {trace.Unit}", 11, _textBrush!);
                context.DrawText(legend, new Point(lane.Left + 18, y));
                return;
            }

            // Lane heading, plus the scale and the value when the lane is tall enough for them.
            var tall = lane.Height >= 44;
            var name = Text(trace.Name, tall ? 11 : 10, _faintBrush!);
            var nameTop = lane.Top + (tall ? 2 : 0);
            context.FillRectangle(_labelBrush!, new Rect(lane.Left + 3, nameTop, name.Width + 20, name.Height), 4);
            context.FillRectangle(trace.Dot, new Rect(lane.Left + 6, nameTop + (name.Height - 8) / 2, 8, 8), 2);
            context.DrawText(name, new Point(lane.Left + 18, nameTop));

            if (tall)
            {
                var top = Text(FormatValue(max), 10, _faintBrush!);
                var bottom = Text(FormatValue(min), 10, _faintBrush!);
                context.DrawText(top, new Point(MarginLeft - top.Width - 6, lane.Top + 1));
                context.DrawText(bottom, new Point(MarginLeft - bottom.Width - 6, lane.Bottom - bottom.Height - 1));
            }

            var reading = Text(valueText, tall ? 16 : 12, _textBrush!, FontWeight.Bold);
            var readingX = lane.Right + 8;
            var readingY = tall ? lane.Top + lane.Height / 2 - reading.Height + 2 : lane.Top + (lane.Height - reading.Height) / 2;
            context.DrawText(reading, new Point(readingX, readingY));
            if (tall && trace.Unit.Length > 0)
                context.DrawText(Text(trace.Unit, 10, _faintBrush!), new Point(readingX, readingY + reading.Height));
        }

        private void DrawLine(DrawingContext context, SeriesBuffer buffer, IPen pen, Rect lane, double start, double end, double window, Func<double, double> y)
        {
            buffer.Downsample(start, end, (int)Math.Max(10, lane.Width), _points);
            if (_points.Count == 0) return;

            double X(double t) => lane.Left + (t - start) / window * lane.Width;

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
                    var x = X(p.T);
                    if (!open)
                    {
                        g.BeginFigure(new Point(x, y(p.First)), false);
                        open = true;
                    }
                    else
                    {
                        g.LineTo(new Point(x, y(p.First)));
                    }
                    if (p.Max > p.Min)
                    {
                        g.LineTo(new Point(x, y(p.Min)));
                        g.LineTo(new Point(x, y(p.Max)));
                        g.LineTo(new Point(x, y(p.Last)));
                    }
                }
                if (open) g.EndFigure(false);
            }
            context.DrawGeometry(null, pen, geometry);
        }

        /// <summary>The Y scale for what's on screen; a flat line sits in the middle of its lane.</summary>
        private static (double Min, double Max) Range(ChartTrace trace, double start, double end)
        {
            var stats = trace.Data.Stats(start, end);
            double min = stats.Min, max = stats.Max;
            if (trace.Compare != null)
            {
                var other = trace.Compare.Stats(start, end);
                if (!double.IsNaN(other.Min))
                {
                    min = double.IsNaN(min) ? other.Min : Math.Min(min, other.Min);
                    max = double.IsNaN(max) ? other.Max : Math.Max(max, other.Max);
                }
            }
            if (double.IsNaN(min)) return (0, 1);
            if (max - min < 1e-6)
            {
                var margin = Math.Max(1, Math.Abs(min) * 0.05);
                return (min - margin, max + margin);
            }
            var pad = (max - min) * 0.08;
            return (min - pad, max + pad);
        }

        public static string FormatValue(double value)
        {
            var magnitude = Math.Abs(value);
            var format = magnitude >= 100 ? "0" : magnitude >= 10 ? "0.0" : "0.00";
            return value.ToString(format, CultureInfo.CurrentCulture);
        }

        public static string FormatTime(double seconds, bool showTenths)
        {
            var span = TimeSpan.FromSeconds(Math.Max(0, seconds));
            var text = span.TotalHours >= 1
                ? string.Create(CultureInfo.InvariantCulture, $"{(int)span.TotalHours}:{span.Minutes:00}:{span.Seconds:00}")
                : string.Create(CultureInfo.InvariantCulture, $"{span.Minutes}:{span.Seconds:00}");
            return showTenths ? text + string.Create(CultureInfo.InvariantCulture, $".{span.Milliseconds / 100}") : text;
        }

        protected override void OnPointerMoved(PointerEventArgs e)
        {
            base.OnPointerMoved(e);
            var position = e.GetPosition(this);
            _pointer = position;
            if (_dragFrom is { } from && _plotWidth > 0)
            {
                var dt = -(position.X - from.X) / _plotWidth * Math.Max(MinWindowSeconds, WindowSeconds);
                SetCurrentValue(FollowProperty, false);
                SetCurrentValue(ViewEndProperty, Math.Clamp(_dragViewEnd + dt, Math.Max(MinWindowSeconds, WindowSeconds), Math.Max(DataEnd(), WindowSeconds)));
            }
            InvalidateVisual();
        }

        protected override void OnPointerExited(PointerEventArgs e)
        {
            base.OnPointerExited(e);
            _pointer = null;
            InvalidateVisual();
        }

        protected override void OnPointerPressed(PointerPressedEventArgs e)
        {
            base.OnPointerPressed(e);
            if (e.ClickCount >= 2)
            {
                ResetView();
                e.Handled = true;
                return;
            }
            if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            {
                _dragFrom = e.GetPosition(this);
                _dragViewEnd = _viewEnd;
                e.Pointer.Capture(this);
            }
        }

        protected override void OnPointerReleased(PointerReleasedEventArgs e)
        {
            base.OnPointerReleased(e);
            _dragFrom = null;
            e.Pointer.Capture(null);
        }

        protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
        {
            base.OnPointerWheelChanged(e);
            var window = Math.Max(MinWindowSeconds, WindowSeconds);
            var longest = Math.Max(DataEnd(), 60);
            var zoomed = Math.Clamp(window * Math.Pow(1.25, -e.Delta.Y), MinWindowSeconds, longest);

            if (!Follow && _plotWidth > 0)
            {
                // Keep the time under the pointer where it is.
                var x = Math.Clamp(e.GetPosition(this).X - MarginLeft, 0, _plotWidth);
                var anchor = _viewStart + x / _plotWidth * window;
                var viewEnd = anchor + (_viewEnd - anchor) * zoomed / window;
                SetCurrentValue(ViewEndProperty, Math.Clamp(viewEnd, zoomed, Math.Max(DataEnd(), zoomed)));
            }
            SetCurrentValue(WindowSecondsProperty, zoomed);
            e.Handled = true;
        }

        /// <summary>Back to the default view: following live data, or the whole of a recorded session.</summary>
        public void ResetView()
        {
            SetCurrentValue(FollowProperty, true);
            ViewReset?.Invoke();
            InvalidateVisual();
        }

        /// <summary>Raised on double-click, for the owner to restore its preferred time window.</summary>
        public event Action? ViewReset;
    }
}
