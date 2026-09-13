using CommunityToolkit.Mvvm.ComponentModel;
using Avalonia.Media;

namespace IES_2.Avalonia.Models
{
    /// <summary>
    /// One entry in the Graphs screen's trace list: mirrors frmMain's cblTraces - a
    /// second, independent checkbox next to each currently-queried parameter that
    /// decides whether it's actually plotted/exported while recording.
    /// </summary>
    public partial class TraceOption : ObservableObject
    {
        public ParameterRow Row { get; }
        public string Description => Row.Description;
        public Color Color { get; }

        [ObservableProperty]
        private bool isPlotted = true;

        public TraceOption(ParameterRow row, Color color)
        {
            Row = row;
            Color = color;
        }
    }
}
