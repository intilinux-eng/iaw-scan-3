using System;
using System.Diagnostics;
using System.IO.Ports;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using IES_2.ECU;

namespace IES_2.Session
{
    /// <summary>
    /// Thin wrapper around the IES_2.Core ECU classes that turns the connect
    /// flow (real hardware or simulation) into an awaitable, cancellable
    /// operation returning a result, instead of the accelerator-driven
    /// state machine of the original program. On success the result carries
    /// a ready-to-use DiagnosticsSession that owns the live ECU/port for the
    /// rest of the connected session (parameters, errors, tests, ...).
    /// </summary>
    public class EcuConnectionService
    {
        private const string DefaultDemoIso = "55D085029440";
        private const string DemoCodric = "6160206301";

        private readonly string _demoIso;

        /// <param name="demoIso">ISO code reported in simulation mode, so an edition limited to
        /// specific cars (e.g. the Coupé one) simulates a car it actually supports.</param>
        public EcuConnectionService(string demoIso = DefaultDemoIso)
        {
            _demoIso = demoIso;
        }

        /// <param name="simulatedIso">In simulation, the ISO code to report instead of the default one,
        /// so that the simulated car is the one the user picked.</param>
        public async Task<ConnectResult> ConnectAsync(EcuOption option, string? portName, bool simulation, CancellationToken ct, string? simulatedIso = null)
        {
            if (simulation)
                return await Task.Run(() => ConnectSimulated(option, simulatedIso ?? _demoIso), ct);

            if (string.IsNullOrEmpty(portName))
                return ConnectResult.Failure(ConnectError.NoPortSelected);

            return await Task.Run(() => ConnectReal(option, portName, ct), ct);
        }

        private static ConnectResult ConnectSimulated(EcuOption option, string iso)
        {
            ecu.ISO = iso;
            ecu.CODRIC = DemoCodric;
            ecu.connected = true;

            var port = new SerialPort();
            var instance = CreateInstance(option.Id, port);
            if (instance == null)
            {
                port.Dispose();
                return ConnectResult.Failure(ConnectError.InvalidEcu);
            }

            instance.hasIMMO = true;
            return BuildSuccess(option.DisplayName, instance, port, simulation: true);
        }

        private ConnectResult ConnectReal(EcuOption option, string portName, CancellationToken ct)
        {
            var serial = new SerialPort(portName);
            try
            {
                serial.Open();
            }
            catch (Exception ex)
            {
                serial.Dispose();
                return ConnectResult.Failure(ConnectError.PortOpenFailed, $"{portName}: {ex.Message}");
            }

            FineTimer? timer = null;
            ConnectResult Fail(ConnectError error, string? detail = null)
            {
                // Release the port, or it stays open and every later attempt fails with "port in use".
                ecu.connected = false;
                timer?.Dispose();
                serial.Dispose();
                return ConnectResult.Failure(error, detail);
            }

            try
            {
                var isoFromSync = WaitForIsoSync(serial, ct);
                if (ct.IsCancellationRequested) return Fail(ConnectError.Cancelled);

                timer = new FineTimer();
                ecu.connected = true;
                var resolved = CreateInstance(option.Id, serial);
                if (resolved == null) return Fail(ConnectError.InvalidEcu);
                resolved.InitPasvDiag();
                resolved.ReadCODRIC();
                if (isoFromSync == null)
                    resolved.ReadISO();

                resolved.hasIMMO = ecu.CheckCODE(ref serial);

                // Nothing answered. An ECU that's off is worth connecting anyway, as the original did:
                // turn the ignition on and reconnect. Without even the cable's echo no request can ever
                // work, so stop here and say why. (Only now: a proper connection sends the ECU nothing extra.)
                if (ecu.ISO == null && ecu.CODRIC == null)
                {
                    ecu.SetReadTimeout(ref serial, CableCheck.EchoTimeoutMs);
                    if (CableCheck.Exchange(serial, CableCheck.DiagnosticSpeedBytes) == CableCondition.NoEcho)
                        return Fail(ConnectError.CableNotAnswering);
                }

                return BuildSuccess(option.DisplayName, resolved, serial, simulation: false, timer);
            }
            catch (Exception ex)
            {
                // e.g. a read timeout halfway through the ISO sync bytes.
                return Fail(ConnectError.Communication, ex.Message);
            }
        }

        /// <summary>
        /// Mirrors the original bgwIsoWait_DoWork: waits up to 10s for the ECU's
        /// unsolicited 0x55 sync byte followed by 5 more ISO bytes. Returns the
        /// hex ISO string if received, otherwise null (caller then reads ISO
        /// explicitly via the passive-diagnostic request sequence).
        /// </summary>
        private static string? WaitForIsoSync(SerialPort serial, CancellationToken ct)
        {
            ecu.SetReadTimeout(ref serial, 1200);
            serial.BaudRate = ecu.initBaud;
            var isoBytes = new byte[6];
            var sw = Stopwatch.StartNew();
            bool sync = false;

            while (sw.ElapsedMilliseconds < 10000 && !sync)
            {
                if (ct.IsCancellationRequested) return null;
                if (serial.BytesToRead == 0)
                {
                    Thread.Sleep(15);
                    continue;
                }
                var b = (byte)serial.ReadByte();
                if (b == 0x55)
                {
                    sync = true;
                    isoBytes[0] = b;
                }
            }

            if (!sync) return null;

            for (int i = 1; i < 6; i++)
            {
                if (ct.IsCancellationRequested) return null;
                isoBytes[i] = (byte)serial.ReadByte();
            }

            var iso = "";
            foreach (var b in isoBytes) iso += b.ToString("X2");
            ecu.ISO = iso;
            return iso;
        }

        private static ecu? CreateInstance(string id, SerialPort port)
        {
            return id switch
            {
                iaw16f.name => new iaw16f(ref port),
                iaw18f.name => new iaw18f(ref port),
                iaw8f_68.name => new iaw8f_68(ref port),
                iaw18fd.name => new iaw18fd(ref port),
                iaw04k.name => new iaw04k(ref port),
                code.name => new code(ref port),
                _ => null,
            };
        }

        private static ConnectResult BuildSuccess(string ecuTypeDisplayName, ecu instance, SerialPort port, bool simulation, FineTimer? timer = null)
        {
            var iso = ecu.ISO;
            var codric = ecu.CODRIC;
            var isoText = iso != null ? Regex.Replace(iso, @"(\w{2})(\w{2})(\w{2})(\w{2})(\w{2})(\w{2})", "$1-$2-$3-$4-$5-$6") : "-";
            var repText = codric != null ? Regex.Replace(codric, @"(\w{5})(\w{3})(\w{2})", "$1.$2.$3") : "-";
            var carModel = instance.GetCarModel();
            var typeText = ecuTypeDisplayName + (instance.hasIMMO ? "" : " ECOL");

            var session = new DiagnosticsSession(instance, port, simulation, typeText, isoText, repText, carModel, timer);

            return new ConnectResult
            {
                Success = true,
                EcuTypeDisplayName = typeText,
                IsoCode = isoText,
                RepCode = repText,
                CarModel = carModel,
                EcuIdentified = iso != null || codric != null,
                Session = session,
            };
        }
    }
}
