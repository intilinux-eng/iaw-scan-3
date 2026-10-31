using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using IES_2.Coupe.Logging;
using IES_2.Coupe.Web;
using IES_2.ECU;
using IES_2.Session;
using Microsoft.Extensions.Configuration;

namespace IES_2.Coupe.Diagnostics
{
    /// <summary>
    /// The single ECU session this device owns, shared by every phone connected to it. Plays the role
    /// MainWindowViewModel plus the per-screen view models play in the desktop UI, on top of the same
    /// EcuConnectionService/DiagnosticsSession, and publishes every change to the browsers through the
    /// EventHub instead of data binding. Commands return straight away; outcomes arrive as events.
    ///
    /// It is also the data logger's source: with automatic logging on (the default) it connects to the
    /// last-used ECU by itself at power-on, retries until the ECU answers, polls every parameter and
    /// the fault codes regardless of the screen a phone shows, and writes it all to the DataLogger.
    /// </summary>
    public sealed class CoupeDiagnostics : IDisposable
    {
        private const int ValuesPushIntervalMs = 100;
        private const double PhoneAbsenceBeforeLoggingSeconds = 60;

        private readonly object _gate = new();
        private readonly EcuConnectionService _connectionService = new(CoupeCatalog.DemoIso);
        private readonly EventHub _hub;
        private readonly SettingsStore _settings;
        private readonly DataLogger _logger;
        private readonly DeviceClock _clock;
        private readonly Recorder _recorder = new();

        private string _status = Statuses.Disconnected;
        private string? _errorMessage;
        private string? _communicationLostMessage;
        private CancellationTokenSource? _connectCts;

        private DiagnosticsSession? _session;
        private ConnectionDto? _connection;
        private bool[] _checked = Array.Empty<bool>();
        private string?[] _values = Array.Empty<string?>();
        private long _lastValuesPush;
        private IReadOnlyList<ErrorDto> _errors = Array.Empty<ErrorDto>();
        private bool _errorsRead;
        private string _lastErrorsJson = "";
        private string? _errorsStatus;
        private bool _isClearing;
        private string?[] _testResults = Array.Empty<string?>();
        private int? _runningTest;
        private CancellationTokenSource? _activeCts;
        private string _screen = Screens.Dashboard;

        // Data logging.
        private bool _logging;          // the current session writes every parameter to the logbook
        private bool _autoSession;      // the current session (or attempt) was started automatically
        private bool _autoPaused;       // the user disconnected by hand: wait for them, or the next power-on
        private string? _autoStatus;
        private bool _ecuWaitingLogged;
        private bool _cableMissingLogged;
        private int _phones;
        private double _lastPhoneGoneT = double.NegativeInfinity;
        private readonly bool _logSimulation; // development aid: log simulated sessions too (IAW_LOG_SIMULATION=1)

        public CoupeDiagnostics(EventHub hub, SettingsStore settings, DataLogger logger, DeviceClock clock, IConfiguration config)
        {
            _hub = hub;
            _settings = settings;
            _logger = logger;
            _clock = clock;
            _logSimulation = config["IAW_LOG_SIMULATION"] == "1";
            _recorder.Sampled += sample => _hub.Publish("sample", sample);
            _hub.ClientCountChanged += OnPhoneCountChanged;
        }

        public static class Statuses
        {
            public const string Disconnected = "disconnected";
            public const string Connecting = "connecting";
            public const string Connected = "connected";
            public const string CommunicationLost = "commLost";
        }

        public static class Screens
        {
            public const string Dashboard = "dashboard";
            public const string Parameters = "parameters";
            public const string Errors = "errors";
            public const string Tests = "tests";
            public const string Graph = "graph";
            public const string Info = "info";
        }

        public static IReadOnlyList<string> ListPorts() => SerialPorts.List();

