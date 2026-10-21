// Once a second: is the active window fullscreen (a game)? Then the touchpad doesn't drive the mouse.
// The same 1-second thread gives Pro its tick (game profiles check which exe is running).
using System;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace Glowstick
{
    internal static class Fullscreen
    {
        [StructLayout(LayoutKind.Sequential)] struct RECT { public int L, T, R, B; }
        [StructLayout(LayoutKind.Sequential)] struct MONITORINFO { public int cbSize; public RECT rcMonitor, rcWork; public int dwFlags; }

        [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h, out RECT r);
        [DllImport("user32.dll")] static extern IntPtr MonitorFromWindow(IntPtr h, int flags);
        [DllImport("user32.dll")] static extern bool GetMonitorInfo(IntPtr mon, ref MONITORINFO mi);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr h, StringBuilder sb, int max);
        [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr h, int index);
        [DllImport("user32.dll")] static extern bool IsZoomed(IntPtr h);

        public static volatile bool App;   // the foreground window is fullscreen
        static volatile bool running;

        public static void Start()
        {
            if (running) return;
            running = true;
            new Thread(Loop) { IsBackground = true, Name = "fullscreen", Priority = ThreadPriority.BelowNormal }.Start();
        }

        public static void Stop() { running = false; }

        static void Loop()
        {
            while (running)
            {
                try { App = IsFullscreen(); } catch { }
                if (Pro.EverySecond != null) try { Pro.EverySecond(); } catch (Exception e) { Log.Error("pro tick", e); }
                Thread.Sleep(1000);
            }
        }

        static bool IsFullscreen()
        {
            IntPtr h = GetForegroundWindow();
            if (h == IntPtr.Zero) return false;
            StringBuilder cls = new StringBuilder(64);
            GetClassName(h, cls, cls.Capacity);
            string c = cls.ToString();
            if (c == "Progman" || c == "WorkerW" || c == "Shell_TrayWnd") return false;   // desktop and taskbar
            const int WS_CAPTION = 0x00C00000;
            if (IsZoomed(h) && (GetWindowLong(h, -16 /* GWL_STYLE */) & WS_CAPTION) == WS_CAPTION) return false;   // a maximized normal window with an auto-hidden taskbar
            RECT r; if (!GetWindowRect(h, out r)) return false;
            MONITORINFO mi = new MONITORINFO { cbSize = Marshal.SizeOf(typeof(MONITORINFO)) };
            if (!GetMonitorInfo(MonitorFromWindow(h, 2 /* NEAREST */), ref mi)) return false;
            return r.L <= mi.rcMonitor.L && r.T <= mi.rcMonitor.T && r.R >= mi.rcMonitor.R && r.B >= mi.rcMonitor.B;
        }
    }
}
