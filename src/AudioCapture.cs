// What the PC is playing, split by frequency — for music presets that follow the bass, the mids or the highs.
// WASAPI loopback capture of the default output device (shared mode, nothing is changed for other apps) — or, when music
// presets listen only to some apps (or to all but some), one process-loopback capture per sound session of the apps that
// count (Windows 11), the loudest of them wins. Each requested band is a band-pass (a 2nd-order high-pass at its low
// edge and a low-pass at its high edge) over the mono mix, and what a preset reads is the band's peak since its last
// read — the same kind of value the peak meter gives (Live.Audio).
// The capture thread runs only while something asks for a band: it stops 10 s after the last request.
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;

namespace Glowstick
{
    // One frequency band: the filters' state and the peak since the last read
    internal sealed class BandFilter
    {
        public readonly double Low, High;   // Hz
        readonly Biquad hp = new Biquad(), lp = new Biquad();
        double rate, peak;
        readonly object gate = new object();
        public long LastAsk;                // ms (AudioCapture's clock) of the last read

        public BandFilter(double low, double high) { Low = low; High = high; }

        // Mono samples at sampleRate → the peak of the filtered signal goes into the band
        public void Process(float[] mono, int count, int sampleRate)
        {
            if (sampleRate != rate)
            {
                rate = sampleRate;
                hp.HighPass(Math.Max(10, Low), sampleRate);
                lp.LowPass(Math.Min(High, sampleRate * 0.45), sampleRate);
            }
            double m = 0;
            for (int k = 0; k < count; k++)
            {
                double y = lp.Step(hp.Step(mono[k]));
                if (y < 0) y = -y;
                if (y > m) m = y;
            }
            lock (gate) if (m > peak) peak = m;
        }

        // The peak since the last read
        public double Take()
        {
            lock (gate) { double p = peak; peak = 0; return p; }
        }
    }

    // RBJ cookbook biquad, Butterworth (Q = 1/√2)
    internal sealed class Biquad
    {
        double b0, b1, b2, a1, a2, x1, x2, y1, y2;

        public void LowPass(double f, double rate) { Set(f, rate, false); }
        public void HighPass(double f, double rate) { Set(f, rate, true); }

        void Set(double f, double rate, bool high)
        {
            double w = 2 * Math.PI * f / rate, cos = Math.Cos(w), alpha = Math.Sin(w) / (2 * Math.Sqrt(0.5));
            double a0 = 1 + alpha;
            if (high) { b0 = (1 + cos) / 2; b1 = -(1 + cos); b2 = (1 + cos) / 2; }
            else { b0 = (1 - cos) / 2; b1 = 1 - cos; b2 = (1 - cos) / 2; }
            b0 /= a0; b1 /= a0; b2 /= a0; a1 = -2 * cos / a0; a2 = (1 - alpha) / a0;
            x1 = x2 = y1 = y2 = 0;
        }

        public double Step(double x)
        {
            double y = b0 * x + b1 * x1 + b2 * x2 - a1 * y1 - a2 * y2;
            x2 = x1; x1 = x; y2 = y1; y1 = y;
            return y;
        }
    }

    internal static class AudioCapture
    {
        // Which apps music presets listen to (settings.json "MusicAppMode", "MusicApps"): "" — everything, "only" — just these
        // apps, "except" — everything but these. Apps — exe names ("Spotify.exe"); replaced whole, never changed in place.
        public static volatile string AppMode = "";
        public static volatile string[] Apps = new string[0];

        public static bool Filtered { get { return AppMode != "" && Apps.Length > 0; } }

        // Per-app capture (process loopback) needs Windows 10 build 20348+ / Windows 11; older ones hear everything
        public static readonly bool PerAppSupported = Build() >= 20348;

        static readonly List<BandFilter> bands = new List<BandFilter>();   // what presets ask for (Low, High, LastAsk)
        static readonly List<Source> sources = new List<Source>();         // what's captured now; each has its own filters
        static readonly object gate = new object();
        static Thread thread;
        static long failedAt = -100000;     // the device couldn't be captured — callers fall back to the peak meter, a retry after 30 s
        static long lastAsk;
        static readonly System.Diagnostics.Stopwatch clock = System.Diagnostics.Stopwatch.StartNew();