        public StateDto Snapshot()
        {
            lock (_gate)
            {
                var parameters = new List<ParameterDto>();
                var tests = new List<TestDto>();
                if (_session != null)
                {
                    var data = _session.Ecu.engineData;
                    for (int i = 0; i < data.Length; i++)
                        parameters.Add(new ParameterDto(i, data[i].Description, data[i].Unit, _checked[i], _values[i]));
                    var activeTests = _session.Ecu.activeTest;
                    for (int i = 0; i < activeTests.Length; i++)
                        tests.Add(new TestDto(i, activeTests[i].Description, activeTests[i].TimeOut, _testResults[i]));
                }

                return new StateDto(
                    _status,
                    _errorMessage,
                    _communicationLostMessage,
                    _connection,
                    parameters,
                    _errors,
                    _errorsRead,
                    _errorsStatus,
                    CanClearCodes(),
                    _isClearing,
                    tests,
                    _runningTest,
                    new RecordingDto(_recorder.IsRecording, _recorder.Indices, _recorder.IsRecording ? _recorder.FileName : null),
                    new LoggerDto(
                        _settings.AutoLog,
                        _autoPaused,
                        _logging,
                        _status == Statuses.Connecting && _autoSession,
                        LoggerStatusText(),
                        _logger.BootSeq,
                        _clock.IsTrusted,
                        _clock.Source,
                        _logger.DataFileName));
            }
        }

        private string LoggerStatusText()
        {
            if (!_settings.AutoLog) return "Registrazione automatica disattivata.";
            if (_logging) return "Registrazione in corso: tutti i parametri e i codici di errore.";
            if (_session is { IsSimulation: true } && !_logSimulation) return "Simulazione in corso: nessun dato registrato.";
            if (_autoPaused) return "In pausa fino alla prossima accensione, o finché non tocchi Riprendi.";
            if (_status == Statuses.Connecting && _autoSession) return "Ricerca della centralina in corso…";
            return _autoStatus ?? "In attesa della centralina.";
        }

        private void PublishState() => _hub.Publish("state", Snapshot());

        /// <summary>Logs events about real ECU sessions only: simulated ones would pollute the logbook.</summary>
        private void LogSession(string type, string message, object? data = null)
        {
            if (_session is { IsSimulation: true } && !_logSimulation) return;
            _logger.Event(type, message, data);
        }

        // ---- Connection --------------------------------------------------------------------

        /// <returns>null if the connection attempt was started, otherwise why not.</returns>
        public string? BeginConnect(string? ecuId, string? port, bool simulation)
        {
            var choice = CoupeCatalog.Find(ecuId);
            if (choice == null) return "Centralina non valida.";

            CancellationTokenSource cts;
            lock (_gate)
            {
                if (_status == Statuses.Connecting && _autoSession)
                    return "La registrazione automatica sta cercando la centralina: mettila in pausa per collegarti a mano.";
                if (_status != Statuses.Disconnected) return "Già connesso o connessione in corso.";
                _status = Statuses.Connecting;
                _autoSession = false;
                _errorMessage = null;
                cts = _connectCts = new CancellationTokenSource();
            }
            PublishState();
            _ = ConnectAsync(choice, port, simulation, cts, auto: false);
            return null;
        }

        private async Task<bool> ConnectAsync(CoupeEcu ecuChoice, string? port, bool simulation, CancellationTokenSource cts, bool auto)
        {
            ConnectResult result;
            try
            {
                result = await _connectionService.ConnectAsync(ecuChoice.Option, port, simulation, cts.Token);
            }
            catch (OperationCanceledException)
            {
                result = ConnectResult.Failure(ConnectError.Cancelled);
            }
            catch (Exception ex)
            {
                result = ConnectResult.Failure(ConnectError.Communication, ex.Message);
            }

            // The connect flow reports success even when nothing answered (as in the desktop UI, where
            // the user sees it at once). Unattended, an ECU that gave neither its ISO nor its spare-part
            // code isn't there yet (ignition still off): drop it and try again later.
            if (auto && result.Success && result.Session != null && !result.EcuIdentified)
            {
                result.Session.Dispose();
                result = ConnectResult.Failure(ConnectError.Communication, "La centralina non risponde.");
            }

            bool connected;
            lock (_gate)
            {
                _connectCts = null;
                cts.Dispose();
                connected = result.Success && result.Session != null;
                if (connected)
                {
                    Attach(ecuChoice.Option.Id, result, port, auto);
                    if (!auto && !simulation)
                    {
                        // What automatic logging connects to from now on (a simulated try doesn't count);
                        // connecting by hand also ends a pause the user started by disconnecting.
                        _settings.SetLastConnection(ecuChoice.Option.Id, port);
                        _autoPaused = false;
                    }
                }
                else
                {
                    _status = Statuses.Disconnected;
                    _autoSession = false;
                    if (auto)
                    {
                        _autoStatus = "In attesa della centralina: quadro spento o centralina che non risponde.";
                        if (!_ecuWaitingLogged)
                        {
                            _logger.Event(EventTypes.EcuWaiting, "La centralina non risponde: nuovo tentativo tra pochi secondi.", new { ecu_id = ecuChoice.Option.Id, port, message = Describe(result) });
                            _ecuWaitingLogged = true;
                        }
                    }
                    else
                    {
                        _errorMessage = Describe(result);
                        if (!simulation)
                            _logger.Event(EventTypes.EcuConnectFailed, "Collegamento alla centralina non riuscito: " + Describe(result), new { ecu_id = ecuChoice.Option.Id, port });
                    }
                }
            }
            PublishState();
            return connected;
        }

