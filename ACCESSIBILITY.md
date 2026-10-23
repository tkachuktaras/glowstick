# Accessibility

Glowstick aims to be usable by people with different abilities, and to work well with the assistive tools and controllers people already have. If something blocks you, please tell us. We treat it as a bug.

## Commitment

- Every page can be used with the keyboard, and controls have readable labels.
- Text can be read in 11 languages (see [`lang/`](lang)).
- The app does not rely on color alone to show status where it matters, such as battery level or errors.
- Motion is limited to the light bar and visual effects you choose; nothing flashes on its own in the UI.

## Supported environments

- Windows 10 and Windows 11.
- DualShock 4 and DualSense controllers over USB or Bluetooth.
- Keyboard navigation in the main window and settings.

## Known limitations

- Glowstick is a desktop app for Windows and does not run on other platforms.
- Some features need a controller: the light bar, rumble, the controller test and the touchpad mouse. Without one, the app shows its settings but cannot demonstrate these.
- Controller-based setup (for example, the controller test) assumes the person can press the buttons and move the sticks.
- Screen reader support has not been fully tested yet. Help with this is welcome.
- Some translations may lag behind the English text.

## Reporting a barrier

Open an issue using the bug report template and label it as an accessibility problem, or describe the barrier in [Discussions](https://github.com/tkachuktaras/glowstick/discussions). Useful details include the screen or feature, what you tried, what you expected, and the assistive tool or controller you use. If the report involves a private detail, use [GitHub Security Advisories](../../security/advisories/new) instead.
