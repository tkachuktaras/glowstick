// One physical controller — DualShock 4 or DualSense: light bar/rumble (output), battery,
// and in game mode, forwarding input to a virtual pad.
//
// Hot path (latency): a separate highest-priority thread blocks on ReadFile
// and as soon as a report arrives hands its 63 bytes to the virtual pad. No timers or queues.
// A DualSense report is first translated to the DS4 layout (DualSenseWire) — after that everything is the same: virtual pad,
// test, touchpad. Separate handles for reading and writing: synchronous operations on one handle are serialized,
// so a light bar write would otherwise wait for the next input report.
// Around it: PsHotkeys (PS + button combos), VirtualPad (what games see), PadWire (the packet formats).
using System;
using System.Threading;
using Microsoft.Win32.SafeHandles;
using Nefarius.ViGEm.Client;

namespace Glowstick
{
    internal class Controller : IDisposable
    {
        public string Path, InstanceId;
        public string Id;          // MAC (12 hex) — the same over USB and BT; if the MAC can't be read — the device path
        public bool Bluetooth, DualSense;
        public volatile int Player;             // number (from 1) — for the DualSense player LEDs
        public volatile string OwnPreset;       // this controller's own preset (name) or null — same as everyone
        public volatile Preset ApiPreset;        // a live override set through the local API (Pro); null — none. Beats OwnPreset.
        public volatile bool Hidden, NoVirtual; // game mode: hidden from games / but the virtual pad wasn't created
        public double Fade = 1;
        public Rgb Current, FadeFrom, Out;
        public long LastSendMs;
        public int Fails;
        public volatile bool Dead;
        public int LR = -1, LG = -1, LB = -1;
        public int ReportRate;   // input reports during the last second
        public volatile int Battery = -1;   // charge, %; -1 — not known yet
        public volatile bool Charging;
        public double DrainPerHour = -1;    // % drained per hour; -1 — not known yet
        public double ChargePerHour = -1;   // % gained per hour on the charger; -1 — not known yet
        public ulong BtAddress;

        // State for the light bar engine (timestamps — Engine.Ms)
        public long LastActiveMs, BatteryShowUntil, NotifyStart, NotifyUntil;
        public Rgb NotifyColor;
        public volatile Btn LastPressed;       // buttons pressed in the last press (for presets that react to buttons)
        public long LastPressMs = -100000;
        public double Dim = 1, StickHue = 0.6;
        public double PadBrightness = 1.0;     // per-controller brightness multiplier on top of the global one (Pro)
        public bool IdleOff;
        public volatile bool PlayerLeds = true;   // DualSense: the controller's number on the player LEDs (from its profile)
        public volatile int MuteLed = -1;          // DualSense: the Mute light (-1 left alone, 0 off, 1 on) — Engine sets it from the profile
        public volatile bool HapticRumble;         // DualSense: the newer, sharper rumble emulation (from the profile)
        public volatile bool MuteHeld;             // DualSense: the Mute button is down

        // What games should see this controller as: "Xbox" — a virtual Xbox 360, anything else — a virtual DualShock 4 (Pro).
        public volatile string OutputKind = "DS4";
        public int BtPollMs = 0;

        int inLen, outLen, reports;
        SafeFileHandle hWrite, hRead;
        volatile VirtualPad vpad;   // null — no game mode
        Thread reader;
        volatile bool alive;
        volatile byte large, small;
        volatile byte[] trigL, trigR;   // DualSense adaptive-trigger FFB (11 bytes each) or null — no effect
        volatile bool rumbleTest;
        readonly object writeLock = new object();
        readonly byte[] outBuf;
        int ds5Seq; bool ds5Released;   // DualSense output: BT sequence, the connect animation turned off

        // Last report (for live presets and the test window) and read-thread state
        readonly byte[] last = new byte[63];
        readonly object lastLock = new object();
        bool hasLast, touchWas;
        Btn prevBtn;
        byte restLX, restLY, restRX, restRY;
        double batT0 = -1; int batB0;
        int ds5Pct = -1, ds5Counter; bool ds5Chg, ds5Mute;
        readonly Touchpad touch = new Touchpad();
        readonly DesktopPad desktop = new DesktopPad();
        readonly byte[] atRest = new byte[63];
        bool desktopWas;
        readonly PsHotkeys hotkeys;
        bool demo;   // a made-up controller of the showcase mode: no device behind it
        // Health (PadHealth.cs): gaps between reports (Bluetooth link), where the sticks rest
        readonly LinkMeter link = new LinkMeter();
        readonly DriftMeter driftL = new DriftMeter(), driftR = new DriftMeter();
        long lastReportMs = -1;
        int worstGap;
        public bool WeakLink { get { return link.Weak; } }
        public int WorstGapMs { get { return link.WorstGapMs; } }
        public double DriftL { get { return demo ? demoDriftL : driftL.Offset; } }
        double demoDriftL = -1;
        public double DriftR { get { return driftR.Offset; } }

