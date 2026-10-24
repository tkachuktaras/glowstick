// The Pro license: a key bought on Gumroad, activated on this PC through the Pro server (ProServer.Url, /v1/license/*):
// it checks the key with Gumroad and keeps the activations (a few PCs per key). No secret in the app.
// Stored in settings.json ("ProLicense"). Offline-friendly: Pro stays unlocked without internet; it's locked only when
// the server says the key or this activation is no longer valid (refunded, charged back, deactivated).
// In both builds: the free one activates the key and then downloads the Pro exe (ProServer); the Pro one unlocks with it.
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Web.Script.Serialization;

namespace Glowstick
{
    internal enum LicenseResult { Ok, BadKey, LimitReached, Offline }

    internal static class License
    {
        static readonly object sync = new object();
        static string key = "", activationId = "", hwid = "";
        static DateTime checkedAt = DateTime.MinValue;   // the last answer from the server that the key is good (UTC)

        // Owned also means this is the PC the key was activated on: a license copied to another machine (settings.json and
        // all) doesn't unlock there. hwid == "" — a license from before this binding existed, still honoured.
        public static bool Owned { get { lock (sync) return key != "" && activationId != "" && (hwid == "" || hwid == Hwid()); } }
        public static string Key { get { lock (sync) return key; } }

        // The showcase mode: a made-up activated key (never checked online — the Pro module's checks don't start there)
        internal static void ShowcaseKey()
        {
            lock (sync) { key = Showcase.Key; activationId = "showcase"; hwid = ""; checkedAt = DateTime.UtcNow; }
        }

        // The activated key and this PC's activation (for the Pro server); false — not activated
        public static bool Current(out string k, out string activation)
        {
            lock (sync) { k = key; activation = activationId; }
            return k != "" && activation != "";
        }

        // POST JSON → (HTTP status, body); status 0 — no connection. Replaced in tests.
        internal static Func<string, string, KeyValuePair<int, string>> Post = HttpPost;

        // ---------- activate / validate / deactivate ----------
        public static LicenseResult Activate(string newKey)
        {
            newKey = (newKey ?? "").Trim();
            if (newKey == "") return LicenseResult.BadKey;
            string hw = Hwid();
            KeyValuePair<int, string> r = Post("/v1/license/activate", Json(new Dictionary<string, object>
            {
                { "key", newKey }, { "hwid", hw }
            }));
            if (r.Key == 0 || r.Key >= 500 || r.Key == 429) return LicenseResult.Offline;
            if (r.Key == 403) return LicenseResult.LimitReached;   // also refunded — the message says what to check
            if (r.Key != 200) return LicenseResult.BadKey;
            string id = Field(r.Value, "id");
            if (string.IsNullOrEmpty(id)) return LicenseResult.BadKey;
            lock (sync) { key = newKey; activationId = id; hwid = hw; checkedAt = DateTime.UtcNow; }
            Log.Info("Pro license activated on this PC");
            return LicenseResult.Ok;
        }

        // On start (in the background): is the key still good? False only when the server says no — then Pro is locked.
        public static bool Validate()
        {
            string k, a;
            lock (sync) { k = key; a = activationId; }
            if (k == "" || a == "") return false;
            KeyValuePair<int, string> r = Post("/v1/license/validate", Json(new Dictionary<string, object>
            {
                { "key", k }, { "activation_id", a }
            }));
            if (r.Key == 0 || r.Key >= 500 || r.Key == 429) return true;   // offline or the server / Gumroad is down — keep Pro
            bool good = r.Key == 200 && Field(r.Value, "status") == "granted";
            lock (sync)
            {
                if (good) checkedAt = DateTime.UtcNow;
                else { key = ""; activationId = ""; hwid = ""; }
            }
            if (!good) Log.Info("Pro license is no longer valid (" + r.Key + ")");
            return good;
        }

        // Frees this PC's activation (to move Pro to another PC). Locally it's forgotten even if the server can't be reached.
        public static void Deactivate()
        {
            string k, a;
            lock (sync) { k = key; a = activationId; key = ""; activationId = ""; hwid = ""; }
            if (k == "" || a == "") return;
            KeyValuePair<int, string> r = Post("/v1/license/deactivate", Json(new Dictionary<string, object>
            {
                { "key", k }, { "activation_id", a }
            }));
            Log.Info("Pro license deactivated on this PC (" + r.Key + ")");
        }

