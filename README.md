<p align="center">
  <img src="https://raw.githubusercontent.com/xboxnuker-rgb/DSP-Signal-Nexus/main/assets/artwork/signal-nexus-key-art.png" width="720" alt="Signal Nexus key art">
</p>

# Signal Nexus

[![Thunderstore downloads](https://img.shields.io/badge/dynamic/json?url=https%3A%2F%2Fthunderstore.io%2Fapi%2Fv1%2Fpackage-metrics%2FGSVS_UK_ACM%2FSignalNexus%2F&query=%24.downloads&label=downloads&style=flat-square&color=23FFB0)](https://old.thunderstore.io/c/dyson-sphere-program/p/GSVS_UK_ACM/SignalNexus/)

Signal Nexus combines three Dyson Sphere Program utility buildings into one compact, belt-mounted facility: a **Traffic Monitor**, **Holo Beacon**, and **Tesla Tower**.

Monitor belt flow, drive a visible holographic status marker from the same signal IP, sound alarms, and connect nearby buildings to the power grid—all from one building and one unified settings panel.

## Features

- Full Traffic Monitor belt scanning, pass/fail logic, global alarm, speaker alarm, item filtering, and signal IP.
- Holo Beacon colour, visibility, display mode, Dark Fog beaconing, height, radius, memo, and tags.
- Shared monitor/beacon IP so monitor state can drive the beacon colour naturally.
- Integrated Tesla Tower power connection and coverage.
- Direct unified configuration window with Holo settings placed below Alarm settings.
- Building copy/paste, Shift-click duplication, blueprints, and save/reload persistence.
- A compact recipe that consumes one Traffic Monitor, one Holo Beacon, and one Tesla Tower.
- A startup log banner that identifies the mod version, target game, plugin ID, creator, and combined facilities.

## Screenshots

| Compact belt-mounted building | Holographic marker in action |
| --- | --- |
| ![Signal Nexus installed on a conveyor belt](https://raw.githubusercontent.com/xboxnuker-rgb/DSP-Signal-Nexus/main/assets/screenshots/signal-nexus-building.png) | ![Signal Nexus projecting a green holographic beacon](https://raw.githubusercontent.com/xboxnuker-rgb/DSP-Signal-Nexus/main/assets/screenshots/signal-nexus-beacon.png) |

![Unified Signal Nexus monitor, alarm, and hologram settings](https://raw.githubusercontent.com/xboxnuker-rgb/DSP-Signal-Nexus/main/assets/screenshots/signal-nexus-settings.png)

## Requirements

- Dyson Sphere Program `0.10.34.28524`
- `xiaoye97-BepInEx` package `5.4.17` (tested with BepInEx runtime `5.4.21`)
- LDBTool `3.0.3`
- CommonAPI `1.6.7`

## Installation

### r2modman / Thunderstore Mod Manager

Install **[Signal Nexus from the Dyson Sphere Program Thunderstore community](https://old.thunderstore.io/c/dyson-sphere-program/p/GSVS_UK_ACM/SignalNexus/)** through r2modman or Thunderstore Mod Manager. Its required dependencies are declared in the package manifest and will be installed automatically.

### Manual

1. Install BepInEx, LDBTool, and CommonAPI.
2. Extract the release ZIP.
3. Copy `SignalNexus.dll` into `BepInEx/plugins/SignalNexus/`.
4. Start the game through the modded profile.

Back up important saves before changing any mod list.

## Usage

Craft Signal Nexus from one Traffic Monitor, one Holo Beacon, and one Tesla Tower. Place it directly on a conveyor belt, then click it to open the combined settings window. Configure the monitor and alarm normally, then use the **Holo settings** section to control the marker.

Shift-click duplication and building copy/paste retain the complete combined configuration. Blueprint placement and save/reload are also supported.

## Building from source

The project targets .NET Standard 2.1 and references assemblies from a local DSP installation and an r2modman profile:

```powershell
dotnet build .\src\SignalNexus\SignalNexus.csproj -c Release `
  -p:DSPGamePath="D:\Games\DSP" `
  -p:DSPProfilePath="$env:APPDATA\r2modmanPlus-local\DysonSphereProgram\profiles\YOUR_PROFILE"
```

To build a release-ready Thunderstore ZIP:

```powershell
.\scripts\Build-Package.ps1 -DSPGamePath "D:\Games\DSP" -DSPProfilePath "PATH_TO_PROFILE"
```

Pass `-DotNetPath "PATH_TO_DOTNET"` if the .NET SDK is not on `PATH`.

## Compatibility notes

Signal Nexus uses LDBTool to register its building and recipe, then patches the vanilla monitor window so the combined state remains native to DSP's save, blueprint, and copy/paste systems. It is tested in a heavily modded DSP 0.10.34 profile, including BuildToolOpt and DeliverySlotsTweaks.

Please report reproducible issues with the game version, mod versions, and the relevant BepInEx log attached.

## License

Signal Nexus is released under the [MIT License](LICENSE).
