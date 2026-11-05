# Changelog

This is a summary of the changes in plain language. It is for people who use this tool to diagnose cars, not to write software. This project starts from the last official release of IAW Scan 2 (v0.85, by Tomasz Orczyk, "TzOk"). For the changelog of the original project, see [readme.txt](readme.txt). For a quick view of the differences, see the [comparison in the README](README.md#what-is-different-from-the-original-iaw-scan-2).

**The diagnostic engine does not change.** The program calculates each value and each fault code exactly as IAW Scan 2 does. We did not recalculate or change any formula. An automated test checks this on each build.

## 0.2.2-beta

The original author of IAW Scan 2 reported this in [issue #8](https://github.com/intilinux-eng/iaw-scan-3/issues/8). Thank you. We did not try it on a real IAW-6F/16F yet.

- **IAW-6F/16F: how to identify the car.** These ECUs send their ISO code only when the ignition comes on. They send it only after they were fully off. Thus, if the ignition is already on, the program cannot identify the exact car. Do these steps: ignition off for 90 seconds, Connect, then ignition on. When you choose one of these ECUs, the Connect page guides you and tells you when to turn the ignition on. If the program does not get the code, a message tells you. Diagnosis works the same. For other ECUs, the program asks for the code, so nothing changes. This includes the IAW-04K.P8 of the Coupé.
- **The bridge between pins 4 and 5.** With the cable and the Fiat adapter of the project, no data came through until a wire joined the two ground pins of the adapter socket. The cable takes its ground from one pin, and the adapter gives ground on the other pin. The README tells how and why, with a photo. When the K-line side does not answer, the cable test and a failed connection now tell you about the bridge and the 12 V.

## 0.2.1-beta

This is the first version that we tried with a real USB cable: a CH340 cable on Windows 11. We did not try it on a car yet.

- **New: Test the cable.** This button on the Connect page works with no car. Transmit and receive use one wire, so a cable that works sends back each byte that it sends. The test checks this at the two speeds that the ECUs use. It tells you if the cable works or if it has no 12 V.
- **The list of ports tells what each port is.** It shows `COM5 · USB-SERIAL CH340`, not only `COM5`, and the cable comes first. This helps when there is more than one port.
- **A failed connection tells why.** If nothing answers, not even the echo of the cable, the program stops. It says that the K-line side of the cable does not answer. Before, it connected and then reported lost communication. If the cable answers but the ECU does not, it connects as before and tells you.
- **The Connect page tells you about a cable that has no driver,** with a link to the page of the chip maker. We did not see this on a real computer yet: Windows 11 installed the driver of this cable automatically.
- **The 2 ms timer of Windows is back during a connection.** The original used it, but the new interface did not. Without it, the short pauses between requests could take up to 15.6 ms, and values could update less often.
- **The advice about the cable is correct now.** We do not recommend FTDI chips any more: this CH340 cable answers in 3.4 ms at 7680 baud. On Windows 11, you do not install anything. The 12 V clips of the adapter are necessary: with the clips off, 0 of 24 test bytes came back. With the clips on, all 24 came back.
- **The names of the downloads include the version,** so a new download does not overwrite an old one. A test checks the download links of the README against the version.
- **`license.txt` does not have the LGPL text any more.** The LGPL text was for ZedGraph, the graph library of the original Windows Forms interface. ZedGraph went away with that interface. The Modified BSD license of the original project stays, with no change.

## Beta 2 (0.2.0-beta)

This is a new desktop application. **We did not use it on a real car yet:** see "How far it has been tested" in the README.

### New look, in English and Italian

- **A fully new interface:** a sidebar, not tabs. A status bar always shows what is connected. There are dark and light themes.
- **A dashboard of gauges:** each gauge shows the lowest and highest values and the last minute of history. Choose the gauges, put them in order, choose the size, or use full screen.
- **English and Italian:** the program uses the language of your system, or you choose it in Settings. Until now, the new interface was only in Italian. The other languages of the original interface (German, Croatian, Polish, Portuguese, Serbian) are not back yet.
- **It is easier to connect:** search for your car by name, with the words in any order. The cable shows in the list when you plug it in.

### A real graph

- **Each parameter has its own lane and scale,** so 6000 rpm does not flatten 14 V. You can also put all of them in one plot to compare their shapes.
- **Use the wheel to zoom.** Drag to move through time. Use the pointer to read exact values.
- **The graph is always on,** with up to one hour of history. Before, it worked only during a recording.

### A log of each session

- **The program logs each connection to a real ECU:** what was connected, each fault code that came or went, tests, the clearing of codes, lost communication, and the values that it read. Recording can start automatically.
- **Open a session again** to study it, with the minimum, average and maximum of each parameter. Click an event to go to that moment.
- **Compare two sessions** on the same chart.
- **Export as a ZIP** that describes the format. It is ready for a script or an AI assistant. You can also open old `IESexp_*.csv` recordings.
- **Sessions are now in your user folder,** not in the folder where the program started. A size limit applies.

### Fixes

- **Two parameters that share a raw value now both update.** Before, if you turned off one of the two scales of manifold pressure, the other one stopped at its last value. It did not tell you.
- **The program always asks before it clears fault codes.** The `C` key, which cleared them at once from any tab, is gone. The other single-letter shortcuts are also gone. Actuator tests and adjustments also ask first.
- **The interface does not freeze when it talks to the ECU,** for example when it connects again or sends an adjustment value.
- **Recordings go to the disk every 5 seconds,** so a crash or a power cut loses only the last few seconds. When the ECU does not answer for a value, that cell is now empty. Before, it showed the previous value again.
- **A failed connection does not keep the cable "busy" any more.** Before, if the ECU stopped answering during the connection, you had to restart the program.
- **The Linux package now lists the libraries that it needs.** Without them, on a minimal system, the program stopped at start.

### Lighter

- **Much smaller and faster to start:** on Windows, it is one 32 MB file, not nearly 100 MB. The window opens in about half a second, not more than one second.
- **On Linux, FTDI cables get their fastest response (1 ms) when you plug them in.** This is usual for K-line, and values update more often. We did not try this with a real cable yet.
- **The interface updates at a steady rate,** at any speed of the ECU. The graph needs the same work for one minute or one hour of data.

### Simpler

- **The old Windows Forms interface is gone.** Of the original program, only the diagnostic engine stays. If you need Windows 7 or earlier, use the original IAW Scan 2.
- **The program reads fault codes all the time,** on each page, so the count in the sidebar is always correct. Thus, parameters update a little less often. The original did the same when you ticked its "Query Errors" box.
- **Simulation now shows a car that you can believe:** values that change smoothly, and the car that you chose.

### Tested automatically

- **The formulas:** 2745 results recorded from the original code, with no changes, and compared on each build.
- **The full application,** in simulation, on Windows and on Linux.
- **The serial port on Linux,** which we never tried before: the full flow against an emulated ECU, with the unusual speed of 7680 baud. This does not replace a test with a real cable and a real car.

## Fiat Coupé edition (experimental)

We publish this edition separately, not with the desktop beta.

- **IAW Scan 3 Coupé** is only for the Fiat Coupé: the Weber-Marelli IAW-04K.P8 engine ECU and the FIAT CODE immobiliser. It runs on an Arduino UNO Q that stays in the car.
- You use it from your phone, through the Wi-Fi of the board. You do not install an app. The screens are a dashboard, parameters, fault codes, actuator tests and a live graph.
- It is also a **data logger.** At each ignition-on, it connects to the ECU automatically. It records each parameter, fault code and event, with or without a phone. The board has no clock battery. Thus, it takes the date and time from the first phone that connects, and it calculates the times of the earlier records.
- You can download each drive as a ZIP to analyse, also with an AI assistant.
- You can cut its power at key-off with no shutdown: it writes its files to the disk every few seconds.
- **Experimental, tested only in simulation:** it did not run on a real UNO Q or in a car yet. We publish it as a separate pre-release. See [docs/coupe](docs/coupe/README.md) (in Italian).

## Beta 1

This is the first public beta. The fork was private and became a public repository, with packages to download. See "Beta downloads" in the README of that time.

- **New name: IAW Scan 3.** It continues from "IAW ECU Scan" and "IAW Scan 2".
- **It runs on current software that has support.** The original ran on a Windows software version from 2008, which got its last updates in 2015. This version runs on a version that has support now.
- **The start of a new interface** that also works on Linux, not only on Windows. The aim is small, cheap hardware such as a Raspberry Pi, which can stay in the car. It uses only packages that also run on Linux and macOS. But we ran and tested it only on Windows. **On Linux, we checked only that the interface starts. We did not check the serial communication with a USB K-line interface.** If you try it on a Raspberry Pi with real hardware, please send a report, good or bad.
- **The new interface does all that the original does:** live parameters, fault codes, actuator tests, a real-time graph with CSV export, and adjustments. This includes the byte-value dial for the adjustments that need it. The original Windows interface stays in the project for now.
- **Live parameters show as a grid of small tiles,** not as one long list. Thus, you can see more of them at the same time. The parameters that you selected are highlighted.
- **The "About" dialog of the new interface gives credit to this fork and to the original IAW Scan 2 project,** with a link to the page of the original project. It keeps the copyright notice and license text of the original author.
- **Automatic ECU detection is gone.** We tested it on real hardware, and it did not work reliably. Now it is easier to choose your ECU or vehicle: search by car model, with the vehicles in groups by make.
- **"Test without a car" is now a visible on/off switch** in the new interface. In the original, it was a hidden shortcut (Ctrl+click).
- **The list of supported vehicles is always visible, and you can search it.** Before, it showed only when you put the mouse on it.
- **You can download beta builds** and not build them from the source: a self-contained Windows `.exe` and a Linux `.deb`. We publish them when necessary in a "beta" release on the project page. These builds come directly from the main branch, and we did not test them as releases. See "Testing status" in the README of that time for what we confirmed on real hardware.
- **The README has screenshots of the new interface,** made in simulation mode.
- **The "no warranty" terms of the license are clear, in plain language,** in the README and in the beta release notes. Before, they were only in the legal text of license.txt.
