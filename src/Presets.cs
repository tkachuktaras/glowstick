// Colors and the library of light bar presets.
using System;
using System.Collections.Generic;
using System.Drawing;

namespace Glowstick
{
    public struct Rgb
    {
        public double R, G, B;
        public Rgb(double r, double g, double b) { R = r; G = g; B = b; }
        public static Rgb Hex(int h) { return new Rgb((h >> 16) & 255, (h >> 8) & 255, h & 255); }
        public static Rgb Lerp(Rgb a, Rgb b, double k) { return new Rgb(a.R + (b.R - a.R) * k, a.G + (b.G - a.G) * k, a.B + (b.B - a.B) * k); }
        public Rgb Mul(double k) { return new Rgb(R * k, G * k, B * k); }
        public Rgb Clamp() { return new Rgb(C(R), C(G), C(B)); }
        static double C(double v) { if (double.IsNaN(v)) return 0; return v < 0 ? 0 : (v > 255 ? 255 : v); }
        public Color ToColor() { Rgb c = Clamp(); return Color.FromArgb((int)Math.Round(c.R), (int)Math.Round(c.G), (int)Math.Round(c.B)); }
        public static Rgb FromColor(Color c) { return new Rgb(c.R, c.G, c.B); }
    }

    public delegate Rgb ColorFn(double t, int i, int n);

    // Name — the preset ID (English; settings.json and profiles refer to it), Title — for display.
    // Custom != null — a custom preset: the user named it, so the name isn't translated.
    public class Preset
    {
        public string Category; public string Name; public ColorFn Fn;
        public object Custom;   // the user's own preset (Pro module), null — built-in
        public bool Pro;        // comes with Pro: locked until it's purchased
        public bool Music;      // follows the sound (the Presets page shows the music reaction slider with it)
        public bool Locked { get { return (Pro || Custom != null) && !Glowstick.Pro.Unlocked; } }
        public string Title { get { return Custom != null ? Name : Lang.T(Name); } }
        public string CategoryTitle { get { return Lang.T(Category); } }
    }

    public static partial class PresetLib
    {
        const string Pairs = "Pairs & players";
        const string Solid = "Solid colors";
        const string Anim = "Animations";
        const string Grads = "Gradients";
        const string WinCat = "Windows";
        const string LiveCat = "Live (react to game & PC)";

        static Rgb H(int h) { return Rgb.Hex(h); }
        static readonly Rgb Black = new Rgb(0, 0, 0);
        public static double Frac(double x) { return x - Math.Floor(x); }
        public static double Wave(double x) { return 0.5 - 0.5 * Math.Cos(2 * Math.PI * x); }
        public static double Tri(double x) { return 1 - Math.Abs(2 * Frac(x) - 1); }
        public static double Smooth(double f) { return f * f * (3 - 2 * f); }
        static double Pulse(double x, double c, double w) { double d = (x - c) / w; return Math.Exp(-d * d); }

        // Preset name from settings.json → current: old versions stored built-in presets in Ukrainian
        public static string Migrate(string name)
        {
            if (string.IsNullOrEmpty(name) || Engine.Presets.Exists(p => p.Name == name)) return name;
            string en = Lang.FromUk(name);
            return en != null && Engine.Presets.Exists(p => p.Name == en) ? en : name;
        }

        public static Rgb Hsv(double h, double s, double v)
        {
            h = Frac(h) * 6; int i = (int)Math.Floor(h); double f = h - i;
            double p = v * (1 - s), q = v * (1 - s * f), u = v * (1 - s * (1 - f)), r, g, b;
            switch (i % 6)
            {
                case 0: r = v; g = u; b = p; break;
                case 1: r = q; g = v; b = p; break;
                case 2: r = p; g = v; b = u; break;
                case 3: r = p; g = q; b = v; break;
                case 4: r = u; g = p; b = v; break;
                default: r = v; g = p; b = q; break;
            }
            return new Rgb(r * 255, g * 255, b * 255);
        }

        public static Rgb Grad(Rgb[] st, double x)
        {
            x = Frac(x) * st.Length; int a = (int)Math.Floor(x) % st.Length; int b = (a + 1) % st.Length;
            return Rgb.Lerp(st[a], st[b], Smooth(x - Math.Floor(x)));
        }

        public static double Hash(int x)
        {
            unchecked
            {
                uint h = (uint)x * 374761393u + 668265263u;
                h = (h ^ (h >> 13)) * 1274126177u; h ^= h >> 16;
                return (h & 0xFFFFFF) / 16777215.0;
            }
        }

        public static double Noise(double x, int seed)
        {
            int i = (int)Math.Floor(x); double f = Smooth(x - i);
            return Hash(i * 7919 + seed * 104729) * (1 - f) + Hash((i + 1) * 7919 + seed * 104729) * f;
        }

        static Rgb[] Hs(params int[] hex) { Rgb[] r = new Rgb[hex.Length]; for (int k = 0; k < hex.Length; k++) r[k] = H(hex[k]); return r; }

        // Controller phase: evenly around the circle (for two — 0 and 0.5, for three — 0, ⅓, ⅔)
        public static double Ph(int i, int n) { return n > 1 ? i / (double)n : 0; }

