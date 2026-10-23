// Tray icon (WinForms NotifyIcon — WPF has none; it runs fine on the WPF dispatcher thread):
// menu, battery icon of the first controller, low-battery alerts, update and other balloons.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using System.Windows.Threading;

namespace Glowstick
{
    internal static class TrayText
    {
        // NotifyIcon.Text is limited to 63 characters: with several controllers — no estimate, then even shorter
        public static string Tooltip(ControllerInfo[] snap)
        {
            List<string> parts = new List<string>(), shortParts = new List<string>();
            for (int k = 0; k < snap.Length; k++)
            {
                ControllerInfo c = snap[k];
                if (c.Battery < 0) continue;
                string left = UiFormat.Hours(c.HoursLeft);
                parts.Add((k + 1) + ": " + c.Battery + "%" + (c.Charging ? "⚡" : "") + (left != "" ? " " + left : ""));
                shortParts.Add((k + 1) + ":" + c.Battery + (c.Charging ? "⚡" : ""));
            }
            string text = parts.Count > 0 ? "Glowstick · " + string.Join(" · ", parts) : "Glowstick";
            if (text.Length > 63) text = "Glowstick · " + string.Join(" ", shortParts) + " %";
            if (text.Length > 63) text = string.Join(" ", shortParts) + " %";
            if (text.Length > 63) text = text.Substring(0, 63);
            return text;
        }
    }

    internal class Tray : IDisposable
    {
        readonly NotifyIcon icon = new NotifyIcon();
        readonly ContextMenuStrip menu = new ContextMenuStrip();
        readonly DispatcherTimer battery = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };   // runs even when the window is hidden
        readonly Dictionary<string, int> lowWarned = new Dictionary<string, int>();   // controller → 0 / 1 (low) / 2 (critical)
        Icon batteryIcon;
        int batterySegments;
        string balloonUrl;              // what clicking the current balloon opens (null — nothing)
        bool balloonShowsWindow;        // the update balloon opens the window (its update card)

        public Tray()
        {
            icon.Icon = UiApp.AppIcon; icon.Text = "Glowstick"; icon.Visible = true;
            menu.Renderer = new DarkMenuRenderer();
            menu.Opening += (s, e) => Build();   // fresh every time: current preset, mode, touchpad
            Build();
            icon.ContextMenuStrip = menu;
            icon.DoubleClick += (s, e) => UiApp.ShowMain();
            icon.BalloonTipClicked += (s, e) => { if (balloonShowsWindow) UiApp.ShowMain(); else if (balloonUrl != null) UiApp.OpenUrl(balloonUrl); };
            battery.Tick += (s, e) => BatteryTick();
            battery.Start();
        }

        public void Balloon(string title, string text, ToolTipIcon kind, string url)
        {
            balloonUrl = url; balloonShowsWindow = false;
            icon.ShowBalloonTip(kind == ToolTipIcon.Warning ? 5000 : 3000, title, text, kind);
        }

        public void UpdateFound(string version)
        {
            balloonUrl = null; balloonShowsWindow = true;
            icon.ShowBalloonTip(8000, "Glowstick", Lang.F("Version {0} is available. Click to update.", version), ToolTipIcon.Info);
        }

        void Build()
        {
            menu.Items.Clear();
            UpdateInfo u = UiApp.Update;
            if (u != null)
                menu.Items.Add(new ToolStripMenuItem(Lang.F("Update to {0}", u.Version), null, (s, e) => UiApp.InstallUpdate())
                    { Font = new Font(menu.Font, FontStyle.Bold), Enabled = !UiApp.Updating });
            menu.Items.Add(Lang.T("Open"), null, (s, e) => UiApp.ShowMain());
            menu.Items.Add("-");
            ToolStripMenuItem presets = new ToolStripMenuItem(Lang.T("Presets"));
            List<Preset> ps = Engine.Presets;
            AddPresetItems(presets, ps[Math.Max(0, Math.Min(Engine.PresetIndex, ps.Count - 1))].Name, name =>
            {
                int idx = Engine.Presets.FindIndex(p => p.Name == name);
                if (idx >= 0) { Engine.SetPreset(idx); Settings.Save(); }
            });
            menu.Items.Add(presets);
            menu.Items.Add(new ToolStripMenuItem(Lang.T("Touchpad as mouse"), null, (s, e) => { Engine.TouchMouse = !Engine.TouchMouse; Settings.Save(); }) { Checked = Engine.TouchMouse });
            menu.Items.Add("-");
            menu.Items.Add(Lang.T("Controller test…"), null, (s, e) => UiApp.ShowPage(MainWindow.Page.Test));
            menu.Items.Add(Lang.T("Settings…"), null, (s, e) => UiApp.ShowPage(MainWindow.Page.Settings));
            menu.Items.Add(Lang.T("About Glowstick…"), null, (s, e) => UiApp.ShowAbout());
            menu.Items.Add("-");
            menu.Items.Add(Lang.T("Remove drivers and settings…"), null, (s, e) => UiApp.UninstallAll());
            menu.Items.Add("-");
            menu.Items.Add(Lang.T("Exit"), null, (s, e) => UiApp.Exit());
        }

