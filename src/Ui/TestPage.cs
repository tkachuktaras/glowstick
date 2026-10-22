// Controller test page: live buttons, sticks, triggers, touchpad, gyro, report rate, rumble test.
// Data comes from Glowstick's own read thread, so it works while the controller is hidden from other programs.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace Glowstick
{
    internal static class TestLayout
    {
        static readonly KeyValuePair<Btn, string>[] Names =
        {
            new KeyValuePair<Btn, string>(Btn.Up, "▲"), new KeyValuePair<Btn, string>(Btn.Down, "▼"),
            new KeyValuePair<Btn, string>(Btn.Left, "◀"), new KeyValuePair<Btn, string>(Btn.Right, "▶"),
            new KeyValuePair<Btn, string>(Btn.Square, "□"), new KeyValuePair<Btn, string>(Btn.Cross, "✕"),
            new KeyValuePair<Btn, string>(Btn.Circle, "○"), new KeyValuePair<Btn, string>(Btn.Triangle, "△"),
            new KeyValuePair<Btn, string>(Btn.L1, "L1"), new KeyValuePair<Btn, string>(Btn.R1, "R1"),
            new KeyValuePair<Btn, string>(Btn.L2, "L2"), new KeyValuePair<Btn, string>(Btn.R2, "R2"),
            new KeyValuePair<Btn, string>(Btn.Share, "Share"), new KeyValuePair<Btn, string>(Btn.Options, "Options"),
            new KeyValuePair<Btn, string>(Btn.L3, "L3"), new KeyValuePair<Btn, string>(Btn.R3, "R3"),
            new KeyValuePair<Btn, string>(Btn.PS, "PS"), new KeyValuePair<Btn, string>(Btn.Touch, "Touch"),
        };

        // Labels of the pressed buttons in a DS4-layout report, in a fixed order
        public static string[] PressedButtons(byte[] raw)
        {
            Btn b = Pad.ReadButtons(raw);
            List<string> r = new List<string>();
            foreach (KeyValuePair<Btn, string> kv in Names) if ((b & kv.Key) != 0) r.Add(kv.Value);
            return r.ToArray();
        }
    }

    internal class TestPage : UserControl
    {
        readonly ComboBox picker = new ComboBox { Width = 360, HorizontalAlignment = HorizontalAlignment.Left };
        readonly PadView view = new PadView();
        readonly DispatcherTimer rumbleOff = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        readonly TextBlock rate = new TextBlock(), battery = new TextBlock(), mode = new TextBlock(), motion = new TextBlock();
        readonly StackPanel bottom = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 12, 0, 0) };
        readonly StackPanel trigRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0) };
        readonly Grid stats = new Grid { Margin = new Thickness(0, 10, 0, 0) };
        Border card;
        Controller[] list = new Controller[0];
        Controller rumbling;
        long lastListMs = -10000;

        public TestPage()
        {
            DockPanel root = new DockPanel { Margin = new Thickness(28, 20, 28, 20) };
            StackPanel top = new StackPanel();
            top.Children.Add(new TextBlock { Text = Lang.T("Controller test"), Style = Theme.Style("PageTitle") });
            top.Children.Add(picker);
            DockPanel.SetDock(top, Dock.Top);
            root.Children.Add(top);

            bottom.Children.Add(new TextBlock { Text = Lang.T("Rumble (1 s):"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) });
            bottom.Children.Add(Rumble(Lang.T("Weak motor"), 0, 255));
            bottom.Children.Add(Rumble(Lang.T("Strong motor"), 255, 0));
            bottom.Children.Add(Rumble(Lang.T("Both"), 255, 255));
            Button bat = new Button { Content = Lang.T("Battery on light bar"), Margin = new Thickness(16, 0, 0, 0) };
            bat.Click += (s, e) => { Controller c = Selected(); if (c != null) c.BatteryShowUntil = Engine.Ms + 3000; };
            bottom.Children.Add(bat);
            DockPanel.SetDock(bottom, Dock.Bottom);
            root.Children.Add(bottom);

            // DualSense adaptive triggers (shown enabled only for a DualSense)
            trigRow.Children.Add(new TextBlock { Text = Lang.T("DualSense triggers:"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) });
            trigRow.Children.Add(Trig(Lang.T("Off"), DsTrigger.Off));
            trigRow.Children.Add(Trig(Lang.T("Resistance"), DsTrigger.Resistance(0x20, 0xB0)));
            trigRow.Children.Add(Trig(Lang.T("Weapon"), DsTrigger.Weapon(0x30, 0x70, 0xD0)));
            trigRow.Children.Add(Trig(Lang.T("Vibration"), DsTrigger.Vibration(0x10, 0xC0, 0x28)));
            DockPanel.SetDock(trigRow, Dock.Bottom);
            root.Children.Add(trigRow);

            // Numbers under the drawing: three small cards and a wide one with the motion sensors and raw axes
            for (int k = 0; k < 4; k++) stats.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(k < 3 ? 1 : 2.4, GridUnitType.Star) });
            stats.Children.Add(Stat(Lang.T("Reports/s"), rate, 0));
            stats.Children.Add(Stat(Lang.T("Battery"), battery, 1));
            stats.Children.Add(Stat(Lang.T("Mode"), mode, 2));
            motion.FontFamily = new FontFamily("Consolas");
            stats.Children.Add(Stat(null, motion, 3));
            DockPanel.SetDock(stats, Dock.Bottom);
            root.Children.Add(stats);

            card = new Border { Style = Theme.Style("CardBox"), Margin = new Thickness(0, 14, 0, 0), Padding = new Thickness(10),
                Child = new Viewbox { Child = view, Stretch = Stretch.Uniform } };
            root.Children.Add(card);
            Content = root;

            rumbleOff.Tick += (s, e) => StopRumble();
            IsVisibleChanged += (s, e) => { if (!IsVisible) StopRumble(); };   // leaving the page — like closing the old test window
        }

        // label null — the value is a multi-line monospaced block
        static FrameworkElement Stat(string label, TextBlock value, int col)
        {
            StackPanel p = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            if (label != null)
            {
                p.Children.Add(new TextBlock { Text = label, Style = Theme.Style("MutedText") });
                value.FontSize = 16; value.FontWeight = FontWeights.SemiBold;
            }
            else value.FontSize = 12;
            value.TextTrimming = TextTrimming.CharacterEllipsis;
            p.Children.Add(value);
            Border b = new Border { Style = Theme.Style("CardBox"), Padding = new Thickness(12, 8, 12, 8), Margin = new Thickness(col == 0 ? 0 : 8, 0, 0, 0), Child = p };
            Grid.SetColumn(b, col);
            return b;
        }

        Button Trig(string text, byte[] effect)
        {
            Button b = new Button { Content = text, Margin = new Thickness(0, 0, 8, 0) };
            b.Click += (s, e) => { Controller c = Selected(); if (c != null) c.SetTriggers(effect, effect); };
            return b;
        }

        Button Rumble(string text, byte large, byte small)
        {
            Button b = new Button { Content = text, Margin = new Thickness(0, 0, 8, 0) };
            b.Click += (s, e) =>
            {
                Controller c = Selected();
                if (c == null) return;
                if (rumbling != null && rumbling != c) rumbling.TestRumble(0, 0);
                rumbling = c; c.TestRumble(large, small);
                rumbleOff.Stop(); rumbleOff.Start();
            };
            return b;
        }

        void StopRumble()
        {
            rumbleOff.Stop();
            if (rumbling != null) rumbling.TestRumble(0, 0);
            rumbling = null;
        }

        Controller Selected()
        {
            int i = picker.SelectedIndex;
            return i >= 0 && i < list.Length ? list[i] : null;
        }

        void RefreshList()
        {
            if (Engine.Ms - lastListMs < 1000) return;
            lastListMs = Engine.Ms;
            Controller[] now = Engine.Controllers();
            bool same = now.Length == list.Length;
            for (int k = 0; same && k < now.Length; k++) same = now[k] == list[k];
            if (same) return;
            Controller sel = Selected();
            list = now;
            picker.Items.Clear();
            for (int k = 0; k < list.Length; k++) picker.Items.Add(Lang.F("Controller {0}", k + 1) + " · " + list[k].Model + " · " + (list[k].Bluetooth ? "Bluetooth" : "USB"));
            int idx = Array.IndexOf(list, sel);
            if (list.Length > 0) picker.SelectedIndex = idx >= 0 ? idx : 0;
        }

        public void OnTick()
        {
            RefreshList();
            Controller c = Selected();
            view.Show(c);
            ShowNumbers(c);
            // No controller — the whole page is shown clearly disabled (the drawing keeps its own "connect a controller" note)
            bool any = c != null;
            if (picker.IsEnabled != any)
            {
                picker.IsEnabled = bottom.IsEnabled = stats.IsEnabled = any;
                picker.Opacity = bottom.Opacity = stats.Opacity = any ? 1 : 0.4;
                card.Opacity = any ? 1 : 0.6;
            }
            // Triggers only make sense on a DualSense
            bool ds = c != null && c.DualSense;
            if (trigRow.IsEnabled != ds) { trigRow.IsEnabled = ds; trigRow.Opacity = ds ? 1 : 0.4; }
        }

        void ShowNumbers(Controller c)
        {
            if (c == null) { rate.Text = battery.Text = mode.Text = "—"; motion.Text = ""; return; }
            rate.Text = c.ReportRate.ToString();
            string bat = c.Battery < 0 ? "—" : c.Battery + "%" + (c.Charging ? " ⚡" : "");
            string left = c.Charging ? "" : UiFormat.Hours(c.HoursLeft);
            if (left != "") bat += "  " + left;
            battery.Text = bat;
            mode.Text = c.Virtual ? Lang.T("game (virtual pad)") : Lang.T("light bar only");
            if (!view.HasData) { motion.Text = ""; return; }
            Pad p = view.LastPad;
            // Monospaced: labels (in any language) are padded to the same width
            string[] names = { Lang.T("Gyro"), Lang.T("Accel."), Lang.T("Sticks"), Lang.T("Triggers") };
            int w = 0; foreach (string s in names) w = Math.Max(w, s.Length);
            Func<int, string> N = i => names[i].PadRight(w + 2);
            motion.Text =
                string.Format(N(0) + "X {0,6}  Y {1,6}  Z {2,6}\n", p.GyroX, p.GyroY, p.GyroZ) +
                string.Format(N(1) + "X {0,6}  Y {1,6}  Z {2,6}\n", p.AccelX, p.AccelY, p.AccelZ) +
                string.Format(N(2) + "L {0,3},{1,3}   R {2,3},{3,3}\n", p.LX, p.LY, p.RX, p.RY) +
                string.Format(N(3) + "L2 {0,3}   R2 {1,3}", p.L2, p.R2);
        }
    }

    // The controller drawn as a controller: body, light bar in its current color, touchpad, d-pad, face buttons in
    // PlayStation colors that light up, triggers as gauges, sticks that move. Fixed 820×470 space — the Viewbox scales it.
    internal class PadView : FrameworkElement
    {
        readonly byte[] raw = new byte[63];
        Controller c;
        public bool HasData { get; private set; }
        public Pad LastPad { get; private set; }
        public Rgb LightBar;   // the color drawn on the light bar (from the controller; settable for previews)

        static readonly Brush Body = Frozen(Color.FromRgb(0x30, 0x30, 0x38)), Inset = Frozen(Color.FromRgb(0x22, 0x22, 0x29)),
            Key = Frozen(Color.FromRgb(0x3C, 0x3C, 0x46)), Edge = Frozen(Color.FromRgb(0x48, 0x48, 0x54));
        static readonly Pen EdgePen = FrozenPen(Edge, 1.5);
        static readonly Color Tri = Color.FromRgb(0x3D, 0xDC, 0x97), Cir = Color.FromRgb(0xFF, 0x5A, 0x6E), Crs = Color.FromRgb(0x6E, 0xA8, 0xFF), Sqr = Color.FromRgb(0xFF, 0x8A, 0xD8);
        static readonly Typeface Ui = new Typeface(new FontFamily("Segoe UI Variable Text, Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);
        static readonly Geometry Silhouette = BuildSilhouette();

        public PadView() { Width = 820; Height = 470; }

        static Brush Frozen(Color c) { SolidColorBrush b = new SolidColorBrush(c); b.Freeze(); return b; }
        static Pen FrozenPen(Brush b, double w) { Pen p = new Pen(b, w); p.Freeze(); return p; }
        static Brush On { get { return Theme.Brush("Accent"); } }
        static Brush OnText { get { return Theme.Brush("AccentText"); } }

        // DualShock-like outline: flat top, rounded shoulders, two grips, a concave edge between them
        static Geometry BuildSilhouette()
        {
            StreamGeometry s = new StreamGeometry();
            using (StreamGeometryContext k = s.Open())
            {
                k.BeginFigure(new Point(210, 68), true, true);
                k.LineTo(new Point(610, 68), true, true);
                k.BezierTo(new Point(680, 68), new Point(702, 108), new Point(707, 160), true, true);        // top-right corner
                k.BezierTo(new Point(714, 232), new Point(732, 330), new Point(737, 382), true, true);       // right side
                k.BezierTo(new Point(744, 446), new Point(690, 474), new Point(648, 458), true, true);      // right grip end
                k.BezierTo(new Point(610, 444), new Point(588, 384), new Point(562, 348), true, true);      // inner edge of the right grip
                k.BezierTo(new Point(520, 316), new Point(300, 316), new Point(258, 348), true, true);      // concave middle
                k.BezierTo(new Point(232, 384), new Point(210, 444), new Point(172, 458), true, true);      // inner edge of the left grip
                k.BezierTo(new Point(130, 474), new Point(76, 446), new Point(83, 382), true, true);        // left grip end
                k.BezierTo(new Point(88, 330), new Point(106, 232), new Point(113, 160), true, true);       // left side
                k.BezierTo(new Point(118, 108), new Point(140, 68), new Point(210, 68), true, true);        // top-left corner
            }
            s.Freeze();
            return s;
        }

        public void Show(Controller ctl)
        {
            c = ctl;
            HasData = c != null && c.CopyLast(raw);
            if (HasData) { LastPad = Pad.Parse(raw); LightBar = c.Out; }
            InvalidateVisual();
        }

        // For previews: draw this report without a controller
        public void ShowReport(byte[] report, Rgb light)
        {
            c = null; HasData = true; LastPad = Pad.Parse(report); LightBar = light;
            InvalidateVisual();
        }

        FormattedText Txt(string s, double size, Brush b)
        {
            return new FormattedText(s, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, Ui, size, b, VisualTreeHelper.GetDpi(this).PixelsPerDip);
        }

        void Label(DrawingContext g, string s, Point center, double size, Brush b)
        {
            FormattedText t = Txt(s, size, b);
            g.DrawText(t, new Point(center.X - t.Width / 2, center.Y - t.Height / 2));
        }

        static void Glow(DrawingContext g, Point at, double r, Color col)
        {
            RadialGradientBrush br = new RadialGradientBrush(Color.FromArgb(0x90, col.R, col.G, col.B), Color.FromArgb(0, col.R, col.G, col.B));
            g.DrawEllipse(br, null, at, r, r);
        }

        protected override void OnRender(DrawingContext g)
        {
            g.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, Width, Height));
            if (!HasData)
            {
                if (c == null)
                {
                    Label(g, Lang.T("No controller connected."), new Point(Width / 2, Height / 2 - 14), 16, Theme.Brush("Text"));
                    Label(g, Lang.T("Plug in a DualShock 4 or DualSense via USB or Bluetooth to test it."), new Point(Width / 2, Height / 2 + 12), 12.5, Theme.Brush("Muted"));
                }
                else Label(g, Lang.T("Waiting for data from the controller…"), new Point(Width / 2, Height / 2), 16, Theme.Brush("Muted"));
                return;
            }
            Pad p = LastPad;
            Btn b = p.Buttons;

            // Triggers (gauges) and bumpers above the body
            Trigger(g, "L2", p.L2, new Rect(178, 6, 150, 24));
            Trigger(g, "R2", p.R2, new Rect(492, 6, 150, 24));
            Pill(g, "L1", (b & Btn.L1) != 0, new Rect(178, 36, 150, 22));
            Pill(g, "R1", (b & Btn.R1) != 0, new Rect(492, 36, 150, 22));

            g.DrawGeometry(Body, EdgePen, Silhouette);

            // Light bar: the controller's current color, with a soft glow
            Color lb = ColorMath.ToMedia(LightBar);
            Rect bar = new Rect(310, 70, 200, 6);
            g.DrawRoundedRectangle(new SolidColorBrush(Color.FromArgb(0x55, lb.R, lb.G, lb.B)), null, new Rect(bar.X - 6, bar.Y - 4, bar.Width + 12, bar.Height + 8), 7, 7);
            g.DrawRoundedRectangle(new SolidColorBrush(lb), null, bar, 3, 3);

            // Touchpad
            Rect tp = new Rect(310, 84, 200, 116);
            bool click = (b & Btn.Touch) != 0;
            g.DrawRoundedRectangle(Inset, click ? new Pen(On, 2) : EdgePen, tp, 14, 14);
            Finger(g, tp, p.Touch1, p.T1X, p.T1Y, Colors.Gold);
            Finger(g, tp, p.Touch2, p.T2X, p.T2Y, Colors.LightGreen);
            if (!p.Touch1 && !p.Touch2) Label(g, Lang.T("touchpad"), new Point(tp.X + tp.Width / 2, tp.Y + tp.Height / 2), 12, Theme.Brush("Muted"));

            Pill(g, c != null && c.DualSense ? "Create" : "Share", (b & Btn.Share) != 0, new Rect(244, 96, 54, 20));
            Pill(g, "Options", (b & Btn.Options) != 0, new Rect(522, 96, 60, 20));
            Round(g, "PS", (b & Btn.PS) != 0, new Point(410, 226), 14);

            DPad(g, b, new Point(225, 172));
            Face(g, b, new Point(595, 172));

            Stick(g, "L3", p.LX, p.LY, (b & Btn.L3) != 0, new Point(322, 268));
            Stick(g, "R3", p.RX, p.RY, (b & Btn.R3) != 0, new Point(498, 268));
        }

        void Trigger(DrawingContext g, string name, byte v, Rect r)
        {
            g.DrawRoundedRectangle(Inset, EdgePen, r, 8, 8);
            if (v > 0)
            {
                LinearGradientBrush fill = new LinearGradientBrush(Color.FromRgb(0x3B, 0x7B, 0xD8), Color.FromRgb(0x7A, 0xC0, 0xFF), 0);
                g.DrawRoundedRectangle(fill, null, new Rect(r.X + 2, r.Y + 2, Math.Max(12, (r.Width - 4) * v / 255), r.Height - 4), 6, 6);
            }
            Label(g, name + "  " + v, new Point(r.X + r.Width / 2, r.Y + r.Height / 2), 11.5, Theme.Brush("Text"));
        }

        void Pill(DrawingContext g, string name, bool on, Rect r)
        {
            g.DrawRoundedRectangle(on ? On : Key, on ? null : EdgePen, r, r.Height / 2, r.Height / 2);
            Label(g, name, new Point(r.X + r.Width / 2, r.Y + r.Height / 2), 11, on ? OnText : Theme.Brush("Text"));
        }

        void Round(DrawingContext g, string name, bool on, Point at, double r)
        {
            if (on) Glow(g, at, r * 2, ((SolidColorBrush)On).Color);
            g.DrawEllipse(on ? On : Key, on ? null : EdgePen, at, r, r);
            Label(g, name, at, 11, on ? OnText : Theme.Brush("Text"));
        }

        // Cross-shaped d-pad: four arms with arrows
        void DPad(DrawingContext g, Btn b, Point o)
        {
            const double A = 30, W = 32;   // arm length beyond the center square, arm width
            Arm(g, (b & Btn.Up) != 0, new Rect(o.X - W / 2, o.Y - W / 2 - A, W, A + 4), 0);
            Arm(g, (b & Btn.Down) != 0, new Rect(o.X - W / 2, o.Y + W / 2 - 4, W, A + 4), 180);
            Arm(g, (b & Btn.Left) != 0, new Rect(o.X - W / 2 - A, o.Y - W / 2, A + 4, W), 270);
            Arm(g, (b & Btn.Right) != 0, new Rect(o.X + W / 2 - 4, o.Y - W / 2, A + 4, W), 90);
            g.DrawRectangle(Key, null, new Rect(o.X - W / 2 + 1, o.Y - W / 2 + 1, W - 2, W - 2));
        }

        void Arm(DrawingContext g, bool on, Rect r, double angle)
        {
            g.DrawRoundedRectangle(on ? On : Key, on ? null : EdgePen, r, 6, 6);
            Point mid = new Point(r.X + r.Width / 2, r.Y + r.Height / 2);
            StreamGeometry s = new StreamGeometry();
            using (StreamGeometryContext k = s.Open())
            {
                k.BeginFigure(new Point(mid.X, mid.Y - 6), true, true);
                k.LineTo(new Point(mid.X + 6, mid.Y + 4), true, false);
                k.LineTo(new Point(mid.X - 6, mid.Y + 4), true, false);
            }
            s.Transform = new RotateTransform(angle, mid.X, mid.Y);   // the arrow points outwards
            g.DrawGeometry(on ? OnText : Theme.Brush("Muted"), null, s);
        }

        // △ ○ ✕ □ drawn as shapes in their colors; pressed — filled with the color and glowing
        void Face(DrawingContext g, Btn b, Point o)
        {
            const double D = 40, R = 18;
            Symbol(g, (b & Btn.Triangle) != 0, new Point(o.X, o.Y - D), R, Tri, 0);
            Symbol(g, (b & Btn.Circle) != 0, new Point(o.X + D, o.Y), R, Cir, 1);
            Symbol(g, (b & Btn.Cross) != 0, new Point(o.X, o.Y + D), R, Crs, 2);
            Symbol(g, (b & Btn.Square) != 0, new Point(o.X - D, o.Y), R, Sqr, 3);
        }

        static void Symbol(DrawingContext g, bool on, Point at, double r, Color col, int kind)
        {
            if (on) Glow(g, at, r * 2.2, col);
            g.DrawEllipse(on ? new SolidColorBrush(col) : Key, on ? null : EdgePen, at, r, r);
            Pen pen = new Pen(on ? Brushes.White : new SolidColorBrush(col), 2.2) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
            double s = r * 0.48;
            if (kind == 0)
            {
                StreamGeometry t = new StreamGeometry();
                using (StreamGeometryContext k = t.Open())
                {
                    k.BeginFigure(new Point(at.X, at.Y - s), false, true);
                    k.LineTo(new Point(at.X + s * 0.95, at.Y + s * 0.7), true, true);
                    k.LineTo(new Point(at.X - s * 0.95, at.Y + s * 0.7), true, true);
                }
                g.DrawGeometry(null, pen, t);
            }
            else if (kind == 1) g.DrawEllipse(null, pen, at, s, s);
            else if (kind == 2)
            {
                g.DrawLine(pen, new Point(at.X - s, at.Y - s), new Point(at.X + s, at.Y + s));
                g.DrawLine(pen, new Point(at.X + s, at.Y - s), new Point(at.X - s, at.Y + s));
            }
            else g.DrawRectangle(null, pen, new Rect(at.X - s * 0.85, at.Y - s * 0.85, s * 1.7, s * 1.7));
        }

        void Stick(DrawingContext g, string name, byte x, byte y, bool pressed, Point o)
        {
            const double Well = 44, Cap = 24, Travel = 18;
            g.DrawEllipse(Inset, EdgePen, o, Well, Well);
            Point cap = new Point(o.X + (x - 127.5) / 127.5 * Travel, o.Y + (y - 127.5) / 127.5 * Travel);
            g.DrawEllipse(Key, pressed ? new Pen(On, 2) : EdgePen, cap, Cap, Cap);
            g.DrawEllipse(pressed ? On : Edge, null, cap, 5, 5);
            o = cap;
            Label(g, name, new Point(o.X, o.Y), 10, pressed ? On : Theme.Brush("Muted"));   // on the cap
        }

        static void Finger(DrawingContext g, Rect tp, bool on, int x, int y, Color col)
        {
            if (!on) return;
            Point at = new Point(tp.X + x * tp.Width / Pad.TouchW, tp.Y + y * tp.Height / Pad.TouchH);
            Glow(g, at, 16, col);
            g.DrawEllipse(new SolidColorBrush(col), null, at, 6, 6);
        }
    }
}
