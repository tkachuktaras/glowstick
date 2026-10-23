// A profile's settings that the free build has too, shared by the free Profiles page and the Pro one: light bar, power (idle
// turn-off), the touchpad as a mouse and the PS hotkeys. Editing the hotkey combos is Pro — the free build shows the table locked.
using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;

namespace Glowstick
{
    internal static class ControlSettings
    {
        // Light bar part of a profile. The Default profile's (Engine.Defaults) preset and brightness are the Presets page's;
        // another profile can have its own.
        public static void Light(Panel p, PadSettings s, bool isDefault)
        {
            if (isDefault) Kit.Note(p, Lang.T("The preset and brightness of the Default profile are the ones chosen on the Presets page."));
            else
            {
                ComboBox preset = ControllersPage.PresetCombo(Lang.T("Same as everyone"));
                foreach (object o in preset.Items)
                {
                    ComboBoxItem it = o as ComboBoxItem;
                    if (it != null && (string)it.Tag == s.Preset) { preset.SelectedItem = it; break; }
                }
                if (preset.SelectedIndex < 0) preset.SelectedIndex = 0;
                preset.SelectionChanged += (o, e) =>
                {
                    ComboBoxItem it = preset.SelectedItem as ComboBoxItem;
                    if (it == null || it.Tag == null) return;
                    s.Preset = (string)it.Tag;
                    Settings.Save();
                };
                preset.MaxWidth = 280; preset.HorizontalAlignment = HorizontalAlignment.Stretch;
                p.Children.Add(Kit.Row(Kit.Label(Lang.T("Preset:")), preset, 240));
                Kit.Slide(p, v => Lang.F("Brightness: {0}%", (int)v), 10, 100, Math.Round(s.Brightness * 100), v => s.Brightness = v / 100.0, 0, 5);
            }
            Kit.Toggle(p, Lang.T("Notification flashes: controller connected — white, low battery — red"), s.NotifyFlash, v => s.NotifyFlash = v);
            Kit.Toggle(p, Lang.T("DualSense: show the controller number on the player LEDs"), s.PlayerLeds, v => s.PlayerLeds = v);
            // DualSense Mute light and rumble kind — Pro
            string[] muteLabels = { Lang.T("Left alone"), Lang.T("The PC microphone is muted"), Lang.T("Low battery (20% or less)"),
                                    Lang.T("Touchpad as mouse is on"), Lang.T("Desktop mode is on") };
            ComboBox mute = Kit.Combo(p, (Pro.Unlocked ? "" : "🔒 ") + Lang.T("DualSense Mute light shows:"), muteLabels, Math.Max(0, Array.IndexOf(PadSettings.MuteLeds, s.MuteLed)), i => s.MuteLed = PadSettings.MuteLeds[i]);
            TextBlock muteNote = Kit.Note(p, Lang.T("Then the Mute button switches the PC microphone (Discord, games, calls)."));
            muteNote.Margin = new Thickness(0, 4, 0, 0);
            CheckBox haptic = Kit.Toggle(p, (Pro.Unlocked ? "" : "🔒 ") + Lang.T("DualSense: haptic rumble (sharper and quieter; firmware 2.24 or newer)"), s.HapticRumble && Pro.Unlocked, v => s.HapticRumble = v);
            if (!Pro.Unlocked) { mute.IsEnabled = false; haptic.IsEnabled = false; }
            Kit.Toggle(p, Lang.T("On the charger and put down, the light bar shows the charge filling up (dim green when full)"), s.ChargeShow, v => s.ChargeShow = v);
            Minutes(p, Lang.T("Turn off the light bar if the controller wasn't touched"), s.IdleDimMin, v => s.IdleDimMin = v);
        }

        // Power part of a profile (Bluetooth idle turn-off). What games see the controller as is Pro — shown for a Pro profile only.
        public static void Power(Panel p, PadSettings s, bool output)
        {
            if (output)
                Kit.Combo(p, Lang.T("Show to games as"), new[] { "DualShock 4", "Xbox 360" }, s.Output == "Xbox" ? 1 : 0, i => s.Output = i == 1 ? "Xbox" : "", 220);
            Minutes(p, Lang.T("Turn off a Bluetooth controller if it wasn't touched"), s.IdleOffMin, v => s.IdleOffMin = v);
        }

