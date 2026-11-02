using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using IES_2.ECU;
using IES_2.Session;
using Microsoft.Win32.SafeHandles;
using Xunit;

namespace IES_2.Tests
{
    /// <summary>
    /// The real connect flow, polling loop and active diagnostics through the real SerialPort class on
    /// Linux, against an emulated ECU at the far end of a pseudo-terminal. It proves everything between
    /// the program and the kernel's serial layer (including the non-standard 7680 baud being accepted),
    /// though not an actual USB cable or the timing of a real ECU. Linux only: Windows has no pty.
    /// </summary>
    [Collection(EcuStaticState.Name)]
    public class LinuxSerialTests
    {
        private static EcuOption Iaw04k() => new(iaw04k.name, iaw04k.longName, iaw04k.GetCars());

        private static EcuOption Iaw16f() => new(iaw16f.name, iaw16f.longName, iaw16f.GetCars());

        private static void SkipUnlessLinux() => Assert.SkipUnless(OperatingSystem.IsLinux(), "Pseudo-terminals are Linux only.");

        [Fact]
        public async Task ConnectsAndIdentifiesTheCarWhenTheEcuAnnouncesItself()
        {
            SkipUnlessLinux();
            using var fake = new FakeEcu();
            fake.AnnounceIsoAfterOpen = true;

            var result = await new EcuConnectionService().ConnectAsync(Iaw04k(), fake.PortName, false, CancellationToken.None);

            Assert.True(result.Success, result.Detail);
            using var session = result.Session!;
            Assert.Equal("55-CE-85-83-94-BF", result.IsoCode);
            Assert.Equal("Coupe S 2.0", result.CarModel);
            Assert.Equal("61602.063.01", result.RepCode);
            Assert.True(result.EcuIdentified);
        }

        [Fact]
        public async Task ReadsTheIsoByRequestWhenTheEcuStaysQuiet()
        {
            SkipUnlessLinux();
            using var fake = new FakeEcu();

            var result = await new EcuConnectionService().ConnectAsync(Iaw04k(), fake.PortName, false, CancellationToken.None);

            Assert.True(result.Success, result.Detail);
            using var session = result.Session!;
            Assert.Equal("Coupe S 2.0", result.CarModel);
            Assert.Contains((byte)0x30, fake.Requests);
        }

        [Fact]
        public async Task ReportsThatNothingAnsweredWhenTheEcuIsOff()
        {
            SkipUnlessLinux();
            using var fake = new FakeEcu { IsOff = true };

            var result = await new EcuConnectionService().ConnectAsync(Iaw04k(), fake.PortName, false, CancellationToken.None);

            Assert.True(result.Success, result.Detail);
            using var session = result.Session!;
            Assert.False(result.EcuIdentified);
        }

        [Fact]
        public async Task AnIaw16fThatDoesntAnnounceItselfConnectsWithTheCarUnknown()
        {
            SkipUnlessLinux();
            using var fake = new FakeEcu();

            var result = await new EcuConnectionService().ConnectAsync(Iaw16f(), fake.PortName, false, CancellationToken.None);

            Assert.True(result.Success, result.Detail);
            using var session = result.Session!;
            Assert.True(result.EcuIdentified);
            Assert.True(result.CarNotIdentified);
            Assert.Equal(IES_2.Res.lang.UnknownISO, result.CarModel);
        }

        [Fact]
        public async Task AnIaw16fIsIdentifiedWhenItAnnouncesItselfAsTheIgnitionComesOn()
        {
            SkipUnlessLinux();
            using var fake = new FakeEcu { AnnounceIsoAfterOpen = true, AnnouncedIso = new byte[] { 0x55, 0xD0, 0x85, 0x85, 0x94, 0x43 } };

            var result = await new EcuConnectionService().ConnectAsync(Iaw16f(), fake.PortName, false, CancellationToken.None);

            Assert.True(result.Success, result.Detail);
            using var session = result.Session!;
            Assert.False(result.CarNotIdentified);
            Assert.Equal("Cinquecento 899 SPI ECE F2", result.CarModel);
        }

