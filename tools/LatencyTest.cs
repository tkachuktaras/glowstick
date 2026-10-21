// Latency test: how much Glowstick adds between a real DS4 and the virtual pad games see (DualShock 4 only:
// a DualSense report is translated, so it doesn't match the virtual one byte for byte).
//
// Reads reports of the real controller and the virtual DS4 (both HID) at the same time, timestamps each one (QPC)
// and pairs them up after the test: the virtual report matches the real one byte for byte (the counter and gyro make
// every report unique).
// Latency = time it appears on the virtual pad − time the real report appeared. The BT radio link isn't included:
// it's the same with and without the app, but the real controller's report rate/regularity is shown separately.
// Build: powershell -ExecutionPolicy Bypass -File tools\build-latency.ps1
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Security.Principal;
using System.Text;
using System.Threading;
using Microsoft.Win32.SafeHandles;

namespace Glowstick
{
    static class LatencyTest
    {
        struct Ev { public long T; public int Dev; public ulong Key; }   // Key — hash of the 63 bytes

        static readonly string HidHideCli = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            @"Nefarius Software Solutions\HidHide\x64\HidHideCLI.exe");
        static readonly string[] Pids = { "05c4", "09cc", "0ba0" };
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        static string csvPath;
        static bool registered;
        static volatile bool running;

