using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using IES_2.Avalonia.Models;
using IES_2.Avalonia.Services;
using IES_2.ECU;
using IES_2.Session;

namespace IES_2.Avalonia.ViewModels
{
    /// <summary>
    /// Everything about the ECU connected right now, shared by all the pages: the parameters and their
    /// history, the fault codes, and the session being written to the log. The polling loop never touches
    /// the UI: it captures readings on its own thread, and a 10 Hz timer brings them to the screen, so a
    /// fast ECU can't flood the interface and a slow screen can't hold up the ECU.
    /// </summary>
    public sealed partial class LiveSession : ObservableObject, IDisposable
    {
        private const double CaptureIntervalSeconds = 0.1;

        private readonly AppSettings _settings;
        private readonly SessionStore _store;
        private readonly Stopwatch _clock = Stopwatch.StartNew();
        private readonly DispatcherTimer _uiTimer;
        private readonly object _gate = new();
        private ParameterRow[] _rows = Array.Empty<ParameterRow>();
        private string?[] _latestText = Array.Empty<string?>();
        private double?[] _latestValue = Array.Empty<double?>();
        private bool _hasNewValues;
        private double _lastCapture = double.NegativeInfinity;
        private List<FaultRow> _lastFaults = new();
        private string _lastFaultsKey = "";
        private readonly Dictionary<string, string> _firstSeen = new();
        private bool _disposed;

        public DiagnosticsSession Session { get; }
        public EcuOption Ecu { get; }
        public string? Port { get; }
        public ConnectResult Connection { get; }
        public SessionRecorder? Recorder { get; }
        public ObservableCollection<ParameterRow> Rows { get; } = new();

        /// <summary>The parameters being read, in dashboard order.</summary>
        public ObservableCollection<ParameterRow> Selected { get; } = new();

        public ObservableCollection<FaultRow> Faults { get; } = new();
        public ObservableCollection<FaultHistoryRow> FaultHistory { get; } = new();

        /// <summary>Raised on the UI thread after new values reach the rows; charts repaint from it.</summary>
        public event Action? Ticked;

        public event Action? SelectionChanged;

        [ObservableProperty]
        private bool faultsRead;

        [ObservableProperty]
        private bool isCommunicationLost;

        [ObservableProperty]
        private bool isRecording;

        public bool IsSimulation => Session.IsSimulation;
        public string EcuType => Connection.EcuTypeDisplayName ?? "-";
        public string CarModel => Connection.CarModel ?? "-";
        public string IsoCode => Connection.IsoCode ?? "-";
        public string RepCode => Connection.RepCode ?? "-";

        public LiveSession(ConnectResult connection, EcuOption option, string? port, AppSettings settings, SessionStore store)
        {
            Connection = connection;
            Session = connection.Session!;
            Ecu = option;
            Port = port;
            _settings = settings;
            _store = store;

            Array.Clear(ecu.Valid);

            var data = Session.Ecu.engineData;
            _rows = new ParameterRow[data.Length];
            for (int i = 0; i < data.Length; i++)
            {
                var row = new ParameterRow(i, data[i]);
                _rows[i] = row;
                Rows.Add(row);
            }
            _latestText = new string?[_rows.Length];
            _latestValue = new double?[_rows.Length];

            Recorder = CreateRecorder();
            Recorder?.Event(SessionEventTypes.Connected, new JsonObject
            {
                ["ecu_id"] = option.Id,
                ["ecu_type"] = EcuType,
                ["vehicle"] = CarModel,
                ["iso"] = IsoCode,
                ["rep_code"] = RepCode,
                ["simulation"] = IsSimulation,
                ["port"] = port,
                ["ecu_identified"] = connection.EcuIdentified,
            });

            RestoreSelection();
            foreach (var row in _rows) row.PropertyChanged += OnRowPropertyChanged;

            Session.ParametersUpdated += OnParametersUpdated;
            Session.ErrorsUpdated += OnEngineFaultsUpdated;
            Session.ImmoErrorsUpdated += UpdateFaults;
            Session.CommunicationLost += OnCommunicationLost;

            _uiTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(100), DispatcherPriority.Background, (_, _) => ApplyLatestValues());
            _uiTimer.Start();

            if (_settings.AutoRecord && !IsSimulation) StartRecording();

            // Parameters and fault codes together, whatever page is open: the fault badge is always
            // live, and a recording never stops because of where the user is looking.
            Session.PollEverything();
            Session.StartLoop();
        }

