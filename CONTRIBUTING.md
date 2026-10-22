# Contributing

Issues and pull requests are welcome — in English or Ukrainian.

**About Glowstick Pro.** This repository is the free, GPL-licensed core. The paid Pro version is the same core plus a closed module. By opening a pull request you agree that your contribution is licensed under the GPL-3.0 here **and** may be distributed by the maintainer as part of Glowstick Pro. Pro features themselves aren't developed in this repository; the core only has the hooks for them (`src/Pro.cs`).

## Building and testing

```powershell
powershell -ExecutionPolicy Bypass -File build.ps1        # → dist\Glowstick.exe
powershell -ExecutionPolicy Bypass -File tools\test.ps1   # automated tests, no controller needed
```

Only .NET Framework 4.7.2+ is needed (it ships with Windows 10/11) — no Visual Studio or .NET SDK. The first build downloads the Roslyn compiler (NuGet `Microsoft.Net.Compilers.Toolset`, pinned by version and SHA-256 in `tools/common.ps1`) into `obj/roslyn`. `build.ps1` downloads the driver installers into `redist/` and checks their SHA-256. If Glowstick is running, close it (tray → Exit) before building.

The tests check every preset with 1–8 controllers, every custom effect, the DualSense → DualShock 4 report translation and that every translation is complete. CI runs them on every push and pull request.

## Project layout

| Path | What |
|---|---|
| `src/Program.cs` | Entry point: admin rights, single instance, embedded ViGEm library |
| `src/Engine.cs` | Finding controllers, their order and per-controller settings, light bar animation, game mode |
| `src/Controller.cs` | One controller: input → virtual pad, DualSense → DS4 translation, battery, hotkeys, output (light bar + rumble) |
| `src/HotkeyMap.cs` | Which button does what together with PS |
| `src/Pad.cs` | Parsing the input report: buttons, sticks, touchpad, gyro |
| `src/Presets.cs` | Built-in presets (the free ones; Pro adds the rest through `PresetLib.AddPro`) |
| `src/Live.cs` | Data for live presets: system audio, screen color |
| `src/Lang.cs`, `lang/*.json` | Localization |
| `src/Log.cs`, `src/Updater.cs` | Log file; update check |
| `src/Fullscreen.cs` | Fullscreen detection (touchpad off in games), the 1-second tick for Pro |
| `src/Pro.cs`, `src/Ui/ProPromo.cs` | Hooks for the closed Pro module and the “Get Pro” card |
| `src/Touchpad.cs` | Touchpad as mouse |
| `src/Bluetooth.cs` | Turning a Bluetooth controller off, MAC address |
| `src/HidHide.cs`, `src/Drivers.cs` | Hiding controllers; installing/removing drivers |
| `src/Native.cs` | WinAPI: HID, SetupAPI, device tree |
| `src/Ui/` | The interface (WPF): `UiApp.cs` — startup, drivers, updates, removal; `MainWindow.cs` and the `*Page.cs` files — the window and its pages; `DragReorder.cs` — drag-and-drop order; `Tray.cs` — tray icon, menu and battery alerts; `Theme.xaml` — the dark theme |
| `src/Settings.cs`, `src/Autostart.cs` | `settings.json`; scheduled tasks |
| `tools/Tests.cs`, `tools/test.ps1` | Automated tests (no controller needed) |
| `tools/LatencyTest.cs` | Latency test for game mode (DualShock 4) |
| `lib/Nefarius.ViGEm.Client.dll` | [ViGEm.NET](https://github.com/ViGEm/ViGEm.NET) 1.21.256, MIT; embedded in the exe |

## Code style

- Modern C# (Roslyn 4.11, compiled for .NET Framework 4.8): `?.`, `$"..."`, `out var`, pattern matching are fine. Older files are in C# 5 style — no need to rewrite them, just don't mix both in one method.
- The per-report input path (Controller.ReadLoop and what it calls) runs on the highest-priority thread: no allocations there in steady state — the Pro tests measure it.
- Code comments are in English; a comment explains *why*, not *what*.
- User-facing text goes through `Lang.T("English text")` or `Lang.F("Text {0}", x)` — never a bare string.
- One file — one topic (see [Project layout](#project-layout)).
- The input hot path (`Controller.ReadLoop` → virtual pad) has no allocations, locks or timers: latency matters more than elegance there.

## New presets

A preset is a function `(t, i, n) => Rgb`: `t` — time in seconds (speed applied), `i` — controller index from 0, `n` — how many are connected. A preset must look good for **any** `n` (1, 2, 3, 4+), not just two: for a phase offset between controllers use `PresetLib.Ph(i, n)`, not `i * 0.5`.

A built-in preset's English name is its ID (`settings.json` and game profiles refer to it), so renaming an existing preset resets it for users. Add the new name to every `lang/*.json` — the tests fail otherwise.

## Translations

- `lang/<code>.json` maps the English text (key) to the translation. Keys with `{0}` placeholders must keep them.
- To add a language: create a template with all keys — `powershell -ExecutionPolicy Bypass -File tools\test.ps1 -NewLang xx` (writes `lang\xx.json`) — translate the values, add the code and the language's own name to `Lang.Languages` in `src/Lang.cs`, and run the tests.
- To try a translation without rebuilding, copy it to `%APPDATA%\Glowstick\lang\<code>.json` and restart Glowstick.
- Russian is intentionally not included.

## Before a PR

- `build.ps1` and `tools\test.ps1` pass.
- Tested on a real controller if you touched input, output or game mode — ideally both USB and Bluetooth, DualShock 4 and/or DualSense.
- README updated if behavior changed.

## Releases

Bump `VERSION`, then push a `vX.Y.Z` tag with the same number — CI builds `Glowstick.exe`, runs the tests and publishes a release with `SHA256.txt`.

### Code signing

Unsigned builds trigger SmartScreen warnings. Open-source projects can get free signing from [SignPath Foundation](https://signpath.org/); once the project is approved, add the SignPath GitHub action to the release job in `.github/workflows/build.yml` between “Build” and “Release”.
