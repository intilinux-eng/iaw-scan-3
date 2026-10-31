using System;
using System.Diagnostics;
using System.IO;
using System.IO.Ports;
using System.Threading;
using System.Threading.Tasks;
using IES_2.ECU;

namespace IES_2.Session
{
    public enum DiagnosticsScreen
    {
        None,
        Parameters,
        Errors,
        Tests,
        Graphs,
        Adjustments,
    }

    public enum ActiveOutcome
    {
        Demo,
        Passed,
        Failed,
        Timeout,
        InitFailed,
        Cancelled,
    }

    /// <summary>
    /// Owns a connected (or simulated) ECU for the lifetime of one session: runs the
    /// passive-diagnostics polling loop and executes one-off active-diagnostics operations
    /// (tests, adjustments, clear-codes), pausing/resuming the passive loop around them
    /// exactly like the original program did.
    /// </summary>
    public sealed class DiagnosticsSession : IDisposable
    {
        private const int DemoParamDelayMs = 100;
        private const int DemoErrorDelayMs = 75;
        private const int DemoImmoDelayMs = 25;
        private const int IdleDelayMs = 20;

        private SerialPort _port;
        private readonly Random _random = new();
        private readonly SimulatedByte?[] _simulated = new SimulatedByte?[255];
        private readonly SemaphoreSlim _activeOpGate = new(1, 1);

        private volatile int _queryFlag;
        private volatile int _pasvDelayMs = 4;
        private CancellationTokenSource? _loopCts;
        private Task? _loopTask;
        private bool _disposed;

        public ecu Ecu { get; }
        public bool IsSimulation { get; }
        public string EcuTypeDisplayName { get; }
        public string IsoCodeText { get; }
        public string RepCodeText { get; }
        public string CarModelText { get; }

        /// <summary>Indexed by request byte: which requests the passive loop should poll for.</summary>
        public bool[] RequestEnabled { get; } = new bool[255];

        public event Action? ParametersUpdated;
        public event Action? ErrorsUpdated;
        public event Action? ImmoErrorsUpdated;
        public event Action<Exception>? CommunicationLost;

        internal DiagnosticsSession(ecu ecuInstance, SerialPort port, bool simulation,
            string ecuTypeDisplayName, string isoCodeText, string repCodeText, string carModelText)
        {
            Ecu = ecuInstance;
            _port = port;
            IsSimulation = simulation;
            EcuTypeDisplayName = ecuTypeDisplayName;
            IsoCodeText = isoCodeText;
            RepCodeText = repCodeText;
            CarModelText = carModelText;
        }

        public void SetActiveScreen(DiagnosticsScreen screen)
        {
            _queryFlag = screen switch
            {
                DiagnosticsScreen.Parameters or DiagnosticsScreen.Graphs => 1,
                DiagnosticsScreen.Errors => 2,
                _ => 0,
            };
        }

        /// <summary>
        /// Polls live parameters and fault codes together instead of following one screen: the loop
        /// already interleaves both (fault codes every fifth cycle). Used for unattended data logging.
        /// </summary>
        public void PollEverything() => _queryFlag = 3;

        public void StartLoop()
        {
            if (_disposed) return;
            _loopCts = new CancellationTokenSource();
            var token = _loopCts.Token;
            _loopTask = Task.Run(() => PassiveLoop(token));
        }

        private async Task PauseLoopAsync()
        {
            var cts = _loopCts;
            var task = _loopTask;
            _loopCts = null;
            _loopTask = null;
            if (cts == null) return;
            cts.Cancel();
            if (task != null)
            {
                try { await task.ConfigureAwait(false); }
                catch { /* loop already reports faults via CommunicationLost */ }
            }
            cts.Dispose();
        }