        // The band's peak since the last read (the loudest of the captured apps), or -1 — no capture (yet, or it doesn't work here)
        public static double Peak(double low, double high)
        {
            long now = clock.ElapsedMilliseconds;
            lock (gate)
            {
                lastAsk = now;
                BandFilter b = null;
                for (int k = 0; k < bands.Count && b == null; k++) if (bands[k].Low == low && bands[k].High == high) b = bands[k];   // (no closure: asked every frame)
                if (b == null) { b = new BandFilter(low, high); bands.Add(b); }
                b.LastAsk = now;
                bool failed = now - failedAt < 30000;
                if (thread == null && !failed)
                {
                    thread = new Thread(Run) { IsBackground = true, Name = "audio capture" };
                    thread.Start();
                }
                if (failed || thread == null) return -1;
                double p = 0;   // nothing captured (no listened app plays) — silence, not the whole PC's meter
                foreach (Source s in sources) { BandFilter f; if (s.Filters.TryGetValue(b, out f)) p = Math.Max(p, f.Take()); }
                return p;
            }
        }

        // The apps with sound sessions on the default output now (for "add an app"), exe names A–Z
        public static List<string> SoundApps()
        {
            List<string> res = new List<string>();
            try
            {
                foreach (int pid in SessionPids(Enumerator()))
                {
                    string exe = ExeOf(pid);
                    if (exe != null && !res.Exists(x => string.Equals(x, exe, StringComparison.OrdinalIgnoreCase))) res.Add(exe);
                }
            }
            catch (Exception e) { Log.Error("audio sessions", e); }
            res.Sort(StringComparer.OrdinalIgnoreCase);
            return res;
        }

        // One captured stream: the default output device (everything) or one app's process tree
        sealed class Source
        {
            public string Key;   // "dev:<id>" or "pid:<n>"
            public IAudioClient Client;
            public IAudioCaptureClient Cap;
            public EventWaitHandle Ready;
            public int Channels, Rate, Bits;
            public bool IsFloat;
            public float[] Mono;
            public byte[] Raw = new byte[0];
            public readonly Dictionary<BandFilter, BandFilter> Filters = new Dictionary<BandFilter, BandFilter>();   // asked band → this stream's

            public void Close()
            {
                try { Client.Stop(); } catch { }
                try { Marshal.ReleaseComObject(Cap); Marshal.ReleaseComObject(Client); } catch { }
                if (Ready != null) Ready.Close();
            }
        }

        // ---------- WASAPI ----------

        [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IMMDeviceEnumerator
        {
            [PreserveSig] int EnumAudioEndpoints(int dataFlow, int stateMask, out IntPtr devices);
            [PreserveSig] int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice endpoint);
        }

        [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IMMDevice
        {
            [PreserveSig] int Activate(ref Guid iid, int clsCtx, IntPtr activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object iface);
            [PreserveSig] int OpenPropertyStore(int access, out IntPtr store);
            [PreserveSig] int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);
        }

