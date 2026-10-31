using System;
using System.Collections.Generic;

namespace IES_2.Session
{
    /// <summary>One line segment's worth of data for the chart: a single sample, or the extremes of several.</summary>
    public readonly record struct ChartPoint(double T, double Min, double Max, double First, double Last, bool IsGap);

    public readonly record struct SeriesStats(double Min, double Max, double Average, int Count);

    /// <summary>
    /// A fixed-size ring of (time, value) samples for one trace: memory stays bounded however long a
    /// session runs, and drawing never walks more than a screenful of points. NaN marks a gap (no valid
    /// answer from the ECU). Written from the polling loop, read from the UI thread.
    /// </summary>
    public sealed class SeriesBuffer
    {
        private readonly object _gate = new();
        private readonly double[] _t;
        private readonly double[] _v;
        private int _start;
        private int _count;

        public SeriesBuffer(int capacity)
        {
            if (capacity < 2) throw new ArgumentOutOfRangeException(nameof(capacity));
            _t = new double[capacity];
            _v = new double[capacity];
        }

        public int Capacity => _t.Length;

        public int Count
        {
            get { lock (_gate) return _count; }
        }

        /// <summary>Appends a sample; time must not go backwards. Beyond capacity the oldest sample is dropped.</summary>
        public void Add(double t, double value)
        {
            lock (_gate)
            {
                var index = (_start + _count) % _t.Length;
                _t[index] = t;
                _v[index] = value;
                if (_count < _t.Length) _count++;
                else _start = (_start + 1) % _t.Length;
            }
        }

        public void Clear()
        {
            lock (_gate)
            {
                _start = 0;
                _count = 0;
            }
        }

        public double? FirstTime
        {
            get { lock (_gate) return _count == 0 ? null : TimeAt(0); }
        }

        public double? LastTime
        {
            get { lock (_gate) return _count == 0 ? null : TimeAt(_count - 1); }
        }

        /// <summary>The value in force at time t (the latest sample at or before it), NaN if none or a gap.</summary>
        public double ValueAt(double t)
        {
            lock (_gate)
            {
                if (_count == 0) return double.NaN;
                var index = LastIndexAtOrBefore(t);
                return index < 0 ? double.NaN : ValueAtIndex(index);
            }
        }

        public SeriesStats Stats(double from, double to)
        {
            lock (_gate)
            {
                double min = double.PositiveInfinity, max = double.NegativeInfinity, sum = 0;
                var count = 0;
                for (int i = Math.Max(0, LastIndexAtOrBefore(from)); i < _count; i++)
                {
                    var t = TimeAt(i);
                    if (t > to) break;
                    if (t < from) continue;
                    var v = ValueAtIndex(i);
                    if (double.IsNaN(v)) continue;
                    if (v < min) min = v;
                    if (v > max) max = v;
                    sum += v;
                    count++;
                }
                return count == 0
                    ? new SeriesStats(double.NaN, double.NaN, double.NaN, 0)
                    : new SeriesStats(min, max, sum / count, count);
            }
        }

        /// <summary>
        /// Fills <paramref name="into"/> with what's needed to draw [from, to] across a given number of
        /// horizontal buckets: every sample when they fit, otherwise each bucket's extremes, so spikes
        /// survive however far the chart is zoomed out. One neighbour either side keeps lines continuous
        /// up to the plot's edges.
        /// </summary>
        public void Downsample(double from, double to, int buckets, List<ChartPoint> into)
        {
            into.Clear();
            if (buckets < 1 || to <= from) return;

            lock (_gate)
            {
                if (_count == 0) return;
                var first = Math.Max(0, LastIndexAtOrBefore(from));
                var last = first;
                while (last < _count - 1 && TimeAt(last) < to) last++;
                var total = last - first + 1;

                if (total <= buckets * 2)
                {
                    for (int i = first; i <= last; i++) AddSample(into, i);
                    return;
                }

                var width = (to - from) / buckets;
                var bucket = int.MinValue;
                var open = false;
                double start = 0, min = 0, max = 0, firstValue = 0, lastValue = 0;
                for (int i = first; i <= last; i++)
                {
                    var t = TimeAt(i);
                    var v = ValueAtIndex(i);
                    if (double.IsNaN(v))
                    {
                        if (open) into.Add(new ChartPoint(start, min, max, firstValue, lastValue, false));
                        open = false;
                        into.Add(new ChartPoint(t, double.NaN, double.NaN, double.NaN, double.NaN, true));
                        continue;
                    }

                    var current = (int)Math.Floor((t - from) / width);
                    if (!open || current != bucket)
                    {
                        if (open) into.Add(new ChartPoint(start, min, max, firstValue, lastValue, false));
                        bucket = current;
                        open = true;
                        start = t;
                        min = max = firstValue = v;
                    }
                    if (v < min) min = v;
                    if (v > max) max = v;
                    lastValue = v;
                }
                if (open) into.Add(new ChartPoint(start, min, max, firstValue, lastValue, false));
            }
        }

        private void AddSample(List<ChartPoint> into, int index)
        {
            var v = ValueAtIndex(index);
            into.Add(new ChartPoint(TimeAt(index), v, v, v, v, double.IsNaN(v)));
        }

        private double TimeAt(int logical) => _t[(_start + logical) % _t.Length];

        private double ValueAtIndex(int logical) => _v[(_start + logical) % _t.Length];

        /// <summary>Binary search over the logical order; -1 when every sample is later than t.</summary>
        private int LastIndexAtOrBefore(double t)
        {
            int low = 0, high = _count - 1, result = -1;
            while (low <= high)
            {
                var mid = (low + high) / 2;
                if (TimeAt(mid) <= t)
                {
                    result = mid;
                    low = mid + 1;
                }
                else
                {
                    high = mid - 1;
                }
            }
            return result;
        }
    }
}