        private void PassiveLoop(CancellationToken ct)
        {
            try
            {
                if (!IsSimulation)
                    ecu.SetReadTimeout(ref _port, 300);

                int comErrCnt = 0;
                byte loopCnt = 0;

                while (!ct.IsCancellationRequested)
                {
                    int flag = _queryFlag;

                    if (IsSimulation)
                        RunSimulatedCycle(flag, ct);
                    else
                        comErrCnt = RunHardwareCycle(flag, loopCnt, comErrCnt, ct);

                    if (flag == 0)
                        Thread.Sleep(IdleDelayMs);

                    loopCnt = (byte)((loopCnt + 1) % 5);
                }
            }
            catch (OperationCanceledException)
            {
                // Normal stop (pause for an active test/adjustment, or disconnect).
            }
            catch (Exception ex)
            {
                CommunicationLost?.Invoke(ex);
            }
        }

        private void RunSimulatedCycle(int flag, CancellationToken ct)
        {
            if ((flag & 1) == 1)
            {
                var seconds = Environment.TickCount64 / 1000.0;
                for (int req = 1; req < RequestEnabled.Length; req++)
                {
                    if (!RequestEnabled[req]) continue;
                    var wave = _simulated[req] ??= new SimulatedByte(_random, PlausibleRawValue(req));
                    ecu.Buffer[req] = wave.At(seconds, _random);
                    ecu.Valid[req] = true;
                }
                Thread.Sleep(DemoParamDelayMs);
                ParametersUpdated?.Invoke();
            }
            if ((flag & 2) == 2)
            {
                ecu.Buffer[0x10] = 3;
                ecu.Buffer[0x14] = 10;
                ecu.Buffer[0x2E] = 8;
                Thread.Sleep(DemoErrorDelayMs);
                ErrorsUpdated?.Invoke();
                ecu.Buffer[0x71] = 1;
                Thread.Sleep(DemoImmoDelayMs);
                ImmoErrorsUpdated?.Invoke();
            }
            Thread.Sleep(25);
            ct.ThrowIfCancellationRequested();
        }

        private int RunHardwareCycle(int flag, byte loopCnt, int comErrCnt, CancellationToken ct)
        {
            if ((flag & 1) == 1)
            {
                bool isEmpty = true;
                for (int req = 1; req < RequestEnabled.Length; req++)
                {
                    if (ct.IsCancellationRequested) break;
                    if (!RequestEnabled[req]) continue;
                    isEmpty = false;
                    if (Ecu.Query((byte)req, out var resp))
                    {
                        ecu.Buffer[req] = resp;
                        ecu.Valid[req] = true;
                        comErrCnt = 0;
                    }
                    else
                    {
                        ecu.Valid[req] = false;
                        comErrCnt++;
                    }
                    Thread.Sleep(_pasvDelayMs);
                }
                if (!isEmpty)
                    ParametersUpdated?.Invoke();
                else
                    Thread.Sleep(IdleDelayMs);
            }

            if ((flag & 2) == 2 && loopCnt == 0)
            {
                foreach (var req in Ecu.engineErrReq)
                {
                    if (ct.IsCancellationRequested) break;
                    if (Ecu.Query(req, out var resp))
                    {
                        ecu.Buffer[req] = resp;
                        ecu.Valid[req] = true;
                        comErrCnt = 0;
                    }
                    else
                    {
                        ecu.Valid[req] = false;
                        comErrCnt++;
                    }
                    Thread.Sleep(_pasvDelayMs);
                }
                ErrorsUpdated?.Invoke();

                if (Ecu.hasIMMO)
                {
                    foreach (var req in Ecu.immoErrReq)
                    {
                        if (ct.IsCancellationRequested) break;
                        if (Ecu.Query(req, out var resp))
                        {
                            ecu.Buffer[req] = resp;
                            ecu.Valid[req] = true;
                            comErrCnt = 0;
                        }
                        else
                        {
                            ecu.Valid[req] = false;
                            comErrCnt++;
                        }
                        Thread.Sleep(_pasvDelayMs);
                    }
                    ImmoErrorsUpdated?.Invoke();
                }
            }

            if (comErrCnt > 1)
                throw new IOException("Comunicazione con la centralina persa.");
            return 0;
        }

        /// <summary>Picks the polling loop back up after CommunicationLost, re-initialising the ECU's diagnostic mode.</summary>
        public Task ReconnectAsync() => Task.Run(() =>
        {
            Ecu.InitPasvDiag();
            StartLoop();
        });

