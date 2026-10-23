// PS hotkeys: which button combo, held together with PS, does what. Changed in Settings (editing is Pro; the free build
// keeps the defaults); stored in settings.json ("HotkeyMap"). A combo is one or more buttons: the action fires when the
// whole combo is held while PS is down. Free uses single-button defaults; Pro can bind any combo (e.g. L1 + R1).
using System;
using System.Collections.Generic;

namespace Glowstick
{
    internal enum HotAction { PrevPreset, NextPreset, Brighter, Dimmer, ShowBattery, TouchMouse, TurnOff, DesktopMode, Keyboard }

    internal static class HotkeyMap
    {
        public static readonly HotAction[] Actions = (HotAction[])Enum.GetValues(typeof(HotAction));
        public static readonly Btn[] Defaults = { Btn.Left, Btn.Right, Btn.Up, Btn.Down, Btn.Cross, Btn.Square, Btn.Options, Btn.Share, Btn.Triangle };
        // Buttons a combo can use (not PS itself — PS is always the modifier)
        public static readonly Btn[] Choices = { Btn.Up, Btn.Down, Btn.Left, Btn.Right, Btn.Cross, Btn.Circle, Btn.Square, Btn.Triangle,
                                                 Btn.L1, Btn.R1, Btn.L2, Btn.R2, Btn.L3, Btn.R3, Btn.Share, Btn.Options, Btn.Touch };
        static readonly uint ChoiceMask = Combine(Choices);

        static readonly Btn[] keys = (Btn[])Defaults.Clone();   // each is a combo (one or more Choice bits), or None
        static readonly object sync = new object();
        static volatile uint all = Combine(Defaults);   // every button used by any combo — read on the input thread

        public static Btn All { get { return (Btn)all; } }

        public static Btn Get(HotAction a) { lock (sync) return keys[(int)a]; }

        // Assign a combo (one or more Choice buttons, or None). Another action with the exact same combo loses it.
        public static void Set(HotAction a, Btn combo)
        {
            if (((uint)combo & ~ChoiceMask) != 0) return;   // only Choice buttons, never PS
            lock (sync)
            {
                if (combo != Btn.None) for (int k = 0; k < keys.Length; k++) if (keys[k] == combo) keys[k] = Btn.None;
                keys[(int)a] = combo;
                all = Combine(keys);
            }
        }

        public static void Reset()
        {
            lock (sync) { Array.Copy(Defaults, keys, keys.Length); all = Combine(keys); }
        }

        // Actions whose combo just became fully held (was not before). A combo is suppressed while a strict superset
        // combo is also held, so L1 + R1 wins over a lone L1.
        public static List<HotAction> Fired(Btn held, Btn prev)
        {
            List<HotAction> r = new List<HotAction>();
            lock (sync)
                for (int k = 0; k < keys.Length; k++)
                {
                    Btn c = keys[k];
                    if (c == Btn.None || (held & c) != c || (prev & c) == c) continue;   // not held now, or already was
                    bool shadowed = false;
                    for (int j = 0; j < keys.Length && !shadowed; j++)
                        if (j != k && keys[j] != Btn.None && (keys[j] & c) == c && keys[j] != c && (held & keys[j]) == keys[j]) shadowed = true;
                    if (!shadowed) r.Add((HotAction)k);
                }
            return r;
        }

        static uint Combine(Btn[] list) { uint m = 0; foreach (Btn b in list) m |= (uint)b; return m; }

        // How a button is shown in Settings
        // A single button (combos are joined with " + " by ComboName)
        public static string Name(Btn b)
        {
            switch (b)
            {
                case Btn.None: return "—";
                case Btn.Up: return "↑"; case Btn.Down: return "↓"; case Btn.Left: return "←"; case Btn.Right: return "→";
                case Btn.Cross: return "✕"; case Btn.Circle: return "○"; case Btn.Square: return "□"; case Btn.Triangle: return "△";
                case Btn.Touch: return Lang.T("Touchpad click");
                default: return b.ToString();
            }
        }

        // A whole combo, e.g. "L1 + R1" (buttons in the Choices order); "—" when unset
        public static string ComboName(Btn combo)
        {
            if (combo == Btn.None) return "—";
            List<string> parts = new List<string>();
            foreach (Btn b in Choices) if ((combo & b) != 0) parts.Add(Name(b));
            return parts.Count == 0 ? "—" : string.Join(" + ", parts.ToArray());
        }

        // ---------- settings.json: { "PrevPreset": "Left", …, "TurnOff": "L1, R1" } ----------
        public static void Load(object v)
        {
            Dictionary<string, object> d = v as Dictionary<string, object>;
            if (d == null) return;
            lock (sync)
            {
                foreach (HotAction a in Actions)
                {
                    object o; Btn b;
                    if (d.TryGetValue(a.ToString(), out o) && o is string && Enum.TryParse((string)o, out b)
                        && ((uint)b & ~ChoiceMask) == 0) keys[(int)a] = b;
                }
                // A hand-edited file with one combo twice: the later action loses it
                for (int k = 0; k < keys.Length; k++)
                    for (int j = 0; j < k; j++) if (keys[k] != Btn.None && keys[k] == keys[j]) keys[k] = Btn.None;
                all = Combine(keys);
            }
        }

        public static Dictionary<string, object> Save()
        {
            Dictionary<string, object> d = new Dictionary<string, object>();
            lock (sync) foreach (HotAction a in Actions) d[a.ToString()] = keys[(int)a].ToString();
            return d;
        }
    }
}
