using System.Collections.Generic;
using Avalonia.Media;

namespace IES_2.Avalonia.Models
{
    /// <summary>One plotted trace on the live chart: a name, a color and its sampled (time, value) points.</summary>
    public sealed class ChartSeries
    {
        public string Name { get; }
        public Color Color { get; }
        public List<(double X, double Y)> Points { get; } = new();

        public ChartSeries(string name, Color color)
        {
            Name = name;
            Color = color;
        }
    }
}
