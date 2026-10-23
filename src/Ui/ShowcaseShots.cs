// The showcase mode's screenshots (Showcase, --showcase-shots <folder>): every page, and the dialogs that sit behind a click,
// rendered off screen at a fixed size — the window's content over a plain dark backdrop under a drawn title bar, so
// nothing of the real desktop gets in. One PNG per Shot; the Pro module adds its own (Pro.ShowcaseShots). Then the app exits
// with the number of shots that failed as its exit code (the reasons are in the showcase log, Showcase.LogDir).
using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Glowstick
{
    internal class Shot
    {
        public string Name;                        // the file name, without .png
        public Action<MainWindow, Shot> Prepare;   // navigate, open a dialog, switch a tab…
        public int WaitMs = 900;                   // for the page to settle: fades, the light bar loop, battery charts
    }

    internal static class ShowcaseShots
    {
        const double Dpi = 120;      // 125%: sharp on GitHub, the size the README screenshots always had
        const double TitleH = 32;    // the drawn title bar

        static List<Shot> Core()
        {
            return new List<Shot>
            {
                new Shot { Name = "presets", Prepare = (w, s) => w.Navigate(MainWindow.Page.Presets) },
                new Shot { Name = "controllers", Prepare = (w, s) => w.Navigate(MainWindow.Page.Controllers), WaitMs = 1500 },
                new Shot { Name = "controller-test", Prepare = (w, s) => w.Navigate(MainWindow.Page.Test) },
                new Shot { Name = "settings", Prepare = (w, s) => w.Navigate(MainWindow.Page.Settings) },
            };
        }

        // ---------- the run: one shot after another on the UI thread ----------
        public static void Start(MainWindow w, string dir)
        {
            List<Shot> shots = Core();
            if (Pro.ShowcaseShots != null) Pro.ShowcaseShots(shots);
            Directory.CreateDirectory(dir);
            int next = 0, failed = 0;
            Shot cur = null;
            DispatcherTimer timer = new DispatcherTimer();
            Action<string, Exception> fail = (what, e) => { failed++; Log.Error("showcase shot " + cur.Name + " (" + what + ")", e); };
            Action step = null;
            // Each shot: prepare → wait → save → close the dialogs it opened → the next one
            step = () =>
            {
                if (cur != null)
                {
                    try { Save(Render(w), Path.Combine(dir, cur.Name + ".png")); }
                    catch (Exception e) { fail("render", e); }
                    while (w.OverlayTop != null) w.CloseOverlay();
                }
                if (next >= shots.Count)
                {
                    timer.Stop();
                    Log.Info("showcase: " + (shots.Count - failed) + " of " + shots.Count + " screenshots saved");
                    Environment.ExitCode = failed;
                    UiApp.Exit();
                    return;
                }
                cur = shots[next++];
                int wait = cur.WaitMs;
                try { if (cur.Prepare != null) cur.Prepare(w, cur); }
                catch (Exception e) { fail("prepare", e); cur = null; wait = 0; }
                timer.Interval = TimeSpan.FromMilliseconds(Math.Max(1, wait));
                timer.Start();
            };
            timer.Tick += (s, e) => { timer.Stop(); step(); };
            // The first shot once the window and the light bar loop are running
            timer.Interval = TimeSpan.FromMilliseconds(1500);
            timer.Start();
        }

        // ---------- rendering ----------
        // The window's content (its client area) under a drawn title bar
        static BitmapSource Render(MainWindow w)
        {
            FrameworkElement root = (FrameworkElement)w.Content;
            w.UpdateLayout();
            double width = root.ActualWidth, height = root.ActualHeight;
            DrawingVisual dv = new DrawingVisual();
            using (DrawingContext dc = dv.RenderOpen())
            {
                dc.DrawRectangle(Theme.Brush("Side"), null, new Rect(0, 0, width, height + TitleH));   // what Mica would show
                TitleBar(dc, width);
                dc.PushTransform(new TranslateTransform(0, TitleH));
                Draw(dc, root, new Point(0, 0));
                dc.Pop();
            }
            RenderTargetBitmap bmp = new RenderTargetBitmap((int)Math.Ceiling(width * Dpi / 96), (int)Math.Ceiling((height + TitleH) * Dpi / 96), Dpi, Dpi, PixelFormats.Pbgra32);
            bmp.Render(dv);
            return bmp;
        }

        static void Draw(DrawingContext dc, FrameworkElement v, Point at)
        {
            Rect r = new Rect(0, 0, v.ActualWidth, v.ActualHeight);
            VisualBrush b = new VisualBrush(v) { Stretch = Stretch.None, AlignmentX = AlignmentX.Left, AlignmentY = AlignmentY.Top, ViewboxUnits = BrushMappingMode.Absolute, Viewbox = r };
            dc.DrawRectangle(b, null, new Rect(at, r.Size));
        }

        // The Windows 11 title bar in the app's dark colors: icon, title and the three caption buttons
        static void TitleBar(DrawingContext dc, double width)
        {
            dc.DrawImage(UiApp.IconSource(), new Rect(12, 8, 16, 16));
            Typeface ui = new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
            dc.DrawText(Text("Glowstick", ui, 12, Theme.Brush("Text")), new Point(36, 8));
            Typeface icons = new Typeface(new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
            string[] glyphs = { "", "", "" };   // minimize, maximize, close
            for (int k = 0; k < glyphs.Length; k++)
            {
                FormattedText g = Text(glyphs[k], icons, 10, Theme.Brush("Text"));
                dc.DrawText(g, new Point(width - 46 * (3 - k) + (46 - g.Width) / 2, (TitleH - g.Height) / 2));
            }
        }

        static FormattedText Text(string s, Typeface face, double size, Brush brush)
        {
            return new FormattedText(s, System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight, face, size, brush, Dpi / 96);
        }

        static void Save(BitmapSource bmp, string path)
        {
            PngBitmapEncoder enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(bmp));
            using (FileStream f = File.Create(path)) enc.Save(f);
        }

        // ---------- for Prepare ----------
        // The first element of type T under root (in the visual tree) that matches
        public static T Find<T>(DependencyObject root, Func<T, bool> match = null) where T : DependencyObject
        {
            if (root == null) return null;
            T t = root as T;
            if (t != null && (match == null || match(t))) return t;
            int n = VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < n; i++)
            {
                T found = Find(VisualTreeHelper.GetChild(root, i), match);
                if (found != null) return found;
            }
            return null;
        }
    }
}
