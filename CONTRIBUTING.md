# Contributing

Issues and pull requests are welcome.

This repository is the free core of Glowstick. Glowstick Pro is this core plus a closed module, which plugs into the hooks in `src/Pro.cs`. By opening a pull request you agree that your contribution is licensed under the GPL-3.0 here and may also ship as part of Glowstick Pro.

## Build and test

```powershell
powershell -ExecutionPolicy Bypass -File build.ps1        # → dist\Glowstick.exe
powershell -ExecutionPolicy Bypass -File tools\test.ps1   # tests, no controller needed
```

Needs only Windows 10/11; the first build downloads the C# compiler. Close Glowstick before building.

## Where things are

| Path | What |
|---|---|
| `src/Engine.cs` | Finding controllers, their order, light bar animation, game mode |
| `src/Controller.cs` | One controller: reading input, battery, writing the light bar and rumble |
| `src/PadWire.cs`, `src/Pad.cs` | Report formats: DualSense ↔ DualShock 4 layout, output packets; parsing |
| `src/VirtualPad.cs` | The virtual DualShock 4 / Xbox 360 games see |
| `src/PsHotkeys.cs`, `src/HotkeyMap.cs` | PS + button hotkeys |
| `src/Touchpad.cs`, `src/InputOut.cs` | Touchpad as mouse; keyboard / mouse output |
| `src/Presets.cs`, `src/Live.cs` | Built-in presets; data for live presets |
| `src/Settings.cs`, `src/Lang.cs`, `lang/` | Settings file; translations |
| `src/HidHide.cs`, `src/Drivers.cs`, `src/Installer.cs`, `src/Autostart.cs` | Drivers, the protected copy, scheduled tasks |
| `src/Pro.cs` | Hooks for the Pro module |
| `src/Ui/` | The window (WPF); `Kit.cs` — shared building blocks of the pages |
| `tools/` | Tests, latency test |

## Code style

- Modern C# is fine (Roslyn, .NET Framework 4.8).
- Comments in English, explaining *why*.
- User-facing text goes through `Lang.T("English text")` / `Lang.F("Text {0}", x)` and into every `lang/*.json` — the tests check it.
- One file, one topic.
- The input path (`Controller.ReadLoop` and what it calls) runs on the highest-priority thread: no allocations, locks or timers there.

## Presets

A preset is a function `(t, i, n) => Rgb`: time, controller index, number of controllers. It must look right for any `n`; use `PresetLib.Ph(i, n)` for a phase offset. The English name is the preset's ID — don't rename existing ones.

## Translations

`lang/<code>.json` maps English text to the translation; keep `{0}` placeholders. New language: `tools\test.ps1 -NewLang xx` writes a template, then add it to `Lang.Languages` in `src/Lang.cs`. To try a translation without rebuilding, put it in `%APPDATA%\Glowstick\lang\`.

## Before a pull request

- Build and tests pass.
- If you touched input, output or game mode, try it on a real controller.

## Releases

Bump `VERSION`, push a matching `vX.Y.Z` tag; CI builds, tests and publishes the release.
