using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Avalonia.Media;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using IES_2.Avalonia.Models;
using IES_2.Avalonia.Services;

namespace IES_2.Avalonia.ViewModels
{
    /// <summary>
    /// [F5] Graphs screen: live rolling chart + CSV export of the currently-plotted parameters,
    /// replaces frmMain's cblTraces + zedGraphControl1 + tGraph timer + RecordToggle()/InitCurves()/
    /// AddDataToGraph()/tGraph_Tick().
    /// </summary>
    public partial class GraphsViewModel : ObservableObject
    {
        private static readonly Color[] Palette =
        {
            Color.FromRgb(0x3B, 0x5B, 0xDB), Color.FromRgb(0xE8, 0x59, 0x0C), Color.FromRgb(0x2F, 0x9E, 0x44),
            Color.FromRgb(0xE0, 0x35, 0x31), Color.FromRgb(0x94, 0x2F, 0xE8), Color.FromRgb(0x0C, 0x8E, 0x8E),
            Color.FromRgb(0xC2, 0x25, 0x5C), Color.FromRgb(0x84, 0x7A, 0x00),
        };

        private readonly DiagnosticsSession _session;
        private readonly ParametersViewModel _parameters;
        private System.Timers.Timer? _sampleTimer;
        private Stopwatch? _stopwatch;
        private StreamWriter? _csv;
        // Never reassigned: Series below is bound once by the view, so new recordings must mutate
        // this same list in place (Clear + re-add) rather than pointing Series at a new instance.
        private readonly List<ChartSeries> _activeSeries = new();
        private int _paletteIndex;

        public ObservableCollection<TraceOption> Traces { get; } = new();
        public IReadOnlyList<ChartSeries> Series => _activeSeries;

        /// <summary>Wired up by the view: repaints the chart control after new samples land.</summary>
        public event Action? Redraw;

        [ObservableProperty]
        private bool isRecording;

        [ObservableProperty]
        private string? statusMessage;

        public GraphsViewModel(DiagnosticsSession session, ParametersViewModel parameters)
        {
            _session = session;
            _parameters = parameters;
            parameters.RowCheckedChanged += OnParameterCheckedChanged;
            foreach (var row in parameters.Rows.Where(r => r.IsChecked))
                Traces.Add(new TraceOption(row, NextColor()));
            Traces.CollectionChanged += (_, _) => ToggleRecordingCommand.NotifyCanExecuteChanged();
        }

        private Color NextColor() => Palette[_paletteIndex++ % Palette.Length];

        private void OnParameterCheckedChanged(ParameterRow row)
        {
            if (IsRecording) return;
            if (row.IsChecked)
            {
                if (Traces.All(t => t.Row != row))
                    Traces.Add(new TraceOption(row, NextColor()));
            }
            else
            {
                var existing = Traces.FirstOrDefault(t => t.Row == row);
                if (existing != null) Traces.Remove(existing);
            }
        }

        private bool CanToggleRecording => Traces.Count > 0 || IsRecording;

        [RelayCommand(CanExecute = nameof(CanToggleRecording))]
        private void ToggleRecording()
        {
            if (!IsRecording) StartRecording();
            else StopRecording();
        }

        private void StartRecording()
        {
            var plotted = Traces.Where(t => t.IsPlotted).ToList();
            if (plotted.Count == 0)
            {
                StatusMessage = "Seleziona almeno un tracciato da registrare.";
                return;
            }

            _activeSeries.Clear();
            _activeSeries.AddRange(plotted.Select(t => new ChartSeries(t.Description, t.Color)));
            StatusMessage = null;

            try
            {
                var logDir = Path.Combine(Directory.GetCurrentDirectory(), "Log");
                Directory.CreateDirectory(logDir);
                _csv = new StreamWriter(Path.Combine(logDir, $"IESexp_{DateTime.Now:yyMMddHHmm}.csv"), true);
                _csv.Write("Timestamp");
                foreach (var t in plotted) _csv.Write(";" + t.Description);
                _csv.WriteLine();
            }
            catch (IOException ex)
            {
                StatusMessage = $"Impossibile creare il file di esportazione: {ex.Message}";
                _csv = null;
            }

            _stopwatch = Stopwatch.StartNew();
            _sampleTimer = new System.Timers.Timer(100);
            _sampleTimer.Elapsed += (_, _) => Sample(plotted);
            _sampleTimer.AutoReset = true;
            _sampleTimer.Start();
            IsRecording = true;
        }

        private void Sample(IReadOnlyList<TraceOption> plotted)
        {
            if (_stopwatch == null) return;
            double x = _stopwatch.Elapsed.TotalSeconds;
            var values = new double[plotted.Count];
            var line = new StringBuilder(_stopwatch.ElapsedMilliseconds.ToString(CultureInfo.InvariantCulture));
            for (int i = 0; i < plotted.Count; i++)
            {
                double v = (double)plotted[i].Row.Data.Value;
                values[i] = v;
                line.Append(';').Append(v.ToString(CultureInfo.InvariantCulture));
            }

            try { _csv?.WriteLine(line.ToString()); } catch (IOException) { /* best-effort export */ }

            Dispatcher.UIThread.Post(() =>
            {
                for (int i = 0; i < _activeSeries.Count && i < values.Length; i++)
                    _activeSeries[i].Points.Add((x, values[i]));
                Redraw?.Invoke();
            });
        }

        private void StopRecording()
        {
            _sampleTimer?.Stop();
            _sampleTimer?.Dispose();
            _sampleTimer = null;
            try { _csv?.Flush(); _csv?.Dispose(); } catch (IOException) { /* best-effort export */ }
            _csv = null;
            _stopwatch = null;
            IsRecording = false;
        }

        partial void OnIsRecordingChanged(bool value) => ToggleRecordingCommand.NotifyCanExecuteChanged();

        public void Detach()
        {
            StopRecording();
            _parameters.RowCheckedChanged -= OnParameterCheckedChanged;
        }
    }
}
