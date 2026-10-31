using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using IES_2.ECU;

namespace IES_2.Coupe.Diagnostics
{
    /// <summary>
    /// Graph recording, same behaviour as the desktop GraphsViewModel: every 100 ms it samples the
    /// chosen parameters' decoded values, appends them to Log/IESexp_yyMMddHHmm.csv (same name, header
    /// and number format), and hands each sample to the browser for the live chart.
    /// </summary>
    public sealed partial class Recorder : IDisposable
    {
        private readonly object _gate = new();
        private System.Timers.Timer? _sampleTimer;
        private Stopwatch? _stopwatch;
        private StreamWriter? _csv;
        private IReadOnlyList<dataElement> _plotted = Array.Empty<dataElement>();

        public static string LogDirectory => Path.Combine(Directory.GetCurrentDirectory(), "Log");

        public event Action<SampleDto>? Sampled;

        public bool IsRecording { get; private set; }
        public IReadOnlyList<int> Indices { get; private set; } = Array.Empty<int>();
        public string? FileName { get; private set; }

        /// <returns>null on success, otherwise a message for the user.</returns>
        public string? Start(IReadOnlyList<(int Index, dataElement Data)> plotted)
        {
            lock (_gate)
            {
                if (IsRecording) return null;
                if (plotted.Count == 0) return "Seleziona almeno un tracciato da registrare.";

                string? warning = null;
                try
                {
                    Directory.CreateDirectory(LogDirectory);
                    FileName = $"IESexp_{DateTime.Now:yyMMddHHmm}.csv";
                    _csv = new StreamWriter(Path.Combine(LogDirectory, FileName), true);
                    _csv.Write("Timestamp");
                    foreach (var (_, data) in plotted) _csv.Write(";" + data.Description);
                    _csv.WriteLine();
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    warning = $"Impossibile creare il file di esportazione: {ex.Message}";
                    _csv = null;
                    FileName = null;
                }

                _plotted = plotted.Select(p => p.Data).ToList();
                Indices = plotted.Select(p => p.Index).ToList();
                _stopwatch = Stopwatch.StartNew();
                _sampleTimer = new System.Timers.Timer(100);
                _sampleTimer.Elapsed += (_, _) => Sample();
                _sampleTimer.AutoReset = true;
                _sampleTimer.Start();
                IsRecording = true;
                return warning;
            }
        }

        private void Sample()
        {
            SampleDto sample;
            lock (_gate)
            {
                if (_stopwatch == null) return;
                var values = new double[_plotted.Count];
                var line = new StringBuilder(_stopwatch.ElapsedMilliseconds.ToString(CultureInfo.InvariantCulture));
                for (int i = 0; i < _plotted.Count; i++)
                {
                    double v = (double)_plotted[i].Value;
                    values[i] = v;
                    line.Append(';').Append(v.ToString(CultureInfo.InvariantCulture));
                }

                try { _csv?.WriteLine(line.ToString()); } catch (IOException) { /* best-effort export */ }
                sample = new SampleDto(_stopwatch.Elapsed.TotalSeconds, values);
            }
            Sampled?.Invoke(sample);
        }

        public void Stop()
        {
            lock (_gate)
            {
                _sampleTimer?.Stop();
                _sampleTimer?.Dispose();
                _sampleTimer = null;
                try { _csv?.Flush(); _csv?.Dispose(); } catch (IOException) { /* best-effort export */ }
                _csv = null;
                _stopwatch = null;
                IsRecording = false;
                Indices = Array.Empty<int>();
            }
        }

        public static IReadOnlyList<RecordingFileDto> ListFiles()
        {
            var dir = new DirectoryInfo(LogDirectory);
            if (!dir.Exists) return Array.Empty<RecordingFileDto>();
            return dir.GetFiles("*.csv")
                .OrderByDescending(f => f.LastWriteTime)
                .Select(f => new RecordingFileDto(f.Name, f.Length, f.LastWriteTime.ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture)))
                .ToList();
        }

        /// <summary>Maps a file name coming from the browser to a path inside Log/, refusing anything else.</summary>
        public static string? ResolveFile(string name)
        {
            if (!SafeFileName().IsMatch(name)) return null;
            var path = Path.Combine(LogDirectory, name);
            return File.Exists(path) ? path : null;
        }

        [GeneratedRegex(@"^[A-Za-z0-9_.\-]+\.csv$")]
        private static partial Regex SafeFileName();

        public void Dispose() => Stop();
    }
}
