using System;
using System.Collections.Generic;
using System.IO.Ports;
using System.Linq;

namespace IES_2.Session
{
    /// <summary>A serial port and, where the system can tell, what's behind it.</summary>
    public sealed record SerialPortInfo(string Name, string? Description = null, bool IsUsbAdapter = false)
    {
        public override string ToString() => Description == null ? Name : $"{Name} · {Description}";
    }

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

        /// <summary>These ports saying what each one is, with whatever a K-line cable can be built on first.</summary>
        public static IReadOnlyList<SerialPortInfo> Describe(IEnumerable<string> names) =>
            Describe(names, UsbSerialDevices.PortsByName());

        public static IReadOnlyList<SerialPortInfo> Describe(IEnumerable<string> names, IReadOnlyDictionary<string, SerialPortInfo> known) =>
            names.Select(name => known.TryGetValue(name, out var info) ? info : new SerialPortInfo(name))
                .OrderBy(port => port.IsUsbAdapter ? 0 : 1)
                .ToList();
    }
}
