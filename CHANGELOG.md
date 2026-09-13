# Changelog

Plain-language summary of what has changed, for people using this tool to diagnose cars rather than to write software. This project starts from the official, last release of IAW Scan 2 (v0.85, by Tomasz Orczyk / "TzOk"). See [readme.txt](readme.txt) for the original project's own changelog and supported vehicle list.

**The diagnostic engine is unchanged.** Every reading and error code is calculated exactly as it was in IAW Scan 2 - nothing here has been recalculated or reinterpreted. This changelog only covers what runs the software and how you interact with it.

## In progress

- **Renamed to IAW Scan 3**, continuing on from "IAW ECU Scan" and "IAW Scan 2".
- **Updated to run on current, supported software.** The original ran on a 2008-era version of Windows software that stopped receiving updates in 2015; this version runs on a currently-supported one.
- **Started building a new interface** that will also work on Linux (not just Windows), aimed at running on small, cheap hardware like a Raspberry Pi that can stay permanently installed in the car. Still in progress: right now it only covers choosing your ECU/vehicle and connecting - the live readings, error codes, tests, graph, and adjustments screens from the original interface aren't there yet, so for those you still need the original Windows interface, included alongside the new one.
- **Automatic ECU detection removed.** It was tested on real hardware and didn't reliably work. Picking your ECU or vehicle by hand is now easier instead: search by car model, with vehicles grouped by make.
- **"Test without a car" mode is now a visible on/off switch** in the new interface, instead of a hidden keyboard shortcut (Ctrl+click) in the original.
- **The list of supported vehicles is now always visible and searchable**, instead of only showing up if you hovered the mouse over it.