        private static string Describe(ConnectResult result) => result.Error switch
        {
            ConnectError.NoPortSelected => "Seleziona una porta.",
            ConnectError.PortOpenFailed => $"Impossibile aprire la porta {result.Detail}",
            ConnectError.Cancelled => "Connessione annullata.",
            ConnectError.InvalidEcu => "Tipo di centralina non valido.",
            ConnectError.CableNotAnswering => "Il lato K-line del cavo non risponde: di solito mancano i suoi 12 V.",
            _ => $"Errore di comunicazione con la centralina: {result.Detail}",
        };

        private void Attach(string ecuId, ConnectResult result, string? port, bool auto)
        {
            var session = result.Session!;
            _session = session;
            _autoSession = auto;
            _ecuWaitingLogged = false;
            _connection = new ConnectionDto(
                ecuId,
                result.EcuTypeDisplayName ?? "-",
                result.IsoCode ?? "-",
                result.RepCode ?? "-",
                result.CarModel ?? "-",
                session.IsSimulation,
                CoupeCatalog.IsCoupeIso(ecu.ISO));

            var dataCount = session.Ecu.engineData.Length;
            _checked = new bool[dataCount];
            _values = new string?[dataCount];
            foreach (var i in _settings.GetSelection(ecuId))
                if (i >= 0 && i < dataCount) _checked[i] = true;

            _testResults = new string?[session.Ecu.activeTest.Length];
            _errors = Array.Empty<ErrorDto>();
            _errorsRead = false;
            _lastErrorsJson = "";
            _errorsStatus = null;
            _status = Statuses.Connected;
            _communicationLostMessage = null;

            _logging = _settings.AutoLog && (!session.IsSimulation || _logSimulation);
            string? dataFile = _logging ? _logger.BeginData(ecuId, DataColumns(session)) : null;
            LogSession(EventTypes.EcuConnected, $"Centralina collegata: {_connection.EcuType}, veicolo {_connection.CarModel}.", new
            {
                ecu_id = ecuId,
                ecu_type = _connection.EcuType,
                car_model = _connection.CarModel,
                iso = _connection.IsoCode,
                rep_code = _connection.RepCode,
                known_coupe = _connection.IsKnownCoupe,
                simulation = session.IsSimulation,
                port,
                auto,
                data_file = dataFile,
            });

            session.ParametersUpdated += OnParametersUpdated;
            session.ErrorsUpdated += OnEngineErrorsUpdated;
            session.ImmoErrorsUpdated += OnErrorsUpdated;
            session.CommunicationLost += OnCommunicationLost;

            ApplyPolling();
            session.StartLoop();
        }

        private static IReadOnlyList<string> DataColumns(DiagnosticsSession session) =>
            session.Ecu.engineData.Select(d => d.Unit.Length > 0 ? $"{d.Description} [{d.Unit}]" : d.Description).ToList();

        public void CancelConnect()
        {
            lock (_gate) _connectCts?.Cancel();
        }

