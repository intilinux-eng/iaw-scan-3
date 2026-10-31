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

namespace IES_2.Coupe.Logging
{
    /// <summary>
    /// Reads the logbook back: one summary per power-on for the phone's list, and ZIP exports in which
    /// every event and data row gets its calendar time, rebuilt from the power-on's first trusted
    /// clock reading when it was written before any phone had set the time. Tolerates the half-written
    /// last line that a power cut can leave behind.
    /// </summary>
    public static partial class DatalogArchive
    {
        public sealed record BootDirectory(int Seq, string Path, long Size);

        public sealed record BootSummary(
            int Seq,
            string? StartUtc,
            double DurationSeconds,
            int EcuSessions,
            IReadOnlyList<string> Vehicles,
            IReadOnlyList<string> Errors,
            long SizeBytes,
            bool Current,
            string Ending);

        public static IReadOnlyList<BootDirectory> BootDirectories()
        {
            var root = new DirectoryInfo(DataLogger.Root);
            if (!root.Exists) return Array.Empty<BootDirectory>();
            return root.GetDirectories("boot-*")
                .Select(d => (Dir: d, Match: BootFolder().Match(d.Name)))
                .Where(x => x.Match.Success)
                .Select(x => new BootDirectory(int.Parse(x.Match.Groups[1].Value, CultureInfo.InvariantCulture), x.Dir.FullName, DirectorySize(x.Dir)))
                .OrderBy(b => b.Seq)
                .ToList();
        }

        public static long DirectorySize(DirectoryInfo dir)
        {
            try { return dir.Exists ? dir.EnumerateFiles("*", SearchOption.AllDirectories).Sum(f => f.Length) : 0; }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return 0; }
        }

        public static BootDirectory? Find(int seq) => BootDirectories().FirstOrDefault(b => b.Seq == seq);

        public static BootSummary Summarize(BootDirectory boot, int currentSeq, double currentT)
        {
            var a = Analyze(boot.Path);
            var current = boot.Seq == currentSeq;
            var ending = current ? "current" : a.LastEventType == EventTypes.AppStop ? "clean" : "power_loss";
            return new BootSummary(
                boot.Seq,
                a.UtcAtZero.HasValue ? DataLogger.FormatUtc(a.UtcAtZero.Value) : null,
                Math.Round(current ? currentT : a.LastT, 1),
                a.EcuSessions,
                a.Vehicles.ToList(),
                a.Errors.ToList(),
                boot.Size,
                current,
                ending);
        }

        /// <summary>Writes a ZIP (synchronously) of the given power-ons plus a LEGGIMI.md describing the format.</summary>
        public static void WriteZip(Stream output, IReadOnlyList<BootDirectory> boots, int currentSeq, double currentT, string? version)
        {
            using var zip = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true);
            var single = boots.Count == 1;
            var described = new List<(BootSummary Summary, string? TimeZone, List<(string File, string[] Columns)> DataFiles)>();

            foreach (var boot in boots)
            {
                var analysis = Analyze(boot.Path);
                var prefix = single ? "" : DataLogger.BootFolderName(boot.Seq) + "/";
                var dataFiles = new List<(string, string[])>();

                var eventsPath = Path.Combine(boot.Path, DataLogger.EventsFileName);
                if (File.Exists(eventsPath))
                {
                    using var writer = new StreamWriter(zip.CreateEntry(prefix + DataLogger.EventsFileName, CompressionLevel.Optimal).Open(), new UTF8Encoding(false));
                    foreach (var line in ReadLinesShared(eventsPath))
                        if (ResolveEventLine(line, analysis.UtcAtZero) is { } resolved) writer.WriteLine(resolved);
                }

                foreach (var csv in Directory.GetFiles(boot.Path, "data-*.csv").OrderBy(f => f, StringComparer.Ordinal))
                {
                    var name = Path.GetFileName(csv);
                    using var writer = new StreamWriter(zip.CreateEntry(prefix + name, CompressionLevel.Optimal).Open(), new UTF8Encoding(false));
                    string[]? columns = null;
                    foreach (var line in ReadLinesShared(csv))
                    {
                        if (columns == null)
                        {
                            columns = line.Split(';').Skip(2).ToArray();
                            writer.WriteLine(line);
                            continue;
                        }
                        if (ResolveDataLine(line, analysis.UtcAtZero) is { } resolved) writer.WriteLine(resolved);
                    }
                    dataFiles.Add((name, columns ?? Array.Empty<string>()));
                }

                described.Add((Summarize(boot, currentSeq, currentT), analysis.TimeZone, dataFiles));
            }

