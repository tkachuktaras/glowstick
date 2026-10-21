// First-run setup: two short steps on the very first start (no settings.json yet) instead of a bare window —
// language, start with Windows. Skippable; everything can be changed later in Settings.
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Glowstick
{
    internal static class FirstRun
    {
        public const int Steps = 2;

        // Not when started in the tray (autostart): the setup waits until the window is opened normally
        public static bool Needed(bool settingsExisted, bool tray) { return !settingsExisted && !tray; }

        // What the user picked (kept across steps and a window reload after the language change)
        static bool autostart = true;

        public static void Show(MainWindow w, int step)
        {
            StackPanel p = new StackPanel { Width = 460 };
            p.Children.Add(Dots(step));
            Button next = new Button { Content = step == Steps - 1 ? Lang.T("Done") : Lang.T("Next"), Style = Theme.Style("AccentButton"), MinWidth = 110, IsDefault = true };
            Button back = new Button { Content = Lang.T("Back"), MinWidth = 90, Margin = new Thickness(0, 0, 8, 0), Visibility = step > 0 ? Visibility.Visible : Visibility.Collapsed };
            Button skip = new Button { Content = Lang.T("Skip"), Style = Theme.Style("GhostButton") };

            if (step == 0)
            {
                Title(p, Lang.T("Welcome to Glowstick"), Lang.T("Light bar control for DualShock 4 and DualSense."));
                p.Children.Add(new TextBlock { Text = Lang.T("Choose the language"), Margin = new Thickness(0, 6, 0, 6) });
                ComboBox lang = new ComboBox();
                lang.Items.Add(Lang.T("As in Windows"));
                foreach (string[] l in Lang.Languages) lang.Items.Add(l[1]);
                lang.SelectedIndex = Array.FindIndex(Lang.Languages, l => l[0] == Lang.Setting) + 1;
                p.Children.Add(lang);
                next.Click += (s, e) =>
                {
                    string code = lang.SelectedIndex <= 0 ? "" : Lang.Languages[lang.SelectedIndex - 1][0];
                    w.CloseOverlay();
                    if (code != Lang.Setting)
                    {
                        // The whole window is built from translated text — rebuild it in the new language
                        Lang.Init(code);
                        Settings.Save();
                        UiApp.ReloadWindow();
                        Show(UiApp.Main, 1);
                    }
                    else Show(w, 1);
                };
            }
            else
            {
                Title(p, Lang.T("Start with Windows (in the tray)"), Lang.T("Glowstick can start with Windows and wait in the tray next to the clock — the light bars are set before you open a game."));
                CheckBox auto = new CheckBox { Content = Lang.T("Start with Windows (in the tray)"), IsChecked = autostart && Engine.IsAdmin, IsEnabled = Engine.IsAdmin, Margin = new Thickness(0, 6, 0, 0) };
                auto.Click += (s, e) => autostart = auto.IsChecked == true;
                p.Children.Add(auto);
                next.Click += (s, e) => { w.CloseOverlay(); Finish(); };
                back.Click += (s, e) => { w.CloseOverlay(); Show(w, 0); };
            }
            skip.Click += (s, e) => { w.CloseOverlay(); Settings.FirstRun = false; Settings.Save(); };

            DockPanel row = new DockPanel { Margin = new Thickness(0, 22, 0, 0) };
            DockPanel.SetDock(skip, Dock.Left);
            row.Children.Add(skip);
            StackPanel right = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            right.Children.Add(back); right.Children.Add(next);
            row.Children.Add(right);
            p.Children.Add(row);
            w.ShowOverlay(p, null, true);   // modal: Esc doesn't drop the setup halfway (Skip does)
        }

        static void Title(Panel p, string title, string text)
        {
            p.Children.Add(new TextBlock { Text = title, FontSize = 20, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 8), TextWrapping = TextWrapping.Wrap });
            p.Children.Add(new TextBlock { Text = text, Style = Theme.Style("MutedText"), FontSize = 13, Margin = new Thickness(0, 0, 0, 12) });
        }

        // ● ○ ○ — where we are
        static FrameworkElement Dots(int step)
        {
            StackPanel d = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 16) };
            for (int k = 0; k < Steps; k++)
                d.Children.Add(new Ellipse { Width = k == step ? 22 : 8, Height = 8, Margin = new Thickness(0, 0, 6, 0),
                    Fill = Theme.Brush(k == step ? "Accent" : "Track") });
            return d;
        }

        static void Finish()
        {
            Settings.FirstRun = false;
            if (Engine.IsAdmin)
            {
                if (autostart) { if (!Autostart.Exists(Autostart.LogonTask)) Autostart.Create(Autostart.LogonTask, true, "-Tray"); }
                else Autostart.Delete(Autostart.LogonTask);
            }
            Settings.Save();
            UiApp.Main.RefreshSettings();
            UiApp.OfferDrivers();
        }
    }
}
