// Data for "live" presets: the current controller's state, system audio level, average screen color.
// A preset only gets (t, i, n), so before calling it the engine puts here the controller the color is computed for.
// Outside the engine (list thumbnails) Cur == null — presets draw a demo.
using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

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
        static double meterAt = -10, audioAt, level, slowMax = 0.1;

        // Level 0..1 with auto-gain (quiet music "pumps" too). Polled at most once every 8 ms.
        public static double Audio()
        {
            double now = clock.Elapsed.TotalSeconds;
            if (now - audioAt < 0.008) return level;
            double dt = Math.Min(0.1, now - audioAt); audioAt = now;
            if (meter == null || now - meterAt > 5) OpenMeter(now);   // every 5 s — in case the output device changed
            float peak = 0;
            try { if (meter != null && meter.GetPeakValue(out peak) != 0) peak = 0; } catch { meter = null; }
            slowMax = Math.Max(peak, Math.Max(0.05, slowMax * Math.Pow(0.85, dt)));   // slowly forgets loud moments
            double target = Math.Pow(Math.Min(1, peak / slowMax), 1.6);
            level = target > level ? target : level * Math.Pow(0.02, dt);              // sharp attack, smooth decay
            return level;
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
