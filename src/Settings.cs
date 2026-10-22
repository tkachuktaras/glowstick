// Settings in %APPDATA%\Glowstick\settings.json.
// First start after the rename: settings.json is copied from the old %APPDATA%\DS4Colors folder;
// built-in preset names that old versions stored in Ukrainian are converted to the current ones (PresetLib.Migrate).
using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;

namespace Glowstick
{
    internal static class Settings
    {
        static readonly string AppData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        public static readonly string Dir = Path.Combine(AppData, "Glowstick");
        public static readonly string OldDir = Path.Combine(AppData, "DS4Colors");
        static readonly string FilePath = Path.Combine(Dir, "settings.json");
        static string BackupPath { get { return FilePath + ".bak"; } }   // the previous good copy (File.Replace keeps it)

        public static string StartPreset = "Red + Blue";
        public static bool FirstRun;   // there was no settings.json at start — the first-run setup is shown
        static bool loaded;   // until loaded — don't save, so the file isn't overwritten with defaults

        public static void Load()
        {
            try
            {
                string oldFile = Path.Combine(OldDir, "settings.json");
                if (!File.Exists(FilePath) && File.Exists(oldFile))
                {
                    Directory.CreateDirectory(Dir);
                    File.Copy(oldFile, FilePath);
                }
                if (!File.Exists(FilePath) && !File.Exists(BackupPath)) { FirstRun = true; Lang.Init(""); return; }
                Dictionary<string, object> s = Read(FilePath);
                if (s == null)
                {
                    s = Read(BackupPath);   // the file is damaged (e.g. the PC lost power while writing) — the previous copy
                    if (s == null) { Log.Info("settings: neither settings.json nor its backup could be read"); Lang.Init(""); return; }
                    Log.Info("settings: settings.json was damaged — loaded the backup");
                    mainDamaged = true;
                }
                loadedRaw = s;
                object v;
                Lang.Init(s.TryGetValue("Language", out v) ? v as string : "");
                if (s.TryGetValue("Preset", out v) && v is string) StartPreset = (string)v;
                if (s.TryGetValue("Brightness", out v)) Engine.Brightness = Convert.ToDouble(v);
                if (s.TryGetValue("Speed", out v)) Engine.Speed = Convert.ToDouble(v);
                if (s.TryGetValue("MusicReaction", out v)) Live.MusicReaction = Math.Max(0.5, Math.Min(3, Convert.ToDouble(v)));
                if (s.TryGetValue("BtPollMs", out v)) Engine.BtPollMs = Math.Max(0, Math.Min(16, Convert.ToInt32(v)));
                if (s.TryGetValue("NotifyFlash", out v)) Engine.Defaults.NotifyFlash = Convert.ToBoolean(v);
                if (s.TryGetValue("Hotkeys", out v)) Engine.Hotkeys = Convert.ToBoolean(v);
                if (s.TryGetValue("HotkeyMap", out v)) HotkeyMap.Load(v);
                if (s.TryGetValue("TouchMouse", out v)) Engine.TouchMouse = Convert.ToBoolean(v);
                if (s.TryGetValue("Touch", out v)) Engine.Touch = TouchConfig.Load(v);
                else if (s.TryGetValue("TouchSens", out v)) Engine.Touch.Sens = Math.Max(0.2, Math.Min(4, Convert.ToDouble(v)));   // before 1.2
                if (s.TryGetValue("IdleDimMin", out v)) Engine.Defaults.IdleDimMin = Math.Max(0, Convert.ToInt32(v));
                if (s.TryGetValue("IdleOffMin", out v)) Engine.Defaults.IdleOffMin = Math.Max(0, Convert.ToInt32(v));
                if (s.TryGetValue("PlayerLeds", out v)) Engine.Defaults.PlayerLeds = Convert.ToBoolean(v);
                if (s.TryGetValue("CheckUpdates", out v)) Updater.Enabled = Convert.ToBoolean(v);
                DateTime last;
                if (s.TryGetValue("LastUpdateCheck", out v) && v is string &&
                    DateTime.TryParse((string)v, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out last)) Updater.LastCheck = last;
                License.Load(s);   // the Pro key (also in the free build — it downloads Pro with it)
                StartPreset = PresetLib.Migrate(StartPreset);
                lock (Engine.Lock)
                {
                    if (s.TryGetValue("Order", out v) && v is IEnumerable)
                        foreach (object o in (IEnumerable)v) if (o is string && !Engine.Order.Contains((string)o)) Engine.Order.Add((string)o);
                    if (s.TryGetValue("Pads", out v) && v is Dictionary<string, object>)
                        foreach (KeyValuePair<string, object> kv in (Dictionary<string, object>)v)
                        {
                            Dictionary<string, object> d = kv.Value as Dictionary<string, object>;
                            if (d == null) continue;
                            PadConfig c = new PadConfig();
                            if (d.TryGetValue("Name", out v) && v is string) c.Name = (string)v;
                            if (d.TryGetValue("Preset", out v) && v is string) c.Preset = PresetLib.Migrate((string)v);
                            if (d.TryGetValue("Output", out v) && v is string) c.Output = (string)v;
                            if (d.TryGetValue("Brightness", out v)) c.Brightness = Math.Max(0.1, Math.Min(1.0, Convert.ToDouble(v)));
                            Engine.Pads[kv.Key] = c;
                        }
                }
                if (Pro.Load != null) Pro.Load(s);   // custom presets, profiles, game profiles, API — the Pro module's part (after the controllers: it moves their old own presets into profiles)
            }
            catch (Exception e) { Log.Error("settings load", e); if (Lang.Code == "en" && Lang.Setting == "") Lang.Init(""); }
            finally { loaded = true; }
        }


