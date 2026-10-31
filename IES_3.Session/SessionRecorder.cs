using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Threading;

namespace IES_2.Session
{
    public sealed record SessionColumn(string Name, string Unit)
    {
        /// <summary>The CSV header cell: the name, then the unit in square brackets when it has one.</summary>
        [JsonIgnore]
        public string Header => (Unit.Length > 0 ? $"{Name} [{Unit}]" : Name).Replace(';', ',');
    }

    /// <summary>What session.json holds: everything about a session except its samples and events.</summary>
    public sealed record SessionHeader
    {
        public int FormatVersion { get; init; } = 1;
        public string AppVersion { get; init; } = "";
        public string EcuId { get; init; } = "";
        public string EcuType { get; init; } = "";
        public string Vehicle { get; init; } = "";
        public string IsoCode { get; init; } = "";
        public string RepCode { get; init; } = "";
        public bool Simulation { get; init; }
        public string? Port { get; init; }
        public DateTime StartedUtc { get; init; }
        public DateTime? EndedUtc { get; init; }
        public double? DurationSeconds { get; init; }
        public long? DataRows { get; init; }
        public IReadOnlyList<SessionColumn> Columns { get; init; } = Array.Empty<SessionColumn>();
    }

    /// <summary>The "type" values of events.jsonl. Stable identifiers: never rename an existing one.</summary>
    public static class SessionEventTypes
    {
        public const string Connected = "connected";
        public const string Disconnected = "disconnected";
        public const string CommLost = "comm_lost";
        public const string CommRestored = "comm_restored";
        public const string FaultsRead = "faults_read";
        public const string FaultsChanged = "faults_changed";
        public const string ClearCodes = "clear_codes";
        public const string ActuatorTest = "actuator_test";
        public const string Adjustment = "adjustment";
        public const string DataStarted = "data_started";
        public const string DataStopped = "data_stopped";
    }

    /// <summary>
    /// Records one ECU connection to its own folder: session.json (what was connected), events.jsonl
    /// (one JSON object per line) and data.csv (every polled parameter, while recording is on). Files
    /// are only appended to and are synced to disk every few seconds, so a crash or a power cut loses at
    /// most those last seconds, and whatever is on disk can always be read back.
    /// </summary>
    public sealed class SessionRecorder : IDisposable
    {
        public const string HeaderFileName = "session.json";
        public const string EventsFileName = "events.jsonl";
        public const string DataFileName = "data.csv";

        private static readonly TimeSpan SyncInterval = TimeSpan.FromSeconds(5);

        internal static readonly SessionJsonContext Json = new(new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            // Keep accented text readable in the files instead of \u00e8 escapes.
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        });

        private readonly object _gate = new();
        private readonly Stopwatch _clock = Stopwatch.StartNew();
        private readonly FileStream _eventsStream;
        private readonly StreamWriter _events;
        private readonly Timer _syncTimer;
        private SessionHeader _header;
        private FileStream? _dataStream;
        private StreamWriter? _data;
        private long _seq;
        private long _rows;
        private bool _disposed;

        public string Directory { get; }

        /// <summary>The folder name: the session's identifier in <see cref="SessionStore"/>.</summary>
        public string Id => Path.GetFileName(Directory);

        public bool IsRecordingData { get; private set; }
        public long DataRows { get { lock (_gate) return _rows; } }

        /// <summary>Seconds since the session started, the time base of every record.</summary>
        public double Elapsed => _clock.Elapsed.TotalSeconds;