        private bool InitActiveDiag(out byte response)
        {
            if (!_port.IsOpen) { response = 0; return false; }
            return Ecu.Query(0xAA, out response);
        }

        private bool ExitActiveDiag()
        {
            if (!_port.IsOpen) return false;
            if (Ecu.Query(0xFF, out var response))
                return response == 0xFF;
            return false;
        }

        public async Task<ActiveOutcome> RunTestAsync(testElement test, CancellationToken ct)
        {
            if (IsSimulation) return ActiveOutcome.Demo;

            await _activeOpGate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                await PauseLoopAsync().ConfigureAwait(false);
                try
                {
                    return await Task.Run(() => RunTestCore(test, ct), CancellationToken.None).ConfigureAwait(false);
                }
                finally
                {
                    StartLoop();
                }
            }
            finally
            {
                _activeOpGate.Release();
            }
        }

        private ActiveOutcome RunTestCore(testElement test, CancellationToken ct)
        {
            // Matches the original bgwTest_DoWork: the 500ms timeout covers the whole active-diag
            // sequence (init + RequestSet + result wait), not just the final wait - otherwise init
            // and RequestSet queries would run under the passive loop's shorter 300ms timeout.
            ecu.SetReadTimeout(ref _port, 500);

            if (!InitActiveDiag(out _))
                return ActiveOutcome.InitFailed;

            foreach (var code in test.RequestSet)
            {
                Thread.Sleep(5);
                bool ok = Ecu.Query(code, out var resp);
                Thread.Sleep(10);
                if (!ok || resp != code)
                {
                    ExitActiveDiag();
                    return ActiveOutcome.Failed;
                }
            }

            var sw = Stopwatch.StartNew();
            while (_port.BytesToRead == 0 && sw.ElapsedMilliseconds < (test.TimeOut + 1) * 1000)
            {
                if (ct.IsCancellationRequested)
                {
                    Ecu.Query(0xFF, out _);
                    ExitActiveDiag();
                    return ActiveOutcome.Cancelled;
                }
                Thread.Sleep(15);
            }

            byte result;
            if (_port.BytesToRead != 0)
            {
                result = (byte)_port.ReadByte();
            }
            else
            {
                Ecu.Query(0xFF, out _);
                result = 0xBB;
            }
            ExitActiveDiag();

            return result switch
            {
                0xFF => ActiveOutcome.Passed,
                0xAA => ActiveOutcome.InitFailed,
                0xBB => ActiveOutcome.Timeout,
                _ => ActiveOutcome.Failed,
            };
        }

        public async Task<ActiveOutcome> ClearCodesAsync(CancellationToken ct) => await RunTestAsync(Ecu.clearCodes, ct).ConfigureAwait(false);