        [Fact]
        public async Task OtherEcusCanBeAskedForTheirCodeSoTheCarIsIdentifiedAnyway()
        {
            SkipUnlessLinux();
            using var fake = new FakeEcu();

            var result = await new EcuConnectionService().ConnectAsync(Iaw04k(), fake.PortName, false, CancellationToken.None);

            Assert.True(result.Success, result.Detail);
            using var session = result.Session!;
            Assert.False(result.CarNotIdentified);
        }

        [Fact]
        public async Task ACableWithoutPowerStopsTheConnectionAndFreesThePort()
        {
            SkipUnlessLinux();
            using var fake = new FakeEcu { CableHasNoPower = true };
            var service = new EcuConnectionService();

            var result = await service.ConnectAsync(Iaw04k(), fake.PortName, false, CancellationToken.None);

            Assert.False(result.Success);
            Assert.Equal(ConnectError.CableNotAnswering, result.Error);
            Assert.Null(result.Session);

            // The clips go on, and the same port connects.
            fake.CableHasNoPower = false;
            var retry = await service.ConnectAsync(Iaw04k(), fake.PortName, false, CancellationToken.None);
            Assert.True(retry.Success, retry.Detail);
            using var session = retry.Session!;
            Assert.True(retry.EcuIdentified);
        }

        [Fact]
        public void TheCableTestPassesWithItsPowerOnEvenWithNoCar()
        {
            SkipUnlessLinux();
            using var fake = new FakeEcu { IsOff = true };
            Assert.Equal(CableCondition.Working, CableCheck.Run(fake.PortName).Condition);
        }

        [Fact]
        public void TheCableTestNoticesItsPowerIsOff()
        {
            SkipUnlessLinux();
            using var fake = new FakeEcu { CableHasNoPower = true };
            Assert.Equal(CableCondition.NoEcho, CableCheck.Run(fake.PortName).Condition);
        }

        [Fact]
        public void TheCableTestNoticesADirtyLine()
        {
            SkipUnlessLinux();
            using var fake = new FakeEcu { IsOff = true, GarbleEcho = true };
            Assert.Equal(CableCondition.Garbled, CableCheck.Run(fake.PortName).Condition);
        }

        [Fact]
        public void TheCableTestReportsAMissingPort()
        {
            SkipUnlessLinux();
            var result = CableCheck.Run("/dev/no-such-port");
            Assert.Equal(CableCondition.PortFailed, result.Condition);
            Assert.Contains("/dev/no-such-port", result.Detail);
        }

        [Fact]
        public void TheCableTestLeavesThePortFree()
        {
            SkipUnlessLinux();
            using var fake = new FakeEcu { IsOff = true };
            CableCheck.Run(fake.PortName);
            Assert.Equal(CableCondition.Working, CableCheck.Run(fake.PortName).Condition);
        }

        [Fact]
        public async Task CancellingWhileWaitingForTheEcuFreesThePort()
        {
            SkipUnlessLinux();
            using var fake = new FakeEcu { IsOff = true };
            using var cts = new CancellationTokenSource(300);
            var service = new EcuConnectionService();

            var result = await service.ConnectAsync(Iaw04k(), fake.PortName, false, cts.Token);
            Assert.Equal(ConnectError.Cancelled, result.Error);

            // Still free: a second attempt can open it.
            using var again = new CancellationTokenSource(300);
            var retry = await service.ConnectAsync(Iaw04k(), fake.PortName, false, again.Token);
            Assert.Equal(ConnectError.Cancelled, retry.Error);
        }

