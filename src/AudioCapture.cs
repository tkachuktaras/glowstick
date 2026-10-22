// What the PC is playing, split by frequency — for music presets that follow the bass, the mids or the highs.
// WASAPI loopback capture of the default output device (shared mode, nothing is changed for other apps); each requested
// band is a band-pass (a 2nd-order high-pass at its low edge and a low-pass at its high edge) over the mono mix, and
// what a preset reads is the band's peak since its last read — the same kind of value the peak meter gives (Live.Audio).
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
        static readonly List<BandFilter> bands = new List<BandFilter>();
        static readonly object gate = new object();
        static Thread thread;
        static long failedAt = -100000;     // the device couldn't be captured — callers fall back to the peak meter, a retry after 30 s
        static long lastAsk;
        static readonly System.Diagnostics.Stopwatch clock = System.Diagnostics.Stopwatch.StartNew();

        // The band's peak since the last read, or -1 — no capture (yet, or it doesn't work here)
        public static double Peak(double low, double high)
        {
            long now = clock.ElapsedMilliseconds;
            BandFilter b;
            lock (gate)
            {
                lastAsk = now;
                b = bands.Find(x => x.Low == low && x.High == high);
                if (b == null) { b = new BandFilter(low, high); bands.Add(b); }
                b.LastAsk = now;
                bool failed = now - failedAt < 30000;
                if (thread == null && !failed)
                {
                    thread = new Thread(Run) { IsBackground = true, Name = "audio capture" };
                    thread.Start();
                }
                if (failed || thread == null) return -1;
            }
            return b.Take();
        }

        // ---------- WASAPI ----------
        [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")] class MMDeviceEnumeratorCom { }

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

        const int AUDCLNT_STREAMFLAGS_LOOPBACK = 0x00020000, AUDCLNT_BUFFERFLAGS_SILENT = 0x2;
        static readonly Guid FloatFormat = new Guid("00000003-0000-0010-8000-00aa00389b71");

        static void Run()
        {
            try
            {
                while (true)
                {
                    string err = CaptureDefault();   // returns when the device changed, nothing asked for a while, or on an error
                    if (err == null) break;
                    if (err != "device changed") { Log.Info("audio capture: " + err); failedAt = clock.ElapsedMilliseconds; break; }
                }
            }
            catch (Exception e) { Log.Error("audio capture", e); failedAt = clock.ElapsedMilliseconds; }
            finally { lock (gate) { thread = null; bands.Clear(); } }
        }

        // null — stopped because nothing asks; "device changed" — start again on the new default device; else an error
        static string CaptureDefault()
        {
            IMMDeviceEnumerator en = (IMMDeviceEnumerator)new MMDeviceEnumeratorCom();
            IMMDevice dev;
            if (en.GetDefaultAudioEndpoint(0 /* eRender */, 1 /* eMultimedia */, out dev) != 0) return "no output device";
            string id; dev.GetId(out id);
            Guid iid = typeof(IAudioClient).GUID; object o;
            if (dev.Activate(ref iid, 23 /* CLSCTX_ALL */, IntPtr.Zero, out o) != 0) return "can't open the output device";
            IAudioClient client = (IAudioClient)o;
            IntPtr fmt;
            if (client.GetMixFormat(out fmt) != 0) return "no mix format";
            int channels, rate, bits; bool isFloat;
            try
            {
                int tag = Marshal.ReadInt16(fmt, 0) & 0xFFFF;
                channels = Marshal.ReadInt16(fmt, 2); rate = Marshal.ReadInt32(fmt, 4); bits = Marshal.ReadInt16(fmt, 14);
                isFloat = tag == 3 || (tag == 0xFFFE && new Guid(ReadBytes(fmt, 24, 16)) == FloatFormat);
                int hr = client.Initialize(0 /* shared */, AUDCLNT_STREAMFLAGS_LOOPBACK, 2000000 /* 200 ms */, 0, fmt, IntPtr.Zero);
                if (hr != 0) return "can't start loopback capture (0x" + hr.ToString("X8") + ")";
            }
            finally { Marshal.FreeCoTaskMem(fmt); }
            if (channels < 1 || rate < 8000 || (!isFloat && bits != 16 && bits != 24 && bits != 32)) return "unsupported format: " + channels + " ch, " + rate + " Hz, " + bits + " bit";
            Guid ciid = typeof(IAudioCaptureClient).GUID;
            if (client.GetService(ref ciid, out o) != 0) return "no capture service";
            IAudioCaptureClient cap = (IAudioCaptureClient)o;
            if (client.Start() != 0) return "can't start the capture";
            Log.Info("audio capture: " + channels + " ch, " + rate + " Hz, " + (isFloat ? "float" : bits + "-bit"));
            float[] mono = new float[rate];   // up to 1 s per packet
            byte[] raw = new byte[0];
            long checkedDevice = clock.ElapsedMilliseconds;
            try
            {
                while (true)
                {
                    Thread.Sleep(5);
                    long now = clock.ElapsedMilliseconds;
                    BandFilter[] bs;
                    lock (gate)
                    {
                        if (now - lastAsk > 10000) return null;   // nobody listens
                        bands.RemoveAll(b => now - b.LastAsk > 10000);
                        bs = bands.ToArray();
                    }
                    if (now - checkedDevice > 3000)   // the default output changed (headphones plugged in…) — follow it
                    {
                        checkedDevice = now;
                        IMMDevice d2; string id2 = null;
                        if (en.GetDefaultAudioEndpoint(0, 1, out d2) == 0) d2.GetId(out id2);
                        if (id2 != null && id2 != id) return "device changed";
                    }
                    int packet;
                    while (cap.GetNextPacketSize(out packet) == 0 && packet > 0)
                    {
                        IntPtr data; int frames, flags; long p1, p2;
                        if (cap.GetBuffer(out data, out frames, out flags, out p1, out p2) != 0) break;
                        int n = Math.Min(frames, mono.Length);
                        if ((flags & AUDCLNT_BUFFERFLAGS_SILENT) != 0) Array.Clear(mono, 0, n);
                        else
                        {
                            int bytes = frames * channels * (bits / 8);
                            if (raw.Length < bytes) raw = new byte[bytes];
                            Marshal.Copy(data, raw, 0, bytes);
                            ToMono(raw, n, channels, bits, isFloat, mono);
                        }
                        cap.ReleaseBuffer(frames);
                        foreach (BandFilter b in bs) b.Process(mono, n, rate);
                    }
                }
            }
            finally { client.Stop(); }
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
