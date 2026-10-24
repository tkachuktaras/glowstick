// Localization. Strings in code are English (they're also the keys), translations — lang/<code>.json, embedded in the exe.
// Your own translation or fixes can go to %APPDATA%\Glowstick\lang\<code>.json — they take priority over the embedded ones.
// No translation for a string — the English text is shown.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Web.Script.Serialization;

namespace Glowstick
{
    internal static class Lang
    {
        // Code, the language's name in that language
        public static readonly string[][] Languages =
        {
            new[] { "en", "English" },
            new[] { "uk", "Українська" },
            new[] { "de", "Deutsch" },
            new[] { "fr", "Français" },
            new[] { "es", "Español" },
            new[] { "pt-BR", "Português (Brasil)" },
            new[] { "it", "Italiano" },
            new[] { "nl", "Nederlands" },
            new[] { "ja", "日本語" },
            new[] { "ko", "한국어" },
            new[] { "zh-CN", "简体中文" },
        };

        public static string Setting = "";   // from settings.json; "" — as in Windows
        public static string Code = "en";    // the language the UI is shown in right now
        static Dictionary<string, string> map = new Dictionary<string, string>();

        public static void Init(string setting)
        {
            Setting = setting ?? "";
            Code = Resolve(Setting);
            map = Load(Code);
        }

        public static string T(string s)
        {
            string v;
            return s != null && map.TryGetValue(s, out v) && !string.IsNullOrEmpty(v) ? v : s;
        }

        // A translation with the wrong placeholders (a hand-made file in %APPDATA%\Glowstick\lang) must not crash the window: the English text is used
        public static string F(string s, params object[] args)
        {
            try { return string.Format(T(s), args); }
            catch (FormatException) { return string.Format(s, args); }
        }

        // Language from settings, otherwise Windows' language (pt → pt-BR, zh → zh-CN), otherwise English
        static string Resolve(string setting)
        {
            if (setting != "" && Known(setting)) return setting;
            CultureInfo ui = CultureInfo.CurrentUICulture;
            if (Known(ui.Name)) return ui.Name;
            string two = ui.TwoLetterISOLanguageName;
            foreach (string[] l in Languages) if (l[0].Split('-')[0] == two) return l[0];
            return "en";
        }

        static bool Known(string code)
        {
            foreach (string[] l in Languages) if (string.Equals(l[0], code, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        public static Dictionary<string, string> Load(string code)
        {
            Dictionary<string, string> d = new Dictionary<string, string>();
            if (code == "en") return d;
            Merge(d, ReadResource("lang." + code + ".json"));
            Merge(d, ReadResource("langpro." + code + ".json"));   // the Pro module's strings (Pro build only)
            try
            {
                string user = Path.Combine(Path.Combine(Settings.Dir, "lang"), code + ".json");
                if (File.Exists(user)) Merge(d, File.ReadAllText(user, Encoding.UTF8));
            }
            catch (Exception e) { Log.Error("lang " + code, e); }
            return d;
        }

        static string ReadResource(string name)
        {
            using (Stream s = Assembly.GetExecutingAssembly().GetManifestResourceStream(name))
            {
                if (s == null) return null;
                using (StreamReader r = new StreamReader(s, Encoding.UTF8)) return r.ReadToEnd();
            }
        }

        static void Merge(Dictionary<string, string> d, string json)
        {
            if (string.IsNullOrEmpty(json)) return;
            try
            {
                Dictionary<string, object> src = new JavaScriptSerializer { MaxJsonLength = int.MaxValue }.Deserialize<Dictionary<string, object>>(json);
                foreach (KeyValuePair<string, object> kv in src) if (kv.Value is string) d[kv.Key] = (string)kv.Value;
            }
            catch (Exception e) { Log.Error("lang json", e); }
        }

        // Ukrainian name → English key. Old versions stored preset names in settings.json in Ukrainian.
        static Dictionary<string, string> fromUk;
        public static string FromUk(string text)
        {
            if (fromUk == null)
            {
                Dictionary<string, string> r = new Dictionary<string, string>();
                foreach (KeyValuePair<string, string> kv in Load("uk")) r[kv.Value] = kv.Key;
                fromUk = r;
            }
            string en;
            return text != null && fromUk.TryGetValue(text, out en) ? en : null;
        }
    }
}
