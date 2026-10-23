// Pure helpers for the WPF views: battery level and texts, tile layout, preset titles. No WPF types — unit-tested.
using System;
using System.Collections.Generic;

namespace Glowstick
{
    internal enum BatteryLevel { Unknown, Ok, Low, Critical }

    internal static class UiFormat
    {
        // Same thresholds as the tile colors in the spec: green ≥ 50 %, yellow < 50 %, red < 25 %
        public static BatteryLevel Level(int percent)
        {
            return percent < 0 ? BatteryLevel.Unknown : percent < 25 ? BatteryLevel.Critical : percent < 50 ? BatteryLevel.Low : BatteryLevel.Ok;
        }

        // Battery time left with its units: "~4 h 25 min", "~2 h", "~40 min" (translated); "" — no estimate, or under a minute
        public static string Hours(double h)
        {
            int total = h > 0 ? (int)Math.Round(h * 60) : 0;
            if (total < 1) return "";
            int hh = total / 60, mm = total % 60;
            return "~" + (hh == 0 ? Lang.F("{0} min", mm) : mm == 0 ? Lang.F("{0} h", hh) : Lang.F("{0} h {1:00} min", hh, mm));
        }

        // Battery and connection: "82% ⚡ · USB · ~1 h 10 min to full", "45% · BT · ~3 h 15 min", "BT". compact (a tile, too
        // narrow for it) — no time to full on the charger
        public static string PadLine(ControllerInfo c, bool compact = false)
        {
            string kind = c.Kind == "Bluetooth" ? "BT" : "USB";
            if (c.Battery < 0) return kind;
            string s = c.Battery + "%" + (c.Charging ? " ⚡" : "") + " · " + kind;
            string left = compact ? (c.Charging ? "" : Hours(c.HoursLeft)) : TimeLeft(c.Charging, c.HoursLeft, c.HoursToFull);
            return left != "" ? s + " · " + left : s;
        }

        // "~3 h 15 min" off the charger, "~1 h 10 min to full" on it; "" — not known yet
        public static string TimeLeft(bool charging, double hoursLeft, double hoursToFull)
        {
            if (!charging) return Hours(hoursLeft);
            string full = Hours(hoursToFull);
            return full == "" ? "" : Lang.F("{0} to full", full);
        }

        // Up to 4 controllers — one row of 4 slots; more — two rows (as the classic window did)
        public static int TileColumns(int count) { return count <= 4 ? 4 : Math.Max(4, (count + 1) / 2); }

        // Quick colors under A/B: left click — A, right click — B
        public static readonly string[] QuickColors = { "#FF0000", "#FF0040", "#FF5000", "#FFC000", "#80FF00", "#00FF00", "#00FFA0", "#00FFFF", "#0080FF", "#0020FF", "#8000FF", "#FF00FF", "#FF0080", "#FFFFFF", "#FFB060" };

        // Preset categories in list order; with Pro — "My presets" first (it holds the "+ New preset" card even when empty)
        public static List<string> Categories(List<Preset> list)
        {
            List<string> cats = new List<string>();
            cats.Add(Pro.MyPresets);   // without Pro it's shown with a lock
            foreach (Preset p in list) if (!cats.Contains(p.Category)) cats.Add(p.Category);
            return cats;
        }

        // Colors of a preset card's stripes at time t: stripe i = controller i of n. A throwing or broken preset → black.
        public static Rgb[] SwatchColors(ColorFn fn, double t, int n)
        {
            Rgb[] r = new Rgb[n];
            for (int i = 0; i < n; i++)
            {
                Rgb c;
                try { c = fn(t, i, n); } catch { c = new Rgb(0, 0, 0); }
                r[i] = new Rgb(Ok(c.R), Ok(c.G), Ok(c.B));
            }
            return r;
        }

        static double Ok(double v) { return double.IsNaN(v) ? 0 : Math.Max(0, Math.Min(255, v)); }

        public static string PresetTitle(string name)
        {
            Preset p = Engine.Presets.Find(x => x.Name == name);
            return p != null ? p.Title : name;
        }
    }
}
