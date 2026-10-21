// Settings page: everything from the classic settings window plus autostart, minimize to tray, about and full removal.
// Every change is saved right away.
using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;

namespace Glowstick
{
    internal class SettingsPage : UserControl
    {
        readonly StackPanel page = new StackPanel { Margin = new Thickness(28, 20, 28, 28), MaxWidth = 820, HorizontalAlignment = HorizontalAlignment.Left };
        CheckBox touch;

        public SettingsPage()
        {
            page.Children.Add(new TextBlock { Text = Lang.T("Settings"), Style = Theme.Style("PageTitle") });

            // ---------- general ----------
            StackPanel general = Section(Lang.T("General"));
            ComboBox lang = new ComboBox { Width = 220 };
            lang.Items.Add(Lang.T("As in Windows"));
            foreach (string[] l in Lang.Languages) lang.Items.Add(l[1]);
            lang.SelectedIndex = Array.FindIndex(Lang.Languages, l => l[0] == Lang.Setting) + 1;
            TextBlock restart = new TextBlock { Style = Theme.Style("MutedText"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };
            lang.SelectionChanged += (s, e) =>
            {
                Lang.Setting = lang.SelectedIndex == 0 ? "" : Lang.Languages[lang.SelectedIndex - 1][0];
                Settings.Save();
                restart.Text = Lang.T("Applies after restart");
            };
            StackPanel langRow = new StackPanel { Orientation = Orientation.Horizontal };
            langRow.Children.Add(lang); langRow.Children.Add(restart);
            general.Children.Add(Row(Lang.T("Language:"), langRow));

            CheckBox auto = new CheckBox { Content = Lang.T("Start with Windows (in the tray)"), IsEnabled = Engine.IsAdmin,
                IsChecked = Engine.IsAdmin && Autostart.Exists(Autostart.LogonTask), Margin = new Thickness(0, 10, 0, 0) };
            auto.Click += (s, e) =>
            {
                if (auto.IsChecked == true) { if (!Autostart.Create(Autostart.LogonTask, true, "-Tray")) auto.IsChecked = false; }
                else Autostart.Delete(Autostart.LogonTask);
            };
            general.Children.Add(auto);

            CheckBox upd = Toggle(general, Lang.T("Check for updates once a day (GitHub Releases; nothing is downloaded by itself)"), Updater.Enabled, v => Updater.Enabled = v);
            upd.IsEnabled = Updater.Available;
            StackPanel buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 12, 0, 0) };
            Button check = new Button { Content = Lang.T("Check now"), IsEnabled = Updater.Available, Margin = new Thickness(0, 0, 8, 0) };
            check.Click += (s, e) => UiApp.CheckUpdates(true);
            Button log = new Button { Content = Lang.T("Open log folder"), ToolTip = Lang.T("log.txt — attach it to a bug report"), Margin = new Thickness(0, 0, 8, 0) };
            log.Click += (s, e) => { Directory.CreateDirectory(Settings.Dir); UiApp.OpenUrl(Settings.Dir); };
            Button tray = new Button { Content = Lang.T("Minimize to tray") };
            tray.Click += (s, e) => UiApp.MinimizeToTray();
            buttons.Children.Add(check); buttons.Children.Add(log); buttons.Children.Add(tray);
            general.Children.Add(buttons);

            // ---------- light bar ----------
            StackPanel light = Section(Lang.T("Light bar"));
            Toggle(light, Lang.T("Notification flashes: controller connected — white, low battery — red"), Engine.NotifyFlash, v => Engine.NotifyFlash = v);
            Toggle(light, Lang.T("DualSense: show the controller number on the player LEDs"), Controller.PlayerLedsOn, v => Controller.PlayerLedsOn = v);
            Minutes(light, Lang.T("Turn off the light bar if the controller wasn't touched"), Engine.IdleDimMin, v => Engine.IdleDimMin = v);

            // ---------- controller ----------
            StackPanel ctl = Section(Lang.T("Controller"));
            Minutes(ctl, Lang.T("Turn off a Bluetooth controller if it wasn't touched"), Engine.IdleOffMin, v => Engine.IdleOffMin = v);
            Toggle(ctl, Lang.T("Hotkeys on the controller (hold PS and press):"), Engine.Hotkeys, v => Engine.Hotkeys = v);
            ctl.Children.Add(HotkeyTable());
            touch = Toggle(ctl, Lang.T("Touchpad as mouse: one finger — cursor, two fingers — scroll, click — left button (with two fingers — right).\nTurns itself off in fullscreen games."),
                Engine.TouchMouse, v => Engine.TouchMouse = v);
            TextBlock lblSens = new TextBlock { VerticalAlignment = VerticalAlignment.Center };
            Slider sens = new Slider { Minimum = 20, Maximum = 400, Value = Math.Max(20, Math.Min(400, Math.Round(Engine.TouchSens * 100))) };
            Action showSens = () => lblSens.Text = Lang.F("Sensitivity: {0}%", (int)Math.Round(sens.Value));
            sens.ValueChanged += (s, e) => { Engine.TouchSens = Math.Round(sens.Value) / 100.0; showSens(); };
            sens.PreviewMouseUp += (s, e) => Settings.Save();
            showSens();
            FrameworkElement sensRow = Row(lblSens, sens, 220);
            sensRow.Margin = new Thickness(50, 8, 0, 0);
            ctl.Children.Add(sensRow);