        public async Task<ActiveOutcome> RunAdjustmentAsync(adjustElement adjust, Func<byte, Func<byte, bool>, Task>? runOneByteStep, CancellationToken ct)
        {
            if (IsSimulation) return ActiveOutcome.Demo;

            await _activeOpGate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                await PauseLoopAsync().ConfigureAwait(false);
                try
                {
                    // Matches the original ExecAdjustment, which sets this explicitly rather than
                    // relying on whatever the passive loop happened to leave the port set to.
                    await Task.Run(() => ecu.SetReadTimeout(ref _port, 300), CancellationToken.None).ConfigureAwait(false);

                    byte status = 0;
                    if (adjust.StatusByte != 0x00)
                    {
                        var okStatus = await Task.Run(() => Ecu.Query(adjust.StatusByte, out status), ct).ConfigureAwait(false);
                        if (!okStatus)
                            return ActiveOutcome.InitFailed;
                    }

                    var initOk = await Task.Run(() => InitActiveDiag(out _), ct).ConfigureAwait(false);
                    if (!initOk)
                        return ActiveOutcome.InitFailed;

                    foreach (var code in adjust.PreSet)
                    {
                        var (ok, resp) = await Task.Run(() =>
                        {
                            Thread.Sleep(5);
                            var success = Ecu.Query(code, out var r);
                            return (success, r);
                        }, ct).ConfigureAwait(false);
                        if (!ok || resp != code)
                        {
                            await Task.Run(() => { ExitActiveDiag(); ExitActiveDiag(); }, CancellationToken.None).ConfigureAwait(false);
                            return ActiveOutcome.InitFailed;
                        }
                    }

                    if (adjust.Type == "onebyte" && runOneByteStep != null)
                    {
                        await runOneByteStep(status, b => Ecu.Query(b, out _)).ConfigureAwait(false);
                    }

                    if (adjust.PostSet != null)
                    {
                        foreach (var code in adjust.PostSet)
                        {
                            var (ok, resp) = await Task.Run(() =>
                            {
                                Thread.Sleep(5);
                                var success = Ecu.Query(code, out var r);
                                return (success, r);
                            }, ct).ConfigureAwait(false);
                            if (!ok || resp != code)
                            {
                                await Task.Run(() => { ExitActiveDiag(); ExitActiveDiag(); }, CancellationToken.None).ConfigureAwait(false);
                                return ActiveOutcome.Failed;
                            }
                        }
                    }

                    await Task.Run(() => ExitActiveDiag(), CancellationToken.None).ConfigureAwait(false);
                    return ActiveOutcome.Passed;
                }
                finally
                {
                    StartLoop();
                }
            }
            finally
            {
                _activeOpGate.Release();
            }
        }

        /// <summary>
        /// The raw byte the ECU's own formula turns into a believable reading for the parameter it
        /// belongs to (14 V rather than 6 V of battery), so a simulated session looks like a car.
        /// Low bytes of two-byte values just get a middling level.
        /// </summary>
        private byte PlausibleRawValue(int request)
        {
            foreach (var data in Ecu.engineData)
            {
                if (data.RequestSet[0] != request) continue;
                var target = PlausibleReading(data);
                var saved = ecu.Buffer[request];
                byte best = 128;
                var smallest = double.MaxValue;
                for (int raw = 0; raw < 256; raw++)
                {
                    ecu.Buffer[request] = (byte)raw;
                    var distance = Math.Abs((double)data.Value - target);
                    if (distance >= smallest) continue;
                    smallest = distance;
                    best = (byte)raw;
                }
                ecu.Buffer[request] = saved;
                return best;
            }
            return 128;
        }

        private double PlausibleReading(dataElement data)
        {
            switch (data.Unit)
            {
                case "V": return 13.8;
                case "mV": return 450;
                case "hPa": return 700;
                case "ms": return 3.5;
                case "°": return 14;
                case "%": return 30;
                case "°C":
                    // Parameter order puts air before coolant.
                    return _simulatedTemperatures++ == 0 ? 28 : 86;
                case "":
                    return 1;
                default:
                    return data.Unit == IES_2.Res.lang.rpm ? 1100 : 12;
            }
        }

        private int _simulatedTemperatures;

        /// <summary>A raw byte drifting smoothly around its level, so simulated values and graphs look like signals.</summary>
        private sealed class SimulatedByte
        {
            private readonly double _level;
            private readonly double _amplitude;
            private readonly double _speed;
            private readonly double _phase;

            public SimulatedByte(Random random, byte level)
            {
                _level = level;
                _amplitude = random.Next(2, 5);
                _speed = 0.15 + random.NextDouble() * 0.5;
                _phase = random.NextDouble() * Math.Tau;
            }

            public byte At(double seconds, Random random)
            {
                var value = _level + _amplitude * Math.Sin(seconds * _speed + _phase) + (random.NextDouble() - 0.5) * 1.5;
                return (byte)Math.Clamp(value, 0, 255);
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            try
            {
                _loopCts?.Cancel();
                _loopTask?.Wait(500);
            }
            catch { /* best-effort shutdown */ }
            try
            {
                if (_port.IsOpen) _port.Close();
                _port.Dispose();
            }
            catch { /* best-effort shutdown */ }
            ecu.connected = false;
            ecu.ISO = null;
            ecu.CODRIC = null;
        }
    }
}
