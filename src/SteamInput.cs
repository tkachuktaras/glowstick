// Steam Input conflict check: with Steam running and its PlayStation controller support on, Steam wraps the controller a game
// sees (Glowstick's virtual DualShock 4) in its own virtual Xbox pad — double input and wrong button prompts in the game.
// Steam's config files are only read (config.vdf and each account's localconfig.vdf), at most every few seconds.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;

namespace Glowstick
{
    internal static class SteamInput
    {
        // A setting such as "...PS4Support" / "...PlayStationSupport" / "...ps5_support" set to 1 (Valve doesn't document the names)
        static readonly Regex Setting = new Regex(@"^\s*""[^""]*(ps4|ps5|ds4|dualsense|dualshock|playstation)[^""]*support[^""]*""\s+""([^""]*)""", RegexOptions.IgnoreCase | RegexOptions.Multiline);

        static long checkedMs = -100000;
        static volatile string cached = null;
        static int busy;

        // Null — nothing to warn about; otherwise the warning for the Controllers page. Never blocks (it is asked from the UI
        // thread): the answer is worked out on a pool thread every few seconds and this returns the last one.
        public static string Warning()
        {
            long now = Engine.Ms;
            if (now - checkedMs >= 5000 && Interlocked.CompareExchange(ref busy, 1, 0) == 0)
            {
                checkedMs = now;
                ThreadPool.QueueUserWorkItem(_ => { try { cached = Compute(); } finally { busy = 0; } });
            }
            return cached;
        }

        static string Compute()
        {
            try
            {
                if (!Engine.GameModeWorking || Showcase.On || !Running()) return null;
                if (PlayStationSupport(Dirs()) != true) return null;
                return Lang.T("Steam Input is on for PlayStation controllers: games started from Steam may see the controller twice and show wrong button prompts. In Steam: Settings → Controller → turn off \"PlayStation Controller Support\" — or per game: Properties → Controller → Disable Steam Input.");
            }
            catch { return null; }
        }

        static bool Running()
        {
            foreach (string n in new[] { "steam", "steamwebhelper" })
            {
                Process[] ps = Process.GetProcessesByName(n);
                bool any = ps.Length > 0;
                foreach (Process p in ps) p.Dispose();
                if (any) return true;
            }
            return false;
        }

        // The Steam config files that can hold the setting
        static string[] Dirs()
        {
            string steam = SteamDir();
            if (steam == null) return new string[0];
            System.Collections.Generic.List<string> files = new System.Collections.Generic.List<string> { Path.Combine(Path.Combine(steam, "config"), "config.vdf") };
            string ud = Path.Combine(steam, "userdata");
            if (Directory.Exists(ud))
                foreach (string acc in Directory.GetDirectories(ud)) files.Add(Path.Combine(Path.Combine(acc, "config"), "localconfig.vdf"));
            return files.ToArray();
        }

        static string SteamDir()
        {
            try
            {
                using (Microsoft.Win32.RegistryKey k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam"))
                {
                    string p = k != null ? k.GetValue("SteamPath") as string : null;
                    if (!string.IsNullOrEmpty(p)) return p.Replace('/', '\\');
                }
            }
            catch { }
            return null;
        }

        // true — one of the files turns PlayStation support on; false — a setting is there and it's off; null — no such setting
        internal static bool? PlayStationSupport(string[] files)
        {
            bool? result = null;
            foreach (string f in files)
            {
                bool? one = Read(f);
                if (one == true) return true;
                if (one == false) result = false;
            }
            return result;
        }

        // What each file said last time, by its size and write time — the big localconfig.vdf isn't read again until it changes
        static readonly Dictionary<string, KeyValuePair<string, bool?>> seen = new Dictionary<string, KeyValuePair<string, bool?>>();

        static bool? Read(string f)
        {
            try
            {
                FileInfo fi = new FileInfo(f);
                if (!fi.Exists) return null;
                string stamp = fi.Length + "|" + fi.LastWriteTimeUtc.Ticks;
                KeyValuePair<string, bool?> old;
                lock (seen) if (seen.TryGetValue(f, out old) && old.Key == stamp) return old.Value;
                bool? result = null;
                foreach (Match m in Setting.Matches(File.ReadAllText(f)))
                {
                    if (m.Groups[2].Value == "1") { result = true; break; }
                    result = false;
                }
                lock (seen) seen[f] = new KeyValuePair<string, bool?>(stamp, result);
                return result;
            }
            catch { return null; }
        }
    }
}