            LicenseSettings.Add(this);   // the Pro key — in the free build too (it downloads Pro)

            // Pro: Local API (and whatever else the module adds here)
            if (Pro.Unlocked && Pro.SettingsPageCreated != null) Pro.SettingsPageCreated(this);
            else if (!Pro.Unlocked) LockedPages.LocalApi(this);

            // ---------- about ----------
            StackPanel about = Section(Lang.T("About Glowstick"));
            about.Children.Add(new TextBlock { Text = UiApp.AboutText(), TextWrapping = TextWrapping.Wrap, Style = Theme.Style("MutedText"), FontSize = 12.5 });
            Button remove = new Button { Content = Lang.T("Remove drivers and settings…"), Foreground = Theme.Brush("Danger"), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 14, 0, 0) };
            remove.Click += (s, e) => UiApp.UninstallAll();
            about.Children.Add(remove);

            Content = new ScrollViewer { Content = page, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        }

        internal StackPanel ProSection;   // set by LicenseSettings
        internal TextBox ProKey;          // the key box (only while Pro isn't activated)

        // Scrolls so the Glowstick Pro section is at the top and puts the cursor into the key box
        public void ScrollToPro()
        {
            if (ProSection == null) return;
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
            {
                ScrollViewer sv = (ScrollViewer)Content;
                UIElement header = page.Children[Math.Max(0, page.Children.IndexOf((UIElement)ProSection.Parent) - 1)];
                sv.ScrollToVerticalOffset(sv.VerticalOffset + header.TranslatePoint(new Point(0, 0), sv).Y - 8);
                if (ProKey != null) ProKey.Focus();
            }));
        }

        internal StackPanel Section(string title)
        {
            page.Children.Add(new TextBlock { Text = title, Style = Theme.Style("Section") });
            StackPanel body = new StackPanel();
            page.Children.Add(new Border { Style = Theme.Style("CardBox"), Padding = new Thickness(18, 14, 18, 16), Child = body });
            return body;
        }

        internal static FrameworkElement Row(string label, FrameworkElement control) { return Row(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center }, control, 170); }

        internal static FrameworkElement Row(TextBlock label, FrameworkElement control, double labelWidth)
        {
            Grid g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(labelWidth) });
            g.ColumnDefinitions.Add(new ColumnDefinition());
            g.Children.Add(label);
            Grid.SetColumn(control, 1);
            g.Children.Add(control);
            return g;
        }

        internal static CheckBox Toggle(Panel parent, string text, bool value, Action<bool> set)
        {
            CheckBox c = new CheckBox { Content = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap }, IsChecked = value, Margin = new Thickness(0, parent.Children.Count > 0 ? 10 : 0, 0, 0) };
            c.Click += (s, e) => { set(c.IsChecked == true); Settings.Save(); };
            parent.Children.Add(c);
            return c;
        }

        // "…wasn't touched (min, 0 — never):" with a number box 0–240
        static void Minutes(Panel parent, string text, int value, Action<int> set)
        {
            DockPanel row = new DockPanel { Margin = new Thickness(0, parent.Children.Count > 0 ? 10 : 0, 0, 0) };
            TextBox box = new TextBox { Width = 70, Text = Math.Max(0, Math.Min(240, value)).ToString(), MaxLength = 3, HorizontalContentAlignment = HorizontalAlignment.Center };
            box.PreviewTextInput += (s, e) => { foreach (char ch in e.Text) if (!char.IsDigit(ch)) e.Handled = true; };
            // Saved as you type — focus doesn't always leave the box (a click on an empty spot, closing the window)
            box.TextChanged += (s, e) =>
            {
                int v;
                if (!int.TryParse(box.Text, out v)) return;   // empty while retyping — keep the last value
                set(Math.Max(0, Math.Min(240, v))); Settings.Save();
            };
            // Leaving the box or Enter: show what was actually saved (empty → 0, over 240 → 240)
            Action tidy = () =>
            {
                int v;
                if (!int.TryParse(box.Text, out v)) { v = 0; set(0); Settings.Save(); }
                string shown = Math.Max(0, Math.Min(240, v)).ToString();
                if (box.Text != shown) box.Text = shown;
            };
            box.LostKeyboardFocus += (s, e) => tidy();
            box.KeyDown += (s, e) => { if (e.Key == Key.Enter) tidy(); };
            DockPanel.SetDock(box, Dock.Right);
            row.Children.Add(box);
            row.Children.Add(new TextBlock { Text = text + " " + Lang.T("(min, 0 — never):"), TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) });
            parent.Children.Add(row);
        }

        // For each action: PS + a combo of buttons. Editing the combos is Pro — the free build shows the table locked,
        // and a click leads to Settings → Glowstick Pro (where it already is).
        FrameworkElement HotkeyTable()
        {
            string[] titles = { Lang.T("Previous preset"), Lang.T("Next preset"), Lang.T("Brighter"), Lang.T("Dimmer"),
                                Lang.T("Show battery on the light bar"), Lang.T("Touchpad as mouse (on/off)"), Lang.T("Turn off a Bluetooth controller") };
            bool pro = Pro.Unlocked;
            StackPanel table = new StackPanel { Margin = new Thickness(50, 8, 0, 0) };
            List<Button> picks = new List<Button>();
            Action refresh = () =>
            {
                for (int k = 0; k < picks.Count; k++) picks[k].Content = "PS + " + HotkeyMap.ComboName(HotkeyMap.Get(HotkeyMap.Actions[k]));
            };
            foreach (HotAction a in HotkeyMap.Actions)
            {
                HotAction act = a;
                Button pick = new Button { Width = 220, HorizontalAlignment = HorizontalAlignment.Left, HorizontalContentAlignment = HorizontalAlignment.Left, Padding = new Thickness(10, 4, 10, 4) };
                if (pro) pick.Click += (s, e) => ComboPopup(pick, act, refresh);
                picks.Add(pick);
                FrameworkElement row = Row(new TextBlock { Text = titles[(int)a], VerticalAlignment = VerticalAlignment.Center,
                    TextTrimming = TextTrimming.CharacterEllipsis }, pick, 260);
                row.Margin = new Thickness(0, 0, 0, 6);
                table.Children.Add(row);
            }
            refresh();
            if (pro)
            {
                Button reset = new Button { Content = Lang.T("Reset hotkeys"), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 4, 0, 0) };
                reset.Click += (s, e) => { HotkeyMap.Reset(); Settings.Save(); refresh(); };
                table.Children.Add(reset);
            }
            table.Children.Add(new TextBlock { Style = Theme.Style("MutedText"), Margin = new Thickness(0, 8, 0, 0), TextWrapping = TextWrapping.Wrap,
                Text = pro ? Lang.T("Tap a combo to change it. These combos don't reach the game; a plain PS press does.")
                           : Lang.T("Changing hotkeys is part of Glowstick Pro — any combo of buttons for any action. The free version uses these defaults.") });
            if (pro) return table;

            // Locked: the table is shown disabled, a 🔒 badge on it, a click leads to the Pro section
            table.IsEnabled = false; table.Opacity = 0.55;
            Grid wrap = new Grid();
            wrap.Children.Add(table);
            wrap.Children.Add(LockedPages.ToPro(new Border { Background = System.Windows.Media.Brushes.Transparent, ToolTip = Lang.T("Available in Glowstick Pro") }));
            return wrap;
        }

        // A little popup with a toggle for every button: the pressed ones make the combo (none = off)
        void ComboPopup(UIElement at, HotAction act, Action refresh)
        {
            WrapPanel chips = new WrapPanel { MaxWidth = 260, Margin = new Thickness(10) };
            foreach (Btn b in HotkeyMap.Choices)
            {
                Btn btn = b;
                ToggleButton t = new ToggleButton { Content = HotkeyMap.Name(b), Margin = new Thickness(0, 0, 6, 6), Padding = new Thickness(8, 3, 8, 3),
                    IsChecked = (HotkeyMap.Get(act) & b) != 0 };
                t.Click += (s, e) =>
                {
                    Btn combo = HotkeyMap.Get(act);
                    combo = t.IsChecked == true ? combo | btn : combo & ~btn;
                    HotkeyMap.Set(act, combo);
                    Settings.Save();
                    refresh();
                };
                chips.Children.Add(t);
            }
            Popup pop = new Popup { PlacementTarget = at, Placement = PlacementMode.Bottom, StaysOpen = false, AllowsTransparency = true };
            pop.Child = new Border { Style = Theme.Style("CardBox"), Background = Theme.Brush("Card"), Child = chips };
            pop.IsOpen = true;
        }

        // Touchpad mode is toggled by a hotkey too — follow it
        public void OnTick()
        {
            bool t = Engine.TouchMouse;
            if ((touch.IsChecked == true) != t) touch.IsChecked = t;
        }
    }
}