        /// <summary>
        /// Disconnect asked for by a user: automatic logging waits until they connect again by hand
        /// (or the next power-on). Ending a simulation instead lets it go back to the real ECU.
        /// </summary>
        public void Disconnect()
        {
            lock (_gate)
            {
                if (_session == null) return;
                if (_settings.AutoLog && !_session.IsSimulation)
                {
                    if (!_autoPaused)
                        _logger.Event(EventTypes.LoggingChanged, "Registrazione automatica in pausa: centralina disconnessa dall'utente.", new { state = "paused" });
                    _autoPaused = true;
                }
            }
            DisconnectCore("user");
        }

        private void DisconnectCore(string reason)
        {
            DiagnosticsSession session;
            lock (_gate)
            {
                if (_session == null) return;
                session = _session;
                _recorder.Stop();
                _activeCts?.Cancel();
                session.ParametersUpdated -= OnParametersUpdated;
                session.ErrorsUpdated -= OnEngineErrorsUpdated;
                session.ImmoErrorsUpdated -= OnErrorsUpdated;
                session.CommunicationLost -= OnCommunicationLost;

                if (!session.IsSimulation || _logSimulation)
                {
                    var (file, rows) = _logging ? _logger.EndData() : (null, 0);
                    var message = reason switch
                    {
                        "user" => "Centralina disconnessa dall'utente.",
                        "comm_lost" => "Centralina disconnessa: ha smesso di rispondere.",
                        _ => "Centralina disconnessa: arresto del software.",
                    };
                    _logger.Event(EventTypes.EcuDisconnected, message, new { reason, data_file = file, data_rows = rows });
                }

                _session = null;
                _connection = null;
                _logging = false;
                _autoSession = false;
                _status = Statuses.Disconnected;
                _communicationLostMessage = null;
                _errors = Array.Empty<ErrorDto>();
                _runningTest = null;
                _isClearing = false;
            }
            // Outside the lock: Dispose waits for the polling loop, which may itself be waiting on it.
            session.Dispose();
            PublishState();
        }

        private void OnCommunicationLost(Exception ex)
        {
            bool reconnectByItself;
            lock (_gate)
            {
                if (_session == null) return;
                LogSession(EventTypes.CommLost, "La centralina ha smesso di rispondere (quadro spento o collegamento interrotto).", new { message = ex.Message });
                reconnectByItself = _logging;
                if (!reconnectByItself)
                {
                    _status = Statuses.CommunicationLost;
                    _communicationLostMessage = "Comunicazione con la centralina persa. Spegnere il quadro e riprovare.";
                }
            }
            // While logging, close the files at once (the power may be about to go) and let automatic
            // logging find the ECU again. Raised on the polling loop's own thread, which DisconnectCore
            // waits for: hand it off.
            if (reconnectByItself) _ = Task.Run(() => DisconnectCore("comm_lost"));
            else PublishState();
        }

        public string? Reconnect()
        {
            DiagnosticsSession session;
            lock (_gate)
            {
                if (_session == null || _status != Statuses.CommunicationLost) return "Nessuna comunicazione da ripristinare.";
                session = _session;
                _status = Statuses.Connected;
                _communicationLostMessage = null;
            }
            PublishState();
            _ = Task.Run(() =>
            {
                try
                {
                    session.Ecu.InitPasvDiag();
                    session.StartLoop();
                }
                catch (Exception ex)
                {
                    OnCommunicationLost(ex);
                }
            });
            return null;
        }

        // ---- Automatic logging -------------------------------------------------------------

