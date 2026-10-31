**Windows:** download `IAWScan3-0.2.1-beta-win-x64.exe` below and run it, there's nothing to install. **Linux (Debian, Ubuntu and derivatives):** download `iaw-scan3_0.2.1-beta_amd64.deb`, then `sudo apt install ./iaw-scan3_0.2.1-beta_amd64.deb`. [Step-by-step instructions](https://github.com/intilinux-eng/iaw-scan-3#install-the-desktop-app).

## What's new

The first version tried with a real USB cable, which led to all of this. Every value is still calculated as in IAW Scan 2.

- **Test the cable,** with no car needed. It tells you if the cable works, or if it's missing its 12 V.
- **The list of ports says what each one is:** `COM5 · USB-SERIAL CH340`, with the cable first.
- **A failed connection says why:** a cable with no 12 V isn't reported like an ECU that doesn't answer.
- **A cable with no working driver is pointed out,** with a link to the chip maker's page.
- **Windows' 2 ms timer is back while connected,** as in the original, so the pauses between requests stay short.
- **The instructions now match reality:** Windows 11 downloads the cable's driver by itself, CH340 cables are fine, and the 12 V clips are needed, as [measured](https://github.com/intilinux-eng/iaw-scan-3#the-12-v-clips-are-needed).
- **File names now carry the version,** so downloads don't overwrite each other.

All the details are in the [changelog](https://github.com/intilinux-eng/iaw-scan-3/blob/master/CHANGELOG.md).

**This is a beta.** It passes the automated tests on Windows and Linux and it's been tried with a real cable on Windows 11, but **this new interface hasn't been run on a real car yet**. A report from a real car, good or bad, is the most useful thing you can send: [open an issue](https://github.com/intilinux-eng/iaw-scan-3/issues).

**Provided with no warranty** of any kind, including that it will correctly diagnose your vehicle: see the License section of the README.

---

**Windows:** scarica qui sotto `IAWScan3-0.2.1-beta-win-x64.exe` e avvialo, non c'è niente da installare. **Linux (Debian, Ubuntu e derivate):** scarica `iaw-scan3_0.2.1-beta_amd64.deb`, poi `sudo apt install ./iaw-scan3_0.2.1-beta_amd64.deb`. [Istruzioni passo per passo](https://github.com/intilinux-eng/iaw-scan-3/blob/master/README.it.md#installare-lapp-desktop).

## Novità

La prima versione provata con un vero cavo USB, da cui è nato tutto questo. Ogni valore è calcolato ancora come in IAW Scan 2.

- **Prova il cavo,** senza bisogno dell'auto. Ti dice se il cavo funziona, o se gli mancano i 12 V.
- **L'elenco delle porte dice cos'è ciascuna:** `COM5 · USB-SERIAL CH340`, con il cavo per primo.
- **Una connessione fallita dice perché:** un cavo senza 12 V non viene segnalato come una centralina che non risponde.
- **Un cavo senza driver funzionante viene segnalato,** con un link alla pagina del produttore del chip.
- **Torna il timer di Windows da 2 ms durante il collegamento,** come nell'originale, così le pause tra le richieste restano brevi.
- **Le istruzioni ora corrispondono alla realtà:** Windows 11 scarica da solo il driver del cavo, i cavi CH340 vanno bene e le pinze a 12 V servono, come [misurato](https://github.com/intilinux-eng/iaw-scan-3/blob/master/README.it.md#le-pinze-a-12-v-servono).
- **I nomi dei file ora hanno la versione,** così i download non si sovrascrivono.

Tutti i dettagli sono nel [changelog](https://github.com/intilinux-eng/iaw-scan-3/blob/master/CHANGELOG.md) (in inglese).

**Questa è una beta.** Supera i test automatici su Windows e Linux ed è stata provata con un vero cavo su Windows 11, ma **questa nuova interfaccia non è ancora stata provata su un'auto vera**. Una segnalazione da un'auto vera, positiva o negativa, è la cosa più utile che puoi inviare: [apri una issue](https://github.com/intilinux-eng/iaw-scan-3/issues).

**Fornita senza alcuna garanzia**, nemmeno sul corretto funzionamento della diagnosi: vedi la sezione Licenza del README.
