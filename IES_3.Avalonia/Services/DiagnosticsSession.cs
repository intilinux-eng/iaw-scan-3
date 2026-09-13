using System;
using System.Diagnostics;
using System.IO;
using System.IO.Ports;
using System.Threading;
using System.Threading.Tasks;
using IES_2.ECU;

namespace IES_2.Avalonia.Services
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
    /// passive-diagnostics polling loop (the async replacement for frmMain's bgwParameters/
    /// bgwDemo BackgroundWorkers) and executes one-off active-diagnostics operations
    /// (tests, adjustments, clear-codes - the bgwTest replacement), pausing/resuming the
    /// passive loop around them exactly like the original did.
    /// </summary>
    public sealed class DiagnosticsSession : IDisposable
    {
        private const int DemoParamDelayMs = 200;
        private const int DemoErrorDelayMs = 75;
        private const int DemoImmoDelayMs = 25;
        private const int IdleDelayMs = 20;

        private SerialPort _port;
        private readonly Random _random = new();
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

        /// <summary>Indexed by request byte, mirrors frmMain's Request[] array: which requests the passive loop should poll for.</summary>
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
                for (int i = 1; i < 16; i++)
                {
                    ecu.Buffer[i] = (byte)_random.Next(255);
                    ecu.Valid[i] = true;
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