        public bool Virtual { get { VirtualPad v = vpad; return demo || v != null && v.Connected; } }
        public int XInputSlot { get { VirtualPad v = vpad; return v != null ? v.XInputSlot : -1; } }

        // Product IDs of DualSense and DualSense Edge
        public static string[] DualSensePids { get { return DualSenseWire.Pids; } }

        Controller(string path, string instanceId, SafeFileHandle w, int inLen, int outLen)
        {
            Path = path; InstanceId = instanceId; hWrite = w; this.inLen = inLen; this.outLen = outLen;
            string lp = path.ToLowerInvariant();
            Bluetooth = lp.Contains("vid&0002054c") || lp.Contains("00001124-0000-1000-8000-00805f9b34fb") || outLen >= 78;
            foreach (string pid in DualSensePids) if (lp.Contains("pid_" + pid) || lp.Contains("pid&" + pid)) DualSense = true;
            outBuf = new byte[Math.Max(outLen, Bluetooth ? 78 : DualSense ? 48 : 32)];
            hotkeys = new PsHotkeys(a => Engine.Hotkey(this, a));   // the action itself runs in the light bar loop
            touch.Haptic = Pulse;
        }

        public string Model { get { return DualSense ? "DualSense" : "DualShock 4"; } }

        public static Controller Open(HidDeviceEntry e)
        {
            SafeFileHandle w = Native.Open(e.Path, Native.GENERIC_READ | Native.GENERIC_WRITE);
            if (w.IsInvalid) { w.Dispose(); w = Native.Open(e.Path, Native.GENERIC_WRITE); }
            if (w.IsInvalid) { w.Dispose(); return null; }
            int inLen, outLen, featLen;
            Native.GetCaps(w, out inLen, out outLen, out featLen);
            Controller c = new Controller(e.Path, e.InstanceId, w, inLen, outLen);
            c.LastActiveMs = Engine.Ms;
            ulong mac = 0;
            try
            {
                if (c.Bluetooth) mac = c.BtAddress = BtRadio.FindAddress(w, e.DevInst);
                else mac = UsbMac(w, featLen, c.DualSense);
                // A DualSense over BT sends full reports (0x31) only after its calibration is read (feature 0x05)
                if (c.DualSense && c.Bluetooth) { byte[] f = new byte[Math.Max(featLen, 41)]; f[0] = 0x05; Native.HidD_GetFeature(w, f, f.Length); }
            }
            catch (Exception ex) { Log.Error("open " + e.Path, ex); }
            c.Id = mac != 0 ? mac.ToString("X12") : e.Path;
            return c;
        }

        // A made-up controller for the showcase mode (Showcase.Pads): nothing is opened, output goes nowhere,
        // input comes from Showcase.Report; never started
        public static Controller Demo(Showcase.DemoPad p)
        {
            Controller c = new Controller("showcase:" + p.Id, null, null, 0, p.Bluetooth ? 78 : 0);
            c.demo = true;
            c.Id = p.Id; c.DualSense = p.DualSense; c.Bluetooth = p.Bluetooth;
            c.Battery = p.Battery; c.Charging = p.Charging;
            c.DrainPerHour = p.Charging ? -1 : p.DrainPerHour; c.ChargePerHour = p.Charging ? p.DrainPerHour : -1;
            c.ReportRate = p.Rate;
            c.demoDriftL = p.DriftL;
            c.LastActiveMs = Engine.Ms;
            return c;
        }

        // MAC over USB — from a feature report: DS4 — 0x12, DualSense — 0x09; bytes 1..6, least significant first
        static ulong UsbMac(SafeFileHandle h, int featLen, bool dualSense)
        {
            byte[] f = new byte[Math.Max(featLen, 64)];
            f[0] = (byte)(dualSense ? 0x09 : 0x12);
            if (!Native.HidD_GetFeature(h, f, Math.Max(featLen, dualSense ? 20 : 16))) return 0;
            ulong mac = 0;
            for (int k = 6; k >= 1; k--) mac = mac << 8 | f[k];
            return mac == 0 || mac == 0xFFFFFFFFFFFF ? 0 : mac;
        }

