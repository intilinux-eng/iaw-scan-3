using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Threading;

namespace IES_2.Coupe.Logging
{
    /// <summary>
    /// The always-on logbook. Every power-on of the board gets its own folder, datalog/boot-NNNNN/,
    /// holding events.jsonl (one JSON object per line: power-on, ECU found/lost, fault codes appearing
    /// and clearing, tests, clock syncs, shutdown...) and one data-NN-*.csv per ECU connection with every
    /// polled parameter. Folders are numbered rather than dated because the date is usually unknown at
    /// power-on (no battery-backed clock): see <see cref="DeviceClock"/>.
    ///
    /// Written for a board powered from the ignition, which simply loses power at key-off with no
    /// shutdown: files are only ever appended to and are fsync'ed every few seconds, so a power cut
    /// loses at most the last few seconds; when no ECU data is flowing a heartbeat event every minute
    /// still pins down when the power went. The oldest power-ons are deleted once the logbook
    /// outgrows its size limit.
    /// </summary>
    public sealed class DataLogger : IDisposable
    {
        public const string EventsFileName = "events.jsonl";
        private static readonly TimeSpan SyncInterval = TimeSpan.FromSeconds(5);
        private const double HeartbeatSeconds = 60;

        public static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            // Keep accented Italian text readable in the file instead of è escapes.
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };

        public static string Root => Path.Combine(Directory.GetCurrentDirectory(), "datalog");
        private static string StatePath => Path.Combine(Root, "state.json");
        private static string RunningMarker => Path.Combine(Root, "running");

        private readonly DeviceClock _clock;
        private readonly long _maxBytes;
        private readonly double _dataIntervalSeconds;
        private readonly object _gate = new();
        private readonly FileStream _eventsStream;
        private readonly StreamWriter _events;
        private readonly Timer _syncTimer;
        private long _seq;
        private FileStream? _dataStream;
        private StreamWriter? _data;
        private double _lastRowT = double.NegativeInfinity;
        private long _dataRows;
        private int _dataFiles;
        private double _lastWriteT;
        private bool _disposed;

        public int BootSeq { get; }
        public string BootDirectory { get; }
        public string? DataFileName { get; private set; }
        public long MaxBytes => _maxBytes;
        public double DataIntervalSeconds => _dataIntervalSeconds;

