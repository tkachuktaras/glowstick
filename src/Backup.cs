// Settings backup: one .zip with settings.json (presets, profiles, game profiles, macros, the Pro key…) and the battery
// history. Made by hand in Settings, and by itself before every update (Settings.Dir\backups, the newest few kept).
// Restoring replaces those files; the app then restarts so nothing of the old settings is written back over them.
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Web.Script.Serialization;

namespace Glowstick
{
    internal static class Backup
    {
        public const string Extension = ".zip";
        const string SettingsEntry = "settings.json", BatteryDir = "battery";
        const int KeepAuto = 5;
        public static string AutoDir { get { return Path.Combine(Settings.Dir, "backups"); } }

        // Writes the backup; the current settings are saved first so it holds exactly what the app has now
        public static void Create(string zipPath)
        {
            Settings.Save();
            Create(zipPath, Settings.Dir);
        }

        internal static void Create(string zipPath, string dir)
        {
            string tmp = zipPath + ".tmp";
            if (File.Exists(tmp)) File.Delete(tmp);
            using (ZipArchive zip = ZipFile.Open(tmp, ZipArchiveMode.Create))
            {
                string settings = Path.Combine(dir, SettingsEntry);
                if (File.Exists(settings)) zip.CreateEntryFromFile(settings, SettingsEntry);
                string battery = Path.Combine(dir, BatteryDir);
                if (Directory.Exists(battery))
                    foreach (string f in Directory.GetFiles(battery, "*.csv")) zip.CreateEntryFromFile(f, BatteryDir + "/" + Path.GetFileName(f));
            }
            if (File.Exists(zipPath)) File.Delete(zipPath);
            File.Move(tmp, zipPath);
        }

        // Before an update: a copy named after the version being replaced; only the newest few are kept
        public static void Auto()
        {
            try
            {
                Directory.CreateDirectory(AutoDir);
                Create(Path.Combine(AutoDir, "before-" + Updater.Current.ToString(3) + "-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + Extension));
                List<string> all = new List<string>(Directory.GetFiles(AutoDir, "before-*" + Extension));
                all.Sort((a, b) => File.GetLastWriteTimeUtc(b).CompareTo(File.GetLastWriteTimeUtc(a)));
                for (int k = KeepAuto; k < all.Count; k++) File.Delete(all[k]);
            }
            catch (Exception e) { Log.Error("auto backup", e); }
        }

        // null — restored; otherwise what's wrong (the current files are left as they were)
        public static string Restore(string zipPath) { return Restore(zipPath, Settings.Dir); }

        internal static string Restore(string zipPath, string dir)
        {
            try
            {
                using (ZipArchive zip = ZipFile.OpenRead(zipPath))
                {
                    ZipArchiveEntry settings = zip.GetEntry(SettingsEntry);
                    if (settings == null) return Lang.T("This file isn't a Glowstick backup.");
                    string json;
                    using (StreamReader r = new StreamReader(settings.Open(), Encoding.UTF8)) json = r.ReadToEnd();
                    if (!(new JavaScriptSerializer { MaxJsonLength = int.MaxValue }.DeserializeObject(json) is Dictionary<string, object>))
                        return Lang.T("This file isn't a Glowstick backup.");

                    Directory.CreateDirectory(dir);
                    string target = Path.Combine(dir, SettingsEntry), tmp = target + ".restore";
                    File.WriteAllText(tmp, json, new UTF8Encoding(false));
                    if (File.Exists(target)) File.Replace(tmp, target, target + ".bak", true);
                    else File.Move(tmp, target);

                    string battery = Path.Combine(dir, BatteryDir);
                    if (Directory.Exists(battery)) foreach (string f in Directory.GetFiles(battery, "*.csv")) File.Delete(f);
                    foreach (ZipArchiveEntry e in zip.Entries)
                    {
                        if (!e.FullName.StartsWith(BatteryDir + "/") || !e.Name.EndsWith(".csv") || e.Name != Path.GetFileName(e.Name)) continue;
                        Directory.CreateDirectory(battery);
                        e.ExtractToFile(Path.Combine(battery, e.Name), true);
                    }
                }
                return null;
            }
            catch (InvalidDataException) { return Lang.T("This file isn't a Glowstick backup."); }
            catch (Exception e) { Log.Error("restore", e); return e.Message; }
        }
    }
}
