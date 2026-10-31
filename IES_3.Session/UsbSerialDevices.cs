using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace IES_2.Session
{
    /// <summary>A USB-to-serial chip that K-line cables are built on, and where its maker offers the driver.</summary>
    public sealed record UsbSerialChip(string Name, string? DriverPage);

    /// <summary>
    /// Tells what's behind a serial port, which the system's own list of ports doesn't: that gives only
    /// names. Reads only what the system already knows, and never opens a port. Anything it can't find
    /// out just leaves a port described by its name, so nothing here can get in the way of connecting.
    /// </summary>
    public static class UsbSerialDevices
    {
        private const string DeviceTree = @"SYSTEM\CurrentControlSet\Enum";

        private static readonly UsbSerialChip Ch340 = new("CH340", "https://www.wch-ic.com/downloads/CH341SER_EXE.html");
        private static readonly UsbSerialChip Ftdi = new("FTDI", "https://ftdichip.com/drivers/vcp-drivers/");
        private static readonly UsbSerialChip Cp210x = new("CP210x", "https://www.silabs.com/developer-tools/usb-to-uart-bridge-vcp-drivers");
        private static readonly UsbSerialChip Pl2303 = new("PL2303", null);

        private static readonly Regex UsbIds = new(@"VID_([0-9A-F]{4})[&+]PID_([0-9A-F]{4})", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        public static UsbSerialChip? Identify(int vendor, int product) => (vendor, product) switch
        {
            (0x1A86, 0x7523 or 0x5523) => Ch340,
            (0x0403, 0x6001 or 0x6010 or 0x6011 or 0x6014 or 0x6015) => Ftdi,
            (0x10C4, 0xEA60 or 0xEA70) => Cp210x,
            (0x067B, 0x2303 or 0x23A3) => Pl2303,
            _ => null,
        };

        /// <summary>From a Windows hardware id: VID_1A86&amp;PID_7523, or VID_0403+PID_6001+A50285BIA for FTDI's bus.</summary>
        public static UsbSerialChip? IdentifyHardwareId(string hardwareId)
        {
            var match = UsbIds.Match(hardwareId);
            return match.Success
                ? Identify(Convert.ToInt32(match.Groups[1].Value, 16), Convert.ToInt32(match.Groups[2].Value, 16))
                : null;
        }

        /// <summary>The name of Linux's kernel driver for a chip, which sysfs shows for each ttyUSB port.</summary>
        public static UsbSerialChip? FromLinuxDriver(string? driver) => driver switch
        {
            "ch341" => Ch340,
            "ftdi_sio" => Ftdi,
            "cp210x" => Cp210x,
            "pl2303" => Pl2303,
            _ => null,
        };

        /// <summary>"USB-SERIAL CH340 (COM5)" gives "USB-SERIAL CH340"; a generic name also gets the chip's.</summary>
        public static string? Describe(string? systemName, string portName, UsbSerialChip? chip)
        {
            var name = systemName?.Replace($"({portName})", "", StringComparison.OrdinalIgnoreCase).Trim();
            if (string.IsNullOrEmpty(name)) return chip?.Name;
            return chip == null || name.Contains(chip.Name, StringComparison.OrdinalIgnoreCase) ? name : $"{name} ({chip.Name})";
        }

        public static IReadOnlyDictionary<string, SerialPortInfo> PortsByName()
        {
            if (OperatingSystem.IsWindows()) return WindowsPorts();
            if (OperatingSystem.IsLinux()) return LinuxPorts("/sys/class/tty");
            return new Dictionary<string, SerialPortInfo>();
        }

        public static IReadOnlyDictionary<string, SerialPortInfo> LinuxPorts(string sysClassTty)
        {
            var ports = new Dictionary<string, SerialPortInfo>();
            if (!Directory.Exists(sysClassTty)) return ports;
            foreach (var tty in Directory.GetFileSystemEntries(sysClassTty, "ttyUSB*"))
            {
                var chip = FromLinuxDriver(LinkTargetName(Path.Combine(tty, "device", "driver")));
                var port = "/dev/" + Path.GetFileName(tty);
                ports[port] = new SerialPortInfo(port, chip?.Name, IsUsbAdapter: true);
            }
            return ports;
        }

        private static string? LinkTargetName(string link)
        {
            try { return new DirectoryInfo(link).ResolveLinkTarget(returnFinalTarget: true)?.Name; }
            catch (IOException) { return null; }
        }

        /// <summary>
        /// A cable that's plugged in but that Windows can't use, normally for lack of a driver: it then
        /// has no COM port, so the list of ports alone can't tell it from a cable that isn't there.
        /// </summary>
        public static UsbSerialChip? FindCableWithoutDriver()
        {
            if (!OperatingSystem.IsWindows()) return null;
            try
            {
                using var bus = Registry.LocalMachine.OpenSubKey($@"{DeviceTree}\USB");
                if (bus == null) return null;
                foreach (var hardwareId in bus.GetSubKeyNames())
                {
                    var chip = IdentifyHardwareId(hardwareId);
                    if (chip == null) continue;
                    using var device = bus.OpenSubKey(hardwareId);
                    if (device == null) continue;
                    foreach (var instance in device.GetSubKeyNames())
                        if (HasProblem($@"USB\{hardwareId}\{instance}")) return chip;
                }
            }
            catch (Exception ex) when (ex is SecurityException or IOException or UnauthorizedAccessException)
            {
                // Nothing to report, then.
            }
            return null;
        }

        [SupportedOSPlatform("windows")]
        private static Dictionary<string, SerialPortInfo> WindowsPorts()
        {
            var ports = new Dictionary<string, SerialPortInfo>(StringComparer.OrdinalIgnoreCase);
            try
            {
                // FTDI's driver puts its ports on a bus of its own.
                foreach (var enumerator in new[] { "USB", "FTDIBUS" })
                {
                    using var bus = Registry.LocalMachine.OpenSubKey($@"{DeviceTree}\{enumerator}");
                    if (bus == null) continue;
                    foreach (var hardwareId in bus.GetSubKeyNames())
                    {
                        using var device = bus.OpenSubKey(hardwareId);
                        if (device == null) continue;
                        var chip = IdentifyHardwareId(hardwareId);
                        foreach (var instance in device.GetSubKeyNames())
                        {
                            using var key = device.OpenSubKey(instance);
                            using var parameters = key?.OpenSubKey("Device Parameters");
                            if (parameters?.GetValue("PortName") is not string port) continue;
                            // Windows remembers devices long gone, and a gone one may have held this number.
                            if (!IsPresent($@"{enumerator}\{hardwareId}\{instance}")) continue;
                            ports[port] = new SerialPortInfo(port, Describe(key?.GetValue("FriendlyName") as string, port, chip), chip != null);
                        }
                    }
                }
            }
            catch (Exception ex) when (ex is SecurityException or IOException or UnauthorizedAccessException)
            {
                // Keep what was found: the rest stay described by their names.
            }
            return ports;
        }

        [SupportedOSPlatform("windows")]
        private static bool IsPresent(string deviceId) => CM_Locate_DevNodeW(out _, deviceId, CM_LOCATE_DEVNODE_NORMAL) == CR_SUCCESS;

        [SupportedOSPlatform("windows")]
        private static bool HasProblem(string deviceId) =>
            CM_Locate_DevNodeW(out var node, deviceId, CM_LOCATE_DEVNODE_NORMAL) == CR_SUCCESS
            && CM_Get_DevNode_Status(out var status, out _, node, 0) == CR_SUCCESS
            && (status & DN_HAS_PROBLEM) != 0;

        private const uint CM_LOCATE_DEVNODE_NORMAL = 0; // only devices that are there right now
        private const uint CR_SUCCESS = 0;
        private const uint DN_HAS_PROBLEM = 0x00000400;

        [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
        private static extern uint CM_Locate_DevNodeW(out uint node, string deviceId, uint flags);

        [DllImport("cfgmgr32.dll")]
        private static extern uint CM_Get_DevNode_Status(out uint status, out uint problem, uint node, uint flags);
    }
}