        [Fact]
        public async Task AMissingPortIsReportedAsSuch()
        {
            SkipUnlessLinux();
            var result = await new EcuConnectionService().ConnectAsync(Iaw04k(), "/dev/no-such-port", false, CancellationToken.None);

            Assert.Equal(ConnectError.PortOpenFailed, result.Error);
            Assert.Contains("/dev/no-such-port", result.Detail);
        }

        [Fact]
        public async Task PollsParametersAndDecodesWhatTheEcuSends()
        {
            SkipUnlessLinux();
            using var fake = new FakeEcu { AnnounceIsoAfterOpen = true };
            // 1000 rpm: the ECU reports the period, 30 000 000 / 1000 = 30000 = 0x7530.
            fake.Values[0x01] = 0x75;
            fake.Values[0x02] = 0x30;
            fake.Values[0x0A] = 220; // battery, 220 * 0.0628 = 13.816 V

            var result = await new EcuConnectionService().ConnectAsync(Iaw04k(), fake.PortName, false, CancellationToken.None);
            using var session = result.Session!;
            var engine = session.Ecu;
            foreach (var request in new byte[] { 0x01, 0x02, 0x0A }) session.RequestEnabled[request] = true;

            var updated = new TaskCompletionSource();
            session.ParametersUpdated += () => updated.TrySetResult();
            session.PollEverything();
            session.StartLoop();
            await updated.Task.WaitAsync(TimeSpan.FromSeconds(10));

            var rpm = engine.engineData.Single(d => d.RequestSet.SequenceEqual(new byte[] { 0x01, 0x02 }));
            var battery = engine.engineData.Single(d => d.RequestSet.SequenceEqual(new byte[] { 0x0A }));
            Assert.True(ecu.Valid[0x01] && ecu.Valid[0x02] && ecu.Valid[0x0A]);
            Assert.Equal(1000m, Math.Round(rpm.Value, 3));
            Assert.Equal(13.816m, Math.Round(battery.Value, 3));
        }

        [Fact]
        public async Task ReadsTheFaultCodes()
        {
            SkipUnlessLinux();
            using var fake = new FakeEcu { AnnounceIsoAfterOpen = true };
            fake.Values[0x0F] = 0b0000_0001; // active: first fault
            fake.Values[0x1D] = 0b0000_0001; // stored: first fault

            var result = await new EcuConnectionService().ConnectAsync(Iaw04k(), fake.PortName, false, CancellationToken.None);
            using var session = result.Session!;

            var read = new TaskCompletionSource();
            session.ErrorsUpdated += () => read.TrySetResult();
            session.PollEverything();
            session.StartLoop();
            await read.Task.WaitAsync(TimeSpan.FromSeconds(10));

            session.Ecu.engineErrors[0].Decode();
            Assert.True(session.Ecu.engineErrors[0].isActive);
            Assert.True(session.Ecu.engineErrors[0].isStored);
            session.Ecu.engineErrors[1].Decode();
            Assert.False(session.Ecu.engineErrors[1].isActive);
        }

        [Fact]
        public async Task RunsAnActuatorTestThroughActiveDiagnosticsAndResumesPolling()
        {
            SkipUnlessLinux();
            using var fake = new FakeEcu { AnnounceIsoAfterOpen = true };

            var result = await new EcuConnectionService().ConnectAsync(Iaw04k(), fake.PortName, false, CancellationToken.None);
            using var session = result.Session!;
            session.RequestEnabled[0x0A] = true;
            session.PollEverything();
            session.StartLoop();

            var outcome = await session.RunTestAsync(session.Ecu.activeTest[1], CancellationToken.None);

            Assert.Equal(ActiveOutcome.Passed, outcome);
            Assert.Contains((byte)0xAA, fake.Requests);
            Assert.Contains((byte)0x81, fake.Requests);

            // Polling is back: the parameter gets asked for again.
            fake.Requests.Clear();
            var deadline = Environment.TickCount64 + 10_000;
            while (!fake.Requests.Contains((byte)0x0A) && Environment.TickCount64 < deadline)
                await Task.Delay(20);
            Assert.Contains((byte)0x0A, fake.Requests);
        }

