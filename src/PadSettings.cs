// The light bar and power part of a controller's profile: own preset, brightness, what games see, notification flashes,
// the DualSense player LEDs, idle dimming and the Bluetooth idle turn-off. The Default profile's values are the app-wide
// ones (Engine.Defaults — the free build has only these); a Pro profile with its own settings brings its own (Pro.PadSettingsFor).
using System;
using System.Collections.Generic;

namespace Glowstick
{
    public class PadSettings
    {
        public string Preset = "";         // own preset (name); "" — the one chosen on the Presets page. Unused in Engine.Defaults.
        public double Brightness = 1.0;    // 0.1 .. 1 — on top of the global brightness. Unused in Engine.Defaults.
        public string Output = "";         // "Xbox" — games see a virtual Xbox 360, else a DualShock 4. Unused in Engine.Defaults.
        public bool NotifyFlash = true;    // connected — white, low battery — red
        public bool PlayerLeds = true;     // DualSense: the controller's number on the player LEDs
        public int IdleDimMin = 10;        // turn the light bar off after this many minutes untouched; 0 — never
        public int IdleOffMin = 30;        // turn a Bluetooth controller off after this many minutes untouched; 0 — never
        public bool ChargeShow = true;     // on the charger and put down: the light bar shows the charge (Engine.Overlays)
        public string MuteLed = "mic";     // DualSense Mute light (Pro): "" left alone, mic (the PC mic muted — and the button switches it), battery, touch, desktop
        public bool HapticRumble;          // DualSense (Pro): the newer, sharper rumble emulation

        public static readonly string[] MuteLeds = { "", "mic", "battery", "touch", "desktop" };

        public PadSettings Clone() { return (PadSettings)MemberwiseClone(); }

        public Dictionary<string, object> Save()
        {
            return new Dictionary<string, object>
            {
                { "Preset", Preset }, { "Brightness", Brightness }, { "Output", Output }, { "NotifyFlash", NotifyFlash },
                { "PlayerLeds", PlayerLeds }, { "IdleDimMin", IdleDimMin }, { "IdleOffMin", IdleOffMin }, { "ChargeShow", ChargeShow },
                { "MuteLed", MuteLed }, { "HapticRumble", HapticRumble }
            };
        }

        public static PadSettings Load(object o)
        {
            PadSettings s = new PadSettings();
            Dictionary<string, object> d = o as Dictionary<string, object>;
            if (d == null) return s;
            object v;
            try
            {
                if (d.TryGetValue("Preset", out v) && v is string) s.Preset = PresetLib.Migrate((string)v);
                if (d.TryGetValue("Brightness", out v)) s.Brightness = Math.Max(0.1, Math.Min(1.0, Convert.ToDouble(v)));
                if (d.TryGetValue("Output", out v) && v is string) s.Output = (string)v == "Xbox" ? "Xbox" : "";
                if (d.TryGetValue("NotifyFlash", out v)) s.NotifyFlash = Convert.ToBoolean(v);
                if (d.TryGetValue("PlayerLeds", out v)) s.PlayerLeds = Convert.ToBoolean(v);
                if (d.TryGetValue("IdleDimMin", out v)) s.IdleDimMin = Math.Max(0, Convert.ToInt32(v));
                if (d.TryGetValue("IdleOffMin", out v)) s.IdleOffMin = Math.Max(0, Convert.ToInt32(v));
                if (d.TryGetValue("ChargeShow", out v)) s.ChargeShow = Convert.ToBoolean(v);
                if (d.TryGetValue("MuteLed", out v) && v is string && Array.IndexOf(MuteLeds, (string)v) >= 0) s.MuteLed = (string)v;
                if (d.TryGetValue("HapticRumble", out v)) s.HapticRumble = Convert.ToBoolean(v);
            }
            catch { }
            return s;
        }
    }
}
