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
            ComboBox lang = new ComboBox { Width = BoxWidth };
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

            // How often a DualShock 4 reports over Bluetooth (the output packet asks for it)
            int[] polls = { 0, 2, 4, 8, 16 };
            string[] pollLabels = Array.ConvertAll(polls, ms => ms == 0 ? Lang.T("As fast as it goes (default)") : Lang.F("Every {0} ms (~{1} Hz)", ms, 1000 / ms));
            ComboBox poll = Kit.Combo(pollLabels, Math.Max(0, Array.IndexOf(polls, Engine.BtPollMs)), i => Engine.SetBtPollMs(polls[i]));
            poll.Width = BoxWidth;
            FrameworkElement pollRow = Row(Lang.T("Bluetooth rate:"), poll);
            pollRow.Margin = new Thickness(0, 10, 0, 0);
            general.Children.Add(pollRow);
            TextBlock pollNote = new TextBlock { Text = Lang.T("DualShock 4 over Bluetooth: how often it sends its input. Slower — less load when several controllers share one adapter, a little longer battery."),
                Style = Theme.Style("MutedText"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(120, 4, 0, 0) };
            general.Children.Add(pollNote);

            CheckBox auto = new CheckBox { Content = Lang.T("Start with Windows (in the tray)"), IsEnabled = Engine.IsAdmin || Showcase.On,
                IsChecked = Showcase.On || Engine.IsAdmin && Autostart.Exists(Autostart.LogonTask), Margin = new Thickness(0, 10, 0, 0) };
            auto.Click += (s, e) =>
            {
                if (Showcase.On) return;   // shown as it looks with admin rights; the real scheduled tasks stay untouched
                if (auto.IsChecked == true) { if (!Autostart.Create(Autostart.LogonTask, true, "-Tray")) auto.IsChecked = false; }
                else Autostart.Delete(Autostart.LogonTask);
            };
            general.Children.Add(auto);

            CheckBox upd = Kit.Toggle(general, Lang.T("Check for updates at every start and once a day (GitHub Releases; nothing is downloaded by itself)"), Updater.Enabled, v => Updater.Enabled = v);
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

            // ---------- backup ----------
            StackPanel backup = Section(Lang.T("Backup"));
            backup.Children.Add(new TextBlock { Style = Theme.Style("MutedText"), TextWrapping = TextWrapping.Wrap,
                Text = Lang.T("Everything in one file: presets, profiles, game profiles, macros and the battery history. A copy is also made by itself before every update.") });
            WrapPanel backupButtons = new WrapPanel { Margin = new Thickness(0, 12, 0, 0) };
            Button save = new Button { Content = Lang.T("Back up…"), Margin = new Thickness(0, 0, 8, 8) };
            save.Click += (s, e) => SaveBackup();
            Button restore = new Button { Content = Lang.T("Restore…"), Margin = new Thickness(0, 0, 8, 8) };
            restore.Click += (s, e) => RestoreBackup();
            Button autoDir = new Button { Content = Lang.T("Automatic copies"), Margin = new Thickness(0, 0, 8, 8) };
            autoDir.Click += (s, e) => { Directory.CreateDirectory(Backup.AutoDir); UiApp.OpenUrl(Backup.AutoDir); };
            backupButtons.Children.Add(save); backupButtons.Children.Add(restore); backupButtons.Children.Add(autoDir);
            backup.Children.Add(backupButtons);

            // The light bar, power, touchpad and hotkey settings are in the profile (Profiles page, ControlSettings)
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

        static string BackupFilter { get { return Lang.T("Glowstick backup") + " (*" + Backup.Extension + ")|*" + Backup.Extension; } }

        void SaveBackup()
        {
            Microsoft.Win32.SaveFileDialog d = new Microsoft.Win32.SaveFileDialog { Filter = BackupFilter, Title = Lang.T("Back up…"),
                FileName = "Glowstick " + DateTime.Now.ToString("yyyy-MM-dd") + Backup.Extension };
            if (d.ShowDialog(UiApp.Main) != true) return;
            try { Backup.Create(d.FileName); }
            catch (Exception ex) { Dialogs.Info(UiApp.Main, "Glowstick", Lang.F("Couldn't save the backup: {0}", ex.Message)); }
        }

        void RestoreBackup()
        {
            Microsoft.Win32.OpenFileDialog d = new Microsoft.Win32.OpenFileDialog { Filter = BackupFilter, Title = Lang.T("Restore…") };
            if (Directory.Exists(Backup.AutoDir)) d.InitialDirectory = Backup.AutoDir;
            if (d.ShowDialog(UiApp.Main) != true) return;
            string file = d.FileName;
            Dialogs.Confirm(UiApp.Main, "Glowstick", Lang.T("Replace all the settings with the ones from this backup? Glowstick restarts."), () =>
            {
                string err = Backup.Restore(file);
                if (err != null) Dialogs.Info(UiApp.Main, "Glowstick", Lang.F("Couldn't restore the backup: {0}", err));
                else UiApp.Restart();
            });
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

        // A labelled setting: every label in one narrow column, every box the same width (BoxWidth) right after it
        internal const double BoxWidth = 280;
        internal static FrameworkElement Row(string label, FrameworkElement control)
        {
            Grid g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });
            g.ColumnDefinitions.Add(new ColumnDefinition());
            g.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 10, 0) });
            control.HorizontalAlignment = HorizontalAlignment.Left;
            Grid.SetColumn(control, 1);
            g.Children.Add(control);
            return g;
        }

        public void OnTick() { }
    }
}
