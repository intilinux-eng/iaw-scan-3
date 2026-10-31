**Windows:** download `IAWScan3-win-x64.exe` below and run it, there's nothing to install. **Linux (Debian, Ubuntu and derivatives):** download `iaw-scan3_amd64.deb`, then `sudo apt install ./iaw-scan3_amd64.deb`. [Step-by-step instructions](https://github.com/intilinux-eng/iaw-scan-3#install-the-desktop-app).

You can try it straight away, with no car and no cable: turn on **Simulation** on the Connect page.

## What's new

A completely new desktop application on the same diagnostic engine: every value is still calculated as in IAW Scan 2, and an automated test now checks it on every build.

- **A new interface** in English and Italian, with a dashboard of gauges and dark and light themes.
- **A graph you can read:** each parameter with its own lane and scale, zoom, pointer readings, up to an hour of history.
- **A log of every session,** to reopen, compare and export as a ZIP ready for a script or an AI assistant.
- **Fault codes always read,** with a history of what appeared and went away. Clearing them now always asks first.
- **Much lighter:** one 32 MB file on Windows instead of nearly 100 MB, and the window opens in about half a second.
- **Linux tested properly for the first time,** with the serial port run against an emulated ECU.
- **Gone:** the old Windows Forms interface. German, Croatian, Polish, Portuguese and Serbian aren't back yet.

All the details are in the [changelog](https://github.com/intilinux-eng/iaw-scan-3/blob/master/CHANGELOG.md).

**This is a beta.** It passes the automated tests on Windows and Linux, but **this new interface hasn't been run on a real car yet**. A report from a real car, good or bad, is the most useful thing you can send: [open an issue](https://github.com/intilinux-eng/iaw-scan-3/issues).

**Provided with no warranty** of any kind, including that it will correctly diagnose your vehicle: see the License section of the README.

---

**Windows:** scarica qui sotto `IAWScan3-win-x64.exe` e avvialo, non c'è niente da installare. **Linux (Debian, Ubuntu e derivate):** scarica `iaw-scan3_amd64.deb`, poi `sudo apt install ./iaw-scan3_amd64.deb`. [Istruzioni passo per passo](https://github.com/intilinux-eng/iaw-scan-3/blob/master/README.it.md#installare-lapp-desktop).

Puoi provarlo subito, senza auto e senza cavo: attiva **Simulazione** nella pagina Connetti.

## Novità

Un'applicazione desktop completamente nuova sullo stesso motore diagnostico: ogni valore è calcolato ancora come in IAW Scan 2, e ora un test automatico lo verifica a ogni build.

- **Una nuova interfaccia** in inglese e italiano, con un cruscotto a lancette e tema scuro e chiaro.
- **Un grafico leggibile:** ogni parametro con la sua corsia e la sua scala, zoom, letture col puntatore, fino a un'ora di storia.
- **Un registro di ogni sessione,** da riaprire, confrontare ed esportare in uno ZIP pronto per uno script o un assistente AI.
- **Codici di errore sempre letti,** con la storia di ciò che è comparso e scomparso. Azzerarli ora chiede sempre conferma.
- **Molto più leggera:** un file da 32 MB su Windows invece di quasi 100 MB, e la finestra si apre in circa mezzo secondo.
- **Linux provato sul serio per la prima volta,** con la porta seriale verificata contro una centralina emulata.
- **Non c'è più:** la vecchia interfaccia Windows Forms. Tedesco, croato, polacco, portoghese e serbo non sono ancora tornati.

Tutti i dettagli sono nel [changelog](https://github.com/intilinux-eng/iaw-scan-3/blob/master/CHANGELOG.md) (in inglese).

**Questa è una beta.** Supera i test automatici su Windows e Linux, ma **questa nuova interfaccia non è ancora stata provata su un'auto vera**. Una segnalazione da un'auto vera, positiva o negativa, è la cosa più utile che puoi inviare: [apri una issue](https://github.com/intilinux-eng/iaw-scan-3/issues).

**Fornita senza alcuna garanzia**, nemmeno sul corretto funzionamento della diagnosi: vedi la sezione Licenza del README.
