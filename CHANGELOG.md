# Changelog

Plain-language summary of what has changed, for people using this tool to diagnose cars rather than to write software. This project starts from the official, last release of IAW Scan 2 (v0.85, by Tomasz Orczyk / "TzOk"). See [readme.txt](readme.txt) for the original project's own changelog, and the [comparison in the README](README.md#whats-different-from-the-original-iaw-scan-2) for the differences at a glance.

**The diagnostic engine is unchanged.** Every reading and error code is calculated exactly as it was in IAW Scan 2 - nothing here has been recalculated or reinterpreted. An automated test now checks this on every build.

## 0.2.2-beta

Thanks to the original author of IAW Scan 2, who reported this in [issue #8](https://github.com/intilinux-eng/iaw-scan-3/issues/8). Not tried on a real IAW-6F/16F yet.

- **IAW-6F/16F: how to identify the car.** These ECUs give their ISO code only by themselves as the ignition comes on, and only after being fully off, so with the ignition already on the exact car can't be identified: the ignition has to be off for 90 seconds, then Connect, then ignition on. The Connect page now explains this when one is chosen, and a message says so if the code was missed. Diagnosis works the same. Other ECUs, the Coupé's IAW-04K.P8 included, can be asked for their code, so nothing changes for them.

## 0.2.1-beta

The first version tried with a real USB cable, a CH340 one on Windows 11. Still not on a car.

- **New: Test the cable.** A button on the Connect page that works with no car. Transmit and receive share one wire, so a working cable hands back every byte it sends: this checks that, at both speeds the ECUs use, and says if the cable works or is missing its 12 V.
- **The list of ports says what each one is:** `COM5 · USB-SERIAL CH340` instead of a bare `COM5`, with the cable first. Useful as soon as there's more than one port.
- **A failed connection says why.** With no answer at all, not even the cable's own echo, the program now stops and says the cable's K-line side isn't answering, instead of connecting and then reporting lost communication. If the cable answers but the ECU doesn't, it connects as before and says so.
- **A cable with no working driver is pointed out** on the Connect page, with a link to the chip maker's page. Not yet seen on a real computer: Windows 11 installed this cable's driver by itself.
- **Windows' 2 ms timer is back while connected.** The original asked for it and the new interface had lost it, so the short pauses between requests could take up to 15.6 ms and values could update less often.
- **Corrected the advice about the cable.** FTDI chips are no longer recommended: this CH340 cable answers in 3.4 ms at 7680 baud. Nothing needs installing on Windows 11. And the adapter's 12 V clips are needed: with them off, 0 of 24 test bytes came back, with them on, all 24.
- **Downloads now carry the version in their names,** so they don't overwrite each other. A test now checks the README's download links against the version.
- **`license.txt` no longer carries the LGPL text.** It was there for ZedGraph, the graph library of the original Windows Forms interface, which left the project with that interface. What remains is the original project's Modified BSD license, unchanged.

## Beta 2 (0.2.0-beta)

A new desktop application. **Not yet run on a real car:** see "How far it has been tested" in the README.

### New look, in English and Italian

- **A completely new interface:** a sidebar instead of tabs, a status bar always showing what's connected, and dark and light themes.
- **A dashboard of gauges:** each with the lowest and highest values seen and the last minute of history. Choose what's on it, reorder it, pick the size of the tiles, or go full screen.
- **English and Italian:** the program follows your system, or you choose in Settings. The new interface was in Italian only until now. The other languages of the original interface (German, Croatian, Polish, Portuguese, Serbian) aren't back yet.
- **Connecting is easier:** search for your car by name, in any word order. The cable appears by itself when you plug it in.

### A real graph

- **Every parameter gets its own lane and scale,** so 6000 rpm no longer flattens 14 V. Or overlay them all to compare shapes.
- **Zoom with the wheel, drag through time, read exact values with the pointer.**
- **Always on,** with up to an hour of history, not only while recording.

### A log of every session

- **Every connection to a real ECU is logged:** what was connected, every fault code that appeared or went away, tests, clearing, lost communication, and the values read. Recording can start by itself.
- **Reopen any session** to study it, with minimum, average and maximum for each parameter, and click an event to jump to that moment.
- **Compare two sessions** on the same chart.
- **Export as a ZIP** with a description of the format, ready for a script or an AI assistant. Old `IESexp_*.csv` recordings can be opened too.
- **Sessions now live in your user folder,** not in whatever folder the program was started from, with a size limit.

### Fixes

- **Parameters sharing a raw value no longer freeze each other.** Turning off one of the two manifold pressure scales silently froze the other at its last value.
- **Clearing fault codes now always asks first,** and the `C` key that did it at once, from any tab, is gone. The other single-letter shortcuts went with it. Actuator tests and adjustments also ask first.
- **The interface no longer freezes while talking to the ECU** when reconnecting or sending an adjustment value.
- **Recordings reach the disk every 5 seconds,** so a crash or power cut costs at most the last few seconds. A value the ECU didn't answer is now left empty instead of repeating the previous one.
- **A failed connection could leave the cable "busy"** until the program was restarted, if the ECU stopped answering halfway through connecting.
- **The Linux package now declares the libraries it needs.** Without them, on a minimal system, the program stopped at launch.

### Lighter

- **Much smaller and faster to start:** on Windows, a single 32 MB file instead of nearly 100 MB, and the window opens in about half a second instead of over a second.
- **On Linux, FTDI cables are set to their fastest response (1 ms) when plugged in,** as is usual for K-line, so values update more often. Not tried with a real cable yet.
- **The interface updates at a steady pace** however fast the ECU answers, and the graph takes the same effort to draw whether it holds a minute or an hour.

### Simpler

- **The old Windows Forms interface is gone.** Of the original program, only the diagnostic engine remains. If you need Windows 7 or earlier, use the original IAW Scan 2.
- **Fault codes are read all the time,** whatever page is open, so the count in the sidebar is always current. Parameters update slightly less often as a result: it's the same reading the original did with its "Query Errors" box ticked.
- **Simulation now behaves like a car:** believable, smoothly changing values, and the car you picked.

### Tested automatically

- **The formulas:** 2745 results recorded from the untouched original code, compared on every build.
- **The whole application** in simulation, on Windows and Linux.
- **The serial port on Linux,** never exercised before: the whole flow against an emulated ECU, including the unusual 7680 baud. This doesn't replace a test with a real cable and a real car.

## Fiat Coupé edition (experimental)

Published separately, not with the desktop beta.

- **IAW Scan 3 Coupé,** for the Fiat Coupé only (Weber-Marelli IAW-04K.P8 engine ECU and FIAT CODE immobiliser), built to live permanently in the car on an Arduino UNO Q.
- You use it from your phone, through the board's own Wi-Fi, with no app to install. The screens are a dashboard, parameters, fault codes, actuator tests and a live graph.
- It is also a **data logger**: at every ignition-on it connects to the ECU by itself and records every parameter, fault code and event, with or without a phone connected. The board has no clock battery, so it takes the date and time from the first phone that connects and reconstructs the times recorded before that.
- Each drive can be downloaded as a ZIP to analyse, including with an AI assistant.
- It is designed to have its power simply cut at key-off: files are synced every few seconds.
- **Experimental, tested in simulation only**: it has never run on a real UNO Q or in a car yet. It is published as a separate pre-release, not with the desktop beta. See [docs/coupe](docs/coupe/README.md) (in Italian).

## Beta 1

First public beta. Published as a private-source fork turned public repository, with downloadable packages - see "Beta downloads" in the README.

- **Renamed to IAW Scan 3**, continuing on from "IAW ECU Scan" and "IAW Scan 2".
- **Updated to run on current, supported software.** The original ran on a 2008-era version of Windows software that stopped receiving updates in 2015; this version runs on a currently-supported one.
- **Started building a new interface** that will also work on Linux (not just Windows), aimed at running on small, cheap hardware like a Raspberry Pi that can stay permanently installed in the car. Built entirely on packages that also run on Linux and macOS, but only actually run and tested on Windows so far - **on Linux specifically, only the interface itself starting up has been checked, never the actual serial communication with a USB K-line interface.** If you try this on a Raspberry Pi with real hardware, a report either way (works or doesn't see the interface at all) is genuinely useful right now.
- **The new interface now covers everything the original one does**: live parameter readings, error codes, actuator tests, a real-time graph with CSV export, and adjustments (including the byte-value dial for the ones that need it) - not just picking your ECU/vehicle and connecting. The original Windows interface stays included alongside it for now.
- **Live parameters are shown as a grid of compact tiles** instead of one long scrolling list, so more of them are visible on screen at once. Parameters you've turned on to watch are highlighted so they stand out from the rest.
- **The new interface's "About" dialog credits both this fork and the original IAW Scan 2 project it's built on**, with a link to the original project page, and keeps the original author's copyright notice and license text intact.
- **Automatic ECU detection removed.** It was tested on real hardware and didn't reliably work. Picking your ECU or vehicle by hand is now easier instead: search by car model, with vehicles grouped by make.
- **"Test without a car" mode is now a visible on/off switch** in the new interface, instead of a hidden keyboard shortcut (Ctrl+click) in the original.
- **The list of supported vehicles is now always visible and searchable**, instead of only showing up if you hovered the mouse over it.
- **Beta builds are now available to download** without building from source: a self-contained Windows `.exe` and a Linux `.deb`, published on demand to a rolling "beta" release on the project page. These are unverified builds straight from the main branch, not tested releases - see "Testing status" in the README for what has and hasn't been confirmed on real hardware.
- **Added screenshots of the new interface to the README**, taken in simulation mode.
- **Made the "no warranty" terms of the license explicit in plain language** in the README and in the beta release notes, not just in the legal text of license.txt.