        // A stable per-PC id: a hash of Windows' MachineGuid (this install's own, survives reboots and app updates).
        // Only used to tell "same PC" from "another PC"; it isn't a secret and never leaves as-is (the server gets the hash).
        static volatile string cachedHwid;
        internal static string Hwid()
        {
            if (cachedHwid != null) return cachedHwid;
            string src = "";
            try
            {
                using (Microsoft.Win32.RegistryKey rk = Microsoft.Win32.RegistryKey.OpenBaseKey(Microsoft.Win32.RegistryHive.LocalMachine, Microsoft.Win32.RegistryView.Registry64)
                    .OpenSubKey(@"SOFTWARE\Microsoft\Cryptography"))
                    if (rk != null) src = rk.GetValue("MachineGuid") as string ?? "";
            }
            catch { }
            if (src == "") src = Environment.MachineName + "|" + Environment.UserName;   // fallback, still stable per PC+user
            using (System.Security.Cryptography.SHA256 sha = System.Security.Cryptography.SHA256.Create())
            {
                byte[] h = sha.ComputeHash(Encoding.UTF8.GetBytes("glowstick:" + src));
                cachedHwid = BitConverter.ToString(h, 0, 12).Replace("-", "").ToLowerInvariant();
            }
            return cachedHwid;
        }

        // "…-ABCD" — enough to recognize the key without showing it
        public static string Masked(string k)
        {
            k = k ?? "";
            return k.Length <= 4 ? k : "…" + k.Substring(k.Length - 4);
        }

        // ---------- settings.json ----------
        public static void Load(Dictionary<string, object> s)
        {
            object v;
            Dictionary<string, object> d = s.TryGetValue("ProLicense", out v) ? v as Dictionary<string, object> : null;
            lock (sync)
            {
                key = activationId = hwid = ""; checkedAt = DateTime.MinValue;
                if (d == null) return;
                if (d.TryGetValue("Key", out v) && v is string) key = (string)v;
                if (d.TryGetValue("ActivationId", out v) && v is string) activationId = (string)v;
                if (d.TryGetValue("Hwid", out v) && v is string) hwid = (string)v;
                DateTime t;
                if (d.TryGetValue("CheckedAt", out v) && v is string && DateTime.TryParse((string)v, System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal, out t)) checkedAt = t;
            }
        }

        public static void Save(Dictionary<string, object> s)
        {
            lock (sync)
            {
                if (key == "") { s.Remove("ProLicense"); return; }
                s["ProLicense"] = new Dictionary<string, object>
                {
                    { "Key", key }, { "ActivationId", activationId }, { "Hwid", hwid },
                    { "CheckedAt", checkedAt.ToString("o", System.Globalization.CultureInfo.InvariantCulture) }
                };
            }
        }

        // ---------- HTTP ----------
        static string Json(Dictionary<string, object> d) { return new JavaScriptSerializer().Serialize(d); }

        // A top-level string field of a JSON object; null if missing or not JSON
        internal static string Field(string json, string name)
        {
            try
            {
                Dictionary<string, object> d = new JavaScriptSerializer().DeserializeObject(json) as Dictionary<string, object>;
                object v;
                return d != null && d.TryGetValue(name, out v) ? v as string : null;
            }
            catch { return null; }
        }

        static KeyValuePair<int, string> HttpPost(string path, string body)
        {
            try
            {
                ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
                HttpWebRequest req = (HttpWebRequest)WebRequest.Create(ProServer.Url + path);
                req.Method = "POST";
                req.ContentType = "application/json";
                req.UserAgent = "Glowstick/" + Updater.Current.ToString(3);
                req.Timeout = 15000;
                byte[] data = Encoding.UTF8.GetBytes(body);
                using (Stream st = req.GetRequestStream()) st.Write(data, 0, data.Length);
                using (HttpWebResponse resp = (HttpWebResponse)req.GetResponse())
                    return new KeyValuePair<int, string>((int)resp.StatusCode, Read(resp));
            }
            catch (WebException e)
            {
                HttpWebResponse resp = e.Response as HttpWebResponse;
                if (resp == null) { Log.Info("license: no connection (" + e.Status + ")"); return new KeyValuePair<int, string>(0, ""); }
                using (resp) return new KeyValuePair<int, string>((int)resp.StatusCode, Read(resp));
            }
            catch (Exception e) { Log.Error("license", e); return new KeyValuePair<int, string>(0, ""); }
        }

        static string Read(HttpWebResponse resp)
        {
            using (StreamReader r = new StreamReader(resp.GetResponseStream(), Encoding.UTF8)) return r.ReadToEnd();
        }
    }
}