        public SessionRecorder(string root, SessionHeader header)
        {
            _header = header;
            Directory = UniqueDirectory(root, header);
            WriteHeader();
            _eventsStream = new FileStream(Path.Combine(Directory, EventsFileName), FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
            _events = new StreamWriter(_eventsStream, new UTF8Encoding(false));
            _syncTimer = new Timer(_ => Sync(), null, SyncInterval, SyncInterval);
        }

        private static string UniqueDirectory(string root, SessionHeader header)
        {
            System.IO.Directory.CreateDirectory(root);
            var slug = new string(header.EcuId.Select(c => char.IsAsciiLetterOrDigit(c) ? c : '_').ToArray());
            var name = $"{header.StartedUtc.ToLocalTime():yyyyMMdd-HHmmss}-{slug}";
            var path = Path.Combine(root, name);
            for (int i = 2; System.IO.Directory.Exists(path); i++)
                path = Path.Combine(root, $"{name}-{i}");
            System.IO.Directory.CreateDirectory(path);
            return path;
        }

        public void Event(string type, JsonObject? data = null)
        {
            lock (_gate)
            {
                if (_disposed) return;
                var record = new EventRecord(++_seq, Math.Round(Elapsed, 3), FormatUtc(DateTime.UtcNow), type, data);
                _events.WriteLine(JsonSerializer.Serialize(record, Json.EventRecord));
                _events.Flush();
            }
        }

        /// <summary>Turns writing samples to data.csv on. Samples before this are not kept.</summary>
        public void StartData()
        {
            lock (_gate)
            {
                if (_disposed || IsRecordingData) return;
                if (_data == null)
                {
                    var path = Path.Combine(Directory, DataFileName);
                    var isNew = !File.Exists(path);
                    _dataStream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
                    _data = new StreamWriter(_dataStream, new UTF8Encoding(false));
                    if (isNew) _data.WriteLine("t_s;utc;" + string.Join(";", _header.Columns.Select(c => c.Header)));
                }
                IsRecordingData = true;
            }
            Event(SessionEventTypes.DataStarted);
        }

        public void StopData()
        {
            lock (_gate)
            {
                if (_disposed || !IsRecordingData) return;
                IsRecordingData = false;
            }
            Event(SessionEventTypes.DataStopped);
            Sync();
        }

        /// <summary>Appends one row of decoded values (null = no valid answer, or not polled).</summary>
        public void Row(IReadOnlyList<double?> values)
        {
            lock (_gate)
            {
                if (_disposed || !IsRecordingData || _data == null) return;
                var line = new StringBuilder(Elapsed.ToString("0.000", CultureInfo.InvariantCulture))
                    .Append(';').Append(FormatUtc(DateTime.UtcNow));
                foreach (var v in values)
                {
                    line.Append(';');
                    if (v.HasValue) line.Append(v.Value.ToString(CultureInfo.InvariantCulture));
                }
                _data.WriteLine(line.ToString());
                _rows++;
            }
        }

        public void Sync()
        {
            lock (_gate)
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
                catch (IOException)
                {
                    // Disk trouble must not stop the diagnosis; the next tick tries again.
                }
            }
        }

        private void WriteHeader()
        {
            var path = Path.Combine(Directory, HeaderFileName);
            var tmp = path + ".tmp";
            using (var stream = new FileStream(tmp, FileMode.Create, FileAccess.Write))
            {
                JsonSerializer.Serialize(stream, _header, Json.SessionHeader);
                stream.Flush(flushToDisk: true);
            }
            File.Move(tmp, path, overwrite: true);
        }

        public static string FormatUtc(DateTime utc) =>
            utc.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);

        public void Dispose()
        {
            _syncTimer.Dispose();
            lock (_gate)
            {
                if (_disposed) return;
                Sync();
                _disposed = true;
                IsRecordingData = false;
                try
                {
                    _data?.Dispose();
                    _events.Dispose();
                    _header = _header with
                    {
                        EndedUtc = DateTime.UtcNow,
                        DurationSeconds = Math.Round(Elapsed, 3),
                        DataRows = _rows,
                    };
                    WriteHeader();
                }
                catch (IOException)
                {
                    // The session is still readable without its closing summary.
                }
            }
        }
    }

    internal sealed record EventRecord(long Seq, double T, string Utc, string Type, JsonNode? Data);

    // Generated serializers: reflection-based JSON doesn't survive trimming the published app.
    [JsonSerializable(typeof(SessionHeader))]
    [JsonSerializable(typeof(EventRecord))]
    internal sealed partial class SessionJsonContext : JsonSerializerContext
    {
    }
}
