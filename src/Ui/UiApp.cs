// The WPF client: application object, window and tray lifetime, app-wide actions (drivers, updates, uninstall, about).
// Program.cs has already dealt with admin rights, the single instance and Settings.Load().
using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ToolTipIcon = System.Windows.Forms.ToolTipIcon;

namespace Glowstick
{
    internal static class UiApp
    {
        static Application app;
        static MainWindow main;
        static Tray tray;
        static bool exiting, uninstalled, installing;
        public static MainWindow Main { get { return main; } }

        // Drivers were installed or the install state changed (the game mode card updates its button)
        public static event Action DriversChanged;

        // The exe's own icon (assets\app.ico, embedded by csc /win32icon) — for windows and the tray
        public static readonly System.Drawing.Icon AppIcon = System.Drawing.Icon.ExtractAssociatedIcon(System.Reflection.Assembly.GetExecutingAssembly().Location);

        static readonly Lifetime life = new Lifetime();

        public static void Run(bool startInTray, bool updated)
        {
            app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            // A crash must not leave the controllers hidden from games — in WPF code and in the tray's WinForms handlers
            app.DispatcherUnhandledException += (s, e) => { e.Handled = true; Crash("UI thread", e.Exception); };
            System.Windows.Forms.Application.ThreadException += (s, e) => Crash("tray", e.Exception);
            // Logoff / shutdown: the window may not exist at all (start in the tray), so cleanup can't rely on it
            app.SessionEnding += (s, e) => { life.Removing = false; Exit(); };
            Theme.Load();
            int start = Engine.Presets.FindIndex(p => p.Name == Settings.StartPreset);
            Engine.SetPreset(Math.Max(0, start));
            Engine.Start();
            if (Pro.AppStarted != null) Pro.AppStarted();   // Pro: local API, game profiles
            tray = new Tray();

            // A second launch of the exe signals this event — show the window
            EventWaitHandle show = new EventWaitHandle(false, EventResetMode.AutoReset, Program.ShowEventName);
            Thread waiter = new Thread(() => { while (show.WaitOne()) app.Dispatcher.BeginInvoke(new Action(ShowMain)); });
            waiter.IsBackground = true; waiter.Start();

            if (!startInTray)
            {
                ShowMain();
                if (FirstRun.Needed(!Settings.FirstRun, startInTray)) FirstRun.Show(main, 0);
                else OfferDrivers();
            }
            if (updated)
            {
                ThreadPool.QueueUserWorkItem(_ => Updater.DeleteOld());
                tray.Balloon("Glowstick", Lang.F("Updated to {0}.", Updater.Current.ToString(3)), ToolTipIcon.Info, null);
            }
            else CheckUpdates(false, true);
            // The app can sit in the tray for weeks: look again once a day (CheckAsync skips it until 24 h have passed)
            DispatcherTimer daily = new DispatcherTimer { Interval = TimeSpan.FromHours(1) };
            daily.Tick += (s, e) => CheckUpdates(false);
            daily.Start();
            app.Run();
            Cleanup();   // whatever ended the dispatcher loop
        }

