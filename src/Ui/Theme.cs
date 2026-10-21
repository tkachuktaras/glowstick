// Theme: the dark resource dictionary (Theme.xaml, embedded and loaded at runtime — csc has no XAML compiler)
// and the window chrome: dark title bar everywhere, Mica backdrop on Windows 11.
// Views take brushes and styles through Theme.Brush/Theme.Style rather than FindResource,
// so they also work without an Application (tests).
using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Media;

namespace Glowstick
{
    internal static class Theme
    {
        public static ResourceDictionary Dict { get; private set; }

        public static void Load()
        {
            if (Dict == null)
            {
                using (Stream s = Assembly.GetExecutingAssembly().GetManifestResourceStream("Theme.xaml"))
                    Dict = (ResourceDictionary)XamlReader.Load(s);
                // Frozen brushes are cheaper and can be shared between threads
                foreach (object key in Dict.Keys)
                {
                    Freezable f = Dict[key] as Freezable;
                    if (f != null && f.CanFreeze) f.Freeze();
                }
            }
            Application app = Application.Current;
            if (app != null && !app.Resources.MergedDictionaries.Contains(Dict)) app.Resources.MergedDictionaries.Add(Dict);
        }

        public static Brush Brush(string key) { Load(); return (Brush)Dict[key]; }
        public static Style Style(string key) { Load(); return (Style)Dict[key]; }
        public static FontFamily IconFont { get { Load(); return (FontFamily)Dict["IconFont"]; } }

        // Mica needs Windows 11 22H2 (build 22621)
        public static readonly bool Mica = Environment.OSVersion.Version.Major >= 10 && Environment.OSVersion.Version.Build >= 22621;

        // Background of the main area: translucent over Mica, solid elsewhere
        public static Brush Layer { get { return Mica ? Brush("Layer") : Brush("Bg"); } }

        [StructLayout(LayoutKind.Sequential)] struct MARGINS { public int L, R, T, B; }
        [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
        [DllImport("dwmapi.dll")] static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref MARGINS m);

        const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20, DWMWA_SYSTEMBACKDROP_TYPE = 38, DWMSBT_MAINWINDOW = 2;

        public static void Chrome(Window w)
        {
            w.Background = Mica ? Brushes.Transparent : Brush("Bg");
            w.SourceInitialized += (s, e) =>
            {
                IntPtr h = new WindowInteropHelper(w).Handle;
                int on = 1;
                try { DwmSetWindowAttribute(h, DWMWA_USE_IMMERSIVE_DARK_MODE, ref on, 4); } catch { }
                if (!Mica) return;
                try
                {
                    HwndSource.FromHwnd(h).CompositionTarget.BackgroundColor = Colors.Transparent;
                    MARGINS m = new MARGINS { L = -1, R = -1, T = -1, B = -1 };
                    DwmExtendFrameIntoClientArea(h, ref m);
                    int type = DWMSBT_MAINWINDOW;
                    if (DwmSetWindowAttribute(h, DWMWA_SYSTEMBACKDROP_TYPE, ref type, 4) != 0) w.Background = Brush("Bg");
                }
                catch { w.Background = Brush("Bg"); }
            };
        }
    }
}
