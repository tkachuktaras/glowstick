// Updates: at most once a day asks GitHub Releases whether a newer version is out and tells the user.
// Installing happens only when the user clicks "Update": the release's Glowstick.exe is downloaded, checked against the
// release's SHA256.txt, swapped in for the running exe (which is renamed to .old — a running exe can be renamed, not overwritten),
// and the new version is started. The repository (BuildInfo.Repo) comes from build.ps1; empty — updates are off.
// The Pro build updates from the Pro server instead (ProServer, for an activated key); the swap is the same.
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using System.Threading;
using System.Web.Script.Serialization;

namespace Glowstick
{
    // From GitHub: ExeUrl + ShaUrl (SHA256.txt); from the Pro server: FromProServer + Sha256 (no page to show)
    internal class UpdateInfo { public string Version, PageUrl, ExeUrl, ShaUrl, Sha256; public bool FromProServer; }

    internal static class Updater
    {
        public static bool Enabled = true;
        public static DateTime LastCheck = DateTime.MinValue;
        const string ExeName = "Glowstick.exe";

        public static string Repo { get { return BuildInfo.Repo; } }
        public static string ReleasesUrl { get { return Repo == "" ? "" : "https://github.com/" + Repo + "/releases"; } }

        public static Version Current { get { return Assembly.GetExecutingAssembly().GetName().Version; } }

        // This build can check for updates: the free one — with a repository, Pro — with the Pro server and an activated key
        public static bool Available { get { return Pro.Present ? ProServer.Available && License.Owned : Repo != ""; } }

        // A newer release with an exe in it — or null
        // current null — any version (going back from Pro to the free build)
        public static UpdateInfo Parse(string json, Version current)
        {
            Dictionary<string, object> r = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(json);
            object v;
            string tag = (r.TryGetValue("tag_name", out v) ? v as string : null) ?? "";
            tag = tag.TrimStart('v', 'V');
            Version ver;
            if (!System.Version.TryParse(tag, out ver)) return null;
            if (current != null && !Newer(ver, current)) return null;
            UpdateInfo u = new UpdateInfo { Version = tag, PageUrl = (r.TryGetValue("html_url", out v) ? v as string : null) ?? ReleasesUrl };
            if (r.TryGetValue("assets", out v) && v is IEnumerable)
                foreach (object o in (IEnumerable)v)
                {
                    Dictionary<string, object> a = o as Dictionary<string, object>;
                    object name, url;
                    if (a == null || !a.TryGetValue("name", out name) || !a.TryGetValue("browser_download_url", out url)) continue;
                    if (string.Equals(name as string, ExeName, StringComparison.OrdinalIgnoreCase)) u.ExeUrl = url as string;
                    if (string.Equals(name as string, "SHA256.txt", StringComparison.OrdinalIgnoreCase)) u.ShaUrl = url as string;
                }
            return u.ExeUrl != null ? u : null;
        }

        // 1.3 and 1.3.0.0 are the same version
        static Version Normalize(Version v) { return new Version(v.Major, v.Minor, Math.Max(0, v.Build), Math.Max(0, v.Revision)); }

        internal static bool Newer(Version v, Version than) { return Normalize(v) > Normalize(than); }

        // "hash  Glowstick.exe" (or "hash *Glowstick.exe") → lowercase hash; null — the file isn't listed
        public static string ExpectedHash(string sha256Txt, string file)
        {
            foreach (string line in sha256Txt.Split('\n'))
            {
                string[] p = line.Trim().Split(new[] { ' ', '\t' }, 2, StringSplitOptions.RemoveEmptyEntries);
                if (p.Length == 2 && string.Equals(p[1].Trim().TrimStart('*'), file, StringComparison.OrdinalIgnoreCase)) return p[0].ToLowerInvariant();
            }
            return null;
        }

        static string Sha256Of(string path)
        {
            using (SHA256 sha = SHA256.Create())
            using (FileStream fs = File.OpenRead(path))
                return BitConverter.ToString(sha.ComputeHash(fs)).Replace("-", "").ToLowerInvariant();
        }

