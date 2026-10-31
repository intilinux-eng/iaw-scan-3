# Formato del registro dati

Riferimento per chi vuole analizzare i dati di IAW Scan 3 Coupé con i propri strumenti (script, fogli di calcolo, AI). Ogni ZIP esportato contiene anche un `LEGGIMI.md` con le stesse informazioni, adattate ai dati che contiene.

## Dove si trova

Sulla scheda, in `/var/lib/iaw-scan3-coupe/datalog/`:

```
datalog/
├── state.json            numero dell'ultima accensione e identificativo di avvio del kernel
├── running               presente mentre il software gira: se c'è ancora all'avvio successivo,
│                         l'accensione precedente è finita togliendo la corrente
├── boot-00001/
│   ├── events.jsonl      eventi dell'accensione n. 1
│   ├── data-01-IAW_04K_P8.csv   valori del primo collegamento alla centralina
│   └── data-02-IAW_04K_P8.csv   ...del secondo (per esempio dopo una perdita di comunicazione)
└── boot-00002/
    └── ...
```

Una **accensione** (`boot-NNNNN`) va dall'accensione della scheda alla perdita di alimentazione: con la scheda alimentata dal quadro, è un giro di chiave. Le cartelle sono numerate e non datate, perché all'accensione la data non è nota.

## Tempi

| Campo | Significato |
|---|---|
| `t` (eventi) / `t_s` (dati) | Secondi dall'accensione della scheda, letti dall'orologio monotono del kernel. Sempre presenti, sempre coerenti all'interno di un'accensione. |
| `utc` | Data e ora UTC in ISO 8601 (`2026-10-28T07:59:56.167Z`). Sul dispositivo è presente solo dopo che un telefono ha fornito l'ora (`clock_sync`) o dopo una sincronizzazione NTP. **Nelle esportazioni** è ricostruito anche per i record precedenti: `utc(t) = utc_sync − (t_sync − t)`. Negli eventi ricostruiti compare `"utc_estimated": true`. |
| `sys` (solo eventi) | Orologio di sistema al momento della scrittura. Prima della sincronizzazione può essere sbagliato anche di giorni: conservato solo per riferimento. |

## `events.jsonl`

Un oggetto JSON per riga:

```json
{"seq":4,"t":31.52,"sys":"2026-10-27T23:29:44.571Z","utc":null,"type":"ecu_connected","msg":"Centralina collegata: Weber-Marelli IAW-04K.P8 MPI, veicolo Coupe S 2.0.","data":{"ecu_id":"IAW-04K.P8","car_model":"Coupe S 2.0","iso":"55-CE-85-83-94-BF","rep_code":"61602.063.01","known_coupe":true,"simulation":false,"port":"/dev/ttyUSB0","auto":true,"data_file":"data-01-IAW_04K_P8.csv"}}
```

`seq` è un progressivo, `type` un identificativo stabile, `msg` una descrizione in italiano, `data` i dettagli. Tipi:

