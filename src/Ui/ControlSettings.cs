// The controller's own settings that live on the Input mapping page (free and Pro): the touchpad as a mouse and the
// PS hotkeys. Editing the hotkey combos is Pro — the free build shows the table locked with its defaults.
using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;

namespace Glowstick
{
    internal static class ControlSettings
    {
        // Touchpad as mouse: the switch (it follows the PS hotkey / tray toggle too) and its options
        public static void Touchpad(Panel p)
        {
            CheckBox on = Kit.Toggle(p, Lang.T("Touchpad as mouse: one finger — cursor, two fingers — scroll, click — left button (with two fingers — right).\nTurns itself off in fullscreen games."),
                Engine.TouchMouse, v => Engine.TouchMouse = v);
            Follow(on, () => Engine.TouchMouse);
            StackPanel opts = new StackPanel { Margin = new Thickness(50, 0, 0, 0) };
            TouchSettings.Build(opts, Engine.Touch);
            p.Children.Add(opts);
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