        public static ImageSource IconSource()
        {
            return Imaging.CreateBitmapSourceFromHIcon(AppIcon.Handle, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
        }

        // The window is created on first show (a start in the tray doesn't create it at all)
        public static void ShowMain()
        {
            if (exiting) return;
            if (main == null)
            {
                main = new MainWindow();
                MainWindow mine = main;
                main.Closing += (s, e) =>
                {
                    if (life.Removing) { e.Cancel = true; return; }   // the full removal must finish
                    // ✕ hides the window to the tray — Glowstick keeps lighting and mapping the controllers; Exit is in the tray
                    // menu and in Settings. (Not while exiting / logging off, and not for a window replaced by ReloadWindow.)
                    if (main == mine && !exiting) { e.Cancel = true; MinimizeToTray(); }
                };
                main.Closed += (s, e) => { if (main == mine) Exit(); };
            }
            main.Show();
            if (main.WindowState == WindowState.Minimized) main.WindowState = WindowState.Normal;
            main.Activate();
        }

        public static void ShowPage(MainWindow.Page page) { ShowMain(); main.Navigate(page); }

        // The language changed: build a new window in it and close the old one (the app keeps running)
        public static void ReloadWindow()
        {
            MainWindow old = main;
            main = null;
            ShowMain();
            if (old != null)
            {
                main.Left = old.Left; main.Top = old.Top; main.Width = old.Width; main.Height = old.Height;
                old.Close();
            }
        }

        static bool trayHinted;

        public static void MinimizeToTray()
        {
            if (main != null) main.Hide();
            if (trayHinted) return;   // once per run — not on every ✕
            trayHinted = true;
            tray.Balloon("Glowstick", Lang.T("Running in the tray. Double-click the icon to open."), ToolTipIcon.Info, null);
        }

        // Tray Exit, ✕, logoff. Ignored while the full removal runs (it exits by itself when done).
        public static void Exit()
        {
            if (!Cleanup()) return;
            app.Shutdown();
        }

        // Saves settings, releases the controllers (hiding off, virtual pads gone), removes the tray icon — once
        static bool Cleanup()
        {
            if (!life.TryBegin()) return false;
            exiting = true;
            try { if (!uninstalled) Settings.Save(); } catch (Exception e) { Log.Error("save on exit", e); }
            try { Engine.Stop(); } catch (Exception e) { Log.Error("stop on exit", e); }
            if (Pro.AppExiting != null) try { Pro.AppExiting(); } catch (Exception e) { Log.Error("pro exit", e); }
            if (tray != null) tray.Dispose();
            return true;
        }

        static void Crash(string where, Exception e)
        {
            Log.Error(where, e);
            life.Removing = false;
            Exit();
        }

        public static void OpenUrl(string url)
        {
            Shell.RunAsUser(url, null);   // not elevated: a browser opened from here must not get administrator rights
        }

        // ---------- updates ----------
        static UpdateInfo update;          // a newer release that was found (null — none)
        static bool updating;
        public static UpdateInfo Update { get { return update; } }
        public static bool Updating { get { return updating; } }
        public static event Action UpdateChanged;   // found / downloading / failed — the window's update card follows

        // manual — "Check now" in Settings: also say when there's nothing new or this build can't check;
        // atStart — every start checks (when updates are on), then once a day while the app runs (the hourly timer)
        public static void CheckUpdates(bool manual, bool atStart = false)
        {
            if (manual && !Updater.Available)
            {
                ShowMain();
                Dialogs.Info(main, "Glowstick", Lang.T("This build can't check for updates."));
                return;
            }
            Updater.CheckAsync(manual, u => app.Dispatcher.BeginInvoke(new Action(() =>
            {
                if (u == null)
                {
                    if (manual && main != null) Dialogs.Info(main, "Glowstick", Lang.F("You have the latest version ({0}).", Updater.Current.ToString(3)));
                    return;
                }
                bool fresh = update == null || update.Version != u.Version;
                update = u;
                if (fresh) tray.UpdateFound(u.Version);
                if (UpdateChanged != null) UpdateChanged();
            })), atStart);
        }

        // "Update": download, check, swap the exe, start the new version, exit
        public static void InstallUpdate()
        {
            if (update == null || updating) return;
            updating = true;
            if (UpdateChanged != null) UpdateChanged();
            Updater.DownloadAsync(update, (file, err) => app.Dispatcher.BeginInvoke(new Action(() =>
            {
                if (err == null)
                {
                    string exe = System.Reflection.Assembly.GetExecutingAssembly().Location;
                    err = Updater.Swap(exe, file);
                    if (err == null)
                    {
                        Log.Info("updating to " + update.Version);
                        // The new process waits until this one has released the controllers and exited
                        bool hidden = main == null || !main.IsVisible;
                        try { Process.Start(new ProcessStartInfo(exe, "--updated" + (hidden ? " -Tray" : "")) { UseShellExecute = false }); }
                        catch (Exception e) { err = e.Message; Updater.Rollback(exe); }
                        if (err == null) { Exit(); return; }
                    }
                }
                updating = false;
                if (UpdateChanged != null) UpdateChanged();
                ShowMain();
                Dialogs.Info(main, "Glowstick", Lang.F("Couldn't update: {0}", err));
            })));
        }

        // A key was activated in the free build: the Pro exe is installed the same way as an update
        public static void InstallPro(UpdateInfo pro)
        {
            if (updating) return;
            update = pro;
            InstallUpdate();
        }

        // The Pro build lost its key (revoked, refunded, deactivated): it turns back into the free Glowstick from GitHub,
        // installed the same way as an update. Until that's possible (no release yet, no internet) Pro just stays locked.
        public static void DowngradeToFree()
        {
            if (!Pro.Present || updating || License.Owned) return;
            Log.Info("no Pro license — going back to the free build");
            Updater.FreeLatestAsync((u, err) => app.Dispatcher.BeginInvoke(new Action(() =>
            {
                if (u == null)
                {
                    Log.Info("couldn't get the free build: " + err);
                    if (main != null) ReloadWindow();   // locked in place
                    return;
                }
                tray.Balloon("Glowstick", Lang.T("The Pro license isn't active any more — switching to the free Glowstick."), ToolTipIcon.Info, null);
                update = u;
                InstallUpdate();
            })));
        }

        // ---------- game mode and drivers ----------
        public static bool DriversMissing { get { return Engine.IsAdmin && Drivers.Bundled && !Drivers.AllInstalled; } }
        public static bool Installing { get { return installing; } }

        // Game mode needs the drivers, and they are missing — one question, then a silent install
        public static void OfferDrivers()
        {
            if (installing || Drivers.RebootPending || !DriversMissing) return;
            ShowMain();
            Dialogs.Confirm(main, "Glowstick", Lang.T("Game mode needs the ViGEmBus and HidHide drivers (they are already inside the app).\nInstall them now?"), InstallDrivers);
        }

        public static void InstallDrivers()
        {
            if (installing) return;
            installing = true;
            if (DriversChanged != null) DriversChanged();
            ThreadPool.QueueUserWorkItem(_ =>
            {
                bool reboot;
                string err = Drivers.InstallMissing(out reboot);
                if (err != null) Log.Info("driver install failed: " + err);
                else Log.Info("drivers installed" + (reboot ? ", the installer asked for a reboot" : ""));
                app.Dispatcher.BeginInvoke(new Action(() =>
                {
                    installing = false;
                    // No reboot question: the engine checks whether the drivers actually loaded and says so in the status
                    Engine.DriversChanged();
                    if (DriversChanged != null) DriversChanged();
                    if (err != null) { ShowMain(); Dialogs.Info(main, "Glowstick", Lang.F("Couldn't install the drivers: {0}", err)); }
                }));
            });
        }

        // ---------- about, full removal ----------
        public static string AboutText()
        {
            string repo = Updater.Repo == "" ? "" : "\n\nhttps://github.com/" + Updater.Repo;
            return "Glowstick " + Updater.Current + "\n" + Lang.T("Light bar control for DualShock 4 and DualSense.") + "\n\n" +
                Lang.T("© Glowstick contributors. Free software under the GNU GPL v3 or later; it comes with ABSOLUTELY NO WARRANTY. You may redistribute it under the terms of the license.") + "\n\n" +
                Lang.T("Includes ViGEm.NET (MIT), ViGEmBus (BSD-3-Clause) and HidHide (MIT) by Nefarius Software Solutions.") + "\n" +
                Lang.T("Not affiliated with Sony Interactive Entertainment.") + repo;
        }

        public static void ShowAbout()
        {
            ShowMain();
            Dialogs.Info(main, Lang.T("About Glowstick"), AboutText());
        }

        // Drivers, scheduled tasks, settings. After that the app closes.
        public static void UninstallAll()
        {
            ShowMain();
            if (!Engine.IsAdmin)
            {
                Dialogs.Info(main, "Glowstick", Lang.T("Removing requires administrator rights — restart Glowstick and confirm UAC."));
                return;
            }
            Dialogs.Confirm(main, Lang.T("Glowstick — remove"),
                Lang.F("This will remove:\n• the ViGEmBus and HidHide drivers\n• autostart and Glowstick scheduled tasks\n• settings ({0})\n\nGlowstick will then close. Delete the exe yourself.\nContinue?", Settings.Dir),
                () =>
                {
                    TextBlock progress = new TextBlock { Text = Lang.T("Glowstick — removing…"), FontSize = 16 };
                    life.Removing = true;   // no Esc, no ✕, no tray Exit until it's done
                    main.ShowOverlay(progress, null, true);
                    ThreadPool.QueueUserWorkItem(_ =>
                    {
                        Engine.Stop();   // turn hiding off and disconnect the virtual controllers before removing the drivers
                        Autostart.Delete(Autostart.LogonTask);
                        Autostart.Delete(Autostart.UiTask);
                        Autostart.Delete(Autostart.OldLogonTask);
                        Autostart.Delete(Autostart.OldUiTask);
                        Installer.Remove();
                        bool reboot;
                        string err = Drivers.UninstallAll(out reboot);
                        foreach (string dir in new[] { Settings.Dir, Settings.OldDir })
                            try { if (Directory.Exists(dir)) Directory.Delete(dir, true); } catch { }
                        app.Dispatcher.BeginInvoke(new Action(() =>
                        {
                            uninstalled = true;
                            life.Removing = false;
                            main.CloseOverlay(progress);
                            string text = err != null
                                ? Lang.F("Not everything was removed: {0}\nYou can remove the drivers manually in “Programs and Features”.", err)
                                : Lang.T("Done: drivers, autostart and settings removed.") + (reboot ? "\n" + Lang.T("Restart the computer to finish removing the drivers.") : "");
                            Dialogs.Info(main, "Glowstick", text, Exit);
                        }));
                    });
                });
        }
    }

    // Exit happens once; not while the full removal is running
    internal class Lifetime
    {
        bool done;
        public bool Removing;

        public bool TryBegin()
        {
            if (done || Removing) return false;
            done = true;
            return true;
        }
    }
}
