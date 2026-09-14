# Changelog

Plain-language summary of what has changed, for people using this tool to diagnose cars rather than to write software. This project starts from the official, last release of IAW Scan 2 (v0.85, by Tomasz Orczyk / "TzOk"). See [readme.txt](readme.txt) for the original project's own changelog and supported vehicle list.

**The diagnostic engine is unchanged.** Every reading and error code is calculated exactly as it was in IAW Scan 2 - nothing here has been recalculated or reinterpreted. This changelog only covers what runs the software and how you interact with it.

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