        /// <summary>
        /// One round of the automatic-logging loop (see AutoLogger): when idle, connect to the last-used
        /// ECU on the K-line cable. Returns once the attempt is over, connected or not.
        /// </summary>
        public async Task AutoConnectStepAsync()
        {
            CoupeEcu choice;
            string? port;
            CancellationTokenSource? cts = null;
            lock (_gate)
            {
                if (!_settings.AutoLog || _autoPaused || _status != Statuses.Disconnected) return;

                choice = CoupeCatalog.Find(_settings.LastEcu) ?? CoupeCatalog.Ecus[0];
                var ports = ListPorts();
                port = _settings.LastPort != null && ports.Contains(_settings.LastPort)
                    ? _settings.LastPort
                    : ports.FirstOrDefault(p => p.Contains("ttyUSB", StringComparison.Ordinal) || p.Contains("ttyACM", StringComparison.Ordinal));
                if (port == null)
                {
                    if (!_cableMissingLogged)
                    {
                        _logger.Event(EventTypes.CableMissing, "Nessun cavo K-line collegato al dispositivo.");
                        _cableMissingLogged = true;
                    }
                    if (_autoStatus == CableMissingText) return;
                    _autoStatus = CableMissingText;
                }
                else
                {
                    _cableMissingLogged = false;
                    _status = Statuses.Connecting;
                    _autoSession = true;
                    _errorMessage = null;
                    cts = _connectCts = new CancellationTokenSource();
                }
            }
            PublishState();
            if (port != null && cts != null)
                await ConnectAsync(choice, port, simulation: false, cts, auto: true);
        }

        private const string CableMissingText = "Cavo K-line non collegato al dispositivo.";

        public void SetAutoLog(bool enabled)
        {
            lock (_gate)
            {
                if (_settings.AutoLog == enabled) return;
                _settings.AutoLog = enabled;
                _autoPaused = false;
                _logger.Event(EventTypes.LoggingChanged, enabled ? "Registrazione automatica attivata." : "Registrazione automatica disattivata.", new { state = enabled ? "on" : "off" });

                // Switch a live real session over without disconnecting whoever is using it.
                if (_session is { } session && (!session.IsSimulation || _logSimulation) && _connection != null)
                {
                    if (enabled && !_logging)
                    {
                        _logging = true;
                        _logger.BeginData(_connection.EcuId, DataColumns(session));
                    }
                    else if (!enabled && _logging)
                    {
                        _logger.EndData();
                        _logging = false;
                        for (int i = 0; i < _values.Length; i++)
                            if (!_checked[i]) _values[i] = null;
                    }
                    ApplyPolling();
                }
            }
            PublishState();
        }

        public void PauseAuto()
        {
            lock (_gate)
            {
                if (!_settings.AutoLog || _autoPaused) return;
                _autoPaused = true;
                if (_autoSession) _connectCts?.Cancel();
                _logger.Event(EventTypes.LoggingChanged, "Registrazione automatica messa in pausa dall'utente.", new { state = "paused" });
            }
            PublishState();
        }

        public void ResumeAuto()
        {
            lock (_gate)
            {
                if (!_autoPaused) return;
                _autoPaused = false;
                _logger.Event(EventTypes.LoggingChanged, "Registrazione automatica ripresa.", new { state = "on" });
            }
            PublishState();
        }

        public void OnClockSynced(ClockSync sync)
        {
            _logger.Event(EventTypes.ClockSync,
                sync.SystemClockSet ? "Data e ora prese dal telefono." : "Data e ora prese dal telefono (orologio di sistema non modificabile: usate solo dal registro).",
                new
                {
                    utc = DataLogger.FormatUtc(sync.Utc),
                    t = Math.Round(sync.T, 3),
                    drift_s = Math.Round(sync.DriftSeconds, 1),
                    system_before = DataLogger.FormatUtc(sync.SystemBefore),
                    system_clock_set = sync.SystemClockSet,
                    time_zone = sync.TimeZone,
                });
            PublishState();
        }

        private void OnPhoneCountChanged(int count)
        {
            lock (_gate)
            {
                var previous = _phones;
                _phones = count;
                // Browsers drop and reopen the event stream whenever the phone dozes: only a phone
                // showing up after a real absence is worth a line in the logbook.
                if (previous == 0 && count > 0 && _clock.Now - _lastPhoneGoneT > PhoneAbsenceBeforeLoggingSeconds)
                    _logger.Event(EventTypes.PhoneConnected, "Un telefono ha aperto l'interfaccia.");
                if (count == 0) _lastPhoneGoneT = _clock.Now;
            }
        }

        // ---- Screens & parameters ----------------------------------------------------------

        /// <summary>
        /// Without logging, what the passive loop polls follows the screen most recently opened on any
        /// phone, as the desktop UI follows its selected tab (and while recording, parameters keep
        /// being polled everywhere but Errors). While logging, everything is polled all the time.
        /// </summary>
        public void SetScreen(string screen)
        {
            lock (_gate)
            {
                _screen = screen;
                ApplyPolling();
            }
        }

