using System;
using System.Diagnostics;
using System.IO.Ports;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using IES_2.Avalonia.Models;
using IES_2.ECU;

namespace IES_2.Avalonia.Services
{
    /// <summary>
    /// Thin wrapper around the IES_2.Core ECU classes that turns the connect
    /// flow (real hardware or simulation) into an awaitable, cancellable
    /// operation returning a result, instead of the accelerator-driven
    /// state machine in the original frmMain. On success the result carries
    /// a ready-to-use DiagnosticsSession that owns the live ECU/port for the
    /// rest of the connected session (parameters, errors, tests, ...).
    /// </summary>
    public class EcuConnectionService
    {
        private const string DemoIso = "55D085029440";
        private const string DemoCodric = "6160206301";

        public async Task<ConnectResult> ConnectAsync(EcuOption option, string? portName, bool simulation, CancellationToken ct)
        {
            if (simulation)
                return await Task.Run(() => ConnectSimulated(option), ct);

            if (string.IsNullOrEmpty(portName))
                return ConnectResult.Failure("Seleziona una porta COM.");

            return await Task.Run(() => ConnectReal(option, portName, ct), ct);
        }

        private ConnectResult ConnectSimulated(EcuOption option)
        {
            ecu.ISO = DemoIso;
            ecu.CODRIC = DemoCodric;
            ecu.connected = true;

            var port = new SerialPort();
            var instance = CreateInstance(option.Id, port);
            if (instance == null)
            {
                port.Dispose();
                return ConnectResult.Failure("Tipo di ECU non valido.");
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
                return ConnectResult.Failure($"Impossibile aprire la porta {portName}: {ex.Message}");
            }

            var isoFromSync = WaitForIsoSync(serial, ct);
            if (ct.IsCancellationRequested)
            {
                serial.Dispose();
                return ConnectResult.Failure("Connessione annullata.");
            }

            ecu.connected = true;
            var resolved = CreateInstance(option.Id, serial);
            if (resolved == null)
            {
                serial.Dispose();
                return ConnectResult.Failure("Tipo di ECU non valido.");
            }
            resolved.InitPasvDiag();
            resolved.ReadCODRIC();
            if (isoFromSync == null)
                resolved.ReadISO();

            resolved.hasIMMO = ecu.CheckCODE(ref serial);
            return BuildSuccess(option.DisplayName, resolved, serial, simulation: false);
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

        private static ConnectResult BuildSuccess(string ecuTypeDisplayName, ecu instance, SerialPort port, bool simulation)
        {
            var iso = ecu.ISO;
            var codric = ecu.CODRIC;
            var isoText = iso != null ? Regex.Replace(iso, @"(\w{2})(\w{2})(\w{2})(\w{2})(\w{2})(\w{2})", "$1-$2-$3-$4-$5-$6") : "-";
            var repText = codric != null ? Regex.Replace(codric, @"(\w{5})(\w{3})(\w{2})", "$1.$2.$3") : "-";
            var carModel = instance.GetCarModel();
            var typeText = ecuTypeDisplayName + (instance.hasIMMO ? "" : " ECOL");

            var session = new DiagnosticsSession(instance, port, simulation, typeText, isoText, repText, carModel);

            return new ConnectResult
            {
                Success = true,
                EcuTypeDisplayName = typeText,
                IsoCode = isoText,
                RepCode = repText,
                CarModel = carModel,
                Session = session,
            };
        }
    }
}