        // Battery color: red → green; "breathes" while charging, blinks red at ≤12%
        public static Rgb BatteryColor(int pct, bool charging, double t)
        {
            if (pct < 0) return new Rgb(40, 40, 40);
            Rgb c = Hsv(pct / 100.0 * 0.33, 1, 1);
            if (charging) return c.Mul(0.25 + 0.75 * Wave(t / 2.5));
            if (pct <= 12) return Frac(t * 2) < 0.5 ? H(0xFF0000) : Black;
            return c;
        }

        public static double Heart(double t)
        {
            double p = Frac(t / 1.1);
            return 0.05 + 0.95 * Math.Max(Pulse(p, 0.08, 0.045), 0.7 * Pulse(p, 0.28, 0.05));
        }

        static partial void AddPro(Action<string, string, ColorFn> add);

        public static List<Preset> Build()
        {
            List<Preset> L = new List<Preset>();
            bool pro = false;
            Action<string, string, ColorFn> add = (c, n, f) => L.Add(new Preset { Category = c, Name = n, Fn = f, Pro = pro, Music = n.StartsWith("Music:") });   // "Music: rainbow" (Pro)

            // --- Pairs: 1st color on odd controllers, 2nd on even ones. Players — everyone gets their own, as on the PS4 ---
            Rgb[] players = Hs(0x0030FF, 0xFF0000, 0x00FF00, 0xFF0080, 0xFF8000, 0x00FFFF, 0x8000FF, 0xFFFFFF);
            add(Pairs, "PS4 players (blue, red, green, pink…)", (t, i, n) => players[i % players.Length]);
            add(Pairs, "Players: even rainbow", (t, i, n) => Hsv(Ph(i, n) + 0.62, 1, 1));
            string[] pn = { "Red + Blue", "Blue + Yellow (Ukraine)", "Violet + Turquoise", "Orange + Sky blue",
                            "Green + Pink", "Gold + Purple", "Red + White", "Lime + Magenta",
                            "Cyberpunk: Pink + Cyan", "Fire + Ice", "Blue + Green", "Red + Green",
                            "Teal + Orange", "Pink + Blue", "Mint + Coral" };
            int[] pa = { 0xFF0000, 0x0057FF, 0x8000FF, 0xFF5000, 0x00FF30, 0xFFAA00, 0xFF0000, 0x80FF00, 0xFF0090, 0xFF3000, 0x0030FF, 0xFF0000,
                         0x00FFB0, 0xFF0090, 0x60FFB0 };
            int[] pb = { 0x0020FF, 0xFFD000, 0x00FFC8, 0x00A0FF, 0xFF0080, 0x9000FF, 0xFFFFFF, 0xFF00C0, 0x00F0FF, 0x60C0FF, 0x00FF40, 0x00FF00,
                         0xFF4000, 0x0030FF, 0xFF4030 };
            for (int k = 0; k < pn.Length; k++)
            {
                Rgb ca = H(pa[k]), cb = H(pb[k]);
                add(Pairs, pn[k], (t, i, n) => i % 2 == 0 ? ca : cb);
            }

            // --- Solid colors ---
            string[] sn = { "Red", "Crimson", "Coral", "Orange", "Gold", "Yellow", "Lime", "Green", "Mint", "Turquoise", "Cyan", "Ice blue",
                            "Sky blue", "Blue", "PlayStation blue", "Lavender", "Violet", "Purple", "Pink", "White", "Warm white", "Off" };
            int[] sc = { 0xFF0000, 0xFF0030, 0xFF4030, 0xFF4000, 0xFF9000, 0xFFC000, 0x80FF00, 0x00FF00, 0x60FFB0, 0x00FFA0, 0x00FFFF, 0x80E0FF,
                         0x0080FF, 0x0000FF, 0x0030C0, 0x9070FF, 0x8000FF, 0xC000FF, 0xFF0080, 0xFFFFFF, 0xFFB060, 0x000000 };
            for (int k = 0; k < sn.Length; k++)
            {
                Rgb c = H(sc[k]);
                add(Solid, sn[k], (t, i, n) => c);
            }

            // --- Animations: three in the free version, the rest come with Pro (AddPro) ---
            add(Anim, "Rainbow (in sync)", (t, i, n) => Hsv(t / 6, 1, 1));
            add(Anim, "Rainbow breathing", (t, i, n) => Hsv(t / 20 + Ph(i, n), 1, 1).Mul(0.05 + 0.95 * Wave(t / 4)));
            add(Anim, "Fire", (t, i, n) =>
            {
                double k = 0.65 * Noise(t * 7, i * 3) + 0.35 * Noise(t * 19, i * 3 + 1);
                return Rgb.Lerp(H(0xFF1500), H(0xFF9000), k).Mul(0.5 + 0.5 * k);
            });

            // --- Windows colors (free, live): follow the Windows accent color or the wallpaper ---
            add(WinCat, "Windows accent", (t, i, n) => Live.Accent());
            add(WinCat, "Windows wallpaper", (t, i, n) => Live.Wallpaper());

            pro = true;
            int free = L.Count;
            AddPro(add);   // Pro: the other animations, gradients, live presets (no-op without the module)
            // The free build: the same Pro presets as recorded colors (assets\pro-previews.json) — shown locked, never applied
            if (L.Count == free) foreach (Preset p in ProPreviews.Load()) add(p.Category, p.Name, p.Fn);
            return L;
        }
    }
}
