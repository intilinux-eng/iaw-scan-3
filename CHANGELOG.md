# Changelog

All notable changes made in this fork relative to the original upstream release are documented here. This project **starts from the official, last upstream release, IAW Scan 2 v0.85** (Tomasz Orczyk / "TzOk", 2015-05-11, http://iaw-scan2.sourceforge.net). No ECU protocol work here has been reverse-engineered from scratch — that comes from upstream.

## Goal of this fork

Give IAW Scan **longevity**: a long-term-supported .NET runtime instead of .NET Framework 2.0 (unmaintained since 2015), and a UI that isn't tied to Windows — including **Linux on a Raspberry Pi**, so the tool can run as a small, cheap unit permanently installed **in the car** instead of a Windows laptop carried in and out. The ECU communication logic itself, field-proven for over a decade, is kept intact.

Developed and tested primarily on a **Fiat Coupé 2.0 16V naturally aspirated**.

## [Unreleased]

### Renamed
- Project renamed to **IAW Scan 3**, continuing the upstream naming (IAW ECU Scan → IAW Scan 2 → IAW Scan 3). Window titles, splash screen, and assembly metadata updated accordingly; the original attribution (Copyright TzOk 2011-2015) is kept wherever it applies.

### Added
- New `IES_2.Core` project: a shared library isolating the ECU communication logic (`ECU/*.cs`, `DataStructs.cs`) and localized strings (`Res/lang*.resx`), reusable from both the existing WinForms UI and the new UI.
- New `IES_2.Avalonia` project: first milestone of the new cross-platform UI (Windows/Linux/macOS), built with Avalonia UI and the MVVM pattern. Covers the ECU selection/connection screen so far:
  - ECU list redesigned as cards, with a colored badge and supported-vehicle count per family;
  - supported vehicles are **always visible**, grouped by brand (Fiat/Lancia/Alfa Romeo) with live search — replaces the original's hover-only tooltip;
  - **Simulation mode** toggled with a visible checkbox in the UI, instead of the original's hidden "Ctrl+click" keyboard shortcut on the Connect button;
  - async, cancellable connect/disconnect flow, with error messages shown inline in the UI instead of modal popups.

### Changed
- Migrated the whole project from .NET Framework 2.0 (2008) to **.NET 10** (LTS, supported through 2028): `.csproj` converted to the modern SDK-style format, dependencies (`System.IO.Ports`, `ZedGraph`) resolved via NuGet instead of file references outside the repository.
- `IES_2.sln` updated to the modern solution file format (the original "Visual Studio 2008" format is no longer loadable by current .NET SDKs).

### Removed
- **Automatic ECU detection**: removed from the new UI after testing against real hardware showed it does not reliably work. Manually selecting the correct ECU by searching for the vehicle model in the grouped list remains fully supported.
- 7 empty `Res/lang.*.Designer.cs` files, leftover code-generation artifacts never referenced by the original project.

### Fixed
- The project did not compile as checked out: it referenced `ZedGraph.dll` via a file path outside the repository that wasn't included.
- Removed the use of `Assembly.CodeBase`, obsolete on modern .NET runtimes.

## [0.85] - 2015-05-11 (upstream baseline)

Last official IAW Scan 2 release before the fork. See [readme.txt](readme.txt) for the full changelog of earlier upstream versions.
