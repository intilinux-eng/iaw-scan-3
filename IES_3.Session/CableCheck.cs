using System;
using System.IO;
using System.IO.Ports;
using System.Threading;
using IES_2.ECU;

namespace IES_2.Session
{
    public enum CableCondition
    {
        Working,
        NoEcho,
        Garbled,
        PortFailed,
    }

    public sealed record CableCheckResult(CableCondition Condition, string? Detail = null);

    /// <summary>
    /// Checks the cable with no car needed. Transmit and receive share the K-line's one wire, so a working
    /// interface hands back every byte it sends, and the engine counts on that echo for every request.
    /// No echo at all means USB works (the port opened) but the K-line side doesn't: measured on a real
    /// cable, that's what happens when its 12 V is off.
    /// </summary>
    public static class CableCheck
    {
        internal const int EchoTimeoutMs = 300;

        // Harmless if an ECU happens to be on the line: below 0x80 these can only be read requests, and
        // they aren't the 0F AA CC wake-up sequence.
        private static readonly byte[] WakeUpSpeedBytes = { 0x55, 0x2A, 0x33 };
        internal static readonly byte[] DiagnosticSpeedBytes = { 0x55, 0x2A, 0x33, 0x4C, 0x0F, 0x70 };

        public static CableCheckResult Run(string portName)
        {
            using var serial = new SerialPort(portName);
            try
            {
                serial.Open();
                serial.ReadTimeout = EchoTimeoutMs;
                // Both speeds the engine uses: 7680 baud is an unusual one, and not every chip manages it.
                serial.BaudRate = ecu.initBaud;
                var condition = Exchange(serial, WakeUpSpeedBytes);
                if (condition != CableCondition.Working) return new CableCheckResult(condition);
                serial.BaudRate = ecu.commBaud;
                return new CableCheckResult(Exchange(serial, DiagnosticSpeedBytes));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
            {
                return new CableCheckResult(CableCondition.PortFailed, $"{portName}: {ex.Message}");
            }
        }

        /// <summary>Sends the bytes one at a time on a port already open, expecting each straight back.</summary>
        internal static CableCondition Exchange(SerialPort serial, byte[] bytes)
        {
            int echoed = 0, wrong = 0;
            foreach (var b in bytes)
            {
                serial.DiscardInBuffer();
                serial.Write(new[] { b }, 0, 1);
                try
                {
                    if (serial.ReadByte() == b) echoed++;
                    else wrong++;
                }
                catch (TimeoutException)
                {
                    // Counts as neither: what's left of the bytes tells silence from a bad line.
                }
                Thread.Sleep(20);
            }
            if (echoed == bytes.Length) return CableCondition.Working;
            return echoed == 0 && wrong == 0 ? CableCondition.NoEcho : CableCondition.Garbled;
        }
    }
}