        // "…wasn't touched (min, 0 — never):" with a number box 0–240
        static void Minutes(Panel parent, string text, int value, Action<int> set)
        {
            DockPanel row = new DockPanel { Margin = new Thickness(0, parent.Children.Count > 0 ? 10 : 0, 0, 0) };
            TextBox box = new TextBox { Width = 70, Text = Math.Max(0, Math.Min(240, value)).ToString(), MaxLength = 3, HorizontalContentAlignment = HorizontalAlignment.Center };
            box.PreviewTextInput += (s, e) => { foreach (char ch in e.Text) if (!char.IsDigit(ch)) e.Handled = true; };
            // Saved as you type (half a second after the last key) — focus doesn't always leave the box (a click on an empty spot, closing the window)
            box.TextChanged += (s, e) =>
            {
                int v;
                if (!int.TryParse(box.Text, out v)) return;   // empty while retyping — keep the last value
                set(Math.Max(0, Math.Min(240, v))); Settings.SaveSoon();
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

        // Touchpad as mouse: the switch (it follows the PS hotkey / tray toggle too) and its options
        public static void Touchpad(Panel p)
        {
            CheckBox on = Kit.Toggle(p, Lang.T("Touchpad as mouse: one finger — cursor, two fingers — scroll, click — left button (with two fingers — right).\nTurns itself off in fullscreen games."),
                Engine.TouchMouse, v => Engine.TouchMouse = v);
            Follow(on, () => Engine.TouchMouse);
            StackPanel opts = new StackPanel { Margin = new Thickness(50, 0, 0, 0) };
            TouchSettings.Build(opts, Engine.Touch);
            p.Children.Add(opts);
            CheckBox desk = Kit.Toggle(p, Lang.T("Desktop mode: with no game in front the controller drives the PC — left stick cursor, right stick scroll, R2 / L2 click, ✕ Enter, ○ Esc, □ Backspace, △ Space, d-pad arrows, L1 / R1 switch windows, Options Start menu. PS + Share turns it on and off."),
                Engine.DesktopMode, v => Engine.DesktopMode = v);
            desk.Margin = new Thickness(0, 14, 0, 0);
            Follow(desk, () => Engine.DesktopMode);
        }

        // PS hotkeys: on / off and the table of combos
        public static void Hotkeys(Panel p)
        {
            Kit.Toggle(p, Lang.T("Hotkeys on the controller (hold PS and press):"), Engine.Hotkeys, v => Engine.Hotkeys = v);
            p.Children.Add(HotkeyTable());
        }

        // Keep a switch in step with a value other places change (a hotkey, the tray menu) while it's on screen
        static void Follow(CheckBox box, Func<bool> value)
        {
            DispatcherTimer t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
            t.Tick += (s, e) => { bool v = value(); if ((box.IsChecked == true) != v) box.IsChecked = v; };
            box.Loaded += (s, e) => t.Start();
            box.Unloaded += (s, e) => t.Stop();
        }

        // For each action: PS + a combo of buttons. The free build shows the table locked; a click leads to Settings → Glowstick Pro.
        static FrameworkElement HotkeyTable()
        {
            string[] titles = { Lang.T("Previous preset"), Lang.T("Next preset"), Lang.T("Brighter"), Lang.T("Dimmer"),
                                Lang.T("Show battery on the light bar"), Lang.T("Touchpad as mouse (on/off)"), Lang.T("Turn off a Bluetooth controller"),
                                Lang.T("Desktop mode (on/off)"), Lang.T("On-screen keyboard") };
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
                FrameworkElement row = Kit.Row(new TextBlock { Text = titles[(int)a], VerticalAlignment = VerticalAlignment.Center,
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

            // Locked: the table is shown disabled, a click leads to the Pro section
            table.IsEnabled = false; table.Opacity = 0.55;
            Grid wrap = new Grid();
            wrap.Children.Add(table);
            wrap.Children.Add(LockedPages.ToPro(new Border { Background = System.Windows.Media.Brushes.Transparent, ToolTip = Lang.T("Available in Glowstick Pro") }));
            return wrap;
        }

        // A little popup with a toggle for every button: the pressed ones make the combo (none = off)
        static void ComboPopup(UIElement at, HotAction act, Action refresh)
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
    }
}
