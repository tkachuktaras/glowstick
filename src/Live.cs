// Data for "live" presets: the current controller's state, system audio level, average screen color.
// A preset only gets (t, i, n), so before calling it the engine puts here the controller the color is computed for.
// Outside the engine (list thumbnails) Cur == null — presets draw a demo.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace Glowstick
{
    internal static class Live
    {
        [ThreadStatic] public static Controller Cur;
        [ThreadStatic] public static Pad Pad;

        static readonly Stopwatch clock = Stopwatch.StartNew();

        // ---------- audio ----------
        [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")] class MMDeviceEnumeratorCom { }

        [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IMMDeviceEnumerator
        {
            [PreserveSig] int EnumAudioEndpoints(int dataFlow, int stateMask, out IntPtr devices);
            [PreserveSig] int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice endpoint);
        }

        [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IMMDevice
        {
            [PreserveSig] int Activate(ref Guid iid, int clsCtx, IntPtr activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object iface);
        }

        [ComImport, Guid("C02216F6-8C67-4B5B-9D00-D008E73E0064"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IAudioMeterInformation { [PreserveSig] int GetPeakValue(out float peak); }

        static IAudioMeterInformation meter;
        static double meterAt = -10;
        static readonly Dictionary<string, AudioState> audio = new Dictionary<string, AudioState>();   // light bar thread only

        // App-wide music settings (Pro, settings.json "MusicReaction", "MusicBand", "MusicLow/High"); a preset can have its own (PresetTune)
        public static double MusicReaction = 1.0;     // 0.5 — soft, 1 — normal, up to 3 — only the hits light up
        public static string MusicBand = "bass";      // MusicBands.Ids
        public static double MusicLow = 30, MusicHigh = 150;

        // The preset being drawn: its own reaction and band (Engine sets them before calling the preset), else the app-wide ones
        [ThreadStatic] public static PresetTune Tune;

        // Level 0..1 for music presets. Polled at most once every 8 ms per band and reaction.
        public static double Audio()
        {
            PresetTune t = Tune;
            double reaction = t != null && !double.IsNaN(t.Reaction) ? t.Reaction : MusicReaction;
            double lo, hi;
            if (t != null && t.Band != "") MusicBands.Range(t.Band, t.Low, t.High, out lo, out hi);
            else MusicBands.Range(MusicBand, MusicLow, MusicHigh, out lo, out hi);
            string key = lo + "-" + hi + "-" + reaction;
            AudioState s;
            if (!audio.TryGetValue(key, out s)) { if (audio.Count > 32) audio.Clear(); s = new AudioState(); audio[key] = s; }   // a slider being dragged makes new ones
            double now = clock.Elapsed.TotalSeconds;
            if (now - s.At < 0.008) return s.Level;
            double dt = Math.Min(0.1, now - s.At); s.At = now;
            double peak = hi > 0 ? AudioCapture.Peak(lo, hi) : -1;   // a frequency band — from the loopback capture
            if (peak < 0)   // all of the sound, or the capture doesn't work here — the device's peak meter
            {
                if (meter == null || now - meterAt > 5) OpenMeter(now);   // every 5 s — in case the output device changed
                float p = 0;
                try { if (meter != null && meter.GetPeakValue(out p) != 0) p = 0; } catch { meter = null; }
                peak = p;
            }
            return AudioStep(s, peak, dt, reaction);
        }

        internal sealed class AudioState { public double Level, Avg, Max = 0.1, At; }

        // Music is mostly loud all the time (mastering squeezes it), so the peak against the loudest recent moment hardly moves.
        // What the eye follows is how far this moment stands out above the song's running loudness — beats and accents:
        // the level is the peak's height between a floor just under the running average (~1 s) and the loudest recent peak.
        // Quiet songs pump too (the scale follows the song). reaction sharpens the contrast and the fade: higher — dark between
        // the hits (a kick at 120 BPM: between them ~0.46 at 0.5, ~0.23 at 1, ~0.07 at 2), lower — a softer glow.
        internal static double AudioStep(AudioState s, double peak, double dt, double reaction)
        {
            reaction = Math.Max(0.25, reaction);
            s.Avg += (peak - s.Avg) * (1 - Math.Exp(-dt / 1.0));
            s.Max = Math.Max(peak, Math.Max(0.05, s.Max * Math.Pow(0.8, dt)));   // slowly forgets loud moments
            double floor = s.Avg * 0.9;
            double x = Math.Max(0, Math.Min(1, (peak - floor) / Math.Max(0.02, s.Max - floor)));
            double target = Math.Pow(x, 1.6 * reaction);
            s.Level = target > s.Level ? target : s.Level * Math.Pow(0.003, dt * reaction);   // instant attack, a quick fade (~0.2 s at 1)
            return s.Level;
        }

        static void OpenMeter(double now)
        {
            meterAt = now;
            try
            {
                IMMDeviceEnumerator en = (IMMDeviceEnumerator)new MMDeviceEnumeratorCom();
                IMMDevice dev;
                if (en.GetDefaultAudioEndpoint(0 /* eRender */, 1 /* eMultimedia */, out dev) != 0) { meter = null; return; }
                Guid iid = typeof(IAudioMeterInformation).GUID; object o;
                meter = dev.Activate(ref iid, 23 /* CLSCTX_ALL */, IntPtr.Zero, out o) == 0 ? (IAudioMeterInformation)o : null;
            }
            catch { meter = null; }
        }

        // ---------- screen ----------
        [DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr hwnd);
        [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr hwnd, IntPtr dc);
        [DllImport("gdi32.dll")] static extern IntPtr CreateCompatibleDC(IntPtr dc);
        [DllImport("gdi32.dll")] static extern IntPtr CreateCompatibleBitmap(IntPtr dc, int w, int h);
        [DllImport("gdi32.dll")] static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);
        [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr obj);
        [DllImport("gdi32.dll")] static extern bool DeleteDC(IntPtr dc);
        [DllImport("gdi32.dll")] static extern int SetStretchBltMode(IntPtr dc, int mode);
        [DllImport("gdi32.dll")] static extern bool StretchBlt(IntPtr dst, int x, int y, int w, int h, IntPtr src, int sx, int sy, int sw, int sh, int rop);
        [DllImport("gdi32.dll")] static extern int GetDIBits(IntPtr dc, IntPtr bmp, int start, int lines, int[] bits, ref BITMAPINFOHEADER bi, int usage);

        [StructLayout(LayoutKind.Sequential)]
        struct BITMAPINFOHEADER
        {
            public int biSize, biWidth, biHeight; public short biPlanes, biBitCount;
            public int biCompression, biSizeImage, biXPelsPerMeter, biYPelsPerMeter, biClrUsed, biClrImportant;
            public int pad0, pad1, pad2, pad3;   // room for the color table GetDIBits may write
        }

        static Thread screenThread;
        static double screenWanted;
        static Rgb screen = new Rgb(0, 0, 0);
        static readonly object screenLock = new object();

        // Average screen color with boosted saturation. The capture thread lives while someone asks for the color.
        public static Rgb Screen()
        {
            lock (screenLock)
            {
                screenWanted = clock.Elapsed.TotalSeconds;
                if (screenThread == null)
                {
                    screenThread = new Thread(ScreenLoop) { IsBackground = true, Name = "DS4 screen", Priority = ThreadPriority.BelowNormal };
                    screenThread.Start();
                }
                return screen;
            }
        }

        static void ScreenLoop()
        {
            const int W = 48, H = 27;
            int[] px = new int[W * H];
            Rgb smooth = screen;
            try
            {
                while (true)
                {
                    lock (screenLock)
                        if (clock.Elapsed.TotalSeconds - screenWanted > 2) { screenThread = null; return; }
                    System.Drawing.Rectangle b = System.Windows.Forms.Screen.PrimaryScreen.Bounds;
                    IntPtr sdc = GetDC(IntPtr.Zero), mdc = CreateCompatibleDC(sdc), bmp = CreateCompatibleBitmap(sdc, W, H);
                    IntPtr old = SelectObject(mdc, bmp);
                    SetStretchBltMode(mdc, 4 /* HALFTONE */);
                    StretchBlt(mdc, 0, 0, W, H, sdc, b.X, b.Y, b.Width, b.Height, 0x00CC0020 /* SRCCOPY */);
                    SelectObject(mdc, old);
                    BITMAPINFOHEADER bi = new BITMAPINFOHEADER { biSize = 40, biWidth = W, biHeight = -H, biPlanes = 1, biBitCount = 32 };
                    GetDIBits(mdc, bmp, 0, H, px, ref bi, 0);
                    DeleteObject(bmp); DeleteDC(mdc); ReleaseDC(IntPtr.Zero, sdc);

                    // Saturated pixels weigh more — otherwise any scene averages to gray
                    double r = 0, g = 0, bl = 0, wsum = 0;
                    foreach (int p in px)
                    {
                        int pr = (p >> 16) & 255, pg = (p >> 8) & 255, pb = p & 255;
                        int mx = Math.Max(pr, Math.Max(pg, pb)), mn = Math.Min(pr, Math.Min(pg, pb));
                        double w = 0.15 + (mx - mn) / 255.0 * 2 + mx / 255.0 * 0.3;
                        r += pr * w; g += pg * w; bl += pb * w; wsum += w;
                    }
                    Rgb avg = new Rgb(r / wsum, g / wsum, bl / wsum);
                    smooth = Rgb.Lerp(smooth, Vivid(avg), 0.35);
                    lock (screenLock) screen = smooth;
                    Thread.Sleep(50);
                }
            }
            catch { lock (screenLock) screenThread = null; }
        }

        // ---------- Windows accent color ----------
        static double accentAt;
        static Rgb accent = new Rgb(0, 120, 215);   // default Windows blue until the first read

        // The current Windows accent color (HKCU\…\DWM\AccentColor is 0xAABBGGRR). Re-read at most once a second.
        public static Rgb Accent()
        {
            double now = clock.Elapsed.TotalSeconds;
            if (now - accentAt > 1)
            {
                accentAt = now;
                try
                {
                    using (RegistryKey k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\DWM"))
                    {
                        object v = k == null ? null : k.GetValue("AccentColor");
                        if (v is int)
                        {
                            int abgr = (int)v;
                            accent = new Rgb(abgr & 0xFF, (abgr >> 8) & 0xFF, (abgr >> 16) & 0xFF);
                        }
                    }
                }
                catch { }
            }
            return accent;
        }

        // ---------- wallpaper dominant color ----------
        [DllImport("user32.dll", CharSet = CharSet.Auto)] static extern int SystemParametersInfo(int action, int param, StringBuilder vparam, int init);

        static Thread wallThread;
        static double wallWanted;
        static Rgb wall = new Rgb(0, 0, 0);
        static readonly object wallLock = new object();

        // The wallpaper's dominant color (like Screen(), but from the desktop image). The worker lives while it's asked for.
        public static Rgb Wallpaper()
        {
            lock (wallLock)
            {
                wallWanted = clock.Elapsed.TotalSeconds;
                if (wallThread == null)
                {
                    wallThread = new Thread(WallLoop) { IsBackground = true, Name = "DS4 wallpaper", Priority = ThreadPriority.BelowNormal };
                    wallThread.Start();
                }
                return wall;
            }
        }

        static void WallLoop()
        {
            Rgb smooth = wall, cached = wall;
            string lastPath = null;
            try
            {
                while (true)
                {
                    lock (wallLock)
                        if (clock.Elapsed.TotalSeconds - wallWanted > 2) { wallThread = null; return; }
                    try
                    {
                        StringBuilder sb = new StringBuilder(600);
                        SystemParametersInfo(0x0073 /* SPI_GETDESKWALLPAPER */, sb.Capacity, sb, 0);
                        string path = sb.ToString();
                        if (path != lastPath) { lastPath = path; cached = ComputeWall(path); }
                    }
                    catch { }
                    smooth = Rgb.Lerp(smooth, cached, 0.3);
                    lock (wallLock) wall = smooth;
                    Thread.Sleep(200);
                }
            }
            catch { lock (wallLock) wallThread = null; }
        }

        // Average of the wallpaper image, saturated like the screen one; keeps the last color on failure (solid-color desktop, etc.)
        static Rgb ComputeWall(string path)
        {
            if (string.IsNullOrEmpty(path) || !System.IO.File.Exists(path)) return cachedWall;
            try
            {
                using (System.Drawing.Bitmap src = new System.Drawing.Bitmap(path))
                using (System.Drawing.Bitmap small = new System.Drawing.Bitmap(48, 27))
                using (System.Drawing.Graphics g = System.Drawing.Graphics.FromImage(small))
                {
                    g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBilinear;
                    g.DrawImage(src, 0, 0, 48, 27);
                    double r = 0, gg = 0, b = 0, wsum = 0;
                    for (int y = 0; y < 27; y++)
                        for (int x = 0; x < 48; x++)
                        {
                            System.Drawing.Color p = small.GetPixel(x, y);
                            int mx = Math.Max(p.R, Math.Max(p.G, p.B)), mn = Math.Min(p.R, Math.Min(p.G, p.B));
                            double w = 0.15 + (mx - mn) / 255.0 * 2 + mx / 255.0 * 0.3;
                            r += p.R * w; gg += p.G * w; b += p.B * w; wsum += w;
                        }
                    cachedWall = Vivid(new Rgb(r / wsum, gg / wsum, b / wsum));
                }
            }
            catch { }
            return cachedWall;
        }
        static Rgb cachedWall = new Rgb(0, 120, 215);

        // Saturation ×1.6, and brightness is lifted so the light bar doesn't go fully dark in dark scenes
        static Rgb Vivid(Rgb c)
        {
            double mx = Math.Max(c.R, Math.Max(c.G, c.B)), mn = Math.Min(c.R, Math.Min(c.G, c.B));
            if (mx < 1) return new Rgb(0, 0, 0);
            double s = Math.Min(1, (mx - mn) / mx * 1.6), v = Math.Pow(mx / 255, 0.6) * 255;
            if (mx - mn < 1) return new Rgb(v, v, v);
            // Pure hue (max component 1, min 0) mixed with white according to the new saturation
            Func<double, double> ch = x => ((x - mn) / (mx - mn) * s + (1 - s)) * v;
            return new Rgb(ch(c.R), ch(c.G), ch(c.B));
        }
    }
}