        static Dictionary<string, object> Read(string path)
        {
            try { return File.Exists(path) ? new JavaScriptSerializer { MaxJsonLength = int.MaxValue }.Deserialize<Dictionary<string, object>>(File.ReadAllText(path, Encoding.UTF8)) : null; }
            catch (Exception e) { Log.Error("settings read " + Path.GetFileName(path), e); return null; }
        }

        static readonly object saveLock = new object();
        static bool mainDamaged;   // loaded from the backup: the next save must not turn the damaged file into the backup
        static System.Threading.Timer soon;

        // For changes that come per keystroke (names, macro text): one save half a second after the last change
        public static void SaveSoon()
        {
            lock (saveLock)
            {
                if (soon == null) soon = new System.Threading.Timer(_ => Save(), null, System.Threading.Timeout.Infinite, System.Threading.Timeout.Infinite);
                soon.Change(500, System.Threading.Timeout.Infinite);
            }
        }
        // What was read: keys this build doesn't know (the Pro module's, when the free build runs) are written back untouched,
        // so switching between the free and the Pro version loses nothing
        static Dictionary<string, object> loadedRaw;

        static string CurrentPreset()
        {
            List<Preset> ps = Engine.Presets;
            return ps[Math.Max(0, Math.Min(Engine.PresetIndex, ps.Count - 1))].Name;
        }

        // Called both from the window and from the light bar thread (hotkeys)
        public static void Save()
        {
            if (!loaded) return;
            lock (saveLock) try
            {
                Directory.CreateDirectory(Dir);
                Dictionary<string, object> s = loadedRaw != null ? new Dictionary<string, object>(loadedRaw) : new Dictionary<string, object>();
                s["Language"] = Lang.Setting;
                s["Preset"] = Pro.SavedPreset != null ? Pro.SavedPreset() : CurrentPreset();   // during a game profile — what was there before it
                s["Brightness"] = Engine.Brightness;
                s["Speed"] = Engine.Speed;
                s["MusicReaction"] = Live.MusicReaction;
                s.Remove("GameMode"); s.Remove("VirtualPad");   // game mode is always on, the virtual pad is always a DS4
                s["BtPollMs"] = Engine.BtPollMs;
                s["NotifyFlash"] = Engine.Defaults.NotifyFlash;
                s["Hotkeys"] = Engine.Hotkeys;
                s["HotkeyMap"] = HotkeyMap.Save();
                s["TouchMouse"] = Engine.TouchMouse;
                s["Touch"] = Engine.Touch.Save();
                s.Remove("TouchSens");
                s["IdleDimMin"] = Engine.Defaults.IdleDimMin;
                s["IdleOffMin"] = Engine.Defaults.IdleOffMin;
                s["PlayerLeds"] = Engine.Defaults.PlayerLeds;
                s["CheckUpdates"] = Updater.Enabled;
                s["LastUpdateCheck"] = Updater.LastCheck.ToString("o", CultureInfo.InvariantCulture);
                License.Save(s);
                if (Pro.Save != null) Pro.Save(s);
                lock (Engine.Lock)
                {
                    s["Order"] = new List<string>(Engine.Order);
                    Dictionary<string, object> pads = new Dictionary<string, object>();
                    foreach (KeyValuePair<string, PadConfig> kv in Engine.Pads)
                        if (kv.Value.Name != "" || kv.Value.Preset != "" || kv.Value.Output != "" || kv.Value.Brightness != 1.0)
                            pads[kv.Key] = new Dictionary<string, object> { { "Name", kv.Value.Name }, { "Preset", kv.Value.Preset }, { "Output", kv.Value.Output }, { "Brightness", kv.Value.Brightness } };
                    s["Pads"] = pads;
                }
                // Write a temporary file, then swap it in: a crash or power loss mid-write never leaves a broken settings.json
                string tmp = FilePath + ".tmp";
                File.WriteAllText(tmp, new JavaScriptSerializer { MaxJsonLength = int.MaxValue }.Serialize(s), new UTF8Encoding(false));
                if (mainDamaged) { File.Delete(FilePath); mainDamaged = false; }
                if (File.Exists(FilePath)) File.Replace(tmp, FilePath, BackupPath, true);
                else File.Move(tmp, FilePath);
            }
            catch (Exception e) { Log.Error("settings save", e); }
        }

        public static string ToHex(Rgb rgb) { Color c = rgb.ToColor(); return string.Format("#{0:X2}{1:X2}{2:X2}", c.R, c.G, c.B); }
        public static Rgb FromHex(string hex) { return Rgb.FromColor(ColorTranslator.FromHtml(hex)); }
    }
}
