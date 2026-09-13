# IAW Scan 3

Diagnostic software for FIAT/Lancia/Alfa Romeo OBD-I engine control units (Marelli IAW 6F/8F/16F/18F/18FD/04K.P8 and the FIAT CODE immobiliser), communicating over an ISO-KKL (K-line) interface.

**This project starts from the official, last upstream release of [IAW Scan 2](http://iaw-scan2.sourceforge.net) v0.85 by Tomasz Orczyk ("TzOk")** — itself the successor of the original IAW ECU Scan. Nothing about the ECU communication protocol has been reverse-engineered from scratch here: that work, already field-proven for over a decade, comes from the upstream project. See [readme.txt](readme.txt) for the full upstream changelog and supported vehicle list, and [CHANGELOG.md](CHANGELOG.md) for exactly what this fork changes on top of it.

## Goal

Give IAW Scan **longevity**: a supported, modern runtime instead of .NET Framework 2.0 (unmaintained since 2015), and a modern UI that isn't tied to Windows. Concretely, that means:

- running on a **long-term-supported .NET version** rather than a dead one;
- a **cross-platform UI** (Windows/Linux/macOS) so the tool isn't limited to a Windows laptop — including **Linux on a Raspberry Pi**, for a small, cheap, permanently-installed diagnostic unit that can live **in the car** rather than being carried in and out each time.

## Testing status

Developed and tested primarily on a **Fiat Coupé 2.0 16V naturally aspirated** (Marelli IAW-04K.P8). Other supported ECUs/vehicles come from the upstream project's own reverse-engineering and haven't all been re-verified against real hardware in this fork yet — see [CHANGELOG.md](CHANGELOG.md) for what has been specifically confirmed working or removed (e.g. automatic ECU detection, dropped after it proved unreliable on real hardware).

## Project structure

- **`IES_2/`** — the original WinForms UI, ported to .NET 10 (mechanical port, behavior unchanged from upstream v0.85).
- **`IES_2.Core/`** — shared library with the ECU protocol/communication logic and localized strings, used by both UIs below.
- **`IES_2.Avalonia/`** — new cross-platform UI (Windows/Linux/macOS) built with [Avalonia](https://avaloniaui.net/) and MVVM. Work in progress: the ECU selection/connection screen is done; the live parameters, errors, tests, graph, and adjustments tabs from the WinForms UI are not ported yet.

## Building

Requires the [.NET 10 SDK](https://dotnet.microsoft.com/download) or later.

```
dotnet build IES_2.sln
```

Run the original WinForms UI (Windows only):

```
dotnet run --project IES_2/IES_2.csproj
```

Run the new cross-platform UI:

```
dotnet run --project IES_2.Avalonia/IES_2.Avalonia.csproj
```

Both UIs support a simulation mode that requires no ECU hardware (a checkbox in the new UI; Ctrl+click the Connect button, or Ctrl+F10, in the WinForms UI).

## License

Modified BSD license (see [license.txt](license.txt)), Copyright (c) 2011, Tomasz Orczyk. The WinForms UI's chart control, [ZedGraph](https://github.com/discomurray/ZedGraph), is LGPLv3-licensed and consumed as an unmodified NuGet package.
