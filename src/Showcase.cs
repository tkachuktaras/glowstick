// Hidden showcase mode for the README screenshots — not in the UI, only a command-line switch:
//   Glowstick.exe --showcase                 the app on demo data only: four made-up controllers (no real one is opened or
//                                            hidden, no drivers, no admin rights), settings.json is neither read nor written,
//                                            Pro unlocked with a made-up key (a Pro build), nothing checked online
//   Glowstick.exe --showcase-shots <folder>  the same, then every page and the hidden menus / dialogs are rendered to PNGs
//                                            in that folder and the app exits (build.ps1 -Screenshots; ShowcaseShots)
// It never touches the real Glowstick: it runs next to it (no single-instance check) and logs to %TEMP%.
using System;
using System.Collections.Generic;
using System.IO;

namespace Glowstick
{
    internal static class Showcase
    {
        public static bool On { get; private set; }
        public static string ShotsDir { get; private set; }   // --showcase-shots: where the PNGs go; null — interactive
        public static string LogDir { get { return Path.Combine(Path.GetTempPath(), "Glowstick-showcase"); } }

        // The made-up controllers: every model and connection, each with its own name, preset, charge and battery history.
        // The charge is what a real one reports: a DualShock 4 in 1/8 steps (1/11 while charging), a DualSense in 10% steps.
        // Gold has just been connected: no drain estimate, no history yet. For the one on the charger DrainPerHour is its charge rate.
        internal class DemoPad
        {
            public string Id, Name, Preset; public bool DualSense, Bluetooth, Charging; public int Battery, Rate; public double DrainPerHour;
            public double DriftL = -1;   // the left stick rests this far off center (the drift warning on its card)
        }

        public static readonly DemoPad[] Pads =
        {
            new DemoPad { Id = "A4AE12F0C3D1", Name = "Jet Black", Preset = "PlayStation blue", Bluetooth = true, Battery = 87, DrainPerHour = 12, Rate = 286 },
            new DemoPad { Id = "1C66FA2B9E40", Name = "Gold", Preset = "Fire", Bluetooth = true, Battery = 37, DrainPerHour = -1, Rate = 261, DriftL = 0.09 },
            new DemoPad { Id = "E8478B11D2A7", Name = "DualSense White", Preset = "Rainbow (offset)", DualSense = true, Battery = 65, Charging = true, DrainPerHour = 30, Rate = 250 },
            new DemoPad { Id = "58A1C3E0F655", Name = "Midnight", Preset = "Police (smooth)", DualSense = true, Bluetooth = true, Battery = 15, DrainPerHour = 11.4, Rate = 248 },
        };

        public const string GlobalPreset = "Rainbow breathing";

        // A made-up key, shown masked on the Settings page (License.ShowcaseKey)
        public const string Key = "4F1C9A2E-7B3D4E8A-9E12C6F0-58D1B7A3";

        public static void Parse(string[] args)
        {
            for (int i = 0; i < args.Length; i++)
            {
                if (string.Equals(args[i], "--showcase", StringComparison.OrdinalIgnoreCase)) On = true;
                if (string.Equals(args[i], "--showcase-shots", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                {
                    On = true;
                    ShotsDir = Path.GetFullPath(args[++i]);
                }
            }
        }

        // What Settings.Load reads instead of settings.json; the Pro module adds its part (profiles, macros, own presets…)
        public static Dictionary<string, object> Settings()
        {
            List<object> order = new List<object>();
            Dictionary<string, object> pads = new Dictionary<string, object>();
            foreach (DemoPad p in Pads)
            {
                order.Add(p.Id);
                pads[p.Id] = new Dictionary<string, object> { { "Name", p.Name }, { "Preset", p.Preset } };   // the Pro module moves the presets into the pads' profiles
            }
            Dictionary<string, object> s = new Dictionary<string, object>
            {
                { "Language", "en" }, { "Preset", GlobalPreset }, { "Brightness", 1.0 }, { "Speed", 1.0 },
                { "TouchMouse", true }, { "CheckUpdates", true }, { "Order", order }, { "Pads", pads }
            };
            if (Pro.ShowcaseSettings != null) Pro.ShowcaseSettings(s);
            return s;
        }

        public static List<Controller> Controllers()
        {
            List<Controller> list = new List<Controller>();
            foreach (DemoPad p in Pads) list.Add(Controller.Demo(p));
            return list;
        }

        static readonly Random jitter = new Random(7);

        // Input reports per second, a little uneven as real ones are
        public static int Rate(Controller c)
        {
            DemoPad p = Array.Find(Pads, x => x.Id == c.Id);
            return p == null ? 0 : p.Rate + jitter.Next(-4, 5);
        }

        // A made-up input report (63 bytes, the DS4 USB layout — Pad.Parse): sticks circling, triggers half / fully pulled,
        // ✕ and R1 held, a finger on the touchpad, the controller tilted a little. Different for every controller.
        public static void Report(Controller c, byte[] raw)
        {
            int k = Math.Max(0, Array.FindIndex(Pads, x => x.Id == c.Id));
            double t = Engine.Ms / 1000.0 + k * 1.7;
            Array.Clear(raw, 0, raw.Length);
            raw[0] = Axis(0.55 * Math.Cos(t * 0.8)); raw[1] = Axis(0.55 * Math.Sin(t * 0.8));
            raw[2] = Axis(0.8 * Math.Cos(-t * 1.3 + 2)); raw[3] = Axis(0.8 * Math.Sin(-t * 1.3 + 2));
            raw[4] = 0x20 | 0x08;                 // ✕ held, d-pad released (hat 8)
            raw[5] = 0x02;                        // R1
            raw[6] = (byte)((Engine.Ms / 5 % 64) << 2);   // report counter
            raw[7] = (byte)(110 + 60 * Math.Sin(t * 0.9));
            raw[8] = 230;
            Short(raw, 12, (int)(900 * Math.Sin(t * 1.1))); Short(raw, 14, (int)(-600 * Math.Cos(t * 0.7))); Short(raw, 16, (int)(300 * Math.Sin(t * 1.9)));
            Short(raw, 18, (int)(1400 * Math.Sin(t * 0.6))); Short(raw, 20, 8000); Short(raw, 22, (int)(1800 * Math.Cos(t * 0.6)));
            int tx = (int)(960 + 520 * Math.Cos(t * 0.7)), ty = (int)(470 + 230 * Math.Sin(t * 1.4));
            raw[34] = 0x05;                       // finger 1 down (bit 7 clear), id 5
            raw[35] = (byte)tx; raw[36] = (byte)((tx >> 8 & 0x0F) | (ty & 0x0F) << 4); raw[37] = (byte)(ty >> 4);
            raw[38] = 0x80;                       // no second finger
        }

        static byte Axis(double v) { return (byte)Math.Max(0, Math.Min(255, Math.Round(127.5 + v * 127.5))); }
        static void Short(byte[] raw, int i, int v) { raw[i] = (byte)v; raw[i + 1] = (byte)(v >> 8); }
    }
}
