<div align="center">

# Glowstick

**Light bar control for DualShock 4 and DualSense on Windows.**

[![Latest release](https://img.shields.io/github/v/release/tkachuktaras/glowstick?style=flat-square)](https://github.com/tkachuktaras/glowstick/releases/latest)
[![Downloads](https://img.shields.io/github/downloads/tkachuktaras/glowstick/total?style=flat-square)](https://github.com/tkachuktaras/glowstick/releases)
[![Build](https://img.shields.io/github/actions/workflow/status/tkachuktaras/glowstick/build.yml?branch=main&style=flat-square)](https://github.com/tkachuktaras/glowstick/actions/workflows/build.yml)
[![License: GPL-3.0](https://img.shields.io/github/license/tkachuktaras/glowstick?style=flat-square)](LICENSE)

[**Download**](https://github.com/tkachuktaras/glowstick/releases/latest) · [Features](#features) · [How it works](#how-it-works) · [Building](#building)

<img src="assets/readme/demo.gif" alt="Two DualShock 4 controllers changing light bar colors with Glowstick" width="800">

</div>

Colors and animations for your controllers' light bars, their battery in the tray, the touchpad as a mouse — and games that see the controller without fighting over it.

## Download

Get `Glowstick.exe` from the [latest release](https://github.com/tkachuktaras/glowstick/releases/latest) and run it. On the first start it offers to install the two drivers it needs.

Needs Windows 10/11 and a DualShock 4 or DualSense (USB or Bluetooth).

## Screenshots

| Presets | Controllers |
|---|---|
| <img src="assets/readme/presets.png" alt="Presets page with controller tiles and animated preset cards"> | <img src="assets/readme/controllers.png" alt="Controllers page with names, own presets and battery history"> |
| **Controller test** | **Settings** |
| <img src="assets/readme/controller-test.png" alt="Controller test with buttons, sticks, triggers and gyro"> | <img src="assets/readme/settings.png" alt="Settings page"> |

## Features

### Free

- **Presets:** solid colors, pairs, player colors and three animations, with brightness and speed.
- **Battery in the tray**, with a warning when it runs low.
- **Hotkeys (hold PS):** switch presets, change brightness, show the battery, toggle the touchpad mouse, turn a Bluetooth controller off.
- **Touchpad as mouse:** cursor, scrolling, tap to click, drag; turns itself off in fullscreen games.
- **DualSense:** light bar, rumble, battery, touchpad, gyro and the player LEDs.
- **Controller test:** buttons, sticks, triggers, touchpad, gyro, rumble.
- **Updates** in one click.

One controller in the free version.

### Glowstick Pro

- **Any number of controllers**, with names, their own presets and brightness.
- **All animations and live presets** that react to music, the screen, the battery, the triggers and the sticks.
- **Your own presets**, shareable as files.
- **Profiles** for every controller and game, switched automatically: light bar, input mapping, touchpad; import and export as files.
- **Input mapping** on par with DS4Windows: remap buttons, sticks, triggers, the touchpad and motion to buttons, keys, the mouse, macros or actions; dead zones and curves; gyro aiming; flick stick.
- **Battery history** and wear warning.
- **[Local API](docs/local-api.md)** for Stream Deck, Home Assistant, OBS and scripts.

The free version and Pro share their settings, so moving to Pro keeps everything.

## How it works

A game and Glowstick talking to the same controller get in each other's way. So Glowstick hides the real controller from games ([HidHide](https://github.com/nefarius/HidHide)) and gives them a virtual one instead ([ViGEmBus](https://github.com/nefarius/ViGEmBus)) — a DualShock 4 or, in Pro, an Xbox 360. That's why it needs the two drivers and administrator rights. When Glowstick exits, games see the real controllers again.

To remove everything: tray → **Remove drivers and settings…**

## Translations

English, Українська, Deutsch, Français, Español, Português (Brasil), Italiano, Nederlands, 日本語, 한국어, 简体中文 — see [`lang/`](lang).

## Building

```powershell
powershell -ExecutionPolicy Bypass -File build.ps1        # → dist\Glowstick.exe
powershell -ExecutionPolicy Bypass -File tools\test.ps1   # tests
```

Details in [CONTRIBUTING.md](CONTRIBUTING.md).

## License

[GNU GPL v3](LICENSE) or later. Third-party components: [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).

Not affiliated with Sony Interactive Entertainment. “DualShock”, “DualSense” and “PlayStation” are trademarks of Sony Interactive Entertainment Inc.
