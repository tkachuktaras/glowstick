// Recorded previews of the Pro presets, for the free build. Only colors — 4 "controllers" × frames at Fps per preset,
// recorded from the real presets by glowstick_pro\tools\export-previews.ps1 into assets\pro-previews.json. No Pro code:
// the free build plays the colors back on locked preset cards (like a GIF); a locked preset never reaches a controller.
//   {"fps": 10, "pads": 4, "presets": [{"category": "Animations", "name": "Comet", "frames": "<base64: frame × pad × R,G,B>"}]}
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Web.Script.Serialization;

namespace Glowstick
{
    internal static class ProPreviews
    {
        public static List<Preset> Load()
        {
            try
            {
                using (Stream s = Assembly.GetExecutingAssembly().GetManifestResourceStream("pro-previews.json"))
                {
                    if (s == null) return new List<Preset>();
                    using (StreamReader r = new StreamReader(s)) return Parse(r.ReadToEnd());
                }
            }
            catch (Exception e) { Log.Error("pro previews", e); return new List<Preset>(); }
        }

        internal static List<Preset> Parse(string json)
        {
            List<Preset> list = new List<Preset>();
            Dictionary<string, object> d = new JavaScriptSerializer { MaxJsonLength = 16 * 1024 * 1024 }.DeserializeObject(json) as Dictionary<string, object>;
            object v;
            if (d == null || !d.TryGetValue("presets", out v) || !(v is IEnumerable)) return list;
            int fps = d.TryGetValue("fps", out v) && v is int ? (int)v : 10, pads = d.TryGetValue("pads", out v) && v is int ? (int)v : 4;
            if (fps < 1 || pads < 1) return list;
            foreach (object o in (IEnumerable)d["presets"])
            {
                Dictionary<string, object> p = o as Dictionary<string, object>;
                object cat, name, frames;
                if (p == null || !p.TryGetValue("category", out cat) || !p.TryGetValue("name", out name) || !p.TryGetValue("frames", out frames)) continue;
                byte[] data;
                try { data = Convert.FromBase64String(frames as string ?? ""); } catch { continue; }
                if (data.Length == 0 || data.Length % (pads * 3) != 0) continue;
                list.Add(new Preset { Category = cat as string, Name = name as string, Fn = Player(data, fps, pads), Pro = true });
            }
            return list;
        }

        // Plays the recorded frames in a loop; controller i shows pad i of the recording (4 pads, as on a preset card)
        static ColorFn Player(byte[] data, int fps, int pads)
        {
            int count = data.Length / (pads * 3);
            return (t, i, n) =>
            {
                int f = (int)(Math.Floor(t * fps) % count);
                if (f < 0) f += count;
                int k = (f * pads + ((i % pads) + pads) % pads) * 3;
                return new Rgb(data[k], data[k + 1], data[k + 2]);
            };
        }
    }
}
