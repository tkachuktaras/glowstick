# Local API

Glowstick Pro can run a small HTTP server on your own PC so Stream Deck, Home Assistant, OBS or a script
can read status, switch presets and set colors and effects live — nothing you do in a game or in Windows needs
to change. Turn it on in **Settings → Local API**; that screen shows the port and the token to use.

> The Local API is part of **Glowstick Pro**. The free version shows the screen locked.

- Loopback only: it listens on `127.0.0.1`, so nothing outside this PC can reach it.
- Every request needs the token, either as the header `X-Glowstick-Token: <token>` or as `?token=<token>`
  in the URL. Without it (or with the wrong one) you get `401`.
- Turning the token over (the "New token" button) immediately invalidates the old one.
- Bodies and replies are JSON.

## Controllers

Every write below acts on **all** controllers by default. Add `?id=<controller id>` to act on one controller only
(the ids come from `GET /status`); an unknown id replies `404`. A plain press of a preset in the window, or
`POST /preset`/`/color`/`/effect` without an `id`, clears any per-controller override.

## Endpoints

### `GET /status`

```json
{
  "active": "Ocean Flow",
  "brightness": 80,
  "speed": 100,
  "controllers": [
    { "id": "xxxxxxxxxxxx", "name": "", "model": "DualSense", "battery": 76, "charging": false, "virtual": false }
  ]
}
```

### `GET /presets`

```json
[
  { "name": "Red + Blue", "category": "Solid colors" },
  { "name": "Ocean Flow", "category": "My presets" }
]
```

### `POST /preset`

```json
{ "name": "Ocean Flow" }
```

Switches to the preset with that name (case-insensitive). Replies with the same body as `/status`.
`404` if no preset has that name.

### `POST /brightness`

```json
{ "value": 80 }
```

`value` is 0–100, clamped. Replies with `/status`.

### `POST /speed`

```json
{ "value": 100 }
```

`value` is 10–400 (percent of normal animation speed), clamped. Replies with `/status`.

### `POST /color`

A solid color, applied live. It is **not** saved as a preset; selecting a preset in the window (or `POST /preset`)
puts things back.

```json
{ "r": 255, "g": 32, "b": 96 }
```

or a hex string:

```json
{ "hex": "#FF2060" }
```

`r`/`g`/`b` are 0–255 (clamped); `hex` is `#RRGGBB` (the `#` is optional). Missing or malformed color → `400`.
Replies with `/status`. Add `?id=` for one controller.

### `POST /effect`

An animated effect, applied live (also not saved as a preset).

```json
{ "effect": "gradient", "hex": "#FF2060", "hex2": "#2060FF", "speed": 150 }
```

| `effect` | Colors used | What it does |
|---|---|---|
| `solid` | `hex` | a steady color (same as `/color`) |
| `breathing` | `hex` | one color fading in and out |
| `blink` | `hex` | one color on/off |
| `gradient` | `hex`, `hex2` | fades back and forth between two colors |
| `rainbow` | — | the whole hue wheel; spread across several controllers |

- Colors are optional (`hex`/`r`,`g`,`b` for the first, `hex2` for the second); sensible defaults are used when omitted.
- `speed` is 10–400 (percent, optional, default 100) and is independent of the global `/speed`.
- Unknown `effect` → `400`. Replies with `/status`. Add `?id=` for one controller.

## Example

```bash
# turn every controller red
curl -s http://127.0.0.1:7777/color \
  -H "X-Glowstick-Token: <token>" -H "Content-Type: application/json" \
  -d "{\"hex\":\"#FF0000\"}"

# a breathing purple on one controller
curl -s "http://127.0.0.1:7777/effect?id=xxxxxxxxxxxx" \
  -H "X-Glowstick-Token: <token>" -H "Content-Type: application/json" \
  -d "{\"effect\":\"breathing\",\"hex\":\"#A020F0\",\"speed\":120}"
```
