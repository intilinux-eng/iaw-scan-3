# IAW Scan 3

Diagnostic software for FIAT/Lancia/Alfa Romeo OBD-I engine control units (Marelli IAW 6F/8F/16F/18F/18FD/04K.P8 and the FIAT CODE immobiliser), communicating over an ISO-KKL (K-line) interface.

**This project starts from the official, last upstream release of [IAW Scan 2](http://iaw-scan2.sourceforge.net) v0.85 by Tomasz Orczyk ("TzOk")** — itself the successor of the original IAW ECU Scan. Nothing about the ECU communication protocol has been reverse-engineered from scratch here: that work, already field-proven for over a decade, comes from the upstream project. See [readme.txt](readme.txt) for the full upstream changelog and supported vehicle list, and [CHANGELOG.md](CHANGELOG.md) for a plain-language summary of what this fork changes on top of it.

> **This is a first beta.** The new interface below is functionally complete but not yet verified against real hardware for every supported ECU - see "Testing status" for exactly what has and hasn't been confirmed. Feedback and issue reports are very welcome. **Provided with no warranty of any kind - use at your own risk** (see License below).

## Screenshots

The new cross-platform interface (`IES_3.Avalonia`), shown here in simulation mode (no ECU hardware needed):

<table>
<tr>
<td width="50%"><img src="docs/screenshots/01-selezione-centralina.png" alt="ECU and vehicle selection"><br><sub>ECU and vehicle selection, connection setup</sub></td>
<td width="50%"><img src="docs/screenshots/02-parametri.png" alt="Live parameters"><br><sub>Live engine parameters, as a compact tile grid</sub></td>
</tr>
<tr>
<td width="50%"><img src="docs/screenshots/03-errori.png" alt="Stored error codes"><br><sub>Stored error codes, with MIL status</sub></td>
<td width="50%"><img src="docs/screenshots/04-grafici.png" alt="Real-time graph"><br><sub>Real-time graph with CSV export</sub></td>
</tr>
<tr>
<td width="50%"><img src="docs/screenshots/05-regolazioni.png" alt="Adjustments"><br><sub>Actuator adjustments</sub></td>
<td width="50%"></td>
</tr>
</table>

## Goal

Give IAW Scan **longevity**: a supported, modern runtime instead of .NET Framework 2.0 (unmaintained since 2015), and a UI that isn't tied to Windows. Concretely, that means:

- running on a **long-term-supported .NET version** rather than a dead one;
- a **cross-platform UI** (Windows/Linux/macOS) so the tool isn't limited to a Windows laptop — including **Linux on a Raspberry Pi**, for a small, cheap, permanently-installed diagnostic unit that can live **in the car** rather than being carried in and out each time.

## Diagnostic accuracy

**The diagnostic engine itself is not being changed.** Every value this tool reads and decodes (RPM, temperatures, pressures, error codes, and so on) uses exactly the same formulas as the original IAW Scan 2 — nothing here has been recalculated or reinterpreted. If a genuine bug in a formula is ever found and fixed, it will be called out explicitly in the changelog as such, never silently. What changes in this fork is how you interact with the software and what it runs on — not what it tells you about your engine.

## Supported operating systems

|                          | Original IAW Scan 2 (v0.85) | This fork |
|--------------------------|------------------------------|-----------|
| Windows                  | Windows 2000 through 7       | Any current Windows, via the .NET 10 Desktop Runtime |
| Linux (incl. Raspberry Pi) | Not supported              | Goal of this fork - see status note below |
| macOS                    | Not supported                | Goal of this fork - see status note below |

**Status note:** the new cross-platform UI (`IES_3.Avalonia`) is built entirely on packages that run on Linux and macOS, but it has only actually been run and tested on Windows so far. Linux/Raspberry Pi support is the direction this project is heading in, not yet a verified, ready-to-use feature.

**Specifically about Linux + Raspberry Pi:** the whole point of that port is a small, cheap touchscreen unit that lives permanently in the car - but the piece that actually matters for that use case, talking to the USB K-line interface over serial on Linux, **has not been tested at all**. Only the UI itself (running with no hardware attached, in simulation mode) has been confirmed to start on Linux. Whether the interface is even detected, and whether reading/writing over it behaves the same as on Windows, is genuinely unknown right now. If you try this on a Raspberry Pi (or any Linux box) with real hardware, please [open an issue](https://github.com/intilinux-eng/iaw-scan-3/issues) either way - a "it doesn't see the interface" report is just as useful as a "it works" one.

**What you need either way:** a USB-to-K-line (ISO-KKL) diagnostic interface cable and its driver (the same requirement as the original tool). On Linux, this typically also means adding your user to the `dialout` group so it can access the serial port without extra privileges.

## Testing status

Developed and tested primarily on a **Fiat Coupé 2.0 16V naturally aspirated** (Marelli IAW-04K.P8), **on Windows**, using the new interface's simulation mode plus real-hardware runs on that one vehicle. Other supported ECUs/vehicles come from the upstream project's own reverse-engineering and haven't all been re-verified against real hardware in this fork yet — see [CHANGELOG.md](CHANGELOG.md) for what has been specifically confirmed working or removed (e.g. automatic ECU detection, dropped after it proved unreliable on real hardware). **Linux has only been tested for the UI itself starting up, never against real ECU hardware** — see the Linux/Raspberry Pi note above.

## Project structure

- **`IES_3/`** — the original WinForms UI, ported to .NET 10 (mechanical port, behavior unchanged from upstream v0.85).
- **`IES_3.Core/`** — shared library with the ECU protocol/communication logic and localized strings, used by both UIs below. Its source files (the actual ECU decoding logic) come unchanged from the original IES_2 codebase, just relocated here.
- **`IES_3.Avalonia/`** — new cross-platform UI (Windows/Linux/macOS) built with [Avalonia](https://avaloniaui.net/) and MVVM. All the original screens are ported: ECU/vehicle selection and connecting, live parameters, error codes, actuator tests, a real-time graph with CSV export, and adjustments.

## Beta downloads

Pre-built, self-contained packages of the new cross-platform interface (`IES_3.Avalonia`) - no need to install .NET yourself - are published on demand to a rolling **[Beta release](https://github.com/intilinux-eng/iaw-scan-3/releases/tag/beta)**: a Windows `.exe` and a Linux `.deb`. These are beta builds straight from `main`, not tested releases - see "Testing status" below for what has and hasn't been confirmed on real hardware. A proper versioned-release process (release branches, version tags) will come later. Provided with no warranty, as with the rest of the project - see License.

## Building

Requires the [.NET 10 SDK](https://dotnet.microsoft.com/download) or later.

```
dotnet build IES_3.sln
```

Run the original WinForms UI (Windows only):

```
dotnet run --project IES_3/IES_3.csproj
```

Run the new cross-platform UI:

```
dotnet run --project IES_3.Avalonia/IES_3.Avalonia.csproj
```

Both UIs support a simulation mode that requires no ECU hardware (a checkbox in the new UI; Ctrl+click the Connect button, or Ctrl+F10, in the WinForms UI).

## License

Modified BSD license (see [license.txt](license.txt)), Copyright (c) 2011, Tomasz Orczyk. The WinForms UI's chart control, [ZedGraph](https://github.com/discomurray/ZedGraph), is LGPLv3-licensed and consumed as an unmodified NuGet package.

**In plain words: no warranty.** This software is provided "as is", with no warranty of any kind, express or implied - including no warranty that it will correctly diagnose your vehicle. The author(s) and contributors are not liable for any damage, to your car or otherwise, arising from its use. See [license.txt](license.txt) for the exact legal text.
