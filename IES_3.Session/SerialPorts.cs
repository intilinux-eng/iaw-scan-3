using System;
using System.Collections.Generic;
using System.IO.Ports;
using System.Linq;

namespace IES_2.Session
{
    public static class SerialPorts
    {
        /// <summary>
        /// The serial ports on this machine. The K-line cable is a USB serial adapter: those come first,
        /// so the default pick is right on boards that also expose on-board UARTs.
        /// </summary>
        public static IReadOnlyList<string> List()
        {
            return SerialPort.GetPortNames()
                .Distinct(StringComparer.Ordinal)
                .OrderBy(p => p.Contains("ttyUSB", StringComparison.Ordinal) ? 0 : p.Contains("ttyACM", StringComparison.Ordinal) ? 1 : 2)
                .ThenBy(p => p.Length)
                .ThenBy(p => p, StringComparer.Ordinal)
                .ToList();
        }
    }
}
