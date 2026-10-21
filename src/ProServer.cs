// Glowstick Pro downloads. The Pro exe isn't public: a small server (a Cloudflare Worker, repo glowstick_pro, folder server\)
// hands it out only for a license key activated through it (License.cs) and still valid on Gumroad. The free build uses it
// to turn into Pro once a key is activated; the Pro build — for its updates instead of GitHub Releases.
//   POST {Url}/v1/pro/latest    {"key", "activation_id"} → 200 {"version": "1.2.0", "sha256": "…"} / 403 — the key isn't valid
//   POST {Url}/v1/pro/download  {"key", "activation_id", "version"} → 200 that version's exe / 403 / 404 — no such version
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Web.Script.Serialization;

namespace Glowstick
{
    internal static class ProServer
    {
        // The deployed Worker; empty — not deployed yet (no Pro downloads, no Pro updates)
        public const string Url = "https://api.glowstick.workers.dev";
        public static bool Available { get { return Url != ""; } }

        // The latest Pro build for the activated key — newer than current, or any when current is null (turning free into Pro).
        // null — nothing newer or no key; throws on network / server errors.
        public static UpdateInfo Latest(Version current)
        {
            string body = Body();
            if (body == null) return null;
            string json = Encoding.UTF8.GetString(Post("/v1/pro/latest", body));
            return Parse(json, current);
        }

        internal static UpdateInfo Parse(string json, Version current)
        {
            Dictionary<string, object> d = new JavaScriptSerializer().DeserializeObject(json) as Dictionary<string, object>;
            object v, s;
            if (d == null || !d.TryGetValue("version", out v) || !d.TryGetValue("sha256", out s)) return null;
            Version ver;
            if (!(v is string) || !System.Version.TryParse((string)v, out ver) || !(s is string) || ((string)s).Length != 64) return null;
            if (current != null && Updater.Newer(ver, current) == false) return null;
            return new UpdateInfo { Version = (string)v, Sha256 = ((string)s).ToLowerInvariant(), FromProServer = true };
        }

        // Saves that Pro version's exe to file (the caller checks its SHA-256)
        public static void Download(UpdateInfo u, string file)
        {
            // The version whose hash we already have — a release published in between doesn't change what comes
            string body = Body(u.Version);
            if (body == null) throw new InvalidOperationException(Lang.T("Glowstick Pro isn't activated on this PC."));
            File.WriteAllBytes(file, Post("/v1/pro/download", body));
        }

        static string Body(string version = null)
        {
            string key, act;
            if (!License.Current(out key, out act)) return null;
            Dictionary<string, object> d = new Dictionary<string, object> { { "key", key }, { "activation_id", act } };
            if (version != null) d["version"] = version;
            return new JavaScriptSerializer().Serialize(d);
        }

        static byte[] Post(string path, string body)
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            HttpWebRequest req = (HttpWebRequest)WebRequest.Create(Url + path);
            req.Method = "POST";
            req.ContentType = "application/json";
            req.UserAgent = "Glowstick/" + Updater.Current.ToString(3);
            req.Timeout = 60000;
            byte[] data = Encoding.UTF8.GetBytes(body);
            using (Stream st = req.GetRequestStream()) st.Write(data, 0, data.Length);
            try
            {
                using (HttpWebResponse resp = (HttpWebResponse)req.GetResponse())
                using (Stream rs = resp.GetResponseStream())
                using (MemoryStream ms = new MemoryStream())
                {
                    rs.CopyTo(ms);
                    return ms.ToArray();
                }
            }
            catch (WebException e)
            {
                HttpWebResponse r = e.Response as HttpWebResponse;
                if (r != null && (int)r.StatusCode == 403) throw new InvalidOperationException(Lang.T("The license key isn't valid any more."));
                throw;
            }
        }
    }
}
