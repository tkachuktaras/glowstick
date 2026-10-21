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

            // Per-controller brightness (on top of the global brightness) — Pro
            if (Pro.Unlocked)
            {
                MenuItem br = new MenuItem { Header = Lang.T("Brightness") };
                int cur = (int)Math.Round(info.Brightness * 100);
                foreach (int pct in new[] { 100, 75, 50, 25 })
                {
                    int p = pct;
                    MenuItem it = new MenuItem { Header = p + "%", IsChecked = cur == p };
                    it.Click += (s, e) => IfPresent(id, () => Engine.SetPadBrightness(id, p / 100.0));
                    br.Items.Add(it);
                }
                m.Items.Add(br);
            }
            else
            {
                MenuItem br = new MenuItem { Header = "🔒 " + Lang.T("Brightness") };
                br.Click += (s, e) => { if (w != null) w.ShowPro(); };
                m.Items.Add(br);
            }

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
