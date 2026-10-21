// Tray icon with a battery: 1–4 segments, colored from red to green (the top of a segment is lighter).
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace Glowstick
{
    internal static class BatteryIcon
    {
        [DllImport("user32.dll")] static extern bool DestroyIcon(IntPtr h);

        static readonly Color[] Main = { Color.FromArgb(255, 0, 0), Color.FromArgb(234, 80, 0), Color.FromArgb(232, 148, 30), Color.FromArgb(0, 200, 30) };
        static readonly Color[] Light = { Color.FromArgb(255, 80, 80), Color.FromArgb(240, 130, 80), Color.FromArgb(238, 178, 92), Color.FromArgb(70, 215, 90) };

        // 0–25% → 1 segment, 26–50% → 2, 51–75% → 3, more → 4
        public static int Segments(int percent) { return percent <= 25 ? 1 : percent <= 50 ? 2 : percent <= 75 ? 3 : 4; }

        // Draw at 32×32 — Windows scales it down to the tray size
        public static Icon Make(int segments)
        {
            const int S = 32;
            using (Bitmap bmp = new Bitmap(S, S))
            {
                using (Graphics g = Graphics.FromImage(bmp))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    Rectangle body = new Rectangle(1, 7, 27, 18);
                    using (GraphicsPath p = Rounded(body, 4))
                    {
                        using (Brush bg = new SolidBrush(Color.FromArgb(248, 248, 248))) g.FillPath(bg, p);
                        using (Pen pen = new Pen(Color.Black, 2.5f)) g.DrawPath(pen, p);
                    }
                    g.FillRectangle(Brushes.Black, 28, 12, 3, 8);   // the terminal nub

                    g.SmoothingMode = SmoothingMode.None;
                    int c = Math.Max(1, Math.Min(4, segments)) - 1;
                    using (Brush main = new SolidBrush(Main[c]), light = new SolidBrush(Light[c]))
                        for (int k = 0; k <= c; k++)
                        {
                            int x = 3 + k * 6;
                            g.FillRectangle(light, x, 10, 5, 5);
                            g.FillRectangle(main, x, 15, 5, 7);
                        }
                }
                IntPtr h = bmp.GetHicon();
                Icon icon = (Icon)Icon.FromHandle(h).Clone();
                DestroyIcon(h);
                return icon;
            }
        }

        static GraphicsPath Rounded(Rectangle r, int radius)
        {
            int d = radius * 2;
            GraphicsPath p = new GraphicsPath();
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }
    }
}
