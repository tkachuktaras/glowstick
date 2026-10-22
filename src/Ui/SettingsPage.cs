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

            CheckBox upd = Kit.Toggle(general, Lang.T("Check for updates once a day (GitHub Releases; nothing is downloaded by itself)"), Updater.Enabled, v => Updater.Enabled = v);
            upd.IsEnabled = Updater.Available;
            WrapPanel buttons = new WrapPanel { Margin = new Thickness(0, 12, 0, 0) };
            Button check = new Button { Content = Lang.T("Check now"), IsEnabled = Updater.Available, Margin = new Thickness(0, 0, 8, 8) };
            check.Click += (s, e) => UiApp.CheckUpdates(true);
            Button log = new Button { Content = Lang.T("Open log folder"), ToolTip = Lang.T("log.txt — attach it to a bug report"), Margin = new Thickness(0, 0, 8, 8) };
            log.Click += (s, e) => { Directory.CreateDirectory(Settings.Dir); UiApp.OpenUrl(Settings.Dir); };
            Button tray = new Button { Content = Lang.T("Minimize to tray"), Margin = new Thickness(0, 0, 8, 8) };
            tray.Click += (s, e) => UiApp.MinimizeToTray();
            Button exit = new Button { Content = Lang.T("Exit"), Margin = new Thickness(0, 0, 8, 8), ToolTip = Lang.T("The ✕ of the window only hides it to the tray") };
            exit.Click += (s, e) => UiApp.Exit();
            buttons.Children.Add(check); buttons.Children.Add(log); buttons.Children.Add(tray); buttons.Children.Add(exit);
            general.Children.Add(buttons);

            // ---------- light bar ----------
            StackPanel light = Section(Lang.T("Light bar"));
            Kit.Toggle(light, Lang.T("Notification flashes: controller connected — white, low battery — red"), Engine.NotifyFlash, v => Engine.NotifyFlash = v);
            Kit.Toggle(light, Lang.T("DualSense: show the controller number on the player LEDs"), Controller.PlayerLedsOn, v => Controller.PlayerLedsOn = v);
            Minutes(light, Lang.T("Turn off the light bar if the controller wasn't touched"), Engine.IdleDimMin, v => Engine.IdleDimMin = v);

            // ---------- controller ----------
            StackPanel ctl = Section(Lang.T("Controller"));
            Minutes(ctl, Lang.T("Turn off a Bluetooth controller if it wasn't touched"), Engine.IdleOffMin, v => Engine.IdleOffMin = v);
            // The touchpad and the PS hotkeys are on the Input mapping page (ControlSettings)

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

        // A section of this page (the Pro module adds its own through this too)
        internal StackPanel Section(string title) { return Kit.Card(page, title); }

        internal static FrameworkElement Row(string label, FrameworkElement control) { return Kit.Row(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center }, control, 170); }

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

        public void OnTick() { }
    }
}
