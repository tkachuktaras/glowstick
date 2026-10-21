// Color conversions for the WPF views.
using System;
using System.Windows.Media;

namespace Glowstick
{
    internal static class ColorMath
    {
        // Inverse of PresetLib.Hsv: rgb 0..255 → h 0..1 (exclusive), s and v 0..1
        public static void ToHsv(Rgb c, out double h, out double s, out double v)
        {
            Rgb k = c.Clamp();
            double r = k.R / 255, g = k.G / 255, b = k.B / 255;
            double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b)), d = max - min;
            v = max;
            s = max <= 0 ? 0 : d / max;
            if (d <= 0) { h = 0; return; }
            if (max == r) h = (g - b) / d;
            else if (max == g) h = 2 + (b - r) / d;
            else h = 4 + (r - g) / d;
            h /= 6;
            if (h < 0) h += 1;
            if (h >= 1) h -= 1;
        }

        public static Color ToMedia(Rgb c) { Rgb k = c.Clamp(); return Color.FromRgb((byte)Math.Round(k.R), (byte)Math.Round(k.G), (byte)Math.Round(k.B)); }
    }
}
