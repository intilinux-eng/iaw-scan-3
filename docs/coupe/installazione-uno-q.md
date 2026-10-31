# Installare IAW Scan 3 Coupé su Arduino UNO Q

Questa guida porta una UNO Q nuova a diventare il dispositivo diagnostico della Coupé:

1. Debian della scheda pronto.
2. Pacchetto `iaw-scan3-coupe` installato.
3. Rete Wi-Fi propria per il telefono.
4. Sistema ottimizzato per essere spento togliendo la corrente.
5. Collegamento in auto.

> **Stato:** progetto sperimentale. La procedura usa strumenti standard di Debian (apt, systemd, NetworkManager), ma **non è ancora stata eseguita su una UNO Q reale**. I punti più incerti sono segnalati nel testo. Se la provi, [apri una issue](https://github.com/intilinux-eng/iaw-scan-3/issues) con l'esito, positivo o negativo: è la cosa più utile in questo momento.

## Cosa serve

| Componente | Note |
|---|---|
| **Arduino UNO Q** | Basta la versione da 2 GB. Il software occupa circa 110 MB, il registro dati al massimo 2 GB (configurabile). |
| **Hub USB-C con Power Delivery** | La UNO Q ha una sola porta USB-C: l'hub la alimenta e offre una porta USB-A per il cavo K-line. Serve un hub con ingresso di alimentazione (PD). |
| **Cavo diagnostico USB K-line** | Tipo "VAG-COM KKL 409.1", meglio se con chip **FTDI** originale. Lo stesso cavo della versione per PC. |
| **Adattatore Fiat 3 poli** | Dalla presa diagnostica triangolare della Coupé al cavo K-line, con l'alimentazione a 12 V per l'interfaccia. Vedi il [README principale](../../README.md#diagnostic-interface--adapter-cable). |
| **Alimentatore 12 V → USB-C PD** | Per l'auto, da almeno 15 W (5 V 3 A), collegato **sotto chiave**. |
| **Un PC** | Solo per la prima configurazione: con Arduino App Lab oppure con ADB (Android platform-tools). |

## 1. Preparare il Debian della UNO Q

La UNO Q esce di fabbrica con Debian installato sulla memoria eMMC interna.

1. **Prima configurazione.** Collega la scheda al PC con il cavo USB-C e avvia **Arduino App Lab**. La procedura iniziale chiede nome della scheda, password dell'utente Linux `arduino` e rete Wi-Fi di casa. Collegala al Wi-Fi di casa: per installare i pacchetti serve internet. Dettagli nel [manuale della UNO Q](https://docs.arduino.cc/tutorials/uno-q/user-manual/).
2. **(Facoltativo) Ripartire da un sistema pulito.** Se la scheda è già stata usata per altro, puoi riscriverla con l'immagine Debian ufficiale tramite [Arduino Flasher CLI](https://github.com/arduino/arduino-flasher-cli) (`arduino-flasher-cli flash latest`, con la scheda in modalità recovery tramite il ponticello descritto nella documentazione Arduino). **Cancella tutto ciò che c'è sulla scheda.**
3. **Aprire un terminale sulla scheda**, in uno di questi due modi:
   - via USB: `adb shell`. Funziona anche senza rete ed è il metodo da usare quando si attiva l'Access Point;
   - via rete: `ssh arduino@<nome-scheda>.local`, oppure con l'indirizzo IP mostrato da `hostname -I`.

   Vedi la [guida Debian della UNO Q](https://docs.arduino.cc/tutorials/uno-q/debian-guide).
4. **Aggiornare il sistema:**
   ```
   sudo apt update && sudo apt full-upgrade
   ```

## 2. Installare il pacchetto

1. Scarica dalla [pre-release sperimentale](https://github.com/intilinux-eng/iaw-scan-3/releases/tag/coupe-experimental) il file `iaw-scan3-coupe_<versione>_arm64.deb` e copialo sulla scheda (per esempio con `adb push file.deb /home/arduino/` o con `scp`).
2. Installalo con `apt`, che scarica da solo le dipendenze:
   ```
   sudo apt install ./iaw-scan3-coupe_*_arm64.deb
   ```
3. Verifica che il servizio sia attivo:
   ```
   systemctl status iaw-scan3-coupe
   ```

Cosa installa il pacchetto:

| Percorso | Contenuto |
|---|---|
| `/opt/iaw-scan3-coupe/` | Il programma, autonomo: .NET è incluso, non va installato a parte. |
| `iaw-scan3-coupe.service` | Servizio systemd, avviato a ogni accensione e riavviato da solo in caso di errore. Gira come utente di sistema `iawscan`, con accesso alla porta seriale (gruppo `dialout`), alla porta 80 e all'orologio di sistema. |
| `/var/lib/iaw-scan3-coupe/` | I dati: `datalog/` (registro), `Log/` (registrazioni manuali del grafico), `settings.json`. **Non viene mai cancellata dal pacchetto**, nemmeno con `apt purge`. |
| `/etc/default/iaw-scan3-coupe` | Impostazioni: porta web, spazio massimo del registro, frequenza dei campioni. |
| `60-iaw-scan3-coupe.rules` | Regola udev: porta a 1 ms il "latency timer" dei cavi FTDI. Con il valore predefinito (16 ms) ogni richiesta alla centralina rallenta parecchio. |
| `/usr/sbin/iaw-coupe-setup` | Lo script di configurazione usato nei passi successivi. |

**Prova subito, prima dell'auto.** Finché la scheda è sul Wi-Fi di casa, apri dal PC o dal telefono `http://<nome-scheda>.local` oppure `http://<indirizzo-IP>`. Attiva **Modalità simulazione** e tocca **Connetti**: se vedi il cruscotto con valori casuali, il software funziona.

## 3. Rete Wi-Fi del dispositivo (Access Point)

In auto non c'è un Wi-Fi di casa: è la scheda a creare la propria rete, alla quale si collega il telefono.

```
sudo iaw-coupe-setup ap
```

Il comando:

- controlla che il Wi-Fi della scheda supporti la modalità Access Point;
- crea la rete `IAW-Coupe-XXXX` con una password casuale di 12 caratteri, protetta WPA2;
- mostra nome, password, indirizzo `http://192.168.4.1` e, se è installato `qrencode`, un **codice QR**. Inquadrato con la fotocamera del telefono, collega il telefono alla rete senza digitare nulla. Conviene stamparlo su un'etichetta.

Nome e password sono salvati in `/etc/iaw-scan3-coupe/ap.conf` (leggibile solo da root). Puoi sceglierli tu con `--ssid NOME --password PAROLA` e cambiare canale con `--canale N` (predefinito 6, solo 2,4 GHz per la massima compatibilità).

La rete si attiva **alla prossima accensione**, poi a ogni accensione, anche con il Wi-Fi di casa nei paraggi. Per attivarla subito: `sudo iaw-coupe-setup ap --attiva`. Fallo da `adb shell`: se sei collegato in SSH via Wi-Fi, la connessione cadrà.

### Tornare al Wi-Fi di casa (aggiornamenti)

```
sudo iaw-coupe-setup casa
```

Disattiva l'Access Point e ricollega la scheda alla rete di casa. Per riattivarlo: `sudo iaw-coupe-setup ap --attiva`.

### Se la modalità Access Point non è disponibile

La documentazione Arduino non dice se il Wi-Fi della UNO Q supporta la modalità Access Point. Se `iaw-coupe-setup ap` risponde che non è supportata, ci sono due alternative:

- **Hotspot del telefono:** è la scheda a collegarsi al telefono. Attiva l'hotspot sul telefono e collega la scheda con `sudo nmcli device wifi connect "<nome hotspot>" password "<password>"`. Poi apri `http://<nome-scheda>.local` oppure l'indirizzo della scheda mostrato nelle impostazioni dell'hotspot. Più scomodo: l'hotspot va acceso ogni volta e l'indirizzo può cambiare.
- **Chiavetta Wi-Fi USB** con supporto Access Point, collegata all'hub: lo script la usa al posto del Wi-Fi interno se è la prima interfaccia Wi-Fi elencata da `nmcli device status`.

## 4. Ottimizzare per l'auto

In auto la scheda viene spenta togliendo l'alimentazione, senza spegnimento ordinato.

```
sudo iaw-coupe-setup ottimizza
```

- **Journal di sistema solo in RAM:** il sistema smette di scrivere i propri log sulla eMMC. Resta solo il registro dati, che sincronizza i propri file ogni 5 secondi.
- **Niente manutenzione automatica dei pacchetti** (`apt-daily`, `man-db`): in auto non ha rete e verrebbe interrotta a metà dallo spegnimento. Gli aggiornamenti si fanno a mano, a casa.

Per avviarsi più in fretta a ogni giro di chiave puoi disattivare anche i servizi di **App Lab**, che questo dispositivo non usa:

```
sudo iaw-coupe-setup ottimizza --disattiva-applab
```

Lo script elenca esattamente quali servizi disattiva e tocca solo quelli che trova. Per misurare il tempo di avvio: `systemd-analyze` e `systemd-analyze blame`.

Tutto è reversibile con `sudo iaw-coupe-setup ripristina`. Lo stato completo (servizio, rete, cavo, registro, ottimizzazioni) si vede con `iaw-coupe-setup stato`.

### Spegnimento brutale: cosa è protetto e cosa no

- **Protetto dal software.** Il registro scrive solo in coda ai file e li forza su disco ogni 5 secondi. A ogni taglio di corrente si perdono al più gli ultimi secondi. Le impostazioni sono salvate in modo atomico: o la versione vecchia, o quella nuova, mai un file a metà. Un'ultima riga troncata dal taglio viene ignorata in lettura.
- **Rischio residuo.** Il filesystem della scheda (ext4 con journal) è progettato per riprendersi da un'interruzione, e la scheda scrive poco. Ma nessun software può garantire al 100% una memoria eMMC spenta di colpo migliaia di volte.
- **Rimedio hardware**, se vuoi andare sul sicuro: un modulo di alimentazione per auto con **spegnimento ritardato**, che resta acceso 1-2 minuti dopo il quadro spento. In quel tempo la centralina tace e il registratore ha già chiuso e sincronizzato i file. Quando poi la corrente viene tolta, la scheda è inattiva.

## 5. Collegamento in auto

> Il collegamento all'impianto elettrico va fatto a regola d'arte, meglio da un elettrauto: linea **sotto chiave** e **protetta da fusibile**, cavi fissati lontano da pedali, airbag e parti in movimento.

```
 linea 12 V sotto chiave ──[fusibile]──► alimentatore 12 V → USB-C PD ──► hub USB-C (ingresso PD)
                                                                              ├──► USB-C ──► Arduino UNO Q
                                                                              └──► USB-A ──► cavo K-line ──► adattatore 3 poli
                                                                                                                 │
                                                                                     presa diagnostica (centralina motore)
```

- **Alimentazione 12 V dell'adattatore 3 poli.** La presa diagnostica Fiat non fornisce l'alimentazione all'interfaccia: l'adattatore ha due pinze per la batteria. In un'installazione fissa conviene collegarle anche loro a una linea 12 V **sotto chiave** con fusibile. Pinze lasciate sempre sulla batteria terrebbero alimentata l'interfaccia anche a quadro spento.
- **Quale presa.** Il registratore automatico si collega alla centralina usata per ultima con una connessione manuale. Di partenza è la centralina motore. Per l'immobilizer FIAT CODE serve la sua presa: collega il cavo lì e fai una connessione manuale all'immobilizer, e da quel momento il registratore userà quella.
- **Primo avvio in auto.** Gira la chiave su MAR e attendi l'avvio della scheda (qualche decina di secondi). Collega il telefono alla rete `IAW-Coupe-XXXX` e apri `http://192.168.4.1`: dovresti vedere "Registrazione in corso". Prosegui con la [guida all'uso](guida-uso.md).

## Aggiornare

A casa:

```
sudo iaw-coupe-setup casa                         # torna sul Wi-Fi di casa
sudo apt install ./iaw-scan3-coupe_<nuova>_arm64.deb
sudo iaw-coupe-setup ap --attiva                  # da adb shell: la connessione SSH cadrà
```

Registro dati e impostazioni restano al loro posto.

## Disinstallare

```
sudo iaw-coupe-setup ripristina                   # annulla le ottimizzazioni
sudo nmcli connection delete iaw-coupe-ap         # elimina la rete del dispositivo
sudo apt remove iaw-scan3-coupe
```

Il registro dati in `/var/lib/iaw-scan3-coupe` **non viene cancellato**: scaricalo prima dal Diario, se ti serve, poi eliminalo a mano con `sudo rm -r /var/lib/iaw-scan3-coupe`.

## Compilare il pacchetto da sé

Su Linux o in WSL, con il [.NET 10 SDK](https://dotnet.microsoft.com/download) e `dpkg-deb`:

```
bash IES_3.Coupe/packaging/build-deb.sh 0.1.0-experimental
```

Il pacchetto viene creato in `IES_3.Coupe/packaging/out/`. Da Windows puoi pubblicare con `dotnet publish IES_3.Coupe/IES_3.Coupe.csproj -c Release -r linux-arm64 --self-contained true -p:PublishSingleFile=true -p:DebugType=None -o <cartella>`, poi impacchettare in WSL con `PUBLISH_DIR=<cartella> bash IES_3.Coupe/packaging/build-deb.sh 0.1.0-experimental`.

## Problemi frequenti

| Sintomo | Cosa controllare |
|---|---|
| Il telefono non apre la pagina | Il telefono è sulla rete `IAW-Coupe-XXXX`? Se chiede "Rete senza accesso a internet", scegli **Resta connesso**. Se non basta, spegni temporaneamente i dati mobili. Scrivi `http://192.168.4.1`, non `https://`. |
| "Cavo K-line non collegato al dispositivo" | `iaw-coupe-setup stato` deve elencare `/dev/ttyUSB0`. Altrimenti controlla l'hub (serve alimentazione PD) e prova `lsusb` e `sudo dmesg \| grep -i ttyUSB`. |
| "In attesa della centralina" | Quadro acceso? Cavo sulla presa giusta? Adattatore 3 poli alimentato a 12 V? Il registratore riprova da solo ogni pochi secondi. |
| Accensioni con "Orario sconosciuto" | Nessun telefono ha aperto l'interfaccia durante quel giro: restano disponibili i secondi dall'accensione. Basta aprire la pagina una volta per viaggio. |
| Errori del servizio | `journalctl -u iaw-scan3-coupe -b` (dopo `ottimizza` il journal è in RAM: solo l'accensione corrente). |