        [Fact]
        public async Task ClearsTheCodes()
        {
            SkipUnlessLinux();
            using var fake = new FakeEcu { AnnounceIsoAfterOpen = true };

            var result = await new EcuConnectionService().ConnectAsync(Iaw04k(), fake.PortName, false, CancellationToken.None);
            using var session = result.Session!;
            session.PollEverything();
            session.StartLoop();

            var outcome = await session.ClearCodesAsync(CancellationToken.None);

            Assert.Equal(ActiveOutcome.Passed, outcome);
            Assert.Contains((byte)0x86, fake.Requests);
        }

        [Fact]
        public async Task NoticesWhenTheEcuStopsAnsweringAndCanPickUpAgain()
        {
            SkipUnlessLinux();
            using var fake = new FakeEcu { AnnounceIsoAfterOpen = true };

            var result = await new EcuConnectionService().ConnectAsync(Iaw04k(), fake.PortName, false, CancellationToken.None);
            using var session = result.Session!;
            session.RequestEnabled[0x0A] = true;
            session.RequestEnabled[0x07] = true;
            session.RequestEnabled[0x08] = true;
            var lost = new TaskCompletionSource();
            session.CommunicationLost += _ => lost.TrySetResult();
            session.PollEverything();
            session.StartLoop();
            await Task.Delay(300);

            fake.IsOff = true;
            await lost.Task.WaitAsync(TimeSpan.FromSeconds(15));

            fake.IsOff = false;
            var updated = new TaskCompletionSource();
            session.ParametersUpdated += () => updated.TrySetResult();
            await session.ReconnectAsync();
            await updated.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.True(ecu.Valid[0x0A]);
        }
    }

    /// <summary>
    /// A Weber-Marelli IAW-04K.P8 as seen from the K-line, cable included: echoes each byte (as the K-line
    /// cable does) and answers it. Behind a pseudo-terminal, whose slave side the program opens as its serial port.
    /// </summary>
    internal sealed class FakeEcu : IDisposable
    {
        private const int O_RDWR = 2;
        private const int O_NOCTTY = 0x100;

        // The ISO code of a Coupé 2.0 16V, and a spare-part code.
        private static readonly byte[] Iso = { 0x55, 0xCE, 0x85, 0x83, 0x94, 0xBF };
        private const string SparePart = "6160206301";

        private readonly SafeFileHandle _master;
        private readonly FileStream _stream;
        private readonly Thread _thread;
        private volatile bool _stopping;
        private bool _inActiveDiagnostics;

        public string PortName { get; }
        public bool AnnounceIsoAfterOpen { get; set; }
        public byte[] AnnouncedIso { get; set; } = Iso;
        public volatile bool IsOffField;
        public bool IsOff { get => IsOffField; set => IsOffField = value; }

        /// <summary>No 12 V on the cable: its K-line side is dead, so not even its echo comes back.</summary>
        public bool CableHasNoPower { get => _cableHasNoPower; set => _cableHasNoPower = value; }
        private volatile bool _cableHasNoPower;

        /// <summary>Something else on the line: what comes back isn't what was sent.</summary>
        public bool GarbleEcho { get; set; }
        public Dictionary<byte, byte> Values { get; } = new();
        public ConcurrentBag<byte> Requests { get; } = new();

        public FakeEcu()
        {
            var fd = posix_openpt(O_RDWR | O_NOCTTY);
            if (fd < 0 || grantpt(fd) != 0 || unlockpt(fd) != 0)
                throw new IOException("Can't create a pseudo-terminal.");
            var name = new StringBuilder(256);
            if (ptsname_r(fd, name, name.Capacity) != 0)
                throw new IOException("Can't name the pseudo-terminal.");
            PortName = name.ToString();
            _master = new SafeFileHandle(fd, ownsHandle: true);
            _stream = new FileStream(_master, FileAccess.ReadWrite, bufferSize: 0, isAsync: false);
            _thread = new Thread(Run) { IsBackground = true };
            _thread.Start();
        }

