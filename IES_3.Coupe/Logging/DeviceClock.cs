using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;

namespace IES_2.Coupe.Logging
{
    /// <summary>
    /// Time on a board with no battery-backed clock. Two time bases:
    /// - <see cref="Now"/>: seconds since the board was powered on. Monotonic and always right, so
    ///   every log record carries it.
    /// - <see cref="TrustedUtc"/>: calendar time, known only once a phone has sent its clock (or NTP
    ///   synced, when the board is on a network with internet). Records written before that are
    ///   given their calendar time afterwards, from the sync's (power-on seconds, UTC) pair.
    /// </summary>
    public sealed class DeviceClock
    {
        public const string SourcePhone = "phone";
        public const string SourceNtp = "ntp";

        /// <summary>A phone clock this close to the trusted one is not worth re-syncing to.</summary>
        private const double ResyncThresholdSeconds = 2;

        private const string NtpSyncedMarker = "/run/systemd/timesync/synchronized";
        private const string RuntimeDirectory = "/run/iaw-scan3-coupe";
        private static readonly string SyncedMarker = Path.Combine(RuntimeDirectory, "clock-synced");

        private readonly object _gate = new();
        private readonly Stopwatch _stopwatch = Stopwatch.StartNew();
        private readonly double _uptimeAtStart = ReadUptimeSeconds();
        private string? _source;
        private double _offsetSeconds; // added to the system clock when it couldn't be set
        private string? _timeZone;

        public DeviceClock()
        {
            // The service restarted within the same boot, after a phone had already set the clock
            // (/run is emptied at every power-on, so the marker can't outlive the boot).
            if (File.Exists(SyncedMarker))
            {
                _source = SourcePhone;
                _timeZone = SafeRead(SyncedMarker);
            }
        }

        /// <summary>Seconds since the board was powered on.</summary>
        public double Now => _uptimeAtStart + _stopwatch.Elapsed.TotalSeconds;

        /// <summary>Raw system clock: may be days off until synced, recorded for reference only.</summary>
        public static DateTime SystemUtc => DateTime.UtcNow;

        public string? Source
        {
            get
            {
                lock (_gate)
                {
                    if (_source == null && File.Exists(NtpSyncedMarker)) _source = SourceNtp;
                    return _source;
                }
            }
        }

        public string? TimeZone { get { lock (_gate) return _timeZone; } }

        public bool IsTrusted => Source != null;

        public DateTime? TrustedUtc
        {
            get
            {
                if (!IsTrusted) return null;
                lock (_gate) return DateTime.UtcNow.AddSeconds(_offsetSeconds);
            }
        }

        /// <summary>
        /// Takes the time from a phone that just opened the page.
        /// </summary>
        /// <returns>What changed, or null when the clock was already right.</returns>
        public ClockSync? SyncFromPhone(long utcMilliseconds, string? timeZone)
        {
            var phoneUtc = DateTimeOffset.FromUnixTimeMilliseconds(utcMilliseconds).UtcDateTime;
            lock (_gate)
            {
                var believed = DateTime.UtcNow.AddSeconds(_offsetSeconds);
                var drift = (phoneUtc - believed).TotalSeconds;
                if (_source != null && Math.Abs(drift) < ResyncThresholdSeconds)
                {
                    _timeZone ??= timeZone;
                    return null;
                }

                var systemBefore = DateTime.UtcNow;
                var systemSet = TrySetSystemClock(phoneUtc);
                _offsetSeconds = systemSet ? 0 : (phoneUtc - DateTime.UtcNow).TotalSeconds;
                _source = SourcePhone;
                _timeZone = timeZone ?? _timeZone;
                if (systemSet) TryWriteMarker(_timeZone);
                return new ClockSync(Now, phoneUtc, systemBefore, drift, systemSet, _timeZone);
            }
        }

        private static double ReadUptimeSeconds()
        {
            if (OperatingSystem.IsLinux())
            {
                var text = SafeRead("/proc/uptime");
                if (text != null && double.TryParse(text.Split(' ')[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds))
                    return seconds;
            }
            return Environment.TickCount64 / 1000.0;
        }

        private static bool TrySetSystemClock(DateTime utc)
        {
            // Needs CAP_SYS_TIME, which the systemd unit grants. Without it (development on a PC)
            // the phone's time is still used, as an offset on top of the unchanged system clock.
            if (!OperatingSystem.IsLinux()) return false;
            try
            {
                var ticks = utc.Ticks - DateTime.UnixEpoch.Ticks;
                var spec = new Timespec
                {
                    Seconds = ticks / TimeSpan.TicksPerSecond,
                    Nanoseconds = ticks % TimeSpan.TicksPerSecond * 100,
                };
                return clock_settime(ClockRealtime, ref spec) == 0;
            }
            catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
            {
                return false;
            }
        }

        private static void TryWriteMarker(string? timeZone)
        {
            try
            {
                if (Directory.Exists(RuntimeDirectory)) File.WriteAllText(SyncedMarker, timeZone ?? "");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Only lets a restarted service skip waiting for the next phone sync.
            }
        }

        private static string? SafeRead(string path)
        {
            try { return File.Exists(path) ? File.ReadAllText(path).Trim() : null; }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return null; }
        }

        private const int ClockRealtime = 0;

        [StructLayout(LayoutKind.Sequential)]
        private struct Timespec
        {
            public long Seconds;
            public long Nanoseconds;
        }

        [DllImport("libc", SetLastError = true)]
        private static extern int clock_settime(int clockId, ref Timespec time);
    }

    /// <param name="T">Power-on seconds at the moment of the sync.</param>
    /// <param name="Utc">The phone's time at that moment.</param>
    /// <param name="DriftSeconds">How far the device's previous belief was from the phone.</param>
    public sealed record ClockSync(double T, DateTime Utc, DateTime SystemBefore, double DriftSeconds, bool SystemClockSet, string? TimeZone);
}