        private SessionRecorder? CreateRecorder()
        {
            try
            {
                return new SessionRecorder(_store.Root, new SessionHeader
                {
                    AppVersion = AppInfo.Version,
                    EcuId = Ecu.Id,
                    EcuType = EcuType,
                    Vehicle = CarModel,
                    IsoCode = IsoCode,
                    RepCode = RepCode,
                    Simulation = IsSimulation,
                    Port = Port,
                    StartedUtc = DateTime.UtcNow,
                    Columns = _rows.Select(r => new SessionColumn(r.Description, r.Unit)).ToList(),
                });
            }
            catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException)
            {
                // No disk space or no permission: the diagnosis still works, just without the log.
                return null;
            }
        }

        // ---- Selection --------------------------------------------------------------------

        private void RestoreSelection()
        {
            IEnumerable<int> saved = _settings.Selection.TryGetValue(Ecu.Id, out var indices)
                ? indices
                : DefaultSelection();
            foreach (var index in saved.Where(i => i >= 0 && i < _rows.Length).Distinct())
            {
                var row = _rows[index];
                row.IsChecked = true;
                Selected.Add(row);
            }
            ApplySelection();
        }

        /// <summary>First time with this ECU: a useful dashboard (speed, temperatures, battery, advance, pressure).</summary>
        private IEnumerable<int> DefaultSelection()
        {
            var wanted = new[] { IES_2.Res.lang.rpm, "°C", "°C", "V", "°", "hPa" };
            var taken = new HashSet<int>();
            foreach (var unit in wanted)
            {
                var row = _rows.FirstOrDefault(r => r.Unit == unit && !taken.Contains(r.Index));
                if (row == null) continue;
                taken.Add(row.Index);
                yield return row.Index;
            }
            if (taken.Count == 0 && _rows.Length > 0) yield return 0;
        }

        private void OnRowPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName != nameof(ParameterRow.IsChecked) || sender is not ParameterRow row) return;
            if (row.IsChecked)
            {
                if (!Selected.Contains(row)) Selected.Add(row);
            }
            else
            {
                Selected.Remove(row);
                row.ClearReading();
            }
            ApplySelection();
            SaveSelection();
            SelectionChanged?.Invoke();
        }

        /// <summary>
        /// A request byte is polled while at least one selected parameter needs it: several parameters
        /// can share one byte (the status flags, the two scales of the MAP sensor), and turning one of
        /// them off must not freeze the others.
        /// </summary>
        private void ApplySelection()
        {
            var enabled = Session.RequestEnabled;
            var wanted = new bool[enabled.Length];
            foreach (var row in _rows)
            {
                row.IsPolled = row.IsChecked;
                if (!row.IsChecked) continue;
                row.EnsureBuffer();
                foreach (var request in row.Data.RequestSet) wanted[request] = true;
            }
            Array.Copy(wanted, enabled, enabled.Length);
        }

        public void SaveSelection()
        {
            _settings.Selection[Ecu.Id] = Selected.Select(r => r.Index).ToArray();
            _settings.Save();
        }

        // ---- Live values ------------------------------------------------------------------

        /// <summary>On the polling loop's thread: must stay quick and touch nothing on screen.</summary>
        private void OnParametersUpdated()
        {
            var t = _clock.Elapsed.TotalSeconds;
            if (t - _lastCapture < CaptureIntervalSeconds) return;
            _lastCapture = t;

            var rows = _rows;
            var texts = new string?[rows.Length];
            var values = new double?[rows.Length];
            for (int i = 0; i < rows.Length; i++)
            {
                var row = rows[i];
                if (!row.IsPolled) continue;
                if (row.HasValidAnswer)
                {
                    var value = (double)row.Data.Value;
                    values[i] = value;
                    texts[i] = row.Data.FormattedValue;
                    row.Buffer?.Add(t, value);
                }
                else
                {
                    row.Buffer?.Add(t, double.NaN);
                }
            }

            lock (_gate)
            {
                _latestText = texts;
                _latestValue = values;
                _hasNewValues = true;
            }
            Recorder?.Row(values);
        }

        private void ApplyLatestValues()
        {
            string?[] texts;
            double?[] values;
            lock (_gate)
            {
                if (!_hasNewValues) return;
                _hasNewValues = false;
                texts = _latestText;
                values = _latestValue;
            }
            foreach (var row in _rows)
                if (row.IsChecked) row.Apply(texts[row.Index], values[row.Index]);
            Ticked?.Invoke();
        }

        // ---- Fault codes ------------------------------------------------------------------

        /// <summary>
        /// With an immobiliser the loop reads its codes right after the engine's in the same cycle:
        /// decoding here already would publish a half-read list.
        /// </summary>
        private void OnEngineFaultsUpdated()
        {
            if (!Session.Ecu.hasIMMO) UpdateFaults();
        }

        /// <summary>On the polling loop's thread. Decoding has the same rules as the original program's error list.</summary>
        private void UpdateFaults()
        {
            var faults = new List<FaultRow>();
            Decode(Session.Ecu.engineErrors, faults);
            if (Session.Ecu.hasIMMO) Decode(Session.Ecu.immoErrors, faults);

            var key = string.Join("\n", faults.Select(f => $"{f.Description}|{f.Reason}|{f.IsActive}|{f.IsVerified}|{f.IsStored}"));
            var firstRead = !_faultsReadOnce;
            _faultsReadOnce = true;
            if (!firstRead && key == _lastFaultsKey) return;
            _lastFaultsKey = key;

            var before = _lastFaults;
            _lastFaults = faults;
            var added = firstRead ? faults : faults.Where(f => before.All(b => b.Description != f.Description)).ToList();
            var removed = firstRead ? new List<FaultRow>() : before.Where(b => faults.All(f => f.Description != b.Description)).ToList();
            LogFaults(faults, added, removed, firstRead);

            Dispatcher.UIThread.Post(() => ApplyFaults(faults, added, removed));
        }

        private bool _faultsReadOnce;

        private void Decode(errorElement[] errors, List<FaultRow> into)
        {
            for (int i = 0; i < errors.Length; i++)
            {
                errors[i].Decode();
                var e = errors[i];
                if (!(e.isActive || e.isVerified || e.isStored)) continue;
                var description = e.Description.Trim();
                if (!_firstSeen.TryGetValue(description, out var firstSeen))
                {
                    firstSeen = DateTime.Now.ToString("HH:mm:ss");
                    _firstSeen[description] = firstSeen;
                }
                into.Add(new FaultRow(description, (e.Reason ?? "").Trim(), e.isActive, e.isVerified, e.isStored,
                    e.isActive && (e.isVerified || e.isStored), firstSeen));
            }
        }

        private void LogFaults(List<FaultRow> faults, List<FaultRow> added, List<FaultRow> removed, bool firstRead)
        {
            if (Recorder == null) return;
            if (firstRead)
            {
                Recorder.Event(SessionEventTypes.FaultsRead, new JsonObject { ["faults"] = ToJson(faults) });
                return;
            }
            Recorder.Event(SessionEventTypes.FaultsChanged, new JsonObject
            {
                ["faults"] = ToJson(faults),
                ["added"] = ToJson(added),
                ["removed"] = ToJson(removed),
            });
        }

        private static JsonArray ToJson(IEnumerable<FaultRow> faults)
        {
            var array = new JsonArray();
            foreach (var f in faults)
            {
                var states = new List<string>();
                if (f.IsActive) states.Add("Active");
                if (f.IsVerified) states.Add("Verified");
                if (f.IsStored) states.Add("Stored");
                array.Add((JsonNode)new JsonObject
                {
                    ["description"] = f.Description,
                    ["reason"] = f.Reason,
                    ["state"] = string.Join(",", states),
                    ["mil"] = f.MilOn,
                });
            }
            return array;
        }

        private void ApplyFaults(List<FaultRow> faults, List<FaultRow> added, List<FaultRow> removed)
        {
            if (_disposed) return;
            FaultsRead = true;
            Faults.Clear();
            foreach (var fault in faults) Faults.Add(fault);
            var time = DateTime.Now.ToString("HH:mm:ss");
            foreach (var fault in added) FaultHistory.Insert(0, new FaultHistoryRow(time, fault.Description, true));
            foreach (var fault in removed) FaultHistory.Insert(0, new FaultHistoryRow(time, fault.Description, false));
        }

        // ---- Communication ----------------------------------------------------------------

        private void OnCommunicationLost(Exception ex)
        {
            Recorder?.Event(SessionEventTypes.CommLost, new JsonObject { ["message"] = ex.Message });
            Dispatcher.UIThread.Post(() => IsCommunicationLost = true);
        }

        /// <summary>Off the UI thread: re-initialising the ECU's diagnostic mode takes a moment.</summary>
        public async Task ReconnectAsync()
        {
            IsCommunicationLost = false;
            try
            {
                await Session.ReconnectAsync();
                Recorder?.Event(SessionEventTypes.CommRestored);
            }
            catch (Exception ex)
            {
                OnCommunicationLost(ex);
            }
        }

        // ---- Recording --------------------------------------------------------------------

        public void StartRecording()
        {
            if (Recorder == null) return;
            Recorder.StartData();
            IsRecording = true;
        }

        public void StopRecording()
        {
            Recorder?.StopData();
            IsRecording = false;
        }

        // ---- End of the session -----------------------------------------------------------

        /// <summary>Safe off the UI thread: closing waits for the polling loop to notice.</summary>
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            Dispatcher.UIThread.Post(() => _uiTimer.Stop());
            Session.ParametersUpdated -= OnParametersUpdated;
            Session.ErrorsUpdated -= OnEngineFaultsUpdated;
            Session.ImmoErrorsUpdated -= UpdateFaults;
            Session.CommunicationLost -= OnCommunicationLost;
            Session.Dispose();

            if (Recorder == null) return;
            Recorder.Event(SessionEventTypes.Disconnected);
            var rows = Recorder.DataRows;
            var id = Recorder.Id;
            Recorder.Dispose();
            try
            {
                // A simulated try that recorded nothing isn't worth an entry in the log.
                if (IsSimulation && rows == 0) _store.Delete(id);
                _store.Prune(_settings.MaxLogMegabytes * 1024L * 1024L, null);
            }
            catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException)
            {
                // Housekeeping only; it runs again at the next session.
            }
        }
    }
}