        // Puts fresh in place of exe; the old one stays as exe.old. Returns an error text or null. On failure exe is untouched.
        public static string Swap(string exe, string fresh)
        {
            string old = exe + ".old";
            try
            {
                if (!File.Exists(fresh)) return "the downloaded file is missing";
                if (File.Exists(old)) File.Delete(old);
                File.Move(exe, old);
            }
            catch (Exception e) { return e.Message; }
            try { File.Move(fresh, exe); return null; }
            catch (Exception e)
            {
                try { File.Move(old, exe); } catch { }
                return e.Message;
            }
        }

        // The new exe couldn't be started: put the old one back. Returns whether it worked.
        public static bool Rollback(string exe)
        {
            string old = exe + ".old";
            try
            {
                if (!File.Exists(old)) return false;
                if (File.Exists(exe)) File.Delete(exe);
                File.Move(old, exe);
                return true;
            }
            catch (Exception e) { Log.Error("update rollback", e); return false; }
        }

        // After an update the previous exe is still lying next to us
        public static void DeleteOld()
        {
            string old = Assembly.GetExecutingAssembly().Location + ".old";
            for (int k = 0; k < 10 && File.Exists(old); k++)
            {
                try { File.Delete(old); } catch { Thread.Sleep(500); }
            }
        }

        static WebClient Client()
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            WebClient wc = new WebClient();
            wc.Headers[HttpRequestHeader.UserAgent] = "Glowstick/" + Current;
            wc.Encoding = System.Text.Encoding.UTF8;
            return wc;
        }

        // force — "Check now". onResult(update or null) — called from a background thread; nothing is called on network errors
        // The latest free build from GitHub Releases — for a Pro build whose key is gone. done(update, null) / done(null, error),
        // called from a background thread.
        public static void FreeLatestAsync(Action<UpdateInfo, string> done)
        {
            if (Repo == "") { done(null, Lang.T("This build can't check for updates.")); return; }
            ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    string json;
                    using (WebClient wc = Client())
                    {
                        wc.Headers[HttpRequestHeader.Accept] = "application/vnd.github+json";
                        json = wc.DownloadString("https://api.github.com/repos/" + Repo + "/releases/latest");
                    }
                    UpdateInfo u = Parse(json, null);
                    done(u, u == null ? Lang.T("No free release was found.") : null);
                }
                catch (Exception e) { Log.Error("free download", e); done(null, e.Message); }
            });
        }

        public static void CheckAsync(bool force, Action<UpdateInfo> onResult)
        {
            if (!Available || (!force && (!Enabled || (DateTime.UtcNow - LastCheck).TotalHours < 24))) return;
            ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    LastCheck = DateTime.UtcNow;
                    Settings.Save();
                    UpdateInfo u;
                    if (Pro.Present) u = ProServer.Latest(Current);
                    else
                    {
                        string json;
                        using (WebClient wc = Client())
                        {
                            wc.Headers[HttpRequestHeader.Accept] = "application/vnd.github+json";
                            json = wc.DownloadString("https://api.github.com/repos/" + Repo + "/releases/latest");
                        }
                        u = Parse(json, Current);
                    }
                    Log.Info("update check: " + (u != null ? "found " + u.Version : "up to date"));
                    onResult(u);
                }
                catch (Exception e) { Log.Error("update check", e); }
            });
        }

        // Downloads and checks the new exe (background thread). done(path, null) — ready to swap; done(null, error) — failed.
        public static void DownloadAsync(UpdateInfo u, Action<string, string> done)
        {
            ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    string dir = Path.Combine(Path.GetTempPath(), "Glowstick_update");
                    Directory.CreateDirectory(dir);
                    string file = Path.Combine(dir, ExeName);
                    string want;
                    if (u.FromProServer) { ProServer.Download(u, file); want = u.Sha256; }
                    else
                    {
                        string sums;
                        using (WebClient wc = Client())
                        {
                            wc.DownloadFile(u.ExeUrl, file);
                            sums = u.ShaUrl != null ? wc.DownloadString(u.ShaUrl) : "";
                        }
                        want = ExpectedHash(sums, ExeName);
                    }
                    if (want == null || want != Sha256Of(file))
                    {
                        try { File.Delete(file); } catch { }
                        done(null, Lang.T("the downloaded file doesn't match its checksum"));
                        return;
                    }
                    Log.Info("update " + u.Version + " downloaded and verified");
                    done(file, null);
                }
                catch (Exception e) { Log.Error("update download", e); done(null, e.Message); }
            });
        }
    }
}
