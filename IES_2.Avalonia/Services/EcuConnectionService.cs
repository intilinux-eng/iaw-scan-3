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
    /// state machine in the original frmMain. Keeps the connected port/ECU
    /// instance alive after a successful connect (for Disconnect and for
    /// later milestones that will need to poll it), so ConnectAsync is
    /// intentionally not disposing on the success path.
    /// </summary>
    public class EcuConnectionService
    {
        private const string DemoIso = "55D085029440";
        private const string DemoCodric = "6160206301";

        private SerialPort? _activePort;

        public bool IsConnected => _activePort != null;

        public async Task<ConnectResult> ConnectAsync(EcuOption option, string? portName, bool simulation, CancellationToken ct)
        {
            if (simulation)
                return await Task.Run(() => ConnectSimulated(option), ct);

            if (string.IsNullOrEmpty(portName))
                return ConnectResult.Failure("Seleziona una porta COM.");

            return await Task.Run(() => ConnectReal(option, portName, ct), ct);
        }

        public void Disconnect()
        {
            if (_activePort == null) return;
            try
            {
                if (_activePort.IsOpen) _activePort.Close();
            }
            finally
            {
                _activePort.Dispose();
                _activePort = null;
                ecu.connected = false;
                ecu.ISO = null;
                ecu.CODRIC = null;
            }
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

            _activePort = port;
            instance.hasIMMO = true;
            return BuildSuccess(option.DisplayName, instance);
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
            _activePort = serial;
            return BuildSuccess(option.DisplayName, resolved);
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

        private static ConnectResult BuildSuccess(string ecuTypeDisplayName, ecu instance)
        {
            var iso = ecu.ISO;
            var codric = ecu.CODRIC;
            return new ConnectResult
            {
                Success = true,
                EcuTypeDisplayName = ecuTypeDisplayName + (instance.hasIMMO ? "" : " ECOL"),
                IsoCode = iso != null ? Regex.Replace(iso, @"(\w{2})(\w{2})(\w{2})(\w{2})(\w{2})(\w{2})", "$1-$2-$3-$4-$5-$6") : "-",
                RepCode = codric != null ? Regex.Replace(codric, @"(\w{5})(\w{3})(\w{2})", "$1.$2.$3") : "-",
                CarModel = instance.GetCarModel(),
            };
        }
    }
}
