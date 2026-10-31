using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using IES_2.Session;
using Xunit;

namespace IES_2.Tests
{
    public class CableRecognitionTests
    {
        [Theory]
        [InlineData(0x1A86, 0x7523, "CH340")]
        [InlineData(0x0403, 0x6001, "FTDI")]
        [InlineData(0x10C4, 0xEA60, "CP210x")]
        [InlineData(0x067B, 0x2303, "PL2303")]
        public void KnowsTheChipsCablesAreBuiltOn(int vendor, int product, string chip)
        {
            Assert.Equal(chip, UsbSerialDevices.Identify(vendor, product)?.Name);
        }

        [Fact]
        public void AnArduinoIsNotACable()
        {
            Assert.Null(UsbSerialDevices.Identify(0x2341, 0x0078));
            Assert.Null(UsbSerialDevices.IdentifyHardwareId("VID_2341&PID_0078&MI_01"));
        }

        [Theory]
        [InlineData("VID_1A86&PID_7523", "CH340")]
        [InlineData("VID_0403+PID_6001+A50285BIA", "FTDI")]
        [InlineData("vid_10c4&pid_ea60", "CP210x")]
        public void ReadsWindowsHardwareIds(string hardwareId, string chip)
        {
            Assert.Equal(chip, UsbSerialDevices.IdentifyHardwareId(hardwareId)?.Name);
        }

        [Fact]
        public void DescribesAPortTheWayDeviceManagerDoes()
        {
            var ch340 = UsbSerialDevices.Identify(0x1A86, 0x7523);
            var ftdi = UsbSerialDevices.Identify(0x0403, 0x6001);

            Assert.Equal("USB-SERIAL CH340", UsbSerialDevices.Describe("USB-SERIAL CH340 (COM5)", "COM5", ch340));
            Assert.Equal("USB Serial Port (FTDI)", UsbSerialDevices.Describe("USB Serial Port (COM3)", "COM3", ftdi));
            Assert.Equal("USB Serial Device", UsbSerialDevices.Describe("USB Serial Device (COM4)", "COM4", null));
            Assert.Equal("CH340", UsbSerialDevices.Describe(null, "COM5", ch340));
            Assert.Null(UsbSerialDevices.Describe(null, "COM1", null));
        }

        [Fact]
        public void ShowsAPortByNameAndWhatItIs()
        {
            Assert.Equal("COM5 · USB-SERIAL CH340", new SerialPortInfo("COM5", "USB-SERIAL CH340", true).ToString());
            Assert.Equal("COM1", new SerialPortInfo("COM1").ToString());
        }

        [Fact]
        public void ACablePortComesBeforeOtherPorts()
        {
            var known = new Dictionary<string, SerialPortInfo>
            {
                ["COM3"] = new("COM3", "USB Serial Device"),
                ["COM5"] = new("COM5", "USB-SERIAL CH340", IsUsbAdapter: true),
            };

            var ports = SerialPorts.Describe(new[] { "COM1", "COM3", "COM5" }, known);

            Assert.Equal(new[] { "COM5", "COM1", "COM3" }, ports.Select(p => p.Name));
            Assert.Null(ports[1].Description);
            Assert.Equal("USB Serial Device", ports[2].Description);
        }

        [Fact]
        public void OnLinuxTheChipComesFromTheKernelDriver()
        {
            Assert.SkipUnless(OperatingSystem.IsLinux(), "Symbolic links as sysfs has them: Linux only.");
            var root = Path.Combine(Path.GetTempPath(), "iawscan-tests", Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(Path.Combine(root, "drivers", "ch341"));
                Directory.CreateDirectory(Path.Combine(root, "tty", "ttyUSB0", "device"));
                Directory.CreateSymbolicLink(Path.Combine(root, "tty", "ttyUSB0", "device", "driver"), Path.Combine(root, "drivers", "ch341"));
                Directory.CreateDirectory(Path.Combine(root, "tty", "ttyUSB1", "device"));
                Directory.CreateDirectory(Path.Combine(root, "tty", "ttyS0"));

                var ports = UsbSerialDevices.LinuxPorts(Path.Combine(root, "tty"));

                Assert.Equal(2, ports.Count);
                Assert.Equal("CH340", ports["/dev/ttyUSB0"].Description);
                Assert.True(ports["/dev/ttyUSB0"].IsUsbAdapter);
                Assert.Null(ports["/dev/ttyUSB1"].Description);
            }
            finally
            {
                Directory.Delete(root, recursive: true);
            }
        }

        [Fact]
        public void WhileConnectedWindowsTimesShortPausesFinely()
        {
            Assert.SkipUnless(OperatingSystem.IsWindows(), "Only Windows has a coarse timer to ask about.");
            using var timer = new FineTimer();
            var pauses = new List<double>();
            for (var i = 0; i < 30; i++)
            {
                var watch = System.Diagnostics.Stopwatch.StartNew();
                Thread.Sleep(4);
                pauses.Add(watch.Elapsed.TotalMilliseconds);
            }
            pauses.Sort();
            Assert.True(pauses[15] < 10, $"typical 4 ms pause took {pauses[15]:0.0} ms");
        }
    }

    /// <summary>The README's download buttons name this version's files: everything has to agree on it.</summary>
    public class ReleaseConsistencyTests
    {
        private static string Root()
        {
            var folder = new DirectoryInfo(AppContext.BaseDirectory);
            while (folder != null && !File.Exists(Path.Combine(folder.FullName, "IES_3.sln"))) folder = folder.Parent;
            return folder?.FullName ?? throw new DirectoryNotFoundException("The repository wasn't found above the tests.");
        }

        private static string Read(params string[] path) => File.ReadAllText(Path.Combine(new[] { Root() }.Concat(path).ToArray()));

        private static string Version() =>
            Regex.Match(Read("IES_3.Avalonia", "IES_3.Avalonia.csproj"), "<Version>(.+)</Version>").Groups[1].Value;

        [Fact]
        public void TheReleaseWorkflowDefaultsToThisVersion()
        {
            Assert.Contains($"default: \"{Version()}\"", Read(".github", "workflows", "beta-release.yml"));
        }

        [Theory]
        [InlineData("README.md")]
        [InlineData("README.it.md")]
        public void TheReadmeOffersThisVersionsFiles(string file)
        {
            var version = Version();
            var text = Read(file);

            Assert.Contains($"releases/download/v{version}/IAWScan3-{version}-win-x64.exe", text);
            Assert.Contains($"releases/download/v{version}/iaw-scan3_{version}_amd64.deb", text);
            Assert.Contains($"sudo apt install ./iaw-scan3_{version}_amd64.deb", text);
            Assert.All(Regex.Matches(text, @"releases/download/v([^/]+)/"), m => Assert.Equal(version, m.Groups[1].Value));
        }

        [Fact]
        public void TheNotesCoverThisVersion()
        {
            var version = Version();
            Assert.Contains(version, Read("CHANGELOG.md"));
            Assert.Contains($"IAWScan3-{version}-win-x64.exe", Read(".github", "release-notes.md"));
            Assert.Contains($"iaw-scan3_{version}_amd64.deb", Read(".github", "release-notes.md"));
        }
    }
}
