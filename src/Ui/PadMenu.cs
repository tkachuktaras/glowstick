// Controller menu (click on a tile): name, own preset, battery, test, turning off. The order is changed by dragging the tiles.
// Every action looks the controller up again when clicked: if it's gone by then, nothing happens.
using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;

namespace Glowstick
{
    internal static class PadMenu
    {
        // Runs the action only if the controller is still connected
        public static bool IfPresent(string id, Action action)
        {
            if (Array.FindIndex(Engine.Snapshot(), c => c.Id == id) < 0) return false;
            action();
            return true;
        }

        public static ContextMenu Build(MainWindow w, string id)
        {
            ContextMenu m = new ContextMenu();
            ControllerInfo[] snap = Engine.Snapshot();
            int k = Array.FindIndex(snap, c => c.Id == id);
            if (k < 0) return m;
            ControllerInfo info = snap[k];

            m.Items.Add(new MenuItem { Header = (k + 1) + ". " + Engine.DisplayName(info, k) + " — " + info.Model + ", " + info.Kind, IsEnabled = false });
            if (info.Warning != null) m.Items.Add(new MenuItem { Header = "⚠ " + info.Warning, IsEnabled = false, Foreground = Theme.Brush("Warn") });
            m.Items.Add(new Separator());
            Add(m, Lang.T("Rename…"), () => Rename(w, id, info.Name));   // Rename checks presence on OK

            MenuItem own = new MenuItem { Header = Lang.T("Own preset") };
            MenuItem same = new MenuItem { Header = Lang.T("Same as everyone"), IsChecked = info.OwnPreset == null };
            same.Click += (s, e) => IfPresent(id, () => Engine.SetPadPreset(id, null));
            own.Items.Add(same);
            own.Items.Add(new Separator());
            AddPresetItems(own, info.OwnPreset, name => IfPresent(id, () => Engine.SetPadPreset(id, name)));
            m.Items.Add(own);

            // What games see this controller as — Pro (a virtual DualSense isn't possible, so the choices are DS4 / Xbox 360)
            if (Pro.Unlocked)
            {
                MenuItem outItem = new MenuItem { Header = Lang.T("Show to games as") };
                MenuItem asDs4 = new MenuItem { Header = "DualShock 4", IsChecked = info.Output != "Xbox" };
                MenuItem asXbox = new MenuItem { Header = "Xbox 360", IsChecked = info.Output == "Xbox" };
                asDs4.Click += (s, e) => IfPresent(id, () => Engine.SetPadOutput(id, "DS4"));
                asXbox.Click += (s, e) => IfPresent(id, () => Engine.SetPadOutput(id, "Xbox"));
                outItem.Items.Add(asDs4);
                outItem.Items.Add(asXbox);
                m.Items.Add(outItem);
            }
            else
            {
                MenuItem outItem = new MenuItem { Header = "🔒 " + Lang.T("Show to games as") };
                outItem.Click += (s, e) => { if (w != null) w.ShowPro(); };
                m.Items.Add(outItem);
            }

            // Per-controller brightness (on top of the global brightness) — Pro: a slider right in the menu, applied while
            // dragged, saved when let go (it goes into the controller's profile, where the Profiles page shows it too)
            if (Pro.Unlocked)
            {
                // One row like the Settings sliders: the label, then the slider to the right edge
                TextBlock label = new TextBlock { Text = Lang.F("Brightness: {0}%", (int)Math.Round(info.Brightness * 100)), MinWidth = 120, VerticalAlignment = VerticalAlignment.Center };
                Slider sl = new Slider { Minimum = 10, Maximum = 100, Value = Math.Round(info.Brightness * 100), MinWidth = 140, Margin = new Thickness(12, 0, 0, 0),
                    VerticalAlignment = VerticalAlignment.Center, SmallChange = 5, LargeChange = 10, IsSnapToTickEnabled = true, TickFrequency = 5 };
                sl.ValueChanged += (s, e) =>
                {
                    label.Text = Lang.F("Brightness: {0}%", (int)sl.Value);
                    IfPresent(id, () => Engine.SetPadBrightness(id, sl.Value / 100, false));
                };
                sl.PreviewMouseUp += (s, e) => Settings.Save();
                sl.KeyUp += (s, e) => Settings.Save();
                DockPanel row = new DockPanel();
                DockPanel.SetDock(label, Dock.Left);
                row.Children.Add(label); row.Children.Add(sl);
                m.Items.Add(new MenuItem { Header = row, StaysOpenOnClick = true, Style = Theme.Style("MenuSlot") });
            }
            else
            {
                MenuItem br = new MenuItem { Header = "🔒 " + Lang.T("Brightness") };
                br.Click += (s, e) => { if (w != null) w.ShowPro(); };
                m.Items.Add(br);
            }

            if (Pro.Unlocked && Pro.PadMenu != null) Pro.PadMenu(m, id);   // e.g. the controller's input profile

            m.Items.Add(new Separator());
            Add(m, Lang.T("Show battery on the light bar"), () => IfPresent(id, () => Engine.ShowBattery(id)));
            MenuItem test = new MenuItem { Header = Lang.T("Controller test…") };
            test.Click += (s, e) => w.Navigate(MainWindow.Page.Test);
            m.Items.Add(test);
            Add(m, Lang.T("Turn off (Bluetooth)"), () => IfPresent(id, () => Engine.Disconnect(id))).IsEnabled = info.Kind == "Bluetooth";
            return m;
        }

        static MenuItem Add(ContextMenu m, string header, Action action)
        {
            MenuItem it = new MenuItem { Header = header };
            it.Click += (s, e) => action();
            m.Items.Add(it);
            return it;
        }

        public static void Rename(MainWindow w, string id, string current)
        {
            Dialogs.Prompt(w, Lang.T("Controller name"), Lang.T("Name (empty — “Controller N”):"), current, 24,
                name => IfPresent(id, () => Engine.SetPadName(id, name)));
        }

        // Presets grouped by category, the selected one checked (with its category)
        public static void AddPresetItems(ItemsControl parent, string selected, Action<string> pick)
        {
            MenuItem cat = null;
            foreach (Preset p in Engine.Presets)
            {
                if (cat == null || (string)cat.Tag != p.Category)
                {
                    cat = new MenuItem { Header = p.CategoryTitle, Tag = p.Category };
                    parent.Items.Add(cat);
                }
                string name = p.Name;
                MenuItem it = new MenuItem { Header = (p.Locked ? "🔒 " : "") + p.Title, IsChecked = name == selected, IsEnabled = !p.Locked };
                it.Click += (s, e) => pick(name);
                if (it.IsChecked) cat.IsChecked = true;
                cat.Items.Add(it);
            }
        }
    }
}