        private void ApplyPolling()
        {
            if (_session == null) return;
            if (_logging)
            {
                var enabled = _session.RequestEnabled;
                Array.Clear(enabled);
                foreach (var data in _session.Ecu.engineData)
                    foreach (var req in data.RequestSet)
                        enabled[req] = true;
                _session.PollEverything();
                return;
            }
            ApplyRequestSelection();
            _session.SetActiveScreen(_screen switch
            {
                Screens.Errors => DiagnosticsScreen.Errors,
                Screens.Dashboard or Screens.Parameters or Screens.Graph => DiagnosticsScreen.Parameters,
                _ => _recorder.IsRecording ? DiagnosticsScreen.Parameters : DiagnosticsScreen.None,
            });
        }

        public string? SetParameterChecked(int index, bool isChecked)
        {
            lock (_gate)
            {
                if (_session == null) return "Non connesso.";
                if (index < 0 || index >= _checked.Length) return "Parametro non valido.";
                if (_recorder.IsRecording && !isChecked && _recorder.Indices.Contains(index))
                    return "Il parametro è in registrazione: interrompi prima la registrazione.";
                _checked[index] = isChecked;
                if (!isChecked && !_logging) _values[index] = null;
                ApplyPolling();
                SaveSelection();
            }
            PublishState();
            return null;
        }

        public string? SetAllParametersChecked(bool isChecked)
        {
            lock (_gate)
            {
                if (_session == null) return "Non connesso.";
                if (_recorder.IsRecording && !isChecked)
                    return "Registrazione in corso: interrompi prima la registrazione.";
                for (int i = 0; i < _checked.Length; i++)
                {
                    _checked[i] = isChecked;
                    if (!isChecked && !_logging) _values[i] = null;
                }
                ApplyPolling();
                SaveSelection();
            }
            PublishState();
            return null;
        }

        /// <summary>
        /// A request byte is polled while at least one checked parameter needs it (some, like the
        /// MAP sensor or the status flags, are shared by several parameters).
        /// </summary>
        private void ApplyRequestSelection()
        {
            if (_session == null) return;
            var enabled = _session.RequestEnabled;
            Array.Clear(enabled);
            var data = _session.Ecu.engineData;
            for (int i = 0; i < data.Length; i++)
                if (_checked[i])
                    foreach (var req in data[i].RequestSet)
                        enabled[req] = true;
        }

        private void SaveSelection()
        {
            if (_connection == null) return;
            var selected = new List<int>();
            for (int i = 0; i < _checked.Length; i++)
                if (_checked[i]) selected.Add(i);
            _settings.SetSelection(_connection.EcuId, selected);
        }

        private void OnParametersUpdated()
        {
            string?[] values;
            lock (_gate)
            {
                if (_session == null) return;
                var data = _session.Ecu.engineData;
                double?[]? row = _logging ? new double?[data.Length] : null;
                for (int i = 0; i < data.Length; i++)
                {
                    if (!_checked[i] && !_logging) continue;
                    var valid = ecu.Valid[data[i].RequestSet[0]];
                    _values[i] = valid ? data[i].FormattedValue : "#ERR";
                    if (row != null && valid) row[i] = (double)data[i].Value;
                }
                if (row != null) _logger.Row(row);

                var now = Environment.TickCount64;
                if (now - _lastValuesPush < ValuesPushIntervalMs) return;
                _lastValuesPush = now;
                values = (string?[])_values.Clone();
            }
            _hub.Publish("values", values);
        }

        // ---- Errors ------------------------------------------------------------------------

        /// <summary>
        /// With an immobiliser, the polling loop reads its codes right after the engine's in the same
        /// cycle and raises ImmoErrorsUpdated then: decoding here already would publish (and log) a
        /// half-read list whose immobiliser part is still the previous cycle's.
        /// </summary>
        private void OnEngineErrorsUpdated()
        {
            if (_session?.Ecu.hasIMMO != true) OnErrorsUpdated();
        }