        // A separate read handle: battery always, and in game mode (client != null) also a virtual pad
        public bool Start(ViGEmClient client)
        {
            if (inLen < 10) return false;
            hRead = Native.Open(Path, Native.GENERIC_READ | Native.GENERIC_WRITE);
            if (hRead.IsInvalid) { hRead.Dispose(); hRead = Native.Open(Path, Native.GENERIC_READ); }
            if (hRead.IsInvalid) { hRead.Dispose(); hRead = null; return false; }
            Native.HidD_SetNumInputBuffers(hRead, 2);   // don't pile up old reports if we ever fall behind

            if (client != null) vpad = new VirtualPad(client, OutputKind, SetRumble, Id);

            alive = true;
            reader = new Thread(ReadLoop);
            reader.IsBackground = true; reader.Name = "DS4 input";
            reader.Priority = ThreadPriority.Highest;   // touchpad-as-mouse and battery/LED need the same low latency without a game too
            reader.Start();
            return true;
        }

        // Change what games see this controller as, live (recreates the virtual pad). No effect outside game mode.
        public void SwitchOutput(string kind)
        {
            OutputKind = kind == "Xbox" ? "Xbox" : "DS4";
            VirtualPad v = vpad;
            if (v == null || VirtualHidden) return;
            lock (writeLock) v.Switch(OutputKind);
        }

        // Games don't see this controller at all while hidden (Pro co-pilot: its input goes into another controller's
        // virtual pad); its own virtual pad is unplugged and plugged back in. No effect outside game mode.
        public volatile bool VirtualHidden;
        public void HideVirtual(bool hide)
        {
            VirtualPad v = vpad;
            if (v == null || hide == VirtualHidden) return;
            VirtualHidden = hide;
            lock (writeLock) { if (hide) v.Disconnect(); else v.Switch(OutputKind); }
        }

        // Rumble from the game (through the virtual pad)
        void SetRumble(byte l, byte s)
        {
            if (Pro.RumbleScale != null)   // a Pro input profile can boost or weaken the rumble
            {
                double k = Pro.RumbleScale(this);
                l = (byte)Math.Min(255, Math.Round(l * k)); s = (byte)Math.Min(255, Math.Round(s * k));
            }
            if (l == large && s == small) return;
            large = l; small = s;
            if (LR >= 0) Send(LR, LG, LB);   // rumble goes out immediately, without waiting for an animation frame
        }

        // DualSense adaptive triggers: 11-byte FFB effects for L2 / R2 (null — no effect). Sent out right away. DualSense only.
        public void SetTriggers(byte[] left, byte[] right)
        {
            if (!DualSense) return;
            if (SameBytes(trigL, left) && SameBytes(trigR, right)) return;   // unchanged — don't re-send (a preset calls this every frame)
            trigL = left; trigR = right;
            if (LR >= 0) Send(LR, LG, LB);
        }

