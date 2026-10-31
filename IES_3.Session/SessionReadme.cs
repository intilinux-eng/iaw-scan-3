using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace IES_2.Session
{
    /// <summary>
    /// The README inside an exported ZIP: what the files are and how to read them, written so that the
    /// archive can be handed as it is to a person, a script or an AI assistant.
    /// </summary>
    public static class SessionReadme
    {
        public static string FileName(string language) => language == "it" ? "LEGGIMI.md" : "README.md";

        public static string Build(IReadOnlyList<SessionSummary> sessions, string language)
        {
            var it = language == "it";
            var md = new StringBuilder();
            void Line(string text = "") => md.Append(text).Append('\n');
            string T(string en, string italian) => it ? italian : en;

            Line(T("# IAW Scan 3 session log", "# Registro sessioni IAW Scan 3"));
            Line();
            Line(T(
                "Recorded by IAW Scan 3, diagnostic software for Fiat/Lancia/Alfa Romeo OBD-I engine control units (Marelli IAW) and the FIAT CODE immobiliser, connected over a K-line cable.",
                "Registrato da IAW Scan 3, software di diagnosi per centraline motore Fiat/Lancia/Alfa Romeo OBD-I (Marelli IAW) e immobilizer FIAT CODE, collegate con un cavo K-line."));
            Line();
            Line(T(
                "Values are decoded with the same formulas as the original IAW Scan 2 v0.85, with no reinterpretation. They are what the ECU reports, not independent measurements.",
                "I valori sono decodificati con le stesse formule dell'originale IAW Scan 2 v0.85, senza reinterpretazioni. Sono ciò che riporta la centralina, non misure indipendenti."));
            Line();

            Line(T("## Sessions", "## Sessioni"));
            Line();
            Line(T("| Session | Started (UTC) | Duration | ECU | Vehicle | ISO code | Simulated | Fault codes seen |",
                   "| Sessione | Inizio (UTC) | Durata | Centralina | Veicolo | Codice ISO | Simulata | Errori visti |"));
            Line("|---|---|---|---|---|---|---|---|");
            foreach (var s in sessions)
            {
                var faults = s.Faults.Count > 0 ? string.Join("; ", s.Faults) : T("none", "nessuno");
                var simulated = s.Header.Simulation ? T("yes", "sì") : T("no", "no");
                Line($"| {s.Id} | {SessionRecorder.FormatUtc(s.Header.StartedUtc)} | {FormatDuration(s.DurationSeconds)} | {s.Header.EcuType} | {s.Header.Vehicle} | {s.Header.IsoCode} | {simulated} | {faults} |");
            }
            Line();
            if (sessions.Any(s => s.Header.Simulation))
            {
                Line(T("**Simulated sessions contain invented values:** ignore them for any diagnosis.",
                       "**Le sessioni simulate contengono valori inventati:** non vanno considerate per la diagnosi."));
                Line();
            }

            Line(T("## Files", "## File"));
            Line();
            Line(T("- `session.json`: what was connected: ECU, vehicle, ISO and spare-part codes, start, end, columns.",
                   "- `session.json`: cosa era collegato: centralina, veicolo, codici ISO e ricambio, inizio, fine, colonne."));
            Line(T("- `events.jsonl`: what happened, one JSON object per line.",
                   "- `events.jsonl`: cosa è successo, un oggetto JSON per riga."));
            Line(T("- `data.csv`: the values read from the ECU, if data recording was on. Separator `;`, decimal point `.`.",
                   "- `data.csv`: i valori letti dalla centralina, se la registrazione dati era attiva. Separatore `;`, decimali con il punto."));
            Line();

            Line(T("## Time", "## Tempi"));
            Line();
            Line(T("`t` (events) and `t_s` (data) are seconds since the session started. `utc` is date and time in UTC from the computer's clock.",
                   "`t` (eventi) e `t_s` (dati) sono i secondi dall'inizio della sessione. `utc` è la data e l'ora UTC dell'orologio del computer."));
            Line();

            Line(T("## Data (`data.csv`)", "## Dati (`data.csv`)"));
            Line();
            Line(T("Columns `t_s` and `utc`, then one column per parameter with its unit in square brackets:",
                   "Colonne `t_s` e `utc`, poi una colonna per parametro con l'unità tra parentesi quadre:"));
            Line();
            foreach (var column in sessions.SelectMany(s => s.Header.Columns).Select(c => c.Header).Distinct())
                Line($"- {column}");
            Line();
            Line(T("- An empty cell means no valid answer from the ECU for that parameter in that sample, or that the parameter was not being read.",
                   "- Una cella vuota significa che la centralina non ha dato una risposta valida per quel parametro in quel campione, oppure che il parametro non veniva letto."));
            Line(T("- Yes/no parameters are 1/0.", "- I parametri sì/no valgono 1/0."));
            Line(T("- The ECU is queried one byte at a time on a slow line: the real sample rate depends on how many parameters were selected (typically a few rows per second).",
                   "- La centralina viene interrogata un byte alla volta su una linea lenta: la frequenza reale dipende da quanti parametri erano selezionati (tipicamente alcune righe al secondo)."));
            Line(T("- Where two columns convert the same raw value differently (for example manifold pressure for naturally aspirated and turbo engines), only the one matching the engine applies.",
                   "- Dove due colonne convertono diversamente lo stesso dato grezzo (per esempio la pressione nel collettore per motori aspirati e turbo), vale solo quella del motore in questione."));
            Line();

            Line(T("## Events (`events.jsonl`)", "## Eventi (`events.jsonl`)"));
            Line();
            Line(T("Fields: `seq` (running number), `t`, `utc`, `type`, `data` (details, depending on the type).",
                   "Campi: `seq` (progressivo), `t`, `utc`, `type`, `data` (dettagli, dipendono dal tipo)."));
            Line();
            Line(T("| type | Meaning |", "| type | Significato |"));
            Line("|---|---|");
            Line($"| `{SessionEventTypes.Connected}` | {T("ECU connected.", "Centralina collegata.")} |");
            Line($"| `{SessionEventTypes.Disconnected}` | {T("Connection closed.", "Collegamento chiuso.")} |");
            Line($"| `{SessionEventTypes.CommLost}` | {T("The ECU stopped answering.", "La centralina ha smesso di rispondere.")} |");
            Line($"| `{SessionEventTypes.CommRestored}` | {T("Communication picked up again.", "Comunicazione ripresa.")} |");
            Line($"| `{SessionEventTypes.FaultsRead}` | {T("First reading of the fault codes, even if empty.", "Prima lettura dei codici di errore, anche se vuota.")} |");
            Line($"| `{SessionEventTypes.FaultsChanged}` | {T("Fault codes changed: `faults` (all current ones), `added`, `removed`.", "Codici di errore cambiati: `faults` (tutti quelli attuali), `added`, `removed`.")} |");
            Line($"| `{SessionEventTypes.ClearCodes}` | {T("Fault codes cleared by the user, with the outcome.", "Azzeramento dei codici di errore da parte dell'utente, con l'esito.")} |");
            Line($"| `{SessionEventTypes.ActuatorTest}` | {T("Actuator test run by the user, with the outcome.", "Test attuatore eseguito dall'utente, con l'esito.")} |");
            Line($"| `{SessionEventTypes.Adjustment}` | {T("Adjustment run by the user, with the outcome.", "Regolazione eseguita dall'utente, con l'esito.")} |");
            Line($"| `{SessionEventTypes.DataStarted}`, `{SessionEventTypes.DataStopped}` | {T("Data recording turned on or off.", "Registrazione dati attivata o disattivata.")} |");
            Line();
            Line(T("Each fault has `description`, `reason`, `state` and `mil`. In `state`, `Active` means present now, `Verified` confirmed by the ECU, `Stored` kept in memory; `mil` is true when it turns on the engine warning light.",
                   "Ogni errore ha `description`, `reason`, `state` e `mil`. In `state`, `Active` significa presente ora, `Verified` confermato dalla centralina, `Stored` conservato in memoria; `mil` è true quando accende la spia avaria motore."));
            Line();

            Line(T("## Example request to an AI assistant", "## Esempio di richiesta a un assistente AI"));
            Line();
            Line(T(
                "> I'm attaching a log from my car's ECU (format described in README.md). Summarise each session: duration, coolant and air temperatures reached, battery voltage, engine speed, any fault codes and when they appeared. Point out values out of the ordinary or suspicious trends, and tell me which checks you would do. Remember these are ECU readings: a sensor fault can make them wrong.",
                "> Ti allego il registro della centralina della mia auto (formato descritto in LEGGIMI.md). Riassumi ogni sessione: durata, temperature dell'acqua e dell'aria raggiunte, tensione di batteria, giri motore, eventuali codici di errore e quando sono comparsi. Segnala valori fuori dal normale o andamenti sospetti e dimmi quali controlli faresti. Ricorda che sono letture della centralina: un sensore guasto può falsarle."));
            return md.ToString();
        }

        private static string FormatDuration(double seconds)
        {
            var span = TimeSpan.FromSeconds(Math.Max(0, seconds));
            return span.TotalHours >= 1
                ? string.Create(CultureInfo.InvariantCulture, $"{(int)span.TotalHours} h {span.Minutes} min")
                : string.Create(CultureInfo.InvariantCulture, $"{span.Minutes} min {span.Seconds} s");
        }
    }
}
