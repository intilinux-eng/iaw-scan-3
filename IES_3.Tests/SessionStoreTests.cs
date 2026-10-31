using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json.Nodes;
using IES_2.Session;
using Xunit;

namespace IES_2.Tests
{
    public sealed class SessionStoreTests : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "iawscan-tests", Guid.NewGuid().ToString("N"));

        public void Dispose()
        {
            if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        }

        private SessionRecorder NewRecorder(DateTime? started = null, bool simulation = false) => new(_root, new SessionHeader
        {
            EcuId = "IAW-04K.P8",
            EcuType = "Weber-Marelli IAW-04K.P8 MPI",
            Vehicle = "Coupe S 2.0",
            IsoCode = "55-CE-85-83-94-BF",
            Simulation = simulation,
            StartedUtc = started ?? DateTime.UtcNow,
            Columns = new[] { new SessionColumn("Engine Speed", "RPM"), new SessionColumn("Battery; main", "V"), new SessionColumn("Never read", "") },
        });

        private static JsonObject Fault(string name) => new() { ["description"] = name, ["reason"] = "", ["state"] = "Active", ["mil"] = true };

        [Fact]
        public void ARecordedSessionReadsBackWithItsValuesAndEvents()
        {
            string id;
            using (var recorder = NewRecorder())
            {
                id = recorder.Id;
                recorder.Event(SessionEventTypes.Connected);
                recorder.StartData();
                recorder.Row(new double?[] { 850.5, 13.8, null });
                recorder.Row(new double?[] { 900, null, null });
                recorder.Event(SessionEventTypes.FaultsChanged, new JsonObject { ["faults"] = new JsonArray(Fault("Sensore colpi")) });
            }

            var store = new SessionStore(_root);
            var summary = Assert.Single(store.List());
            Assert.Equal(id, summary.Id);
            Assert.Equal("Coupe S 2.0", summary.Header.Vehicle);
            Assert.Equal(2, summary.Header.DataRows);
            Assert.True(summary.HasData);
            Assert.Equal(new[] { "Sensore colpi" }, summary.Faults);

            var loaded = store.Load(id);
            Assert.Equal(new[] { "Engine Speed", "Battery, main" }, loaded.Series.Select(s => s.Name));
            Assert.Equal("RPM", loaded.Series[0].Unit);
            Assert.Equal(900, loaded.Series[0].Data.ValueAt(1000));
            Assert.Contains(loaded.Events, e => e.Type == SessionEventTypes.Connected);
        }

        [Fact]
        public void DecimalsAreWrittenWithAPointWhateverTheLanguage()
        {
            var previous = System.Globalization.CultureInfo.CurrentCulture;
            System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("it-IT");
            try
            {
                string path;
                using (var recorder = NewRecorder())
                {
                    recorder.StartData();
                    recorder.Row(new double?[] { 13.8, null, null });
                    path = Path.Combine(recorder.Directory, SessionRecorder.DataFileName);
                }
                var row = File.ReadAllLines(path)[1].Split(';');
                Assert.Equal("13.8", row[2]);
            }
            finally
            {
                System.Globalization.CultureInfo.CurrentCulture = previous;
            }
        }

        [Fact]
        public void ASessionCutOffByAPowerLossStillReadsBack()
        {
            string id;
            string directory;
            // No Dispose, as after a power cut: no closing summary in session.json...
            var recorder = NewRecorder();
            id = recorder.Id;
            directory = recorder.Directory;
            recorder.Event(SessionEventTypes.Connected);
            recorder.StartData();
            recorder.Row(new double?[] { 850, 13.8, null });
            recorder.Row(new double?[] { 860, 13.9, null });
            recorder.Sync();
            recorder.Dispose();

            // ...and half a line at the end of each file.
            var headerPath = Path.Combine(directory, SessionRecorder.HeaderFileName);
            var header = JsonNode.Parse(File.ReadAllText(headerPath))!.AsObject();
            header.Remove("ended_utc");
            header.Remove("duration_seconds");
            header.Remove("data_rows");
            File.WriteAllText(headerPath, header.ToJsonString());
            File.AppendAllText(Path.Combine(directory, SessionRecorder.DataFileName), "12.34");
            File.AppendAllText(Path.Combine(directory, SessionRecorder.EventsFileName), "{\"seq\":99,\"t\":1");

            var store = new SessionStore(_root);
            var summary = store.Find(id)!;
            Assert.Null(summary.Header.EndedUtc);
            Assert.True(summary.DurationSeconds >= 0);

            var loaded = store.Load(id);
            Assert.Equal(860, loaded.Series[0].Data.ValueAt(5));
            Assert.Single(loaded.Events, e => e.Type == SessionEventTypes.Connected);
        }

        [Fact]
        public void AGapInTheDataIsNotDrawnAsALine()
        {
            var path = Path.Combine(Directory.CreateDirectory(_root).FullName, "gap.csv");
            File.WriteAllText(path, "t_s;utc;Speed [RPM]\n1;x;100\n2;x;110\n60;x;120\n61;x;130\n");

            var series = SessionStore.LoadCsv(path).Series[0].Data;

            var points = new System.Collections.Generic.List<ChartPoint>();
            series.Downsample(0, 100, 1000, points);
            Assert.Contains(points, p => p.IsGap);
        }

        [Fact]
        public void RecordingsFromEarlierVersionsOpen()
        {
            var path = Path.Combine(Directory.CreateDirectory(_root).FullName, "IESexp_2610280010.csv");
            File.WriteAllText(path, "Timestamp;Velocità motore;Temperatura Acqua\n109;1229.81;24.3\n1203;1300;25\n");

            var loaded = SessionStore.LoadCsv(path);

            Assert.Equal(new[] { "Velocità motore", "Temperatura Acqua" }, loaded.Series.Select(s => s.Name));
            Assert.Equal(1.203, loaded.DurationSeconds, 3);
            Assert.Equal(25, loaded.Series[1].Data.ValueAt(2));
        }

        [Fact]
        public void AnUnrelatedFileIsRefused()
        {
            var path = Path.Combine(Directory.CreateDirectory(_root).FullName, "other.csv");
            File.WriteAllText(path, "name,surname\nmario,rossi\n");
            Assert.Throws<FormatException>(() => SessionStore.LoadCsv(path));
        }

        [Theory]
        [InlineData("en", "README.md", "Example request to an AI assistant")]
        [InlineData("it", "LEGGIMI.md", "Esempio di richiesta a un assistente AI")]
        public void AnExportHoldsTheFilesAndAReadmeInTheChosenLanguage(string language, string readmeName, string expectedText)
        {
            string id;
            using (var recorder = NewRecorder())
            {
                id = recorder.Id;
                recorder.StartData();
                recorder.Row(new double?[] { 850, 13.8, null });
            }

            using var zipStream = new MemoryStream();
            new SessionStore(_root).ExportZip(zipStream, new[] { id }, language);
            zipStream.Position = 0;
            using var zip = new ZipArchive(zipStream);

            Assert.NotNull(zip.GetEntry(SessionRecorder.HeaderFileName));
            Assert.NotNull(zip.GetEntry(SessionRecorder.EventsFileName));
            Assert.NotNull(zip.GetEntry(SessionRecorder.DataFileName));
            using var reader = new StreamReader(zip.GetEntry(readmeName)!.Open());
            var readme = reader.ReadToEnd();
            Assert.Contains(expectedText, readme);
            Assert.Contains("Coupe S 2.0", readme);
            Assert.Contains("Engine Speed [RPM]", readme);
        }

        [Fact]
        public void ExportingSeveralSessionsKeepsThemInSeparateFolders()
        {
            string first, second;
            using (var recorder = NewRecorder(new DateTime(2026, 1, 1, 8, 0, 0, DateTimeKind.Utc))) first = recorder.Id;
            using (var recorder = NewRecorder(new DateTime(2026, 1, 2, 8, 0, 0, DateTimeKind.Utc))) second = recorder.Id;

            using var zipStream = new MemoryStream();
            new SessionStore(_root).ExportZip(zipStream, new[] { first, second }, "en");
            zipStream.Position = 0;
            using var zip = new ZipArchive(zipStream);

            Assert.NotNull(zip.GetEntry(first + "/" + SessionRecorder.HeaderFileName));
            Assert.NotNull(zip.GetEntry(second + "/" + SessionRecorder.HeaderFileName));
            Assert.NotNull(zip.GetEntry("README.md"));
        }

        [Fact]
        public void BeyondTheLimitTheOldestSessionsGoFirstButNeverTheOneInProgress()
        {
            string oldest, middle, newest;
            using (var r = NewRecorder(new DateTime(2026, 1, 1, 8, 0, 0, DateTimeKind.Utc))) oldest = r.Id;
            using (var r = NewRecorder(new DateTime(2026, 1, 2, 8, 0, 0, DateTimeKind.Utc))) middle = r.Id;
            using (var r = NewRecorder(new DateTime(2026, 1, 3, 8, 0, 0, DateTimeKind.Utc))) newest = r.Id;
            var store = new SessionStore(_root);

            var deleted = store.Prune(0, exceptId: oldest);

            Assert.Equal(2, deleted);
            Assert.NotNull(store.Find(oldest));
            Assert.Null(store.Find(middle));
            Assert.Null(store.Find(newest));
        }

        [Theory]
        [InlineData("..")]
        [InlineData("../other")]
        [InlineData("..\\other")]
        [InlineData("a/b")]
        public void SessionIdsCantEscapeTheLogFolder(string id)
        {
            var store = new SessionStore(_root);
            Assert.Null(store.Find(id));
            store.Delete(id); // must neither throw nor touch anything
        }

        [Fact]
        public void AFolderThatIsntASessionIsIgnored()
        {
            Directory.CreateDirectory(Path.Combine(_root, "something-else"));
            Assert.Empty(new SessionStore(_root).List());
        }
    }
}
