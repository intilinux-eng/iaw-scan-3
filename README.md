# IAW Scan 2 (fork)

Diagnostic software for FIAT/Lancia/Alfa Romeo OBD-I engine control units (Marelli IAW 6F/8F/16F/18F/18FD/04K.P8 and the FIAT CODE immobiliser), communicating over an ISO-KKL (K-line) interface.

This is a fork of [IAW Scan 2](http://iaw-scan2.sourceforge.net) v0.85 by Tomasz Orczyk ("TzOk"), the last upstream release. The original project is itself the successor of IAW ECU Scan. See [readme.txt](readme.txt) for the full upstream changelog and supported vehicle list.

## Why this fork

The upstream project targets .NET Framework 2.0 and hasn't been updated since 2015. This fork's goal is to give it **longevity on current and future operating systems**: a supported, modern .NET runtime, and — in progress — a modern, cross-platform UI, while keeping the ECU communication logic that has been field-proven for over a decade. See [CHANGELOG.md](CHANGELOG.md) for the detailed list of changes.

## Project structure

- **`IES_2/`** — the original WinForms UI, ported to .NET 10 (mechanical port, behavior unchanged from upstream v0.85).
- **`IES_2.Core/`** — shared library with the ECU protocol/communication logic and localized strings, used by both UIs below.
- **`IES_2.Avalonia/`** — new cross-platform UI (Windows/Linux/macOS) built with [Avalonia](https://avaloniaui.net/) and MVVM. Work in progress: the ECU selection/connection screen is done; the live parameters, errors, tests, graph, and adjustments tabs from the WinForms UI are not ported yet.

## Building

Requires the [.NET 10 SDK](https://dotnet.microsoft.com/download) or later.

```
dotnet build IES_2.sln
```

Run the original WinForms UI:

```
dotnet run --project IES_2/IES_2.csproj
```

Run the new Avalonia UI:

```
dotnet run --project IES_2.Avalonia/IES_2.Avalonia.csproj
```

Both UIs support a simulation mode that requires no ECU hardware (a checkbox in the Avalonia UI; Ctrl+click the Connect button, or Ctrl+F10, in the WinForms UI).

## License

Modified BSD license (see [license.txt](license.txt)), Copyright (c) 2011, Tomasz Orczyk. The WinForms UI's chart control, [ZedGraph](https://github.com/discomurray/ZedGraph), is LGPLv3-licensed and consumed as an unmodified NuGet package.