        [ComImport, Guid("1CB9AD4C-DBFA-4C32-B178-C2F568A703B2"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IAudioClient
        {
            [PreserveSig] int Initialize(int shareMode, int streamFlags, long bufferDuration, long periodicity, IntPtr format, IntPtr sessionGuid);
            [PreserveSig] int GetBufferSize(out int frames);
            [PreserveSig] int GetStreamLatency(out long latency);
            [PreserveSig] int GetCurrentPadding(out int frames);
            [PreserveSig] int IsFormatSupported(int shareMode, IntPtr format, out IntPtr closest);
            [PreserveSig] int GetMixFormat(out IntPtr format);
            [PreserveSig] int GetDevicePeriod(out long defaultPeriod, out long minimumPeriod);
            [PreserveSig] int Start();
            [PreserveSig] int Stop();
            [PreserveSig] int Reset();
            [PreserveSig] int SetEventHandle(IntPtr handle);
            [PreserveSig] int GetService(ref Guid iid, [MarshalAs(UnmanagedType.IUnknown)] out object service);
        }

        [ComImport, Guid("C8ADBD64-E71E-48A0-A4DE-185C395CD317"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IAudioCaptureClient
        {
            [PreserveSig] int GetBuffer(out IntPtr data, out int frames, out int flags, out long devicePosition, out long qpcPosition);
            [PreserveSig] int ReleaseBuffer(int frames);
            [PreserveSig] int GetNextPacketSize(out int frames);
        }

        // The output device's sound sessions — which apps play there
        [ComImport, Guid("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IAudioSessionManager2
        {
            [PreserveSig] int GetAudioSessionControl(IntPtr sessionGuid, int flags, out IntPtr control);
            [PreserveSig] int GetSimpleAudioVolume(IntPtr sessionGuid, int flags, out IntPtr volume);
            [PreserveSig] int GetSessionEnumerator(out IAudioSessionEnumerator sessions);
        }

        [ComImport, Guid("E2F5BB11-0570-40CA-ACDD-3AA01277DEE8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IAudioSessionEnumerator
        {
            [PreserveSig] int GetCount(out int count);
            [PreserveSig] int GetSession(int index, [MarshalAs(UnmanagedType.IUnknown)] out object session);
        }

        [ComImport, Guid("BFB7FF88-7239-4FC9-8FA2-07C950BE9C6D"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IAudioSessionControl2
        {
            [PreserveSig] int GetState(out int state);
            [PreserveSig] int GetDisplayName(out IntPtr name);
            [PreserveSig] int SetDisplayName(IntPtr name, IntPtr context);
            [PreserveSig] int GetIconPath(out IntPtr path);
            [PreserveSig] int SetIconPath(IntPtr path, IntPtr context);
            [PreserveSig] int GetGroupingParam(out Guid param);
            [PreserveSig] int SetGroupingParam(IntPtr param, IntPtr context);
            [PreserveSig] int RegisterAudioSessionNotification(IntPtr client);
            [PreserveSig] int UnregisterAudioSessionNotification(IntPtr client);
            [PreserveSig] int GetSessionIdentifier(out IntPtr id);
            [PreserveSig] int GetSessionInstanceIdentifier(out IntPtr id);
            [PreserveSig] int GetProcessId(out int pid);
        }

        // Process loopback: one app's (and its child processes') sound, activated asynchronously on a virtual device
        [ComImport, Guid("41D949AB-9862-444A-80F6-C261334DA5EB"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IActivateAudioInterfaceCompletionHandler { void ActivateCompleted(IActivateAudioInterfaceAsyncOperation op); }

        [ComImport, Guid("72A22D78-CDE4-431D-B8CC-843A71199B6D"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IActivateAudioInterfaceAsyncOperation { void GetActivateResult(out int hr, [MarshalAs(UnmanagedType.IUnknown)] out object iface); }

        [ComImport, Guid("94EA2B94-E9CC-49E0-C0FF-EE64CA8F5B90"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IAgileObject { }

        sealed class Activation : IActivateAudioInterfaceCompletionHandler, IAgileObject
        {
            public readonly ManualResetEvent Done = new ManualResetEvent(false);
            public void ActivateCompleted(IActivateAudioInterfaceAsyncOperation op) { Done.Set(); }
        }

        [DllImport("Mmdevapi.dll", ExactSpelling = true, PreserveSig = false)]
        static extern void ActivateAudioInterfaceAsync([MarshalAs(UnmanagedType.LPWStr)] string path, [MarshalAs(UnmanagedType.LPStruct)] Guid iid,
            IntPtr activationParams, IActivateAudioInterfaceCompletionHandler handler, out IActivateAudioInterfaceAsyncOperation op);

        const int AUDCLNT_STREAMFLAGS_LOOPBACK = 0x00020000, AUDCLNT_STREAMFLAGS_EVENTCALLBACK = 0x00040000, AUDCLNT_BUFFERFLAGS_SILENT = 0x2;
        const int AUDCLNT_STREAMFLAGS_AUTOCONVERTPCM = unchecked((int)0x80000000), AUDCLNT_STREAMFLAGS_SRC_DEFAULT_QUALITY = 0x08000000;
        static readonly Guid FloatFormat = new Guid("00000003-0000-0010-8000-00aa00389b71");

        // By CLSID, not "new MMDeviceEnumeratorCom()": the enumerator is one shared COM object, and its wrapper may already have
        // been made for Live's own class of the same name — casting that to this class throws
        // There is a sound output to listen to (not on a server without audio)
        internal static bool HasOutput()
        {
            try { IMMDevice d; return Enumerator().GetDefaultAudioEndpoint(0, 1, out d) == 0; } catch { return false; }
        }

        static IMMDeviceEnumerator Enumerator() { return (IMMDeviceEnumerator)Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("BCDE0395-E52F-467C-8E3D-C4579291692E"))); }

        static void Run()
        {
            List<Source> mine = new List<Source>();   // this thread's copy of sources
            try
            {
                IMMDeviceEnumerator en = Enumerator();
                long reconciled = -100000;
                string shown = null;
                while (true)
                {
                    long now = clock.ElapsedMilliseconds;
                    BandFilter[] asked;
                    lock (gate)
                    {
                        if (now - lastAsk > 10000) return;   // nobody listens
                        bands.RemoveAll(b => now - b.LastAsk > 10000);
                        asked = bands.ToArray();
                    }
                    // Every second: what should be captured — the default device (it may have changed: headphones plugged in…),
                    // or the sessions of the listened apps (an app started or stopped playing)
                    if (now - reconciled > 1000)
                    {
                        reconciled = now;
                        List<string> want = Wanted(en);
                        if (want == null) { Log.Info("audio capture: no output device"); failedAt = now; return; }
                        List<Source> gone = mine.FindAll(s => !want.Contains(s.Key));
                        foreach (string key in want)
                        {
                            if (mine.Exists(s => s.Key == key)) continue;
                            string err;
                            Source s = key.StartsWith("dev:") ? OpenDevice(en, key, out err) : OpenProcess(int.Parse(key.Substring(4)), out err);
                            if (s != null) mine.Add(s);
                            else if (key.StartsWith("dev:")) { Log.Info("audio capture: " + err); failedAt = now; return; }
                            else if (!quiet.Contains(key)) { quiet.Add(key); Log.Info("audio capture of " + key + ": " + err); }
                        }
                        mine.RemoveAll(s => gone.Contains(s));
                        lock (gate) { sources.Clear(); sources.AddRange(mine); }
                        foreach (Source s in gone) s.Close();
                        string state = Filtered && PerAppSupported ? (AppMode == "only" ? "only " : "all but ") + string.Join(", ", Apps) + " — " + mine.Count + " playing" : "everything";
                        if (state != shown) { shown = state; Log.Info("audio capture: " + state); }
                    }
                    foreach (Source s in mine) Drain(s, asked);
                    Thread.Sleep(5);
                }
            }
            catch (Exception e) { Log.Error("audio capture", e); failedAt = clock.ElapsedMilliseconds; }
            finally
            {
                lock (gate) { thread = null; bands.Clear(); sources.Clear(); }
                foreach (Source s in mine) s.Close();
            }
        }

        static readonly HashSet<string> quiet = new HashSet<string>();   // apps whose capture failed once — not logged again

        // The sources to capture now: "dev:<id>" (everything), or "pid:<n>" per sound session of a listened app; null — no device
        static List<string> Wanted(IMMDeviceEnumerator en)
        {
            List<string> want = new List<string>();
            if (!Filtered || !PerAppSupported)
            {
                IMMDevice dev; string id = null;
                if (en.GetDefaultAudioEndpoint(0 /* eRender */, 1 /* eMultimedia */, out dev) != 0 || dev.GetId(out id) != 0 || id == null) return null;
                want.Add("dev:" + id);
                return want;
            }
            string[] apps = Apps;
            bool only = AppMode == "only";
            foreach (int pid in SessionPids(en))
            {
                if (pid == 0) continue;   // system sounds: not an app's process
                string exe = ExeOf(pid);
                if (exe == null) continue;
                bool listed = Array.Exists(apps, a => string.Equals(a, exe, StringComparison.OrdinalIgnoreCase));
                if (listed == only && !want.Contains("pid:" + pid)) want.Add("pid:" + pid);
            }
            return want;
        }

        // The process ids of the default output's sound sessions (not the expired ones)
        static List<int> SessionPids(IMMDeviceEnumerator en)
        {
            List<int> pids = new List<int>();
            IMMDevice dev;
            if (en.GetDefaultAudioEndpoint(0, 1, out dev) != 0) return pids;
            Guid iid = typeof(IAudioSessionManager2).GUID; object o;
            if (dev.Activate(ref iid, 23 /* CLSCTX_ALL */, IntPtr.Zero, out o) != 0) return pids;
            IAudioSessionEnumerator list;
            if (((IAudioSessionManager2)o).GetSessionEnumerator(out list) != 0) return pids;
            int n;
            if (list.GetCount(out n) != 0) return pids;
            for (int k = 0; k < n; k++)
            {
                object so; int state, pid;
                if (list.GetSession(k, out so) != 0) continue;
                IAudioSessionControl2 c = so as IAudioSessionControl2;
                if (c == null || c.GetState(out state) != 0 || state == 2 /* expired */ || c.GetProcessId(out pid) != 0) continue;
                if (!pids.Contains(pid)) pids.Add(pid);
            }
            return pids;
        }

        static string ExeOf(int pid)
        {
            try { using (System.Diagnostics.Process p = System.Diagnostics.Process.GetProcessById(pid)) return p.ProcessName + ".exe"; }
            catch { return null; }
        }

        // Everything the default device plays: loopback in its own mix format
        static Source OpenDevice(IMMDeviceEnumerator en, string key, out string err)
        {
            err = null;
            IMMDevice dev;
            if (en.GetDefaultAudioEndpoint(0, 1, out dev) != 0) { err = "no output device"; return null; }
            Guid iid = typeof(IAudioClient).GUID; object o;
            if (dev.Activate(ref iid, 23, IntPtr.Zero, out o) != 0) { err = "can't open the output device"; return null; }
            IAudioClient client = (IAudioClient)o;
            IntPtr fmt;
            if (client.GetMixFormat(out fmt) != 0) { err = "no mix format"; return null; }
            Source s = new Source { Key = key, Client = client };
            try
            {
                int tag = Marshal.ReadInt16(fmt, 0) & 0xFFFF;
                s.Channels = Marshal.ReadInt16(fmt, 2); s.Rate = Marshal.ReadInt32(fmt, 4); s.Bits = Marshal.ReadInt16(fmt, 14);
                s.IsFloat = tag == 3 || (tag == 0xFFFE && new Guid(ReadBytes(fmt, 24, 16)) == FloatFormat);
                int hr = client.Initialize(0 /* shared */, AUDCLNT_STREAMFLAGS_LOOPBACK, 2000000 /* 200 ms */, 0, fmt, IntPtr.Zero);
                if (hr != 0) { err = "can't start loopback capture (0x" + hr.ToString("X8") + ")"; return null; }
            }
            finally { Marshal.FreeCoTaskMem(fmt); }
            if (s.Channels < 1 || s.Rate < 8000 || (!s.IsFloat && s.Bits != 16 && s.Bits != 24 && s.Bits != 32)) { err = "unsupported format: " + s.Channels + " ch, " + s.Rate + " Hz, " + s.Bits + " bit"; return null; }
            return Start(s, out err) ? s : null;
        }

        // One app (its process and the ones it started): process loopback, asked for as 48 kHz stereo float
        static Source OpenProcess(int pid, out string err)
        {
            err = null;
            IntPtr prm = Marshal.AllocCoTaskMem(12), pv = Marshal.AllocCoTaskMem(24), fmt = Marshal.AllocCoTaskMem(18);
            try
            {
                Marshal.WriteInt32(prm, 0, 1);       // AUDIOCLIENT_ACTIVATION_TYPE_PROCESS_LOOPBACK
                Marshal.WriteInt32(prm, 4, pid);     // the target process
                Marshal.WriteInt32(prm, 8, 0);       // PROCESS_LOOPBACK_MODE_INCLUDE_TARGET_PROCESS_TREE
                for (int k = 0; k < 24; k++) Marshal.WriteByte(pv, k, 0);
                Marshal.WriteInt16(pv, 0, 65);       // PROPVARIANT VT_BLOB { cbSize, pBlobData }
                Marshal.WriteInt32(pv, 8, 12);
                Marshal.WriteIntPtr(pv, IntPtr.Size == 8 ? 16 : 12, prm);
                Activation done = new Activation();
                IActivateAudioInterfaceAsyncOperation op;
                ActivateAudioInterfaceAsync("VAD\\Process_Loopback", typeof(IAudioClient).GUID, pv, done, out op);
                if (!done.Done.WaitOne(3000)) { err = "activation timed out"; return null; }
                int hr; object o;
                op.GetActivateResult(out hr, out o);
                if (hr != 0 || o == null) { err = "can't activate (0x" + hr.ToString("X8") + ")"; return null; }
                Source s = new Source { Key = "pid:" + pid, Client = (IAudioClient)o, Channels = 2, Rate = 48000, Bits = 32, IsFloat = true };
                Marshal.WriteInt16(fmt, 0, 3);           // WAVE_FORMAT_IEEE_FLOAT
                Marshal.WriteInt16(fmt, 2, 2);
                Marshal.WriteInt32(fmt, 4, 48000);
                Marshal.WriteInt32(fmt, 8, 48000 * 8);
                Marshal.WriteInt16(fmt, 12, 8);
                Marshal.WriteInt16(fmt, 14, 32);
                Marshal.WriteInt16(fmt, 16, 0);
                hr = s.Client.Initialize(0, AUDCLNT_STREAMFLAGS_LOOPBACK | AUDCLNT_STREAMFLAGS_EVENTCALLBACK | AUDCLNT_STREAMFLAGS_AUTOCONVERTPCM | AUDCLNT_STREAMFLAGS_SRC_DEFAULT_QUALITY,
                    2000000, 0, fmt, IntPtr.Zero);
                if (hr != 0) { err = "can't start (0x" + hr.ToString("X8") + ")"; return null; }
                s.Ready = new EventWaitHandle(false, EventResetMode.AutoReset);
                if (s.Client.SetEventHandle(s.Ready.SafeWaitHandle.DangerousGetHandle()) != 0) { err = "no event"; return null; }
                return Start(s, out err) ? s : null;
            }
            catch (Exception e) { err = e.Message; return null; }
            finally { Marshal.FreeCoTaskMem(prm); Marshal.FreeCoTaskMem(pv); Marshal.FreeCoTaskMem(fmt); }
        }

        static bool Start(Source s, out string err)
        {
            err = null;
            Guid ciid = typeof(IAudioCaptureClient).GUID; object o;
            if (s.Client.GetService(ref ciid, out o) != 0) { err = "no capture service"; return false; }
            s.Cap = (IAudioCaptureClient)o;
            if (s.Client.Start() != 0) { err = "can't start the capture"; return false; }
            s.Mono = new float[s.Rate];   // up to 1 s per packet
            if (s.Key.StartsWith("dev:")) Log.Info("audio capture: " + s.Channels + " ch, " + s.Rate + " Hz, " + (s.IsFloat ? "float" : s.Bits + "-bit"));
            return true;
        }

        static readonly List<BandFilter> drainFilters = new List<BandFilter>();

        // Everything captured since the last call → this stream's filter of every asked band
        static void Drain(Source s, BandFilter[] asked)
        {
            List<BandFilter> fs = drainFilters;   // only the capture thread gets here
            fs.Clear();
            lock (gate)
            {
                foreach (BandFilter a in asked)
                {
                    BandFilter f;
                    if (!s.Filters.TryGetValue(a, out f)) s.Filters[a] = f = new BandFilter(a.Low, a.High);
                    fs.Add(f);
                }
                if (s.Filters.Count > asked.Length) foreach (BandFilter old in new List<BandFilter>(s.Filters.Keys)) if (Array.IndexOf(asked, old) < 0) s.Filters.Remove(old);
            }
            while (true)
            {
                IntPtr data; int frames, flags; long p1, p2;
                if (s.Cap.GetBuffer(out data, out frames, out flags, out p1, out p2) != 0) break;   // S_BUFFER_EMPTY (or an error) — nothing more now
                if (frames == 0) { s.Cap.ReleaseBuffer(0); break; }
                int n = Math.Min(frames, s.Mono.Length);
                if ((flags & AUDCLNT_BUFFERFLAGS_SILENT) != 0) Array.Clear(s.Mono, 0, n);
                else
                {
                    int bytes = n * s.Channels * (s.Bits / 8);
                    if (s.Raw.Length < bytes) s.Raw = new byte[bytes];
                    Marshal.Copy(data, s.Raw, 0, bytes);
                    ToMono(s.Raw, n, s.Channels, s.Bits, s.IsFloat, s.Mono);
                }
                s.Cap.ReleaseBuffer(frames);
                foreach (BandFilter f in fs) f.Process(s.Mono, n, s.Rate);
            }
        }

        static int Build()
        {
            try
            {
                using (Microsoft.Win32.RegistryKey k = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion"))
                {
                    int b;
                    return k != null && int.TryParse(k.GetValue("CurrentBuildNumber") as string, out b) ? b : 0;
                }
            }
            catch { return 0; }
        }

        static byte[] ReadBytes(IntPtr p, int offset, int count) { byte[] b = new byte[count]; Marshal.Copy(p + offset, b, 0, count); return b; }

        // Interleaved frames → the average of the channels, −1..1
        internal static void ToMono(byte[] raw, int frames, int channels, int bits, bool isFloat, float[] mono)
        {
            int step = bits / 8, k = 0;
            for (int f = 0; f < frames; f++)
            {
                double sum = 0;
                for (int c = 0; c < channels; c++, k += step)
                {
                    if (isFloat) sum += BitConverter.ToSingle(raw, k);
                    else if (bits == 16) sum += BitConverter.ToInt16(raw, k) / 32768.0;
                    else if (bits == 24) sum += ((raw[k] | raw[k + 1] << 8 | raw[k + 2] << 16) << 8 >> 8) / 8388608.0;
                    else sum += BitConverter.ToInt32(raw, k) / 2147483648.0;
                }
                mono[f] = (float)(sum / channels);
            }
        }
    }
}