        // Preset items grouped by category
        public static void AddPresetItems(ToolStripMenuItem parent, string selected, Action<string> pick)
        {
            ToolStripMenuItem cat = null;
            foreach (Preset p in Engine.Presets)
            {
                if (cat == null || cat.Tag as string != p.Category) { cat = new ToolStripMenuItem(p.CategoryTitle) { Tag = p.Category }; parent.DropDownItems.Add(cat); }
                string name = p.Name;
                ToolStripMenuItem it = new ToolStripMenuItem((p.Locked ? "🔒 " : "") + p.Title, null, (s, e) => pick(name)) { Enabled = !p.Locked };
                if (name == selected) { it.Checked = true; cat.Checked = true; }
                cat.DropDownItems.Add(it);
            }
        }

        // Tray icon — the charge of the first controller (as the tiles are ordered); notifications at 25% and 12% for every one (not while charging)
        void BatteryTick()
        {
            ControllerInfo[] snap = Engine.Snapshot();
            if (Pro.BatteryTick != null) Pro.BatteryTick(snap);   // Pro: battery history
            int first = -1;
            for (int k = 0; k < snap.Length; k++)
            {
                ControllerInfo c = snap[k];
                if (c.Battery < 0) continue;
                if (first < 0) first = c.Battery;
                int had; lowWarned.TryGetValue(c.Id, out had);
                int level = c.Charging ? 0 : c.Battery <= 12 ? 2 : c.Battery <= 25 ? 1 : 0;
                if (c.Charging || c.Battery >= 50) lowWarned[c.Id] = 0;   // charged — warn again next time
                else if (level > had && !Showcase.On)   // the showcase's made-up low charge warns nobody
                {
                    lowWarned[c.Id] = level;
                    Engine.Flash(c.Id, Rgb.Hex(0xFF0000), 3);
                    Balloon(level == 2 ? Lang.T("Glowstick — controller will turn off soon") : Lang.T("Glowstick — low battery"),
                        Lang.F("{0}: {1}% left. Time to plug in the charger.", Engine.DisplayName(c, k), c.Battery), ToolTipIcon.Warning, null);
                }
            }

            int seg = first < 0 ? 0 : BatteryIcon.Segments(first);
            if (seg != batterySegments)
            {
                batterySegments = seg;
                Icon old = batteryIcon;
                batteryIcon = seg > 0 ? BatteryIcon.Make(seg) : null;
                icon.Icon = batteryIcon ?? UiApp.AppIcon;
                if (old != null) old.Dispose();
            }
            string text = TrayText.Tooltip(snap);
            if (icon.Text != text) icon.Text = text;
        }

        public void Dispose()
        {
            battery.Stop();
            icon.Visible = false;
            icon.Dispose();
            if (batteryIcon != null) batteryIcon.Dispose();
        }
    }

    // Tray menu in the app's dark colors
    internal class DarkMenuRenderer : ToolStripProfessionalRenderer
    {
        static readonly Color Text = Color.FromArgb(0xE8, 0xE8, 0xEE), Muted = Color.FromArgb(0x9A, 0x9A, 0xA6);

        public DarkMenuRenderer() : base(new DarkColors()) { RoundedEdges = false; }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = e.Item.Enabled ? Text : Muted;
            base.OnRenderItemText(e);
        }

        protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
        {
            e.ArrowColor = Muted;
            base.OnRenderArrow(e);
        }

        protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
        {
            Rectangle r = e.ImageRectangle;
            using (Pen p = new Pen(Color.FromArgb(0x60, 0xA5, 0xFA), 2))
            {
                e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                e.Graphics.DrawLines(p, new[] { new Point(r.Left + 3, r.Top + r.Height / 2), new Point(r.Left + r.Width / 2 - 1, r.Bottom - 4), new Point(r.Right - 3, r.Top + 4) });
            }
        }

        class DarkColors : ProfessionalColorTable
        {
            static readonly Color Bg = Color.FromArgb(0x2A, 0x2A, 0x30), Hover = Color.FromArgb(0x38, 0x38, 0x40), Border = Color.FromArgb(0x34, 0x34, 0x3B);
            public override Color ToolStripDropDownBackground { get { return Bg; } }
            public override Color ImageMarginGradientBegin { get { return Bg; } }
            public override Color ImageMarginGradientMiddle { get { return Bg; } }
            public override Color ImageMarginGradientEnd { get { return Bg; } }
            public override Color MenuBorder { get { return Border; } }
            public override Color MenuItemBorder { get { return Hover; } }
            public override Color MenuItemSelected { get { return Hover; } }
            public override Color MenuItemSelectedGradientBegin { get { return Hover; } }
            public override Color MenuItemSelectedGradientEnd { get { return Hover; } }
            public override Color MenuItemPressedGradientBegin { get { return Hover; } }
            public override Color MenuItemPressedGradientEnd { get { return Hover; } }
            public override Color SeparatorDark { get { return Border; } }
            public override Color SeparatorLight { get { return Bg; } }
            public override Color CheckBackground { get { return Bg; } }
            public override Color CheckSelectedBackground { get { return Hover; } }
            public override Color CheckPressedBackground { get { return Hover; } }
        }
    }
}