        public DataLogger(DeviceClock clock, long maxBytes, int dataIntervalMs)
        {
            _clock = clock;
            _maxBytes = maxBytes;
            _dataIntervalSeconds = dataIntervalMs / 1000.0;

            Directory.CreateDirectory(Root);
            var state = LoadState();
            var bootId = ReadBootId();
            var newBoot = bootId != state.LastBootId;
            // Deleted on a clean stop: still there means the last run ended by power loss or crash.
            var previousRunEndedCleanly = !File.Exists(RunningMarker);
            if (newBoot)
            {
                state.BootSeq++;
                state.LastBootId = bootId;
                SaveState(state);
            }

            BootSeq = state.BootSeq;
            BootDirectory = Path.Combine(Root, BootFolderName(BootSeq));
            Directory.CreateDirectory(BootDirectory);
            _dataFiles = Directory.GetFiles(BootDirectory, "data-*.csv").Length;
            _seq = CountLines(Path.Combine(BootDirectory, EventsFileName));
            _eventsStream = new FileStream(Path.Combine(BootDirectory, EventsFileName), FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
            _events = new StreamWriter(_eventsStream, new UTF8Encoding(false));
            File.WriteAllText(RunningMarker, bootId);

            var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString();
            if (newBoot)
            {
                // Powered from the ignition, "power_loss" is the normal way a drive ends.
                Event(EventTypes.PowerOn,
                    previousRunEndedCleanly
                        ? "Dispositivo acceso (quadro acceso). L'accensione precedente si era chiusa con un arresto del software."
                        : "Dispositivo acceso (quadro acceso). L'accensione precedente si era chiusa togliendo l'alimentazione (quadro spento).",
                    new { previous_shutdown = previousRunEndedCleanly ? "clean" : "power_loss" },
                    t: 0);
                Event(EventTypes.AppStart, "Software di diagnosi avviato.", new { version, power_on_seconds = Math.Round(_clock.Now, 1) });
            }
            else
            {
                Event(EventTypes.AppStart, "Software di diagnosi riavviato senza spegnere il dispositivo.",
                    new { version, previous_run = previousRunEndedCleanly ? "clean" : "crash" });
            }
            Sync();
            EnforceRetention();
            _syncTimer = new Timer(_ => Sync(), null, SyncInterval, SyncInterval);
        }

        public static string BootFolderName(int seq) => $"boot-{seq:D5}";

        /// <summary>Appends one line to events.jsonl.</summary>
        /// <param name="t">Power-on seconds the event refers to, when not "now".</param>
        public void Event(string type, string message, object? data = null, double? t = null)
        {
            lock (_gate)
            {
                if (_disposed) return;
                var now = _clock.Now;
                var at = t ?? now;
                var trusted = _clock.TrustedUtc;
                var record = new EventRecord(
                    ++_seq,
                    Math.Round(at, 3),
                    FormatUtc(DeviceClock.SystemUtc.AddSeconds(at - now)),
                    trusted.HasValue ? FormatUtc(trusted.Value.AddSeconds(at - now)) : null,
                    type,
                    message,
                    data);
                _events.WriteLine(JsonSerializer.Serialize(record, JsonOptions));
                _events.Flush();
                _lastWriteT = Math.Max(_lastWriteT, now);
            }
        }

        /// <summary>Starts a new data CSV for an ECU connection, one column per parameter.</summary>
        public string BeginData(string ecuId, IReadOnlyList<string> columns)
        {
            lock (_gate)
            {
                EndDataCore();
                EnforceRetention();
                _dataFiles++;
                DataFileName = $"data-{_dataFiles:D2}-{Slug(ecuId)}.csv";
                _dataStream = new FileStream(Path.Combine(BootDirectory, DataFileName), FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
                _data = new StreamWriter(_dataStream, new UTF8Encoding(false));
                _data.WriteLine("t_s;utc;" + string.Join(";", columns.Select(c => c.Replace(';', ','))));
                _lastRowT = double.NegativeInfinity;
                _dataRows = 0;
                return DataFileName;
            }
        }

        /// <summary>
        /// Adds a row of decoded values (null = no valid answer from the ECU), at most once per
        /// sampling interval however often the ECU loop delivers new values.
        /// </summary>
        public void Row(IReadOnlyList<double?> values)
        {
            lock (_gate)
            {
                if (_data == null) return;
                var t = _clock.Now;
                if (t - _lastRowT < _dataIntervalSeconds) return;
                _lastRowT = t;
                var line = new StringBuilder(t.ToString("0.000", CultureInfo.InvariantCulture)).Append(';');
                if (_clock.TrustedUtc is { } utc) line.Append(FormatUtc(utc));
                foreach (var v in values)
                {
                    line.Append(';');
                    if (v.HasValue) line.Append(v.Value.ToString(CultureInfo.InvariantCulture));
                }
                _data.WriteLine(line.ToString());
                _dataRows++;
                _lastWriteT = t;
            }
        }

        /// <summary>Closes the current data CSV (synced to disk). Returns its name and row count, if any.</summary>
        public (string? File, long Rows) EndData()
        {
            lock (_gate)
            {
                var result = (DataFileName, _dataRows);
                EndDataCore();
                SyncCore();
                return result;
            }
        }

        private void EndDataCore()
        {
            if (_data == null) return;
            try
            {
                _data.Flush();
                _dataStream!.Flush(flushToDisk: true);
                _data.Dispose();
            }
            catch (IOException) { /* nothing more can be saved */ }
            _data = null;
            _dataStream = null;
            DataFileName = null;
        }

        /// <summary>Forces everything written so far onto the eMMC.</summary>
        public void Sync()
        {
            if (_clock.Now - _lastWriteT >= HeartbeatSeconds)
                Event(EventTypes.Heartbeat, "Dispositivo acceso, nessun dato dalla centralina nell'ultimo minuto.");
            lock (_gate) SyncCore();
        }

        private void SyncCore()
        {
            if (_disposed) return;
            try
            {
                _events.Flush();
                _eventsStream.Flush(flushToDisk: true);
                if (_data != null)
                {
                    _data.Flush();
                    _dataStream!.Flush(flushToDisk: true);
                }
            }
            catch (IOException) { /* retried at the next tick */ }
        }

        public long UsedBytes() => DatalogArchive.DirectorySize(new DirectoryInfo(Root));

        private void EnforceRetention()
        {
            var boots = DatalogArchive.BootDirectories();
            var total = boots.Sum(b => b.Size);
            foreach (var boot in boots)
            {
                if (total <= _maxBytes) break;
                if (boot.Seq == BootSeq) continue;
                try
                {
                    Directory.Delete(boot.Path, recursive: true);
                    total -= boot.Size;
                    Event(EventTypes.Pruned, $"Spazio esaurito: eliminato il registro dell'accensione n. {boot.Seq}.", new { boot = boot.Seq, freed_bytes = boot.Size });
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    break;
                }
            }
        }

        public static string FormatUtc(DateTime utc) => utc.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);

        private static string Slug(string text) =>
            new string(text.Select(c => char.IsAsciiLetterOrDigit(c) ? c : '_').ToArray());

        private static string ReadBootId()
        {
            // A new value at every power-on of a Linux board; elsewhere (development on a PC) every
            // start of the program counts as a new power-on.
            try
            {
                const string path = "/proc/sys/kernel/random/boot_id";
                if (File.Exists(path)) return File.ReadAllText(path).Trim();
            }
            catch (IOException) { }
            return Guid.NewGuid().ToString();
        }

        private static long CountLines(string path)
        {
            if (!File.Exists(path)) return 0;
            try { return File.ReadLines(path).LongCount(); }
            catch (IOException) { return 0; }
        }

        private static LoggerState LoadState()
        {
            try
            {
                if (File.Exists(StatePath))
                    return JsonSerializer.Deserialize<LoggerState>(File.ReadAllText(StatePath)) ?? new LoggerState();
            }
            catch (Exception ex) when (ex is IOException or JsonException) { }
            // Lost state: continue numbering after the folders already on disk.
            var last = DatalogArchive.BootDirectories().Select(b => b.Seq).DefaultIfEmpty(0).Max();
            return new LoggerState { BootSeq = last };
        }

        private static void SaveState(LoggerState state)
        {
            var tmp = StatePath + ".tmp";
            using (var stream = new FileStream(tmp, FileMode.Create, FileAccess.Write))
            {
                JsonSerializer.Serialize(stream, state);
                stream.Flush(flushToDisk: true);
            }
            File.Move(tmp, StatePath, overwrite: true);
        }

        public void Dispose()
        {
            _syncTimer?.Dispose();
            Event(EventTypes.AppStop, "Software arrestato (spegnimento o riavvio regolare).");
            lock (_gate)
            {
                EndDataCore();
                SyncCore();
                _disposed = true;
                _events.Dispose();
            }
            try { File.Delete(RunningMarker); } catch (IOException) { }
        }

        private sealed class LoggerState
        {
            public string? LastBootId { get; set; }
            public int BootSeq { get; set; }
        }

        private sealed record EventRecord(long Seq, double T, string Sys, string? Utc, string Type, string Msg, object? Data);
    }
}
