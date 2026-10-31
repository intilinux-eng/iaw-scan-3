using System;
using System.Linq;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using IES_2.Avalonia.Controls;
using IES_2.ECU;
using IES_2.Res;
using IES_2.Session;

namespace IES_2.Avalonia.Models
{
    /// <summary>One engine parameter: what it is, whether it's being read, and its live value and history.</summary>
    public partial class ParameterRow : ObservableObject
    {
        /// <summary>One hour of history at the highest capture rate, per parameter being read.</summary>
        private const int HistoryCapacity = 36_000;

        private static readonly Color[] Palette =
        {
            Color.FromRgb(0x4F, 0x8C, 0xFF), Color.FromRgb(0xF5, 0x9E, 0x0B), Color.FromRgb(0x22, 0xC5, 0x5E),
            Color.FromRgb(0xEF, 0x44, 0x44), Color.FromRgb(0xA8, 0x55, 0xF7), Color.FromRgb(0x06, 0xB6, 0xD4),
            Color.FromRgb(0xEC, 0x48, 0x99), Color.FromRgb(0x84, 0xCC, 0x16),
        };

        public int Index { get; }
        public dataElement Data { get; }
        public string Description { get; }
        public string Unit { get; }
        public Color TraceColor { get; }
        public IBrush TraceBrush { get; }

        /// <summary>History for the charts, kept only for parameters being read.</summary>
        public SeriesBuffer? Buffer { get; private set; }

        /// <summary>Read from the polling loop; mirrors IsChecked.</summary>
        internal volatile bool IsPolled;

        [ObservableProperty]
        private bool isChecked;

        [ObservableProperty]
        private bool isPlotted = true;

        [ObservableProperty]
        private string value = "";

        [ObservableProperty]
        private bool isInvalid;

        [ObservableProperty]
        private bool isFlag;

        [ObservableProperty]
        private bool flagOn;

        [ObservableProperty]
        private double numeric = double.NaN;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(ExtremesText))]
        private double sessionMin = double.NaN;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(ExtremesText))]
        private double sessionMax = double.NaN;

        [ObservableProperty]
        private double gaugeMinimum;

        [ObservableProperty]
        private double gaugeMaximum = 100;

        /// <summary>Changes whenever a new sample lands, for controls that repaint from the history.</summary>
        [ObservableProperty]
        private long stamp;

        /// <summary>The lowest and highest values seen, under the gauge.</summary>
        public string ExtremesText => double.IsNaN(SessionMin)
            ? ""
            : $"↓ {LaneChart.FormatValue(SessionMin)}   ↑ {LaneChart.FormatValue(SessionMax)}";

        public ParameterRow(int index, dataElement data)
        {
            Index = index;
            Data = data;
            Description = data.Description.Trim();
            Unit = data.Unit;
            TraceColor = Palette[index % Palette.Length];
            TraceBrush = new SolidColorBrush(TraceColor);
            (GaugeMinimum, GaugeMaximum) = GaugeRange.For(Unit);
        }

        /// <summary>True when every byte this parameter is made of got a valid answer.</summary>
        internal bool HasValidAnswer => Data.RequestSet.All(request => ecu.Valid[request]);

        internal void EnsureBuffer() => Buffer ??= new SeriesBuffer(HistoryCapacity);

        internal void Apply(string? formatted, double? reading)
        {
            if (formatted == null)
            {
                Value = "#ERR";
                IsInvalid = true;
                Numeric = double.NaN;
                Stamp++;
                return;
            }

            IsInvalid = false;
            Value = formatted;
            // Yes/no parameters format as words, whatever the language.
            IsFlag = !formatted.Any(char.IsDigit);
            if (reading is { } number)
            {
                Numeric = number;
                FlagOn = number != 0;
                if (double.IsNaN(SessionMin) || number < SessionMin) SessionMin = number;
                if (double.IsNaN(SessionMax) || number > SessionMax) SessionMax = number;
                if (number > GaugeMaximum) GaugeMaximum = GaugeRange.Ceiling(number);
                if (number < GaugeMinimum) GaugeMinimum = -GaugeRange.Ceiling(-number);
            }
            Stamp++;
        }

        internal void ClearReading()
        {
            Value = "";
            IsInvalid = false;
            Numeric = double.NaN;
            SessionMin = double.NaN;
            SessionMax = double.NaN;
            Buffer?.Clear();
            Stamp++;
        }

        public void ResetExtremes()
        {
            SessionMin = Numeric;
            SessionMax = Numeric;
        }
    }

    /// <summary>The scale a gauge starts from for each unit; it widens by itself if a value goes beyond.</summary>
    public static class GaugeRange
    {
        public static (double Min, double Max) For(string unit)
        {
            if (unit == lang.rpm) return (0, 7000);
            if (unit == lang.steps) return (0, 100);
            return unit switch
            {
                "ms" => (0, 25),
                "°" => (0, 60),
                "°C" => (-40, 130),
                "hPa" => (0, 1200),
                "V" => (0, 16),
                "mV" => (0, 1000),
                "%" => (0, 100),
                "#" => (0, 255),
                _ => (0, 2),
            };
        }

        /// <summary>The next round number at or above a value.</summary>
        public static double Ceiling(double value)
        {
            if (value <= 0) return 1;
            var magnitude = Math.Pow(10, Math.Floor(Math.Log10(value)));
            foreach (var step in new[] { 1.0, 1.5, 2, 2.5, 3, 4, 5, 6, 8, 10 })
                if (step * magnitude >= value) return step * magnitude;
            return 10 * magnitude;
        }
    }
}
