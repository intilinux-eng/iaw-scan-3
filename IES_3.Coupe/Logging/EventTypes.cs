using System.Collections.Generic;

namespace IES_2.Coupe.Logging
{
    /// <summary>
    /// The "type" values written to events.jsonl. Stable identifiers: analysis scripts and the
    /// exported LEGGIMI.md rely on them, so existing ones must never be renamed.
    /// </summary>
    public static class EventTypes
    {
        public const string PowerOn = "power_on";
        public const string AppStart = "app_start";
        public const string AppStop = "app_stop";
        public const string Heartbeat = "heartbeat";
        public const string ClockSync = "clock_sync";
        public const string PhoneConnected = "phone_connected";
        public const string CableMissing = "cable_missing";
        public const string EcuWaiting = "ecu_waiting";
        public const string EcuConnected = "ecu_connected";
        public const string EcuConnectFailed = "ecu_connect_failed";
        public const string EcuDisconnected = "ecu_disconnected";
        public const string CommLost = "comm_lost";
        public const string ErrorsRead = "errors_read";
        public const string ErrorsChanged = "errors_changed";
        public const string ClearCodes = "clear_codes";
        public const string ActuatorTest = "actuator_test";
        public const string RecordingStarted = "recording_started";
        public const string RecordingStopped = "recording_stopped";
        public const string LoggingChanged = "logging_changed";
        public const string Pruned = "datalog_pruned";

        /// <summary>Plain-language meaning of each type, for the export's LEGGIMI.md.</summary>
        public static readonly IReadOnlyList<(string Type, string Meaning)> Catalog = new[]
        {
            (PowerOn, "Il dispositivo si è acceso (t = 0). Alimentato dal quadro: corrisponde al giro di chiave. `data.previous_shutdown` = `power_loss` è il caso normale (quadro spento)."),
            (AppStart, "Avvio del software, pochi secondi dopo l'accensione (o riavvio del solo software)."),
            (AppStop, "Arresto regolare del software. Raro in auto: di norma l'alimentazione viene semplicemente tolta."),
            (Heartbeat, "Segnale di vita ogni minuto senza dati dalla centralina: delimita l'ora dello spegnimento."),
            (ClockSync, "Data e ora prese da un telefono (o da internet). Da qui in poi `utc` è affidabile; serve anche a ricostruire gli orari precedenti."),
            (PhoneConnected, "Un telefono ha aperto l'interfaccia."),
            (CableMissing, "Nessun cavo K-line collegato al dispositivo."),
            (EcuWaiting, "Il dispositivo cerca la centralina ma questa non risponde."),
            (EcuConnected, "Centralina collegata: tipo, veicolo riconosciuto, codice ISO, codice ricambio, porta, file dati aperto."),
            (EcuConnectFailed, "Tentativo di collegamento alla centralina non riuscito."),
            (EcuDisconnected, "Fine del collegamento alla centralina (`data.reason`) e chiusura del file dati."),
            (CommLost, "La centralina ha smesso di rispondere durante la lettura."),
            (ErrorsRead, "Prima lettura dei codici di errore dopo il collegamento (anche se vuota)."),
            (ErrorsChanged, "I codici di errore sono cambiati: elenco completo attuale più quelli comparsi e scomparsi."),
            (ClearCodes, "Azzeramento dei codici di errore richiesto dall'utente, con esito."),
            (ActuatorTest, "Test attuatore eseguito dall'utente, con esito."),
            (RecordingStarted, "Inizio di una registrazione manuale del grafico (file in Log/, formato IAW Scan)."),
            (RecordingStopped, "Fine della registrazione manuale del grafico."),
            (LoggingChanged, "Registrazione automatica attivata, disattivata, messa in pausa o ripresa dall'utente."),
            (Pruned, "Registro di una vecchia accensione eliminato per fare spazio."),
        };
    }
}
