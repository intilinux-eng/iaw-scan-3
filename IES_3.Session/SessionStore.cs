using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace IES_2.Session
{
    public sealed record SessionSummary(
        string Id,
        string Directory,
        SessionHeader Header,
        double DurationSeconds,
        bool HasData,
        IReadOnlyList<string> Faults,
        long SizeBytes);

    public sealed record SessionEvent(double T, DateTime? Utc, string Type, JsonNode? Data);

    public sealed record LoadedSeries(string Name, string Unit, SeriesBuffer Data);

    /// <summary>A session (or any compatible CSV) read back into memory, ready to chart.</summary>
    public sealed record LoadedSession(
        string Title,
        double DurationSeconds,
        IReadOnlyList<LoadedSeries> Series,
        IReadOnlyList<SessionEvent> Events);

    /// <summary>
    /// Reads back what <see cref="SessionRecorder"/> wrote: the list of sessions, a session's samples and
    /// events, ZIP export, deletion and the size limit. Tolerates whatever a crash or a power cut can
    /// leave behind (no closing summary, a half-written last line).
    /// </summary>
    public sealed partial class SessionStore
    {
        /// <summary>Samples further apart than this are drawn as a gap, not joined by a line.</summary>
        private const double GapSeconds = 3;

        /// <summary>Above this many rows a loaded session keeps every n-th one, to stay bounded in memory.</summary>
        private const int MaxLoadedRows = 400_000;

        public string Root { get; }

        public SessionStore(string root)
        {
            Root = root;
        }

        public IReadOnlyList<SessionSummary> List()
        {
            var root = new DirectoryInfo(Root);
            if (!root.Exists) return Array.Empty<SessionSummary>();
            return root.GetDirectories()
                .OrderByDescending(d => d.Name, StringComparer.Ordinal)
                .Select(d => Summarize(d.FullName))
                .OfType<SessionSummary>()
                .ToList();
        }

        public SessionSummary? Find(string id) => IsValidId(id) ? Summarize(Path.Combine(Root, id)) : null;

        public long TotalBytes()
        {
            var root = new DirectoryInfo(Root);
            return root.Exists ? DirectorySize(root) : 0;
        }

        public void Delete(string id)
        {
            if (!IsValidId(id)) return;
            var path = Path.Combine(Root, id);
            if (System.IO.Directory.Exists(path)) System.IO.Directory.Delete(path, recursive: true);
        }

        /// <summary>Deletes the oldest sessions until the total fits the limit; never the one in progress.</summary>
        public int Prune(long maxBytes, string? exceptId)
        {
            var root = new DirectoryInfo(Root);
            if (!root.Exists) return 0;
            var sessions = root.GetDirectories().OrderBy(d => d.Name, StringComparer.Ordinal).ToList();
            var total = sessions.Sum(DirectorySize);
            var deleted = 0;
            foreach (var session in sessions)
            {
                if (total <= maxBytes) break;
                if (session.Name == exceptId) continue;
                try
                {
                    var size = DirectorySize(session);
                    session.Delete(recursive: true);
                    total -= size;
                    deleted++;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // Busy or protected: leave it and try the next one.
                }
            }
            return deleted;
        }

        public LoadedSession Load(string id)
        {
            var summary = Find(id) ?? throw new FileNotFoundException(id);
            var dataPath = Path.Combine(summary.Directory, SessionRecorder.DataFileName);
            var series = File.Exists(dataPath) ? ParseCsv(dataPath, out _) : Array.Empty<LoadedSeries>();
            return new LoadedSession(summary.Header.Vehicle, summary.DurationSeconds, series, ReadEvents(summary.Directory));
        }

        /// <summary>
        /// Opens a CSV from outside the logbook: this program's data.csv, or the recording format of
        /// earlier versions (Timestamp in milliseconds).
        /// </summary>
        /// <exception cref="FormatException">The file isn't in a recognised format.</exception>
        public static LoadedSession LoadCsv(string path)
        {
            var series = ParseCsv(path, out var duration);
            return new LoadedSession(Path.GetFileNameWithoutExtension(path), duration, series, Array.Empty<SessionEvent>());
        }

        /// <summary>Writes the given sessions to a ZIP with a README describing the format, in the given language.</summary>
        public void ExportZip(Stream output, IReadOnlyList<string> ids, string language)
        {
            using var zip = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true);
            var summaries = ids.Select(Find).OfType<SessionSummary>().ToList();
            foreach (var summary in summaries)
            {
                var prefix = summaries.Count == 1 ? "" : summary.Id + "/";
                foreach (var file in new[] { SessionRecorder.HeaderFileName, SessionRecorder.EventsFileName, SessionRecorder.DataFileName })
                {
                    var path = Path.Combine(summary.Directory, file);
                    if (!File.Exists(path)) continue;
                    using var source = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                    using var target = zip.CreateEntry(prefix + file, CompressionLevel.Optimal).Open();
                    source.CopyTo(target);
                }
            }

            using var readme = new StreamWriter(
                zip.CreateEntry(SessionReadme.FileName(language), CompressionLevel.Optimal).Open(), new UTF8Encoding(false));
            readme.Write(SessionReadme.Build(summaries, language));
        }

        private bool IsValidId(string id) => SafeId().IsMatch(id);

        private static SessionSummary? Summarize(string directory)
        {
            var headerPath = Path.Combine(directory, SessionRecorder.HeaderFileName);
            if (!File.Exists(headerPath)) return null;

            SessionHeader? header;
            try
            {
                header = JsonSerializer.Deserialize(File.ReadAllText(headerPath), SessionRecorder.Json.SessionHeader);
            }
            catch (Exception ex) when (ex is IOException or JsonException)
            {
                return null;
            }
            if (header == null) return null;

            var events = ReadEvents(directory);
            var faults = new SortedSet<string>(StringComparer.CurrentCulture);
            foreach (var e in events)
            {
                if (e.Type is not (SessionEventTypes.FaultsRead or SessionEventTypes.FaultsChanged)) continue;
                if (e.Data?["faults"] is not JsonArray list) continue;
                foreach (var fault in list)
                    if (fault?["description"]?.GetValue<string>() is { } description) faults.Add(description);
            }

            var dataPath = Path.Combine(directory, SessionRecorder.DataFileName);
            var hasData = File.Exists(dataPath);
            var duration = header.DurationSeconds
                ?? Math.Max(events.Count > 0 ? events[^1].T : 0, hasData ? LastRowTime(dataPath) ?? 0 : 0);

            return new SessionSummary(
                Path.GetFileName(directory), directory, header, duration, hasData, faults.ToList(),
                DirectorySize(new DirectoryInfo(directory)));
        }

        private static IReadOnlyList<SessionEvent> ReadEvents(string directory)
        {
            var path = Path.Combine(directory, SessionRecorder.EventsFileName);
            var events = new List<SessionEvent>();
            if (!File.Exists(path)) return events;

            foreach (var line in ReadLinesShared(path))
            {
                JsonNode? node;
                try { node = JsonNode.Parse(line); }
                catch (JsonException) { continue; } // half-written line from a power cut
                if (node?["type"]?.GetValue<string>() is not { } type) continue;

                DateTime? utc = null;
                if (node["utc"]?.GetValue<string>() is { } text
                    && DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var parsed))
                    utc = parsed;
                events.Add(new SessionEvent(node["t"]?.GetValue<double>() ?? 0, utc, type, node["data"]?.DeepClone()));
            }
            return events;
        }

        private static double? LastRowTime(string path)
        {
            // Only the tail matters: read the last few KB rather than the whole file.
            try
            {
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                stream.Seek(Math.Max(0, stream.Length - 8192), SeekOrigin.Begin);
                using var reader = new StreamReader(stream);
                var lines = reader.ReadToEnd().Split('\n', StringSplitOptions.RemoveEmptyEntries);
                for (int i = lines.Length - 1; i >= 0; i--)
                    if (double.TryParse(lines[i].Split(';', 2)[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var t))
                        return t;
            }
            catch (IOException) { }
            return null;
        }

        private static IReadOnlyList<LoadedSeries> ParseCsv(string path, out double duration)
        {
            duration = 0;
            var lines = 0L;
            foreach (var _ in ReadLinesShared(path)) lines++;
            var stride = Math.Max(1, (int)(lines / MaxLoadedRows));
            var capacity = (int)Math.Max(2, lines / stride + 2) * 2; // room for the NaN marking each gap

            using var reader = ReadLinesShared(path).GetEnumerator();
            if (!reader.MoveNext()) throw new FormatException("empty file");
            var header = reader.Current.Split(';');

            int firstColumn;
            double timeScale;
            if (header.Length >= 3 && header[0] == "t_s" && header[1] == "utc")
            {
                firstColumn = 2;
                timeScale = 1;
            }
            else if (header.Length >= 2 && header[0] == "Timestamp")
            {
                firstColumn = 1;
                timeScale = 0.001;
            }
            else
            {
                throw new FormatException("unrecognised header");
            }

            var columnCount = header.Length - firstColumn;
            var buffers = new SeriesBuffer[columnCount];
            for (int c = 0; c < columnCount; c++) buffers[c] = new SeriesBuffer(capacity);
            var used = new bool[columnCount];

            double? lastT = null;
            long row = 0;
            while (reader.MoveNext())
            {
                if (row++ % stride != 0) continue;
                var cells = reader.Current.Split(';');
                // Every row has all its columns: fewer means a line cut off by a crash or a power cut.
                if (cells.Length < header.Length) continue;
                if (!double.TryParse(cells[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var raw)) continue;
                var t = raw * timeScale;
                if (lastT.HasValue && t < lastT.Value) continue;

                if (lastT.HasValue && t - lastT.Value > Math.Max(GapSeconds, stride * 0.5))
                    foreach (var buffer in buffers) buffer.Add(lastT.Value, double.NaN);

                for (int c = 0; c < columnCount; c++)
                {
                    if (double.TryParse(cells[c + firstColumn], NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
                    {
                        buffers[c].Add(t, value);
                        used[c] = true;
                    }
                    else
                    {
                        buffers[c].Add(t, double.NaN);
                    }
                }
                lastT = t;
            }
            duration = lastT ?? 0;

            var result = new List<LoadedSeries>();
            for (int c = 0; c < columnCount; c++)
            {
                if (!used[c]) continue; // a parameter never polled in this session
                var (name, unit) = SplitHeader(header[c + firstColumn]);
                result.Add(new LoadedSeries(name, unit, buffers[c]));
            }
            return result;
        }

        private static (string Name, string Unit) SplitHeader(string cell)
        {
            var match = HeaderWithUnit().Match(cell);
            return match.Success ? (match.Groups[1].Value.Trim(), match.Groups[2].Value) : (cell.Trim(), "");
        }

        private static IEnumerable<string> ReadLinesShared(string path)
        {
            // The session in progress is still open for appending.
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream);
            while (reader.ReadLine() is { } line)
                if (line.Length > 0) yield return line;
        }

        private static long DirectorySize(DirectoryInfo directory)
        {
            try { return directory.EnumerateFiles("*", SearchOption.AllDirectories).Sum(f => f.Length); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return 0; }
        }

        [GeneratedRegex(@"^[A-Za-z0-9_\-]+$")]
        private static partial Regex SafeId();

        [GeneratedRegex(@"^(.*)\[([^\]]*)\]\s*$")]
        private static partial Regex HeaderWithUnit();
    }
}