        private void OnErrorsUpdated()
        {
            IReadOnlyList<ErrorDto> errors;
            lock (_gate)
            {
                if (_session == null) return;
                var list = new List<ErrorDto>();
                Decode(_session.Ecu.engineErrors, list);
                if (_session.Ecu.hasIMMO)
                    Decode(_session.Ecu.immoErrors, list);

                var firstRead = !_errorsRead;
                _errorsRead = true;
                var json = JsonSerializer.Serialize(list);
                if (json == _lastErrorsJson) return;
                _lastErrorsJson = json;
                LogErrors(_errors, list, firstRead);
                _errors = errors = list;
            }
            _hub.Publish("errors", errors);
        }

        private void LogErrors(IReadOnlyList<ErrorDto> before, IReadOnlyList<ErrorDto> now, bool firstRead)
        {
            var current = now.Select(ErrorLog).ToList();
            if (firstRead)
            {
                LogSession(EventTypes.ErrorsRead,
                    now.Count == 0 ? "Codici di errore letti: nessun errore." : $"Codici di errore letti: {now.Count} presenti.",
                    new { errors = current });
                return;
            }
            var added = now.Where(e => !before.Any(b => b.Description == e.Description)).Select(ErrorLog).ToList();
            var removed = before.Where(b => !now.Any(e => e.Description == b.Description)).Select(ErrorLog).ToList();
            var parts = new List<string>();
            if (added.Count > 0) parts.Add("comparsi: " + string.Join(", ", added.Select(e => e.Description)));
            if (removed.Count > 0) parts.Add("scomparsi: " + string.Join(", ", removed.Select(e => e.Description)));
            if (parts.Count == 0) parts.Add("cambiato lo stato");
            LogSession(EventTypes.ErrorsChanged, "Codici di errore cambiati (" + string.Join("; ", parts) + ").",
                new { errors = current, added, removed });
        }

        private static ErrorLogEntry ErrorLog(ErrorDto e) => new(e.Description, e.Reason, e.State, e.MilOn);

        private sealed record ErrorLogEntry(string Description, string Reason, string State, bool Mil);

        /// <summary>Same visibility, state labels and MIL rule as the desktop ErrorsViewModel.</summary>
        private static void Decode(errorElement[] errors, List<ErrorDto> into)
        {
            for (int i = 0; i < errors.Length; i++)
            {
                errors[i].Decode();
                var e = errors[i];
                if (!(e.isActive || e.isVerified || e.isStored)) continue;
                var state = ((e.isActive ? "Att. " : "") + (e.isVerified ? "Ver. " : "") + (e.isStored ? "Mem. " : "")).Trim();
                // Some upstream descriptions carry stray trailing spaces; trimmed for the logbook's sake.
                into.Add(new ErrorDto(e.Description.Trim(), (e.Reason ?? "").Trim(), state, e.isActive && (e.isVerified || e.isStored)));
            }
        }

        private bool CanClearCodes() =>
            _status == Statuses.Connected && _session is { IsSimulation: false } && !_isClearing && _runningTest == null;

        public string? BeginClearCodes()
        {
            DiagnosticsSession session;
            CancellationTokenSource cts;
            lock (_gate)
            {
                if (_session == null) return "Non connesso.";
                if (_session.IsSimulation) return "L'azzeramento dei codici non è disponibile in modalità simulazione.";
                if (!CanClearCodes()) return "Un'altra operazione è in corso.";
                session = _session;
                _isClearing = true;
                _errorsStatus = null;
                cts = _activeCts = new CancellationTokenSource();
            }
            PublishState();
            _ = Task.Run(async () =>
            {
                ActiveOutcome outcome;
                try { outcome = await session.ClearCodesAsync(cts.Token); }
                catch (OperationCanceledException) { outcome = ActiveOutcome.Cancelled; }
                catch (Exception) { outcome = ActiveOutcome.Failed; } // e.g. port closed by a disconnect mid-operation
                lock (_gate)
                {
                    if (_session != session) return; // disconnected meanwhile
                    _isClearing = false;
                    _activeCts = null;
                    _errorsStatus = outcome switch
                    {
                        ActiveOutcome.Passed => "Codici di errore azzerati.",
                        ActiveOutcome.Cancelled => "Operazione annullata.",
                        ActiveOutcome.InitFailed => "Impossibile avviare la diagnosi attiva.",
                        _ => "Azzeramento non riuscito.",
                    };
                    LogSession(EventTypes.ClearCodes, "Azzeramento codici di errore: " + _errorsStatus, new { outcome = outcome.ToString() });
                }
                cts.Dispose();
                PublishState();
            });
            return null;
        }

