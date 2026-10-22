// The elevated scheduled tasks (Autostart) must never point at an exe a normal user can overwrite: a portable exe in
// Downloads could be swapped by any program and then started as administrator through the task, without a UAC prompt.
// So when Glowstick runs elevated, it keeps a copy of itself in %ProgramFiles%\Glowstick (writable only by administrators)
// and the tasks start that copy. A normal launch goes through the task only when that copy isn't older than the exe the
// user started; otherwise it asks for UAC, and the elevated run refreshes the copy.
using System;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Windows.Forms;

namespace Glowstick
{
    internal static class Installer
    {
        public static string Dir { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Glowstick"); } }
        public static string Exe { get { return Path.Combine(Dir, "Glowstick.exe"); } }

        static string Running { get { return Path.GetFullPath(Application.ExecutablePath); } }
        public static bool RunningInstalled { get { return string.Equals(Running, Exe, StringComparison.OrdinalIgnoreCase); } }

        static Version VersionOf(string file) { try { return AssemblyName.GetAssemblyName(file).Version; } catch { return null; } }

        static string HashOf(string file)
        {
            try
            {
                using (SHA256 sha = SHA256.Create())
                using (FileStream fs = File.OpenRead(file))
                    return BitConverter.ToString(sha.ComputeHash(fs));
            }
            catch { return null; }
        }

        // Copy the running exe over the installed one? — none there, the running one is newer, or the same version but a
        // different build (e.g. free ↔ Pro)
        internal static bool ShouldCopy(Version mine, string myHash, Version installed, string installedHash)
        {
            if (installed == null) return true;
            if (Updater.Newer(mine, installed)) return true;
            return !Updater.Newer(installed, mine) && myHash != installedHash;
        }

        // A normal (non-elevated) launch may start the installed copy through the task: it is at least this version, and
        // with the same version it is the same build
        internal static bool TaskMayRun(Version mine, string myHash, Version installed, string installedHash)
        {
            if (installed == null) return false;
            if (Updater.Newer(installed, mine)) return true;
            return !Updater.Newer(mine, installed) && myHash == installedHash;
        }

        public static bool InstalledIsUsable()
        {
            if (RunningInstalled) return true;
            if (!File.Exists(Exe)) return false;
            return TaskMayRun(Updater.Current, HashOf(Running), VersionOf(Exe), HashOf(Exe));
        }

        // Elevated: makes sure the protected copy is there and current. Returns its path for the tasks, or null — the tasks
        // must not be created (the copy failed; a UAC prompt on every start is better than an unprotected task).
        public static string Ensure()
        {
            if (RunningInstalled) return Exe;
            try
            {
                string mine = HashOf(Running);
                bool exists = File.Exists(Exe);
                if (ShouldCopy(Updater.Current, mine, exists ? VersionOf(Exe) : null, exists ? HashOf(Exe) : null))
                {
                    Directory.CreateDirectory(Dir);
                    string tmp = Exe + ".new";
                    File.Copy(Running, tmp, true);
                    if (HashOf(tmp) != mine) { File.Delete(tmp); return null; }
                    if (exists) File.Replace(tmp, Exe, null); else File.Move(tmp, Exe);
                    Log.Info("installed a protected copy: " + Exe);
                }
                return Exe;
            }
            catch (Exception e) { Log.Error("protected copy", e); return null; }
        }

        // Full removal: the protected copy goes too (if it's the one running, it is deleted right after Glowstick exits)
        public static void Remove()
        {
            try
            {
                if (!Directory.Exists(Dir)) return;
                if (!RunningInstalled) { Directory.Delete(Dir, true); return; }
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("cmd.exe",
                    "/c timeout /t 3 /nobreak >nul & rmdir /s /q \"" + Dir + "\"") { CreateNoWindow = true, UseShellExecute = false });
            }
            catch (Exception e) { Log.Error("remove protected copy", e); }
        }
    }
}