            using var readme = new StreamWriter(zip.CreateEntry("LEGGIMI.md", CompressionLevel.Optimal).Open(), new UTF8Encoding(false));
            readme.Write(BuildReadme(described, single, version));
        }

        // ---- Analysis ----------------------------------------------------------------------

        private sealed class Analysis
        {
            public DateTime? UtcAtZero;
            public string? TimeZone;
            public double LastT;
            public int EcuSessions;
            public readonly SortedSet<string> Vehicles = new(StringComparer.Ordinal);
            public readonly SortedSet<string> Errors = new(StringComparer.Ordinal);
            public string? LastEventType;
        }

        private static Analysis Analyze(string bootPath)
        {
            var a = new Analysis();
            var eventsPath = Path.Combine(bootPath, DataLogger.EventsFileName);
            if (File.Exists(eventsPath))
            {
                foreach (var line in ReadLinesShared(eventsPath))
                {
                    JsonNode? node;
                    try { node = JsonNode.Parse(line); }
                    catch (JsonException) { continue; } // half-written line from a power cut
                    if (node == null) continue;

                    var t = node["t"]?.GetValue<double>() ?? 0;
                    a.LastT = Math.Max(a.LastT, t);
                    var type = node["type"]?.GetValue<string>();
                    a.LastEventType = type;

                    // Any record written with a trusted clock pins the calendar time of the whole power-on.
                    if (a.UtcAtZero == null && node["utc"]?.GetValue<string>() is { } utcText && TryParseUtc(utcText, out var utc))
                        a.UtcAtZero = utc.AddSeconds(-t);

                    // Simulated sessions only reach the logbook when explicitly asked for
                    // (IAW_LOG_SIMULATION), and are marked as such: count them like the others.
                    var data = node["data"];
                    switch (type)
                    {
                        case EventTypes.ClockSync:
                            a.TimeZone = data?["time_zone"]?.GetValue<string>() ?? a.TimeZone;
                            break;
                        case EventTypes.EcuConnected:
                            a.EcuSessions++;
                            if (data?["car_model"]?.GetValue<string>() is { } model) a.Vehicles.Add(model);
                            break;
                        case EventTypes.ErrorsRead or EventTypes.ErrorsChanged:
                            if (data?["errors"] is JsonArray errors)
                                foreach (var e in errors)
                                    if (e?["description"]?.GetValue<string>() is { } description) a.Errors.Add(description);
                            break;
                    }
                }
            }

            foreach (var csv in Directory.GetFiles(bootPath, "data-*.csv"))
                if (LastDataT(csv) is { } t) a.LastT = Math.Max(a.LastT, t);
            return a;
        }

        private static double? LastDataT(string path)
        {
            // Only the tail matters: read the last few KB rather than the whole file.
            try
            {
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                stream.Seek(Math.Max(0, stream.Length - 4096), SeekOrigin.Begin);
                using var reader = new StreamReader(stream);
                var lines = reader.ReadToEnd().Split('\n', StringSplitOptions.RemoveEmptyEntries);
                for (int i = lines.Length - 1; i >= 0; i--)
                {
                    var first = lines[i].Split(';', 2)[0];
                    if (double.TryParse(first, NumberStyles.Float, CultureInfo.InvariantCulture, out var t)) return t;
                }
            }
            catch (IOException) { }
            return null;
        }

        private static string? ResolveEventLine(string line, DateTime? utcAtZero)
        {
            JsonNode? node;
            try { node = JsonNode.Parse(line); }
            catch (JsonException) { return null; }
            if (node is not JsonObject obj) return null;
            if (obj["utc"] == null && utcAtZero.HasValue && obj["t"] is { } tNode)
            {
                obj["utc"] = DataLogger.FormatUtc(utcAtZero.Value.AddSeconds(tNode.GetValue<double>()));
                obj["utc_estimated"] = true;
            }
            return obj.ToJsonString(DataLogger.JsonOptions);
        }

        private static string? ResolveDataLine(string line, DateTime? utcAtZero)
        {
            var parts = line.Split(';', 3);
            if (parts.Length < 3 || !double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var t)) return null;
            if (parts[1].Length == 0 && utcAtZero.HasValue)
                return parts[0] + ";" + DataLogger.FormatUtc(utcAtZero.Value.AddSeconds(t)) + ";" + parts[2];
            return line;
        }

        private static bool TryParseUtc(string text, out DateTime utc) =>
            DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out utc);

        private static IEnumerable<string> ReadLinesShared(string path)
        {
            // The current power-on's files are still open for appending.
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream);
            while (reader.ReadLine() is { } line)
                if (line.Length > 0) yield return line;
        }

        [GeneratedRegex(@"^boot-(\d+)$")]
        private static partial Regex BootFolder();

        // ---- LEGGIMI.md --------------------------------------------------------------------

        private static string BuildReadme(
            IReadOnlyList<(BootSummary Summary, string? TimeZone, List<(string File, string[] Columns)> DataFiles)> boots,
            bool single,
            string? version)
        {
            var md = new StringBuilder();
            md.AppendLine("# Registro dati IAW Scan 3 Coupé");
            md.AppendLine();
            md.AppendLine($"Esportato da IAW Scan 3 Coupé {version}: registratore di dati per Fiat Coupé (centralina motore Weber-Marelli IAW-04K.P8 e immobilizer FIAT CODE), collegato alla presa diagnostica con un cavo K-line e alimentato dal quadro dell'auto.");
            md.AppendLine();
            md.AppendLine("I valori sono decodificati con le stesse formule del progetto originale IAW Scan 2 v0.85, senza reinterpretazioni. Il registratore è però un progetto sperimentale, non ancora verificato su un'auto vera: valori incoerenti possono dipendere anche dal dispositivo.");
            md.AppendLine();

            md.AppendLine("## Accensioni contenute");
            md.AppendLine();
            md.AppendLine("Ogni accensione corrisponde a un giro di chiave: dal quadro acceso (`t` = 0) al quadro spento, quando il dispositivo perde l'alimentazione.");
            md.AppendLine();
            md.AppendLine("| Accensione | Inizio (UTC) | Fuso orario del telefono | Durata | Collegamenti centralina | Veicolo | Errori visti | Fine |");
            md.AppendLine("|---|---|---|---|---|---|---|---|");
            foreach (var (s, tz, _) in boots)
            {
                var ending = s.Ending switch { "current" => "in corso", "clean" => "arresto regolare", _ => "alimentazione tolta" };
                md.AppendLine($"| {s.Seq} | {s.StartUtc ?? "sconosciuto"} | {tz ?? "-"} | {FormatDuration(s.DurationSeconds)} | {s.EcuSessions} | {(s.Vehicles.Count > 0 ? string.Join(", ", s.Vehicles) : "-")} | {(s.Errors.Count > 0 ? string.Join("; ", s.Errors) : "nessuno")} | {ending} |");
            }
            md.AppendLine();

            md.AppendLine("## File");
            md.AppendLine();
            md.AppendLine(single
                ? "- `events.jsonl`: eventi dell'accensione, uno per riga."
                : "- `boot-NNNNN/`: una cartella per accensione, con dentro i file sotto.");
            if (!single) md.AppendLine("  - `events.jsonl`: eventi dell'accensione, uno per riga.");
            md.AppendLine((single ? "" : "  ") + "- `data-NN-<centralina>.csv`: valori letti dalla centralina, uno per ogni collegamento. Separatore `;`, decimali con il punto.");
            md.AppendLine();

            md.AppendLine("## Orari");
            md.AppendLine();
            md.AppendLine("Il dispositivo non ha un orologio a batteria: all'accensione non conosce la data. Per questo ogni riga ha due tempi:");
            md.AppendLine();
            md.AppendLine("- `t` / `t_s`: secondi trascorsi dall'accensione. Sempre presente e affidabile.");
            md.AppendLine("- `utc`: data e ora UTC (ISO 8601). Il dispositivo la prende dal primo telefono che apre l'interfaccia. Gli orari precedenti a quel momento sono stati **ricostruiti** in questa esportazione (ora del telefono meno i secondi trascorsi) e negli eventi sono marcati `\"utc_estimated\": true`. Se nessun telefono si è collegato durante un'accensione, `utc` resta vuoto e vale solo `t`.");
            md.AppendLine("- `sys` (solo eventi): l'orologio interno al momento della scrittura, non affidabile prima della sincronizzazione. Solo per riferimento.");
            md.AppendLine();

            md.AppendLine("## Eventi (`events.jsonl`)");
            md.AppendLine();
            md.AppendLine("Campi: `seq` (progressivo), `t`, `sys`, `utc`, `type`, `msg` (descrizione in italiano), `data` (dettagli, dipendono dal tipo).");
            md.AppendLine();
            md.AppendLine("| type | Significato |");
            md.AppendLine("|---|---|");
            foreach (var (type, meaning) in EventTypes.Catalog)
                md.AppendLine($"| `{type}` | {meaning} |");
            md.AppendLine();

            md.AppendLine("## Dati (`data-*.csv`)");
            md.AppendLine();
            md.AppendLine("Colonne `t_s` e `utc`, poi un parametro per colonna con l'unità tra parentesi quadre. Cella vuota = la centralina non ha dato una risposta valida per quel parametro in quel campione. I parametri SI/NO valgono 1/0.");
            md.AppendLine();
            foreach (var (s, _, files) in boots)
                foreach (var (file, columns) in files)
                {
                    md.AppendLine($"- {(single ? "" : DataLogger.BootFolderName(s.Seq) + "/")}`{file}`: {string.Join(" · ", columns)}");
                }
            md.AppendLine();

            md.AppendLine("## Note per l'analisi");
            md.AppendLine();
            md.AppendLine("- La centralina viene interrogata un parametro alla volta su una linea lenta: la frequenza reale delle righe dipende da quanti parametri vengono letti (tipicamente alcune righe al secondo).");
            md.AppendLine("- I codici di errore vengono riletti circa ogni cinque giri di lettura dei parametri.");
            md.AppendLine("- Le due colonne della pressione assoluta nel collettore (aspirato e TURBO) sono due conversioni dello stesso dato grezzo: va considerata quella del motore indicato in `ecu_connected` (`car_model`).");
            md.AppendLine("- Il dispositivo si accende insieme al quadro e impiega alcune decine di secondi ad avviarsi: i primissimi istanti dopo il giro di chiave (avviamento compreso) possono mancare.");
            md.AppendLine("- Lo spegnimento avviene togliendo l'alimentazione: gli ultimi secondi prima del quadro spento possono mancare.");
            md.AppendLine();

            md.AppendLine("## Esempio di richiesta a un assistente AI");
            md.AppendLine();
            md.AppendLine("> Ti allego il registro della centralina della mia Fiat Coupé (formato descritto in LEGGIMI.md). Riassumi ogni accensione: durata, temperature dell'acqua e dell'aria raggiunte, tensione di batteria, giri motore, eventuali codici di errore e quando sono comparsi. Segnala valori fuori dal normale o andamenti sospetti (per esempio correzione lambda sempre al limite, anticipo che crolla, tensione bassa) e dimmi quali controlli faresti.");
            return md.ToString();
        }

        private static string FormatDuration(double seconds)
        {
            var span = TimeSpan.FromSeconds(Math.Max(0, seconds));
            return span.TotalHours >= 1 ? $"{(int)span.TotalHours} h {span.Minutes} min" : $"{span.Minutes} min {span.Seconds} s";
        }
    }
}