        static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            int seconds = 15;
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "--sec" && i + 1 < args.Length) seconds = Math.Max(3, int.Parse(args[++i]));
                else if (args[i] == "--summary") { csvPath = DefaultCsv(); PrintSummary(); return 0; }
            }
            csvPath = DefaultCsv();
            try { Process.GetCurrentProcess().PriorityClass = ProcessPriorityClass.High; } catch { }

            // In game mode the real DS4 is hidden by HidHide — whitelist the tester while it runs
            bool admin = new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);
            if (File.Exists(HidHideCli))
            {
                if (admin) registered = RunCli("--app-reg \"" + Process.GetCurrentProcess().MainModule.FileName + "\"") == 0;
                else Console.WriteLine("Warning: without administrator rights the tester won't see a DS4 hidden in game mode.");
            }
            Console.CancelKeyPress += (s, e) => Unregister();
            try { Interactive(seconds); }
            finally { Unregister(); }
            return 0;
        }

        static string DefaultCsv()
        {
            return Path.Combine(Path.GetDirectoryName(Process.GetCurrentProcess().MainModule.FileName), "latency-results.csv");
        }

        static void Unregister()
        {
            if (!registered) return;
            registered = false;
            RunCli("--app-unreg \"" + Process.GetCurrentProcess().MainModule.FileName + "\"");
        }

        static void Interactive(int seconds)
        {
            Console.WriteLine("Glowstick latency test. Between tests switch the color in the app, then enter a test name.");
            Console.WriteLine("Keep moving BOTH sticks during the test.");
            while (true)
            {
                Console.WriteLine();
                Setup s = Detect();
                Console.WriteLine("Glowstick: " + (s.ProgramRunning ? "running" : "not running") +
                    " | real DS4: " + s.Physical.Count + " | virtual DS4: " + s.VirtualDs4.Count + " → mode: " + s.Mode);
                Console.Write("Test name (e.g. \"bt red\"; Enter — detect again, s — summary, q — quit): ");
                string label = Console.ReadLine();
                if (label == null || label.Trim() == "q") break;
                label = label.Trim();
                if (label == "") { s.Close(); continue; }
                if (label == "s") { s.Close(); PrintSummary(); continue; }
                if (s.Physical.Count == 0)
                {
                    s.Close();
                    Console.WriteLine("No real DS4 found. If the app is in game mode, run the tester as administrator.");
                    continue;
                }
                for (int k = 3; k > 0; k--) { Console.Write("\rMove the sticks… starting in " + k + " "); Thread.Sleep(1000); }
                Console.WriteLine("\rTest running for " + seconds + " s — move the sticks!            ");
                Result r = RunTest(s, seconds);
                r.Label = label;
                PrintResult(r);
                AppendCsv(r);
            }
        }

        // ---------- Device detection ----------

        class Setup
        {
            public List<SafeFileHandle> Physical = new List<SafeFileHandle>();
            public List<int> PhysInLen = new List<int>();
            public List<bool> PhysBt = new List<bool>();
            public List<SafeFileHandle> VirtualDs4 = new List<SafeFileHandle>();
            public bool ProgramRunning;
            public string Mode { get { return VirtualDs4.Count > 0 ? "DualShock 4" : "no virtual pad"; } }
            public void Close()
            {
                foreach (SafeFileHandle h in Physical) { Native.CancelIoEx(h, IntPtr.Zero); h.Dispose(); }
                foreach (SafeFileHandle h in VirtualDs4) { Native.CancelIoEx(h, IntPtr.Zero); h.Dispose(); }
            }
        }

        static Setup Detect()
        {
            Setup s = new Setup();
            s.ProgramRunning = Process.GetProcessesByName("Glowstick").Length > 0;
            foreach (HidDeviceEntry e in Native.EnumerateHid())
            {
                if (!IsDs4(e.Path)) continue;
                SafeFileHandle h = Native.Open(e.Path, Native.GENERIC_READ);
                if (h.IsInvalid) { h.Dispose(); continue; }
                int inLen, outLen;
                Native.GetCaps(h, out inLen, out outLen);
                if (inLen < 10) { h.Dispose(); continue; }
                Native.HidD_SetNumInputBuffers(h, 64);
                if (Native.IsOnViGEmBus(e.DevInst)) s.VirtualDs4.Add(h);
                else
                {
                    string lp = e.Path.ToLowerInvariant();
                    s.Physical.Add(h); s.PhysInLen.Add(inLen);
                    s.PhysBt.Add(lp.Contains("vid&0002054c") || lp.Contains("00001124-0000-1000-8000-00805f9b34fb") || outLen >= 78);
                }
            }
            return s;
        }

        static bool IsDs4(string p)
        {
            p = p.ToLowerInvariant();
            if (!p.Contains("vid_054c") && !p.Contains("vid&0002054c")) return false;
            foreach (string id in Pids) if (p.Contains("pid_" + id) || p.Contains("pid&" + id)) return true;
            return false;
        }

        // ---------- Recording ----------

        class Result
        {
            public string Label, Mode; public bool Program;
            public string PhysKind;
            public double PhysHz, PhysIntAvg, PhysIntP99, PhysIntMax;
            public double VirtHz;
            public List<double> Lat = new List<double>();
            public int Unmatched, Repeats;
        }

        static Result RunTest(Setup s, int seconds)
        {
            int np = s.Physical.Count;
            List<Ev>[] phys = new List<Ev>[np];
            List<Ev> virt = new List<Ev>();
            List<Thread> threads = new List<Thread>();
            running = true;

            for (int d = 0; d < np; d++)
            {
                int dev = d; phys[d] = new List<Ev>(seconds * 1200);
                SafeFileHandle h = s.Physical[d]; int len = s.PhysInLen[d];
                threads.Add(Reader(() =>
                {
                    byte[] buf = new byte[Math.Max(len, 80)], raw = new byte[63];
                    while (running)
                    {
                        int n;
                        if (!Native.ReadFile(h, buf, len, out n, IntPtr.Zero)) break;
                        long t = Stopwatch.GetTimestamp();
                        if (!Normalize(buf, n, raw)) continue;
                        phys[dev].Add(new Ev { T = t, Dev = dev, Key = Hash(raw) });
                    }
                }));
            }
            object vlock = new object();
            foreach (SafeFileHandle vh in s.VirtualDs4)
            {
                SafeFileHandle h = vh;
                threads.Add(Reader(() =>
                {
                    byte[] buf = new byte[80], raw = new byte[63];
                    while (running)
                    {
                        int n;
                        if (!Native.ReadFile(h, buf, 64, out n, IntPtr.Zero)) break;
                        long t = Stopwatch.GetTimestamp();
                        if (buf[0] != 0x01 || n < 64) continue;
                        Array.Copy(buf, 1, raw, 0, 63);
                        lock (vlock) virt.Add(new Ev { T = t, Key = Hash(raw) });
                    }
                }));
            }
            Thread.Sleep(seconds * 1000);
            running = false;
            s.Close();   // CancelIoEx unblocks ReadFile
            foreach (Thread th in threads) th.Join(2000);

            Result r = new Result { Mode = s.Mode, Program = s.ProgramRunning };
            double tick = 1000.0 / Stopwatch.Frequency;

            // Report rate and regularity of the real controllers
            List<string> kinds = new List<string>();
            List<double> intervals = new List<double>(); double hzSum = 0;
            for (int d = 0; d < np; d++)
            {
                kinds.Add(s.PhysBt[d] ? "BT" : "USB");
                List<Ev> p = phys[d];
                if (p.Count < 2) continue;
                for (int k = 1; k < p.Count; k++) intervals.Add((p[k].T - p[k - 1].T) * tick);
                hzSum += (p.Count - 1) / ((p[p.Count - 1].T - p[0].T) * tick / 1000.0);
            }
            r.PhysKind = string.Join("+", kinds);
            r.PhysHz = np > 0 ? hzSum / np : 0;
            if (intervals.Count > 0)
            {
                intervals.Sort();
                r.PhysIntAvg = Avg(intervals); r.PhysIntP99 = Pct(intervals, 0.99); r.PhysIntMax = intervals[intervals.Count - 1];
            }

            if (virt.Count > 0)
            {
                virt.Sort((a, b) => a.T.CompareTo(b.T));
                if (virt.Count > 1) r.VirtHz = (virt.Count - 1) / ((virt[virt.Count - 1].T - virt[0].T) * tick / 1000.0);
                // Key → times the real reports appeared
                Dictionary<ulong, List<long>> index = new Dictionary<ulong, List<long>>();
                for (int d = 0; d < np; d++)
                    foreach (Ev e in phys[d])
                    {
                        List<long> l;
                        if (!index.TryGetValue(e.Key, out l)) index[e.Key] = l = new List<long>();
                        l.Add(e.T);
                    }
                long slack = (long)(Stopwatch.Frequency * 0.002);   // our read thread may wake up a bit later than the app
                // ViGEm itself repeats the virtual DS4's last report until a new one arrives. A repeat isn't latency:
                // only the first appearance of each real report counts.
                HashSet<long> used = new HashSet<long>();
                foreach (Ev v in virt)
                {
                    List<long> l;
                    if (!index.TryGetValue(v.Key, out l)) { r.Unmatched++; continue; }
                    long best = long.MinValue;
                    foreach (long t in l) if (t <= v.T + slack && t > best) best = t;
                    if (best == long.MinValue || (v.T - best) * tick > 100) { r.Unmatched++; continue; }
                    if (!used.Add(best)) { r.Repeats++; continue; }
                    r.Lat.Add((v.T - best) * tick);
                }
                r.Lat.Sort();
            }
            return r;
        }

        static Thread Reader(ThreadStart fn)
        {
            Thread t = new Thread(fn); t.IsBackground = true; t.Priority = ThreadPriority.Highest; t.Start();
            return t;
        }

        // The same 63 bytes the app sends to the virtual DS4 (see Controller.ReadLoop)
        static bool Normalize(byte[] buf, int n, byte[] raw)
        {
            if (buf[0] == 0x11 && n >= 66) { Array.Copy(buf, 3, raw, 0, 63); return true; }
            if (buf[0] == 0x01 && n >= 64) { Array.Copy(buf, 1, raw, 0, 63); return true; }
            if (buf[0] == 0x01 && n >= 10)
            {
                Array.Clear(raw, 0, raw.Length);
                Array.Copy(buf, 1, raw, 0, 9);
                raw[34] = 0x80; raw[38] = 0x80;
                return true;
            }
            return false;
        }

        static ulong Hash(byte[] raw)
        {
            ulong h = 14695981039346656037UL;
            for (int i = 0; i < raw.Length; i++) { h ^= raw[i]; h *= 1099511628211UL; }
            return h;
        }

        // ---------- Output ----------

        static double Avg(List<double> l) { double s = 0; foreach (double x in l) s += x; return l.Count > 0 ? s / l.Count : 0; }

        static double Pct(List<double> sorted, double p)
        {
            if (sorted.Count == 0) return 0;
            return sorted[Math.Min(sorted.Count - 1, (int)Math.Floor(p * (sorted.Count - 1) + 0.5))];
        }

        static string F(double v, int digits) { return v.ToString("F" + digits, Inv); }

        static void PrintResult(Result r)
        {
            Console.WriteLine();
            Console.WriteLine("── " + r.Label + " ── mode: " + r.Mode + ", app: " + (r.Program ? "yes" : "no"));
            Console.WriteLine("  Real DS4 (" + r.PhysKind + "): " + F(r.PhysHz, 0) + " Hz, interval avg " + F(r.PhysIntAvg, 2) +
                " ms, p99 " + F(r.PhysIntP99, 2) + " ms, max " + F(r.PhysIntMax, 2) + " ms");
            if (r.Lat.Count > 0)
            {
                Console.WriteLine("  Virtual pad: " + F(r.VirtHz, 0) + " Hz");
                Console.WriteLine("  App latency: median " + F(Pct(r.Lat, 0.5), 3) + " ms, avg " + F(Avg(r.Lat), 3) +
                    " ms, p95 " + F(Pct(r.Lat, 0.95), 3) + " ms, p99 " + F(Pct(r.Lat, 0.99), 3) + " ms, max " +
                    F(r.Lat[r.Lat.Count - 1], 3) + " ms  (pairs: " + r.Lat.Count + ", unpaired: " + r.Unmatched + ", ViGEm repeats: " + r.Repeats + ")");
                if (r.Lat.Count < 200) Console.WriteLine("  Few events — move the sticks more.");
            }
            else if (r.Mode == "no virtual pad") Console.WriteLine("  No virtual pad — the game reads the controller directly, the app adds nothing.");
            else Console.WriteLine("  Couldn't pair a single event (unpaired: " + r.Unmatched + ").");
        }

        const string CsvHeader = "time,label,program,mode,phys,phys_hz,phys_int_avg_ms,phys_int_p99_ms,phys_int_max_ms,virt_hz,lat_med_ms,lat_avg_ms,lat_p95_ms,lat_p99_ms,lat_max_ms,pairs,unmatched";

        static void AppendCsv(Result r)
        {
            bool has = r.Lat.Count > 0;
            string[] f =
            {
                DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", Inv), "\"" + r.Label.Replace("\"", "'") + "\"", r.Program ? "1" : "0",
                r.Mode, r.PhysKind, F(r.PhysHz, 1), F(r.PhysIntAvg, 3), F(r.PhysIntP99, 3), F(r.PhysIntMax, 3),
                has ? F(r.VirtHz, 1) : "", has ? F(Pct(r.Lat, 0.5), 4) : "", has ? F(Avg(r.Lat), 4) : "",
                has ? F(Pct(r.Lat, 0.95), 4) : "", has ? F(Pct(r.Lat, 0.99), 4) : "", has ? F(r.Lat[r.Lat.Count - 1], 4) : "",
                r.Lat.Count.ToString(Inv), r.Unmatched.ToString(Inv)
            };
            bool exists = File.Exists(csvPath);
            using (StreamWriter w = new StreamWriter(csvPath, true, new UTF8Encoding(true)))
            {
                if (!exists) w.WriteLine(CsvHeader);
                w.WriteLine(string.Join(",", f));
            }
            Console.WriteLine("  Saved to " + csvPath);
        }

        static void PrintSummary()
        {
            if (!File.Exists(csvPath)) { Console.WriteLine("No results yet."); return; }
            Console.WriteLine();
            Console.WriteLine(string.Format("{0,-24} {1,-12} {2,-6} {3,7} {4,8} {5,8} {6,9} {7,9} {8,9}",
                "Test", "Mode", "App", "DS4 Hz", "int.p99", "int.max", "lat.med", "lat.p99", "lat.max"));
            foreach (string line in File.ReadAllLines(csvPath))
            {
                if (line.StartsWith("time,")) continue;
                List<string> c = SplitCsv(line);
                if (c.Count < 17) continue;
                Console.WriteLine(string.Format("{0,-24} {1,-12} {2,-6} {3,7} {4,8} {5,8} {6,9} {7,9} {8,9}",
                    Cut(c[1], 24), Cut(c[3], 12), c[2] == "1" ? "yes" : "no", c[5], c[7], c[8],
                    c[10] == "" ? "—" : c[10], c[13] == "" ? "—" : c[13], c[14] == "" ? "—" : c[14]));
            }
        }

        static string Cut(string s, int n) { return s.Length <= n ? s : s.Substring(0, n - 1) + "…"; }

        static List<string> SplitCsv(string line)
        {
            List<string> res = new List<string>(); StringBuilder sb = new StringBuilder(); bool q = false;
            foreach (char ch in line)
            {
                if (ch == '"') q = !q;
                else if (ch == ',' && !q) { res.Add(sb.ToString()); sb.Clear(); }
                else sb.Append(ch);
            }
            res.Add(sb.ToString());
            return res;
        }

        static int RunCli(string args)
        {
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo(HidHideCli, args);
                psi.UseShellExecute = false; psi.CreateNoWindow = true;
                psi.RedirectStandardOutput = true; psi.RedirectStandardError = true;
                using (Process p = Process.Start(psi))
                {
                    p.StandardOutput.ReadToEnd(); p.StandardError.ReadToEnd();
                    if (!p.WaitForExit(15000)) { try { p.Kill(); } catch { } return -1; }
                    return p.ExitCode;
                }
            }
            catch { return -1; }
        }
    }
}