        static bool SameBytes(byte[] a, byte[] b)
        {
            if (ReferenceEquals(a, b)) return true;
            if (a == null || b == null || a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
            return true;
        }

        void ReadLoop()
        {
            byte[] buf = new byte[Math.Max(inLen, 80)];
            byte[] raw = new byte[63];
            while (alive)
            {
                int n;
                if (!Native.ReadFile(hRead, buf, inLen, out n, IntPtr.Zero)) { if (alive) Dead = true; break; }
                bool full;   // full report — has the battery level
                if (!ToDs4(buf, n, raw, out full)) continue;

                long now = Engine.Ms;
                Btn btn = Pad.ReadButtons(raw);
                bool desk = Engine.DesktopActive;   // the controller drives the PC: the game sees it at rest
                VirtualPad v = vpad;
                if (v != null && v.Connected)
                {
                    byte[] send = raw;
                    if (Engine.Hotkeys && hotkeys.Update(btn, now)) send = hotkeys.Mask(raw, now);
                    if (Pro.MapReport != null) send = Pro.MapReport(this, raw, send, now);
                    if (desk) send = DesktopPad.AtRest(raw, atRest);
                    v.Submit(send);
                }
                else if (Engine.Hotkeys) hotkeys.Update(btn, now);   // no game to hide them from — the actions still fire
                Interlocked.Increment(ref reports);
                if (lastReportMs >= 0 && now - lastReportMs > worstGap) worstGap = (int)(now - lastReportMs);
                lastReportMs = now;

                // Below — everything that must not delay forwarding
                lock (lastLock) { Buffer.BlockCopy(raw, 0, last, 0, 63); hasLast = true; }
                Activity(raw, btn, now);
                bool idle = btn == 0 && raw[7] < 10 && raw[8] < 10;   // put down: nothing held
                driftL.Add(raw[0], raw[1], idle, now);
                driftR.Add(raw[2], raw[3], idle, now);
                if (Pro.InputReport != null) Pro.InputReport(this, raw, now);   // Pro: the motion server (DSU) sends it on
                if (full)
                {
                    if (DualSense) UpdateBattery(ds5Pct, ds5Chg, now);
                    else
                    {
                        int b = raw[29]; bool chg = (b & 0x10) != 0;   // DS4: bits 0-3 level, bit 4 — cable power
                        UpdateBattery(Math.Min(100, (b & 0x0F) * 100 / (chg ? 11 : 8)), chg, now);
                    }
                }
                if (Engine.TouchMouse && !Fullscreen.App && (Pro.TouchMouseOff == null || !Pro.TouchMouseOff(this)))
                {
                    Pad p = Pad.Parse(raw);
                    TouchConfig tc = Pro.TouchFor != null ? Pro.TouchFor(this) : null;   // a Pro input profile may bring its own
                    touch.Process(ref p, tc ?? Engine.Touch, Engine.MsExact);
                    touchWas = true;
                }
                else if (touchWas) { touch.Reset(); touchWas = false; }
                if (desk) { desktop.Process(Pad.Parse(raw), now); desktopWas = true; }
                else if (desktopWas) { desktop.Release(); desktopWas = false; }
            }
            if (touchWas) touch.Reset();
            if (desktopWas) desktop.Release();
        }

        // A HID input report (n bytes in buf) → the 63-byte DS4 layout in raw. False — not an input report we use.
        bool ToDs4(byte[] buf, int n, byte[] raw, out bool full)
        {
            full = false;
            if (DualSense)
            {
                int o = buf[0] == 0x31 && n >= 78 ? 2 : buf[0] == 0x01 && n >= 64 ? 1 : 0;   // BT / USB
                if (o == 0) return false;
                DualSenseWire.ToDs4(buf, o, raw, ref ds5Counter, out ds5Pct, out ds5Chg, out ds5Mute);
                if (ds5Mute && !MuteHeld) Engine.MutePressed(this);
                MuteHeld = ds5Mute;
                full = true;
                return true;
            }
            if (buf[0] == 0x11 && n >= 66) { Array.Copy(buf, 3, raw, 0, 63); full = true; return true; }   // Bluetooth, full report
            // USB; over BT the short 0x01 report padded by ReadFile to full length also lands here — it has no battery
            if (buf[0] == 0x01 && n >= 64) { Array.Copy(buf, 1, raw, 0, 63); full = !Bluetooth; return true; }
            if (buf[0] == 0x01 && n >= 10 && Virtual)
            {
                // Bluetooth, short report (before the first output packet): sticks/buttons/triggers only
                Array.Clear(raw, 0, raw.Length);
                Array.Copy(buf, 1, raw, 0, 9);
                raw[34] = 0x80; raw[38] = 0x80;   // "no touch" on the touchpad
                return true;
            }
            return false;
        }

        // ---------- activity (for dimming and auto-off) ----------
        void Activity(byte[] raw, Btn b, long now)
        {
            Btn pressed = b & ~prevBtn;
            if (pressed != 0) { LastPressed = pressed; LastPressMs = now; }
            bool active = b != prevBtn || raw[7] > 30 || raw[8] > 30 || (raw[34] & 0x80) == 0 ||
                Math.Abs(raw[0] - restLX) > 20 || Math.Abs(raw[1] - restLY) > 20 || Math.Abs(raw[2] - restRX) > 20 || Math.Abs(raw[3] - restRY) > 20;
            if (active) { LastActiveMs = now; restLX = raw[0]; restLY = raw[1]; restRX = raw[2]; restRY = raw[3]; }
            prevBtn = b;
        }

        // ---------- battery ----------
        // Over BT the level "jitters" between neighboring values, so off the charger it only goes down (or jumps up sharply —
        // e.g. the battery was swapped), and on the charger it only goes up. Drain rate — between two moments the level dropped.
        void UpdateBattery(int pct, bool chg, long now)
        {
            if (Battery < 0 || chg != Charging) { Battery = pct; Charging = chg; batT0 = chgT0 = -1; DrainPerHour = ChargePerHour = -1; return; }
            if (!chg)
            {
                if (pct < Battery)
                {
                    if (batT0 < 0) { batT0 = now; batB0 = pct; }
                    else DrainPerHour = (batB0 - pct) / ((now - batT0) / 3600000.0);
                    Battery = pct;
                }
                else if (pct >= Battery + 25) { Battery = pct; batT0 = -1; DrainPerHour = -1; }
            }
            else if (pct > Battery)
            {
                // Charge rate — between two moments the level rose, like the drain rate
                if (chgT0 < 0) { chgT0 = now; chgB0 = pct; }
                else ChargePerHour = (pct - chgB0) / ((now - chgT0) / 3600000.0);
                Battery = pct;
            }
            else if (pct <= Battery - 25) { Battery = pct; chgT0 = -1; ChargePerHour = -1; }
        }

        double chgT0 = -1; int chgB0;

        // Hours until full on the charger; -1 — not known yet
        public double HoursToFull { get { double r = ChargePerHour; return r > 0 && Charging && Battery >= 0 && Battery < 100 ? (100 - Battery) / r : -1; } }

        // Hours left; -1 — not known yet
        public double HoursLeft { get { double d = DrainPerHour; return d > 0 && !Charging && Battery >= 0 ? Battery / d : -1; } }

        public bool CopyLast(byte[] dst)
        {
            if (demo) { Showcase.Report(this, dst); return true; }
            lock (lastLock) { if (!hasLast) return false; Buffer.BlockCopy(last, 0, dst, 0, 63); return true; }
        }

        // A faint, short rumble — the touchpad's tap-click (off the read thread: the write mustn't hold up the input)
        void Pulse()
        {
            ThreadPool.QueueUserWorkItem(_ =>
            {
                try { TestRumble(0, 70); Thread.Sleep(35); TestRumble(0, 0); } catch { }
            });
        }

        // Rumble test from the test window (works without game mode too)
        public void TestRumble(byte l, byte s)
        {
            rumbleTest = l != 0 || s != 0 || rumbleTest;
            large = l; small = s;
            Send(LR < 0 ? 0 : LR, LG < 0 ? 0 : LG, LB < 0 ? 0 : LB);
            if (l == 0 && s == 0) rumbleTest = false;
        }

        // Once a second (the light bar loop): the report rate, and for the link meter the longest silence in that second
        public void TickRate()
        {
            ReportRate = demo ? Showcase.Rate(this) : Interlocked.Exchange(ref reports, 0);
            if (demo || !Bluetooth) return;   // a cable doesn't drop reports
            long last = lastReportMs;
            int gap = worstGap; worstGap = 0;
            if (last >= 0) gap = (int)Math.Max(gap, Engine.Ms - last);   // nothing came at all
            link.Second(gap);
        }

        // Light bar color + rumble (+ player LEDs, adaptive triggers on a DualSense) — one output packet
        public bool Send(int r, int g, int b)
        {
            if (demo) { LR = r; LG = g; LB = b; return true; }
            lock (writeLock)
            {
                if (hWrite == null || hWrite.IsClosed) return false;
                byte[] buf = outBuf;
                Array.Clear(buf, 0, buf.Length);
                bool rumble = Virtual || rumbleTest;   // only in game mode, so we don't override someone else's rumble otherwise
                int len = DualSense
                    ? DualSenseWire.Output(buf, Bluetooth, outLen, ref ds5Seq, ref ds5Released, rumble, small, large, trigL, trigR, PlayerLeds, Player, r, g, b, MuteLed, HapticRumble)
                    : DualShock4Wire.Output(buf, Bluetooth, outLen, BtPollMs, rumble, small, large, r, g, b);
                int written;
                bool ok = Native.WriteFile(hWrite, buf, len, out written, IntPtr.Zero) || Native.HidD_SetOutputReport(hWrite, buf, len);
                if (ok) { LR = r; LG = g; LB = b; }
                return ok;
            }
        }

        public void Dispose()
        {
            alive = false;
            if (hRead != null) { Native.CancelIoEx(hRead, IntPtr.Zero); }
            if (reader != null) reader.Join(500);
            if (hRead != null) hRead.Dispose();
            if (Pro.ControllerGone != null) try { Pro.ControllerGone(Id); } catch (Exception e) { Log.Error("controller gone", e); }   // e.g. let go of remapped keys
            VirtualPad v = vpad;
            vpad = null;
            if (v != null) v.Disconnect();
            lock (writeLock) { if (hWrite != null) hWrite.Dispose(); }
        }
    }
}
