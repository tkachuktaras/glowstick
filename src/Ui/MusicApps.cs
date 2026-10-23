// Which apps music presets listen to (AudioCapture.AppMode / Apps): everything, only some apps, or everything but some —
// a dialog on the Presets page. Apps are picked from the ones playing sound now, or as an exe. Applied at once.
using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace Glowstick
{
    internal static class MusicApps
    {
        static readonly string[] Modes = { "", "only", "except" };

        // One line for the Presets page: what music presets listen to now
        public static string Summary()
        {
            if (!AudioCapture.Filtered) return Lang.T("Listens to: everything the PC plays");
            string apps = string.Join(", ", Array.ConvertAll(AudioCapture.Apps, Short));
            return AudioCapture.AppMode == "only" ? Lang.F("Listens to: only {0}", apps) : Lang.F("Listens to: everything but {0}", apps);
        }

        static string Short(string exe) { return exe.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? exe.Substring(0, exe.Length - 4) : exe; }

        public static void Show(MainWindow w, Action changed)
        {
            StackPanel p = new StackPanel { Width = 480 };
            p.Children.Add(new TextBlock { Text = Lang.T("Which apps music presets listen to"), FontSize = 18, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 12) });
            ComboBox mode = new ComboBox();
            mode.Items.Add(Lang.T("Everything the PC plays"));
            mode.Items.Add(Lang.T("Only these apps"));
            mode.Items.Add(Lang.T("Everything but these apps"));
            mode.SelectedIndex = Math.Max(0, Array.IndexOf(Modes, AudioCapture.AppMode));
            p.Children.Add(mode);
            if (!AudioCapture.PerAppSupported)
                p.Children.Add(new TextBlock { Text = Lang.T("Listening to single apps needs Windows 11 — here music presets hear everything."), Style = Theme.Style("MutedText"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) });
            StackPanel appsBox = new StackPanel { Margin = new Thickness(0, 14, 0, 0) };
            StackPanel list = new StackPanel();
            appsBox.Children.Add(list);
            WrapPanel add = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
            Button playing = new Button { Content = Lang.T("Add an app playing sound ▾"), Margin = new Thickness(0, 0, 8, 0) };
            Button browse = new Button { Content = Lang.T("Choose exe…") };
            add.Children.Add(playing); add.Children.Add(browse);
            appsBox.Children.Add(add);
            p.Children.Add(appsBox);

            List<string> apps = new List<string>(AudioCapture.Apps);
            Action apply = null, fill = null;
            apply = () =>
            {
                AudioCapture.AppMode = Modes[Math.Max(0, mode.SelectedIndex)];
                AudioCapture.Apps = apps.ToArray();
                appsBox.Visibility = mode.SelectedIndex > 0 ? Visibility.Visible : Visibility.Collapsed;
                Settings.Save();
                if (changed != null) changed();
            };
            Action<string> addApp = exe =>
            {
                if (string.IsNullOrEmpty(exe) || apps.Exists(a => string.Equals(a, exe, StringComparison.OrdinalIgnoreCase))) return;
                apps.Add(exe); fill(); apply();
            };
            fill = () =>
            {
                list.Children.Clear();
                if (apps.Count == 0) list.Children.Add(new TextBlock { Text = Lang.T("No apps yet."), Style = Theme.Style("MutedText") });
                foreach (string a in apps)
                {
                    string app = a;
                    DockPanel row = new DockPanel { Margin = new Thickness(0, 0, 0, 4) };
                    Button del = new Button { Content = "", FontFamily = Theme.IconFont, Style = Theme.Style("GhostButton"), ToolTip = Lang.T("Delete") };
                    del.Click += (s, e) => { apps.Remove(app); fill(); apply(); };
                    DockPanel.SetDock(del, Dock.Right);
                    row.Children.Add(del);
                    row.Children.Add(new TextBlock { Text = app, VerticalAlignment = VerticalAlignment.Center });
                    list.Children.Add(row);
                }
            };
            mode.SelectionChanged += (s, e) => apply();
            playing.Click += (s, e) =>
            {
                ContextMenu m = new ContextMenu();
                foreach (string exe in AudioCapture.SoundApps())
                {
                    if (apps.Exists(a => string.Equals(a, exe, StringComparison.OrdinalIgnoreCase))) continue;
                    string x = exe;
                    MenuItem mi = new MenuItem { Header = x };
                    mi.Click += (s2, e2) => addApp(x);
                    m.Items.Add(mi);
                }
                if (m.Items.Count == 0) m.Items.Add(new MenuItem { Header = Lang.T("No other app is playing sound now"), IsEnabled = false });
                m.PlacementTarget = playing; m.Placement = PlacementMode.Bottom; m.IsOpen = true;
            };
            browse.Click += (s, e) =>
            {
                Microsoft.Win32.OpenFileDialog d = new Microsoft.Win32.OpenFileDialog { Filter = Lang.T("Programs (*.exe)") + "|*.exe" };
                if (d.ShowDialog(w) == true) addApp(Path.GetFileName(d.FileName));
            };
            fill();
            appsBox.Visibility = mode.SelectedIndex > 0 ? Visibility.Visible : Visibility.Collapsed;

            Button ok = new Button { Content = "OK", Style = Theme.Style("AccentButton"), IsDefault = true, IsCancel = true, MinWidth = 100, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 18, 0, 0) };
            ok.Click += (s, e) => w.CloseOverlay(p);
            p.Children.Add(ok);
            w.ShowOverlay(p);
        }
    }
}
