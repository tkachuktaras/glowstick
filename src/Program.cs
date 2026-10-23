// Entry point: administrator rights (needed for HidHide), single instance, embedded ViGEm library.
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Principal;
using System.Threading;
using System.Windows.Forms;

namespace Glowstick
{
    static class Program
    {
        internal const string ShowEventName = @"Local\Glowstick_Show";

        [STAThread]
        static void Main(string[] args)
        {
            AppDomain.CurrentDomain.AssemblyResolve += ResolveEmbedded;
            Run(args);
        }

        // Nefarius.ViGEm.Client.dll is embedded in the exe — so the app is a single file.
        internal static Assembly ResolveEmbedded(object sender, ResolveEventArgs e)
        {
            string name = new AssemblyName(e.Name).Name + ".dll";
            using (Stream s = Assembly.GetExecutingAssembly().GetManifestResourceStream(name))
            {
                if (s == null) return null;
                byte[] data = new byte[s.Length];
                s.Read(data, 0, data.Length);
                return Assembly.Load(data);
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        static void Run(string[] args)
        {
            bool tray = Array.Exists(args, a => string.Equals(a, "-Tray", StringComparison.OrdinalIgnoreCase));
            bool admin = new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);
            Showcase.Parse(args);
            if (Showcase.On) { RunShowcase(); return; }

            if (!admin)
            {
                // Without UAC — through our own scheduled task, which starts the protected copy in Program Files (if that copy
                // isn't older than this exe); otherwise — a regular UAC prompt, and the elevated run refreshes the copy
                string task = tray ? Autostart.LogonTask : Autostart.UiTask;
                if (Autostart.PointsHere(task) && Installer.InstalledIsUsable() && Autostart.RunTask(task)) return;
                try
                {
                    ProcessStartInfo psi = new ProcessStartInfo(Application.ExecutablePath, tray ? "-Tray" : "");
                    psi.UseShellExecute = true; psi.Verb = "runas";
                    Process.Start(psi);
                    return;
                }
                catch (Win32Exception) { }   // UAC cancelled — run without game mode
            }

            bool created;
            Mutex mutex = new Mutex(true, @"Local\Glowstick_SingleInstance", out created);
            // Started by an update: the previous version is releasing the controllers and exiting — wait for it
            bool updated = Array.Exists(args, a => a == "--updated");
            if (!created && updated)
            {
                try { created = mutex.WaitOne(20000); }
                catch (AbandonedMutexException) { created = true; }
            }
            if (!created)
            {
                EventWaitHandle ev;
                if (EventWaitHandle.TryOpenExisting(ShowEventName, out ev)) { ev.Set(); ev.Dispose(); }
                else { Lang.Init(""); MessageBox.Show(Lang.T("Glowstick is already running (look in the tray next to the clock)."), "Glowstick"); }
                return;
            }

            try { Process.GetCurrentProcess().PriorityClass = ProcessPriorityClass.High; } catch { }
            Engine.IsAdmin = admin;
            Pro.Startup();   // the Pro module (Pro build) fills its hooks before the settings are read
            Settings.Load();
            Log.Info("start " + Updater.Current + (admin ? " (admin)" : "") + ", lang " + Lang.Code + ", " + Environment.OSVersion);

            if (admin)
            {
                ThreadPool.QueueUserWorkItem(_ =>
                {
                    Autostart.Target = Installer.Ensure();
                    Autostart.MigrateOld();
                    if (Autostart.Target == null)
                    {
                        // No protected copy: a task pointing at a replaceable exe would be a way around UAC — remove them
                        Autostart.Delete(Autostart.UiTask);
                        if (Autostart.Exists(Autostart.LogonTask)) { Autostart.Delete(Autostart.LogonTask); Log.Info("autostart removed: no protected copy"); }
                        return;
                    }
                    if (!Autostart.PointsHere(Autostart.UiTask)) Autostart.Create(Autostart.UiTask, false, null);
                    if (Autostart.Exists(Autostart.LogonTask) && !Autostart.PointsHere(Autostart.LogonTask)) Autostart.Create(Autostart.LogonTask, true, "-Tray");
                });
            }

            // If the app crashes — don't leave the controllers hidden from games
            AppDomain.CurrentDomain.UnhandledException += (s, e) => { Log.Error("unhandled", e.ExceptionObject as Exception); try { Engine.Stop(); } catch { } };

            Application.EnableVisualStyles();   // WinForms is used only for the tray icon and its menu
            UiApp.Run(tray, updated);
            GC.KeepAlive(mutex);
        }

        // Demo data only (Showcase): no admin rights, no drivers, no single-instance check — it runs next to the real Glowstick
        // and never touches its controllers, settings or scheduled tasks
        static void RunShowcase()
        {
            Engine.IsAdmin = false;
            // English numbers too (8.3 h, not 8,3 h), whatever the PC's regional format
            System.Globalization.CultureInfo en = new System.Globalization.CultureInfo("en-US");
            Thread.CurrentThread.CurrentCulture = en; System.Globalization.CultureInfo.DefaultThreadCurrentCulture = en;
            Pro.Startup();
            Settings.Load();
            License.ShowcaseKey();
            Log.Info("showcase " + Updater.Current + (Showcase.ShotsDir != null ? ", shots → " + Showcase.ShotsDir : ""));
            Application.EnableVisualStyles();
            UiApp.Run(false, false);
        }
    }
}
