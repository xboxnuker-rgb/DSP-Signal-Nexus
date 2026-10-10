# Thunderstore release records

## 0.2.15 release candidate

- **Community:** Dyson Sphere Program
- **Package name:** SignalNexus
- **Version:** 0.2.15
- **Website:** https://github.com/xboxnuker-rgb/DSP-Signal-Nexus
- **Expected GitHub release:** https://github.com/xboxnuker-rgb/DSP-Signal-Nexus/releases/tag/v0.2.15
- **Upload asset:** `dist/SignalNexus-0.2.15.zip`
- **SHA-256:** `2359E65AE0D3A1250EC1154187198D13F6D11172671708F204ACC8A9AE682033`

This patch adds the GSVS "CREATED BY" startup banner and logs the Signal Nexus version, current DSP version, plugin ID, creator, and combined facilities during BepInEx startup.

### 0.2.15 verification

- [x] Package, assembly, and manifest versions match.
- [x] Release build completes with zero warnings and zero errors.
- [x] Package root contains the DLL, manifest, icon, README, changelog, and license.
- [ ] Verify the banner in a live BepInEx startup log.
- [ ] Upload the ZIP to the DSP Thunderstore community.
- [ ] Verify the live dependency string reports 0.2.15.

## 0.2.14 published record

### Listing

- **Community:** Dyson Sphere Program
- **Package name:** SignalNexus
- **Version:** 0.2.14
- **Published:** 2026-10-06
- **Website:** https://github.com/xboxnuker-rgb/DSP-Signal-Nexus
- **Published listing:** https://old.thunderstore.io/c/dyson-sphere-program/p/GSVS_UK_ACM/SignalNexus/
- **GitHub release:** https://github.com/xboxnuker-rgb/DSP-Signal-Nexus/releases/tag/v0.2.14
- **Short description:** Combines a Traffic Monitor, Holo Beacon and Tesla Tower into one compact, configurable utility building.
- **Categories:** Belts and Sorters, Info, Power, Quality of Life

### Package dependencies

- `xiaoye97-BepInEx-5.4.17`
- `xiaoye97-LDBTool-3.0.3`
- `CommonAPI-CommonAPI-1.6.7`

### Upload asset

Published `dist/SignalNexus-0.2.14.zip` (`SHA-256 F18EC0B4DF50BE3B2252DDE5FCDF49A948E54A0F6BEAA141C606F11DE102772F`). Its root contains:

- `SignalNexus.dll`
- `manifest.json`
- `icon.png` (256 x 256 PNG)
- `README.md`
- `CHANGELOG.md`
- `LICENSE`

### Store copy

Signal Nexus combines a Traffic Monitor, Holo Beacon, and Tesla Tower into one compact belt-mounted facility. Configure belt-flow conditions, alarms, signal IP, hologram colour and visibility, Dark Fog beaconing, height, radius, memo, and tags from one unified window. The built-in Tesla Tower connects and powers nearby facilities. Copy/paste, Shift-click duplication, blueprints, and save/reload retain the combined settings.

### Release verification

- [x] Package name and version match the assembly and manifest.
- [x] Dependencies are declared.
- [x] Icon is a square 256 x 256 PNG.
- [x] README, changelog, and license are included.
- [x] DLL was rebuilt from the repository source.
- [x] Shift-click duplication tested.
- [x] Save/reload persistence tested.
- [x] Selector placement, Logistics F9 placement, and live physical flow display tested in game.
- [x] Uploaded the 0.2.14 ZIP to the DSP Thunderstore community.
- [x] Verified the live dependency string and download links report 0.2.14.
- [ ] Optional: install the published package into a separate clean r2modman profile for an additional smoke test.
