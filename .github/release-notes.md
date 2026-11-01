**Windows 11:** download `IAWScan3-0.2.2-beta-win-x64.exe` below and run it, there's nothing to install. [Step-by-step instructions](https://github.com/intilinux-eng/iaw-scan-3#install-on-windows-11). **Linux PC, work in progress:** `iaw-scan3_0.2.2-beta_amd64.deb`, then `sudo apt install ./iaw-scan3_0.2.2-beta_amd64.deb`.

## What's new

Thanks to the original author of IAW Scan 2, who reported this in [issue #8](https://github.com/intilinux-eng/iaw-scan-3/issues/8).

- **IAW-6F/16F: how to identify the car.** These ECUs give their ISO code only by themselves as the ignition comes on, and only after being fully off: ignition off for 90 seconds, Connect, then ignition on. When one is chosen, the Connect page now guides you through this and tells you when to turn the ignition on, and a message says so if the code was missed. Diagnosis works the same. Nothing changes for other ECUs, the Coupé's IAW-04K.P8 included.

All the details are in the [changelog](https://github.com/intilinux-eng/iaw-scan-3/blob/master/CHANGELOG.md).

**This is a beta.** It passes the automated tests on Windows and Linux and it's been tried with a real cable on Windows 11, but **this new interface hasn't been run on a real car yet**, and this change hasn't been tried on a real IAW-6F/16F. A report from a real car, good or bad, is the most useful thing you can send: [open an issue](https://github.com/intilinux-eng/iaw-scan-3/issues).

**Provided with no warranty** of any kind, including that it will correctly diagnose your vehicle: see the License section of the README.

---

**Windows 11:** scarica qui sotto `IAWScan3-0.2.2-beta-win-x64.exe` e avvialo, non c'è niente da installare. [Istruzioni passo per passo](https://github.com/intilinux-eng/iaw-scan-3/blob/master/README.it.md#installare-su-windows-11). **PC Linux, lavori in corso:** `iaw-scan3_0.2.2-beta_amd64.deb`, poi `sudo apt install ./iaw-scan3_0.2.2-beta_amd64.deb`.

## Novità

Grazie all'autore originale di IAW Scan 2, che l'ha segnalato nella [issue #8](https://github.com/intilinux-eng/iaw-scan-3/issues/8).

- **IAW-6F/16F: come identificare l'auto.** Queste centraline danno il codice ISO solo da sole quando il quadro si accende, e solo se erano del tutto spente: quadro spento per 90 secondi, Connetti, poi quadro acceso. Quando ne scegli una, la pagina Connetti ora ti guida e ti dice quando accendere il quadro, e un messaggio lo dice se il codice è andato perso. La diagnosi funziona lo stesso. Non cambia nulla per le altre centraline, compresa la IAW-04K.P8 della Coupé.

Tutti i dettagli sono nel [changelog](https://github.com/intilinux-eng/iaw-scan-3/blob/master/CHANGELOG.md) (in inglese).

**Questa è una beta.** Supera i test automatici su Windows e Linux ed è stata provata con un vero cavo su Windows 11, ma **questa nuova interfaccia non è ancora stata provata su un'auto vera**, e questa modifica non è stata provata su una vera IAW-6F/16F. Una segnalazione da un'auto vera, positiva o negativa, è la cosa più utile che puoi inviare: [apri una issue](https://github.com/intilinux-eng/iaw-scan-3/issues).

**Fornita senza alcuna garanzia**, nemmeno sul corretto funzionamento della diagnosi: vedi la sezione Licenza del README.
