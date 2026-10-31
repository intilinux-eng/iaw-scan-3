using System.Collections.Generic;
using System.Linq;
using IES_2.Session;
using Xunit;

namespace IES_2.Tests
{
    public class SeriesBufferTests
    {
        [Fact]
        public void KeepsOnlyTheNewestSamplesOnceFull()
        {
            var buffer = new SeriesBuffer(100);
            for (int i = 0; i < 1000; i++) buffer.Add(i, i * 2);

            Assert.Equal(100, buffer.Count);
            Assert.Equal(900, buffer.FirstTime);
            Assert.Equal(999, buffer.LastTime);
            Assert.Equal(1998, buffer.ValueAt(999));
        }

        [Fact]
        public void ValueAtIsTheLatestSampleAtOrBeforeThatTime()
        {
            var buffer = new SeriesBuffer(10);
            buffer.Add(1, 10);
            buffer.Add(2, 20);

            Assert.True(double.IsNaN(buffer.ValueAt(0.5)));
            Assert.Equal(10, buffer.ValueAt(1.9));
            Assert.Equal(20, buffer.ValueAt(5));
        }

        [Fact]
        public void StatsCoverOnlyTheRequestedRangeAndSkipGaps()
        {
            var buffer = new SeriesBuffer(10);
            buffer.Add(0, 100);
            buffer.Add(1, 2);
            buffer.Add(2, double.NaN);
            buffer.Add(3, 4);
            buffer.Add(4, 100);

            var stats = buffer.Stats(1, 3);

            Assert.Equal(2, stats.Min);
            Assert.Equal(4, stats.Max);
            Assert.Equal(3, stats.Average);
            Assert.Equal(2, stats.Count);
        }

        [Fact]
        public void DrawingAnHourOfDataTakesOnlyAScreenfulOfPoints()
        {
            var buffer = new SeriesBuffer(36_000);
            for (int i = 0; i < 36_000; i++) buffer.Add(i * 0.1, i % 7);

            var points = new List<ChartPoint>();
            buffer.Downsample(0, 3600, 800, points);

            Assert.InRange(points.Count, 1, 1600);
        }

        [Fact]
        public void ReducingPointsKeepsASingleSpike()
        {
            var buffer = new SeriesBuffer(36_000);
            for (int i = 0; i < 36_000; i++) buffer.Add(i * 0.1, i == 12_345 ? 999 : 1);

            var points = new List<ChartPoint>();
            buffer.Downsample(0, 3600, 100, points);

            Assert.Equal(999, points.Max(p => p.Max));
            Assert.Equal(1, points.Min(p => p.Min));
        }

        [Fact]
        public void GapsSurviveReducingPoints()
        {
            var buffer = new SeriesBuffer(10_000);
            for (int i = 0; i < 10_000; i++) buffer.Add(i, i == 5000 ? double.NaN : 1);

            var points = new List<ChartPoint>();
            buffer.Downsample(0, 10_000, 50, points);

            Assert.Contains(points, p => p.IsGap);
        }

        [Fact]
        public void WhatFitsIsDrawnSampleBySample()
        {
            var buffer = new SeriesBuffer(100);
            for (int i = 0; i < 10; i++) buffer.Add(i, i);

            var points = new List<ChartPoint>();
            buffer.Downsample(0, 9, 100, points);

            Assert.Equal(10, points.Count);
            Assert.All(points, p => Assert.Equal(p.Min, p.Max));
        }

        [Fact]
        public void AnEmptyBufferDrawsNothing()
        {
            var points = new List<ChartPoint> { new(0, 0, 0, 0, 0, false) };
            new SeriesBuffer(10).Downsample(0, 10, 10, points);
            Assert.Empty(points);
        }
    }
}