        // ---- Actuator tests ----------------------------------------------------------------

        public string? BeginTest(int index)
        {
            DiagnosticsSession session;
            CancellationTokenSource cts;
            testElement test;
            lock (_gate)
            {
                if (_session == null || _status != Statuses.Connected) return "Non connesso.";
                if (index < 0 || index >= _session.Ecu.activeTest.Length) return "Test non valido.";
                if (_runningTest != null || _isClearing) return "Un'altra operazione è in corso.";
                session = _session;
                test = session.Ecu.activeTest[index];
                _runningTest = index;
                _testResults[index] = "In corso...";
                cts = _activeCts = new CancellationTokenSource();
            }
            PublishState();
            _ = Task.Run(async () =>
            {
                ActiveOutcome outcome;
                try { outcome = await session.RunTestAsync(test, cts.Token); }
                catch (OperationCanceledException) { outcome = ActiveOutcome.Cancelled; }
                catch (Exception) { outcome = ActiveOutcome.Failed; } // e.g. port closed by a disconnect mid-test
                lock (_gate)
                {
                    // A disconnect meanwhile already reset this state (and a new session may own it now).
                    if (_session == session)
                    {
                        _testResults[index] = DescribeTest(outcome);
                        _runningTest = null;
                        _activeCts = null;
                        LogSession(EventTypes.ActuatorTest, $"Test \"{test.Description}\": {_testResults[index]}.", new { test = test.Description, outcome = outcome.ToString() });
                    }
                }
                cts.Dispose();
                PublishState();
            });
            return null;
        }

        public void CancelTest()
        {
            lock (_gate)
                if (_runningTest != null) _activeCts?.Cancel();
        }

        /// <summary>Same wording as the desktop TestsViewModel.</summary>
        private static string DescribeTest(ActiveOutcome outcome) => outcome switch
        {
            ActiveOutcome.Demo => "DEMO",
            ActiveOutcome.Passed => "Superato",
            ActiveOutcome.Timeout => "Scaduto",
            ActiveOutcome.InitFailed => "Avvio fallito",
            ActiveOutcome.Cancelled => "Annullato",
            _ => "Fallito",
        };

        // ---- Recording ---------------------------------------------------------------------

        /// <returns>Whether recording started, plus an error or (when started) a warning for the user.</returns>
        public (bool Started, string? Message) StartRecording(IReadOnlyList<int> indices)
        {
            string? message;
            lock (_gate)
            {
                if (_session == null || _status != Statuses.Connected) return (false, "Non connesso.");
                var data = _session.Ecu.engineData;
                var plotted = indices.Distinct()
                    .Where(i => i >= 0 && i < data.Length && _checked[i])
                    .Select(i => (i, data[i]))
                    .ToList();
                message = _recorder.Start(plotted);
                if (!_recorder.IsRecording) return (false, message);
                ApplyPolling();
                LogSession(EventTypes.RecordingStarted, "Registrazione del grafico avviata.", new { file = _recorder.FileName, parameters = plotted.Select(p => p.Item2.Description).ToList() });
            }
            PublishState();
            return (true, message);
        }

        public void StopRecording()
        {
            lock (_gate)
            {
                if (!_recorder.IsRecording) return;
                var file = _recorder.FileName;
                _recorder.Stop();
                ApplyPolling();
                LogSession(EventTypes.RecordingStopped, "Registrazione del grafico interrotta.", new { file });
            }
            PublishState();
        }

        public bool IsRecordingFile(string name)
        {
            lock (_gate) return _recorder.IsRecording && _recorder.FileName == name;
        }

        public void Dispose()
        {
            _hub.ClientCountChanged -= OnPhoneCountChanged;
            DisconnectCore("shutdown");
            _recorder.Dispose();
        }
    }
}