| `type` | Quando | `data` |
|---|---|---|
| `power_on` | Accensione della scheda (`t` = 0) | `previous_shutdown`: `power_loss` (corrente tolta, il caso normale in auto) o `clean` |
| `app_start` | Avvio del software | `version`, `power_on_seconds` o `previous_run` (`crash`/`clean`) se riavviato nella stessa accensione |
| `app_stop` | Arresto ordinato del software (raro in auto) | |
| `heartbeat` | Ogni minuto senza dati dalla centralina | |
| `clock_sync` | Ora ricevuta da un telefono | `utc`, `t`, `drift_s` (errore dell'orologio precedente), `system_clock_set`, `time_zone` del telefono |
| `phone_connected` | Un telefono apre l'interfaccia dopo almeno un minuto senza telefoni | |
| `cable_missing` | Nessun cavo K-line collegato (una volta finché non ricompare) | |
| `ecu_waiting` | La centralina non risponde (una volta finché non risponde) | `ecu_id`, `port`, `message` |
| `ecu_connected` | Centralina collegata | `ecu_id`, `ecu_type`, `car_model`, `iso`, `rep_code`, `known_coupe`, `simulation`, `port`, `auto`, `data_file` |
| `ecu_connect_failed` | Connessione manuale non riuscita | `ecu_id`, `port` |
| `ecu_disconnected` | Fine collegamento | `reason` (`user`, `comm_lost`, `shutdown`), `data_file`, `data_rows` |
| `comm_lost` | La centralina smette di rispondere | `message` |
| `errors_read` | Prima lettura dei codici dopo il collegamento | `errors` |
| `errors_changed` | I codici sono cambiati | `errors` (elenco completo), `added`, `removed` |
| `clear_codes` | Azzeramento codici richiesto | `outcome` (`Passed`, `Failed`, `InitFailed`, `Cancelled`...) |
| `actuator_test` | Test attuatore eseguito | `test`, `outcome` (`Passed`, `Failed`, `Timeout`, `InitFailed`, `Cancelled`) |
| `recording_started` / `recording_stopped` | Registrazione manuale del grafico | `file`, `parameters` |
| `logging_changed` | Registrazione automatica attivata, disattivata, in pausa o ripresa | `state` (`on`, `off`, `paused`) |
| `datalog_pruned` | Accensione vecchia eliminata per spazio | `boot`, `freed_bytes` |

Ogni codice di errore è `{"description", "reason", "state", "mil"}`. `state` usa le sigle dell'interfaccia: `Att.` attivo, `Ver.` verificato, `Mem.` memorizzato. `mil` è true quando l'errore accende la spia.

## `data-NN-<centralina>.csv`

```
t_s;utc;Velocità motore [RPM];Durata ignezione [ms];Anticipo di accensione [°];...
31.734;;850.34;3.248;12.5;...
```

- Separatore `;`, decimali con il punto, codifica UTF-8.
- Una colonna per ogni parametro della centralina, nell'ordine della centralina, con l'unità tra parentesi quadre. I nomi sono quelli italiani dell'interfaccia, presi dalle risorse originali di IAW Scan 2, refusi compresi.
- Valori decodificati con le formule di IAW Scan 2 v0.85, senza reinterpretazioni. I parametri SI/NO valgono `1`/`0`.
- **Cella vuota** = in quel campione la centralina non ha risposto validamente a quel parametro.
- Al massimo una riga ogni 200 ms (`IAW_DATALOG_INTERVAL_MS`). La frequenza reale è più bassa e dipende dalla linea K: la centralina viene interrogata un byte alla volta, e ogni circa cinque giri si leggono anche i codici di errore.
- Un nuovo file a ogni collegamento alla centralina. Dopo una perdita di comunicazione e il ricollegamento automatico si apre `data-02-...`.

## Esportazione

Dal Diario dell'interfaccia (o con `GET /api/datalog/export?boot=N` / `?boot=all`) si scarica uno ZIP:

- una singola accensione ha `events.jsonl`, `data-*.csv` e `LEGGIMI.md` nella radice;
- tutto il registro ha una cartella `boot-NNNNN/` per accensione e `LEGGIMI.md` nella radice.

Nell'esportazione `utc` è ricostruito dove possibile. Le righe troncate da un taglio di corrente vengono tralasciate. I file sulla scheda non vengono modificati.

## Impostazioni

In `/etc/default/iaw-scan3-coupe` (poi `sudo systemctl restart iaw-scan3-coupe`):

| Variabile | Default | Significato |
|---|---|---|
| `IAW_DATALOG_MAX_MB` | `2048` | Spazio massimo del registro. Oltre, si eliminano le accensioni più vecchie (mai quella in corso). |
| `IAW_DATALOG_INTERVAL_MS` | `200` | Intervallo minimo tra due righe di dati. |
| `ASPNETCORE_URLS` | `http://0.0.0.0:80` | Indirizzo e porta dell'interfaccia web. |
| `IAW_LOG_SIMULATION` | (non impostata) | `1` = registra anche le sessioni in simulazione. Solo per prove. |
