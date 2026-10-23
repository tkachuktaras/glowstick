// An own music preset's settings on top of the app-wide ones (Pro): how strongly it reacts and which frequencies it follows
// (set in the preset editor). Anything unset (NaN / "") is the app-wide value — the Presets page's music sliders.
// Kept by preset name in settings.json ("PresetTune"). Brightness and speed are not kept per preset any more: the brightness
// belongs to the controller, the speed to an own preset itself (CustomPreset.Speed) — older files' values are dropped.
using System;
using System.Collections.Generic;

namespace Glowstick
{
    public class PresetTune
    {
        public double Reaction = double.NaN;     // 0.5 .. 3 — Live.MusicReaction
        public string Band = "";                 // "" — app-wide; else one of MusicBands.Ids
        public double Low = 30, High = 150;      // Hz, for the "custom" band

        public bool Empty { get { return double.IsNaN(Reaction) && Band == ""; } }

        public PresetTune Clone() { return (PresetTune)MemberwiseClone(); }

        public Dictionary<string, object> Save()
        {
            Dictionary<string, object> d = new Dictionary<string, object>();
            if (!double.IsNaN(Reaction)) d["Reaction"] = Reaction;
            if (Band != "") { d["Band"] = Band; d["Low"] = Low; d["High"] = High; }
            return d;
        }

        public static PresetTune Load(object o)
        {
            PresetTune t = new PresetTune();
            Dictionary<string, object> d = o as Dictionary<string, object>;
            if (d == null) return t;
            object v;
            try
            {
                if (d.TryGetValue("Reaction", out v)) t.Reaction = Math.Max(0.5, Math.Min(3, Convert.ToDouble(v)));
                if (d.TryGetValue("Band", out v) && v is string && Array.IndexOf(MusicBands.Ids, (string)v) >= 0) t.Band = (string)v;
                if (d.TryGetValue("Low", out v)) t.Low = Math.Max(20, Math.Min(18000, Convert.ToDouble(v)));
                if (d.TryGetValue("High", out v)) t.High = Math.Max(t.Low + 10, Math.Min(20000, Convert.ToDouble(v)));
            }
            catch { }
            return t;
        }
    }

    // Which part of the sound music presets follow
    public static class MusicBands
    {
        public static readonly string[] Ids = { "bass", "mid", "high", "all", "custom" };

        public static string Title(string id)
        {
            switch (id)
            {
                case "bass": return Lang.T("Bass and kick (30–150 Hz)");
                case "mid": return Lang.T("Vocals and instruments (250–2000 Hz)");
                case "high": return Lang.T("Hi-hats and cymbals (4–16 kHz)");
                case "all": return Lang.T("All of the sound");
                default: return Lang.T("Own range");
            }
        }

        // The band's edges in Hz; 0, 0 — all of the sound (the peak meter)
        public static void Range(string id, double low, double high, out double lo, out double hi)
        {
            switch (id)
            {
                case "bass": lo = 30; hi = 150; return;
                case "mid": lo = 250; hi = 2000; return;
                case "high": lo = 4000; hi = 16000; return;
                case "custom": lo = low; hi = high; return;
                default: lo = 0; hi = 0; return;
            }
        }
    }

    public static class PresetTunes
    {
        // Copy-on-write: the light bar loop reads it every frame without locking
        static volatile Dictionary<string, PresetTune> map = new Dictionary<string, PresetTune>();

        public static PresetTune Get(string preset)
        {
            PresetTune t;
            return preset != null && map.TryGetValue(preset, out t) ? t : null;
        }

        // An empty tune (or null) removes the preset's own settings
        public static void Set(string preset, PresetTune t)
        {
            Dictionary<string, PresetTune> m = new Dictionary<string, PresetTune>(map);
            if (t == null || t.Empty) m.Remove(preset); else m[preset] = t;
            map = m;
        }

        // A custom preset was renamed (to) or deleted (to == null)
        public static void Rename(string from, string to)
        {
            PresetTune t = Get(from);
            if (t == null) return;
            Set(from, null);
            if (to != null) Set(to, t);
        }

        public static Dictionary<string, object> Save()
        {
            Dictionary<string, object> d = new Dictionary<string, object>();
            foreach (KeyValuePair<string, PresetTune> kv in map) d[kv.Key] = kv.Value.Save();
            return d;
        }

        public static void Load(object o)
        {
            Dictionary<string, PresetTune> m = new Dictionary<string, PresetTune>();
            Dictionary<string, object> d = o as Dictionary<string, object>;
            if (d != null) foreach (KeyValuePair<string, object> kv in d) { PresetTune t = PresetTune.Load(kv.Value); if (!t.Empty) m[PresetLib.Migrate(kv.Key)] = t; }
            map = m;
        }
    }
}