        private void Run()
        {
            var one = new byte[1];
            var announced = false;
            while (!_stopping)
            {
                // Until the program opens its end, reading the master fails with EIO: wait and retry.
                if (!IsSlaveOpen())
                {
                    announced = false;
                    Thread.Sleep(5);
                    continue;
                }

                if (AnnounceIsoAfterOpen && !announced && !IsOff)
                {
                    announced = true;
                    Thread.Sleep(50);
                    Send(AnnouncedIso);
                }

                if (!HasInput())
                {
                    Thread.Sleep(1);
                    continue;
                }

                int read;
                try { read = _stream.Read(one, 0, 1); }
                catch (IOException) { continue; }
                if (read == 0) continue;
                Respond(one[0]);
            }
        }

        private void Respond(byte request)
        {
            Requests.Add(request);
            if (CableHasNoPower) return;
            if (GarbleEcho)
            {
                Send(new[] { (byte)(request ^ 0xFF) });
                return;
            }
            if (IsOff)
            {
                Send(new[] { request }); // the ECU is silent, but the cable still echoes
                return;
            }

            var echoAndAnswer = new[] { request, Answer(request) };
            Send(echoAndAnswer);

            // An actuator test's request is followed, once done, by its result byte.
            if (_inActiveDiagnostics && request is not 0xAA and not 0xFF)
                Send(new byte[] { 0xFF });
        }

        private byte Answer(byte request)
        {
            switch (request)
            {
                case 0xAA:
                    _inActiveDiagnostics = true;
                    return 0xAA;
                case 0xFF:
                    _inActiveDiagnostics = false;
                    return 0xFF;
            }
            if (_inActiveDiagnostics) return request;
            if (request is >= 0x30 and <= 0x35) return Iso[request - 0x30];
            if (request is >= 0x36 and <= 0x3F) return (byte)SparePart[request - 0x36];
            return Values.TryGetValue(request, out var value) ? value : (byte)0;
        }

        private void Send(byte[] bytes)
        {
            try
            {
                _stream.Write(bytes, 0, bytes.Length);
                _stream.Flush();
            }
            catch (IOException)
            {
                // The program closed its end meanwhile.
            }
        }

        private bool IsSlaveOpen()
        {
            // With no slave open, poll reports POLLHUP on the master.
            var fds = new PollFd { Fd = (int)_master.DangerousGetHandle(), Events = PollIn };
            poll(ref fds, 1, 0);
            return (fds.Revents & PollHup) == 0;
        }

        private bool HasInput()
        {
            var fds = new PollFd { Fd = (int)_master.DangerousGetHandle(), Events = PollIn };
            return poll(ref fds, 1, 1) > 0 && (fds.Revents & PollIn) != 0;
        }

        public void Dispose()
        {
            _stopping = true;
            _thread.Join(1000);
            _stream.Dispose();
        }

        private const short PollIn = 0x001;
        private const short PollHup = 0x010;

        [StructLayout(LayoutKind.Sequential)]
        private struct PollFd
        {
            public int Fd;
            public short Events;
            public short Revents;
        }

        [DllImport("libc", SetLastError = true)]
        private static extern int posix_openpt(int flags);

        [DllImport("libc", SetLastError = true)]
        private static extern int grantpt(int fd);

        [DllImport("libc", SetLastError = true)]
        private static extern int unlockpt(int fd);

        [DllImport("libc", SetLastError = true)]
        private static extern int ptsname_r(int fd, StringBuilder buffer, int size);

        [DllImport("libc", SetLastError = true)]
        private static extern int poll(ref PollFd fds, ulong count, int timeout);
    }
}
