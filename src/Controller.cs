// One physical controller — DualShock 4 or DualSense: light bar/rumble (output), battery,
// and in game mode, forwarding input to a virtual DS4.
//
// Hot path (latency): a separate highest-priority thread blocks on ReadFile
// and as soon as a report arrives copies its 63 bytes as-is into the virtual pad's SubmitRawReport. No timers or queues.
// A DualSense report is first translated to the DS4 layout (Ds5ToDs4) — after that everything is the same: virtual DS4, test, touchpad.
// Separate handles for reading and writing: synchronous operations on one handle are serialized,
// so a light bar write would otherwise wait for the next input report.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using Microsoft.Win32.SafeHandles;
using Nefarius.ViGEm.Client;
using Nefarius.ViGEm.Client.Targets;
using Nefarius.ViGEm.Client.Targets.DualShock4;
using Nefarius.ViGEm.Client.Targets.Xbox360;

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
        public ulong BtAddress;

        // State for the light bar engine (timestamps — Engine.Ms)
        public long LastActiveMs, BatteryShowUntil, NotifyStart, NotifyUntil;
        public Rgb NotifyColor;
        public volatile Btn LastPressed;       // buttons pressed in the last press (for presets that react to buttons)
        public long LastPressMs = -100000;
        public double Dim = 1, StickHue = 0.6;
        public bool IdleOff;

        int inLen, outLen, reports;
        SafeFileHandle hWrite, hRead;
        IVirtualGamepad pad;
        IDualShock4Controller ds4;
        IXbox360Controller x360;
        ViGEmClient vigem;
        // What games should see this controller as: "Xbox" — a virtual Xbox 360, anything else — a virtual DualShock 4 (Pro).
        public volatile string OutputKind = "DS4";
        Thread reader;
        volatile bool alive;
        volatile byte large, small;
        volatile byte[] trigL, trigR;   // DualSense adaptive-trigger FFB (11 bytes each) or null — no effect
        volatile bool rumbleTest;
        readonly object writeLock = new object();
        readonly byte[] outBuf;

        // Last report (for live presets and the test window) and read-thread state
        readonly byte[] last = new byte[63], fwd = new byte[63];
        readonly object lastLock = new object();
        bool hasLast;
        Btn prevBtn, hotPrev;
        byte restLX, restLY, restRX, restRY;
        bool psHeld, psCombo, psPass, touchWas;
        long psDownMs, psTapUntil;
        double batT0 = -1; int batB0;
        readonly Touchpad touch = new Touchpad();

        public bool Virtual { get { return pad != null; } }
        public int BtPollMs = 0;

        // Product IDs of DualSense and DualSense Edge
        public static readonly string[] DualSensePids = { "0ce6", "0df2" };

        Controller(string path, string instanceId, SafeFileHandle w, int inLen, int outLen)
        {
            Path = path; InstanceId = instanceId; hWrite = w; this.inLen = inLen; this.outLen = outLen;
            string lp = path.ToLowerInvariant();
            Bluetooth = lp.Contains("vid&0002054c") || lp.Contains("00001124-0000-1000-8000-00805f9b34fb") || outLen >= 78;
            foreach (string pid in DualSensePids) if (lp.Contains("pid_" + pid) || lp.Contains("pid&" + pid)) DualSense = true;
            outBuf = new byte[Math.Max(outLen, Bluetooth ? 78 : DualSense ? 48 : 32)];
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

        // A separate read handle: battery always, and in game mode (client != null) also a virtual DS4
        public bool Start(ViGEmClient client)
        {
            if (inLen < 10) return false;
            hRead = Native.Open(Path, Native.GENERIC_READ | Native.GENERIC_WRITE);
            if (hRead.IsInvalid) { hRead.Dispose(); hRead = Native.Open(Path, Native.GENERIC_READ); }
            if (hRead.IsInvalid) { hRead.Dispose(); hRead = null; return false; }
            Native.HidD_SetNumInputBuffers(hRead, 2);   // don't pile up old reports if we ever fall behind

            vigem = client;
            if (client != null) ConnectVirtual();

            alive = true;
            reader = new Thread(ReadLoop);
            reader.IsBackground = true; reader.Name = "DS4 input";
            reader.Priority = pad != null ? ThreadPriority.Highest : ThreadPriority.Normal;
            reader.Start();
            return true;
        }

        // Creates the virtual pad of the chosen kind (DualShock 4 or Xbox 360). On failure — no virtual pad, battery still read.
        void ConnectVirtual()
        {
            try
            {
                if (OutputKind == "Xbox")
                {
                    x360 = vigem.CreateXbox360Controller();
                    x360.AutoSubmitReport = false;
                    x360.FeedbackReceived += OnX360Feedback;
                    pad = x360;
                }
                else
                {
                    ds4 = vigem.CreateDualShock4Controller();
                    ds4.FeedbackReceived += OnFeedback;
                    pad = ds4;
                }
                pad.Connect();
            }
            catch (Exception e) { pad = null; ds4 = null; x360 = null; Log.Error("virtual pad", e); }
        }

        void DisconnectVirtual()
        {
            try
            {
                if (ds4 != null) ds4.FeedbackReceived -= OnFeedback;
                if (x360 != null) x360.FeedbackReceived -= OnX360Feedback;
                if (pad != null) pad.Disconnect();
            }
            catch { }
            pad = null; ds4 = null; x360 = null;
        }

        // Change what games see this controller as, live (recreates the virtual pad). No effect outside game mode.
        public void SwitchOutput(string kind)
        {
            OutputKind = kind == "Xbox" ? "Xbox" : "DS4";
            if (vigem == null) return;
            lock (writeLock) { DisconnectVirtual(); ConnectVirtual(); }
        }

        void OnFeedback(object sender, DualShock4FeedbackReceivedEventArgs e) { SetRumble(e.LargeMotor, e.SmallMotor); }
        void OnX360Feedback(object sender, Xbox360FeedbackReceivedEventArgs e) { SetRumble(e.LargeMotor, e.SmallMotor); }

        // Forward the (possibly hotkey-masked) DS4 report to the virtual Xbox 360: map buttons, sticks (Y inverted) and triggers
        static void FeedX360(IXbox360Controller x, byte[] raw)
        {
            Pad p = Pad.Parse(raw);
            Btn b = p.Buttons;
            x.SetButtonState(Xbox360Button.A, (b & Btn.Cross) != 0);
            x.SetButtonState(Xbox360Button.B, (b & Btn.Circle) != 0);
            x.SetButtonState(Xbox360Button.X, (b & Btn.Square) != 0);
            x.SetButtonState(Xbox360Button.Y, (b & Btn.Triangle) != 0);
            x.SetButtonState(Xbox360Button.LeftShoulder, (b & Btn.L1) != 0);
            x.SetButtonState(Xbox360Button.RightShoulder, (b & Btn.R1) != 0);
            x.SetButtonState(Xbox360Button.Back, (b & Btn.Share) != 0);
            x.SetButtonState(Xbox360Button.Start, (b & Btn.Options) != 0);
            x.SetButtonState(Xbox360Button.LeftThumb, (b & Btn.L3) != 0);
            x.SetButtonState(Xbox360Button.RightThumb, (b & Btn.R3) != 0);
            x.SetButtonState(Xbox360Button.Guide, (b & Btn.PS) != 0);
            x.SetButtonState(Xbox360Button.Up, (b & Btn.Up) != 0);
            x.SetButtonState(Xbox360Button.Down, (b & Btn.Down) != 0);
            x.SetButtonState(Xbox360Button.Left, (b & Btn.Left) != 0);
            x.SetButtonState(Xbox360Button.Right, (b & Btn.Right) != 0);
            x.SetAxisValue(Xbox360Axis.LeftThumbX, Axis(p.LX, false));
            x.SetAxisValue(Xbox360Axis.LeftThumbY, Axis(p.LY, true));
            x.SetAxisValue(Xbox360Axis.RightThumbX, Axis(p.RX, false));
            x.SetAxisValue(Xbox360Axis.RightThumbY, Axis(p.RY, true));
            x.SetSliderValue(Xbox360Slider.LeftTrigger, p.L2);
            x.SetSliderValue(Xbox360Slider.RightTrigger, p.R2);
            x.SubmitReport();
        }

        // Stick byte 0..255 (128 = center) → Xbox short; invert — the Y axes (DS4 up = 0, Xbox up = +)
        static short Axis(byte v, bool invert)
        {
            int s = (v - 128) * 257;
            if (invert) s = -s;
            return (short)Math.Max(-32768, Math.Min(32767, s));
        }

        void SetRumble(byte l, byte s)
        {
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
                bool full = false;   // full report — has the battery level
                if (DualSense)
                {
                    int o = buf[0] == 0x31 && n >= 78 ? 2 : buf[0] == 0x01 && n >= 64 ? 1 : 0;   // BT / USB
                    if (o == 0) continue;
                    Ds5ToDs4(buf, o, raw);
                    full = true;
                }
                else if (buf[0] == 0x11 && n >= 66) { Array.Copy(buf, 3, raw, 0, 63); full = true; }   // Bluetooth, full report
                // USB; over BT the short 0x01 report padded by ReadFile to full length also lands here — it has no battery
                else if (buf[0] == 0x01 && n >= 64) { Array.Copy(buf, 1, raw, 0, 63); full = !Bluetooth; }
                else if (buf[0] == 0x01 && n >= 10)
                {
                    if (pad == null) continue;
                    // Bluetooth, short report (before the first output packet): sticks/buttons/triggers only
                    Array.Clear(raw, 0, raw.Length);
                    Array.Copy(buf, 1, raw, 0, 9);
                    raw[34] = 0x80; raw[38] = 0x80;   // "no touch" on the touchpad
                }
                else continue;
                long now = Engine.Ms;
                Btn btn = Pad.ReadButtons(raw);
                byte[] send = raw;
                if (Engine.Hotkeys && Hotkeys(btn, now) && pad != null) send = Masked(raw, now);
                if (Pro.MapReport != null && pad != null) send = Pro.MapReport(this, raw, send, now);
                try
                {
                    IDualShock4Controller d = ds4; IXbox360Controller x = x360;
                    if (d != null) d.SubmitRawReport(send);
                    else if (x != null) FeedX360(x, send);
                }
                catch { }
                Interlocked.Increment(ref reports);

                // Below — everything that must not delay forwarding
                lock (lastLock) { Buffer.BlockCopy(raw, 0, last, 0, 63); hasLast = true; }
                Activity(raw, btn, now);
                // Battery: DS4 — bits 0-3 level, bit 4 — cable power; DualSense — more precise, from Ds5ToDs4
                if (full)
                {
                    if (DualSense) UpdateBattery(ds5Pct, ds5Chg, now);
                    else
                    {
                        int b = raw[29]; bool chg = (b & 0x10) != 0;
                        UpdateBattery(Math.Min(100, (b & 0x0F) * 100 / (chg ? 11 : 8)), chg, now);
                    }
                }
                if (Engine.TouchMouse && !Fullscreen.App) { Pad p = Pad.Parse(raw); touch.Process(ref p); touchWas = true; }
                else if (touchWas) { touch.Reset(); touchWas = false; }
            }
            if (touchWas) touch.Reset();
        }

        // ---------- DualSense → DS4 layout ----------
        // DualSense report (after the ID; over BT also after a sequence byte — o = 2): 0-3 sticks, 4-5 L2/R2, 7-9 buttons
        // (7 and 8 are bitwise the same as DS4), 15-26 gyro/accelerometer, 27-30 sensor time (×0.33 µs), 32-39 touchpad, 52 — battery.
        int ds5Pct = -1, ds5Counter; bool ds5Chg;

        void Ds5ToDs4(byte[] b, int o, byte[] raw) { Ds5ToDs4(b, o, raw, ref ds5Counter, out ds5Pct, out ds5Chg); }

        internal static void Ds5ToDs4(byte[] b, int o, byte[] raw, ref int counter, out int pct, out bool charging)
        {
            Array.Clear(raw, 0, raw.Length);
            raw[0] = b[o]; raw[1] = b[o + 1]; raw[2] = b[o + 2]; raw[3] = b[o + 3];
            raw[4] = b[o + 7]; raw[5] = b[o + 8];
            counter = (counter + 1) & 0x3F;
            raw[6] = (byte)((b[o + 9] & 0x03) | counter << 2);   // PS, touchpad click + counter (the DS4 has no Mute button)
            raw[7] = b[o + 4]; raw[8] = b[o + 5];
            uint ts = (uint)(b[o + 27] | b[o + 28] << 8 | b[o + 29] << 16 | b[o + 30] << 24) / 16;   // the DS4 counts in 5.33 µs units
            raw[9] = (byte)ts; raw[10] = (byte)(ts >> 8);
            Buffer.BlockCopy(b, o + 15, raw, 12, 12);   // gyro and accelerometer — same order

            int st = b[o + 52], lvl = Math.Min(10, st & 0x0F), chgState = st >> 4 & 0x0F;
            charging = chgState == 1 || chgState == 2;   // 1 — charging, 2 — fully charged
            pct = Math.Min(100, lvl * 10 + 5);
            raw[29] = (byte)(charging ? 0x10 | Math.Min(11, lvl * 11 / 10) : Math.Min(8, lvl * 8 / 10));

            raw[32] = 1; raw[33] = (byte)counter;    // one touchpad packet
            Touch(b, o + 32, raw, 34);
            Touch(b, o + 36, raw, 38);
        }

        // A touchpad point — same encoding as the DS4, but 1080 high instead of 943
        static void Touch(byte[] b, int s, byte[] raw, int d)
        {
            int x = b[s + 1] | (b[s + 2] & 0x0F) << 8, y = (b[s + 2] >> 4 | b[s + 3] << 4) * Pad.TouchH / 1080;
            raw[d] = b[s]; raw[d + 1] = (byte)x; raw[d + 2] = (byte)(x >> 8 & 0x0F | (y & 0x0F) << 4); raw[d + 3] = (byte)(y >> 4);
        }

        // ---------- hotkeys: PS + button (which buttons — HotkeyMap) ----------

        // Returns true if the report for the game must be adjusted (PS is held or a PS "tap" is still in progress).
        // While PS is held, neither PS nor combo buttons reach the game. Released without a combo — the game gets a short PS;
        // held longer than 0.4 s without a combo — PS goes to the game right away (long press for Steam etc.).
        bool Hotkeys(Btn b, long now)
        {
            bool ps = (b & Btn.PS) != 0;
            if (ps && !psHeld) { psHeld = true; psCombo = false; psPass = false; psDownMs = now; }
            if (psHeld)
            {
                if (!ps)
                {
                    psHeld = false;
                    if (!psCombo && !psPass) psTapUntil = now + 100;
                }
                else if (!psPass)
                {
                    List<HotAction> fired = HotkeyMap.Fired(b, hotPrev);
                    if (fired.Count > 0) { psCombo = true; foreach (HotAction a in fired) Engine.Hotkey(this, a); }
                    else if (!psCombo && now - psDownMs > 400) psPass = true;
                }
            }
            hotPrev = b;
            return (psHeld && !psPass) || now < psTapUntil;
        }

        byte[] Masked(byte[] raw, long now)
        {
            Buffer.BlockCopy(raw, 0, fwd, 0, 63);
            if (psHeld && !psPass)
            {
                uint m = (uint)HotkeyMap.All;
                fwd[4] = 0x08;                               // d-pad neutral, □✕○△ released
                fwd[5] &= (byte)~(m >> 8);                   // shoulder buttons, Share, Options, L3/R3 used as hotkeys
                fwd[6] &= (byte)~(0x01 | (m >> 16) & 0x02); // PS and, if it's a hotkey, the touchpad click
                if ((m & (uint)Btn.L2) != 0) fwd[7] = 0;     // the triggers' analog values too
                if ((m & (uint)Btn.R2) != 0) fwd[8] = 0;
            }
            else if (now < psTapUntil) fwd[6] |= 0x01;
            return fwd;
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
            if (Battery < 0 || chg != Charging) { Battery = pct; Charging = chg; batT0 = -1; DrainPerHour = -1; return; }
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
            else if (pct > Battery || pct <= Battery - 25) Battery = pct;
        }

        // Hours left; -1 — not known yet
        public double HoursLeft { get { double d = DrainPerHour; return d > 0 && !Charging && Battery >= 0 ? Battery / d : -1; } }

        public bool CopyLast(byte[] dst)
        {
            lock (lastLock) { if (!hasLast) return false; Buffer.BlockCopy(last, 0, dst, 0, 63); return true; }
        }

        // Rumble test from the test window (works without game mode too)
        public void TestRumble(byte l, byte s)
        {
            rumbleTest = l != 0 || s != 0 || rumbleTest;
            large = l; small = s;
            Send(LR < 0 ? 0 : LR, LG < 0 ? 0 : LG, LB < 0 ? 0 : LB);
            if (l == 0 && s == 0) rumbleTest = false;
        }

        public void TickRate() { ReportRate = Interlocked.Exchange(ref reports, 0); }

        public bool Send(int r, int g, int b)
        {
            lock (writeLock)
            {
                if (hWrite == null || hWrite.IsClosed) return false;
                byte[] buf = outBuf;
                Array.Clear(buf, 0, buf.Length);
                // 0x01 — rumble (only in game mode, so we don't override someone else's otherwise), 0x02 — light bar, 0x04 — blink
                byte flags = (byte)(Virtual || rumbleTest ? 0x07 : 0x06);
                int len;
                if (DualSense) len = Ds5Packet(buf, r, g, b);
                else if (Bluetooth)
                {
                    buf[0] = 0x11; buf[1] = (byte)(0xC0 | (BtPollMs & 0x3F)); buf[3] = flags;
                    buf[6] = small; buf[7] = large;
                    buf[8] = (byte)r; buf[9] = (byte)g; buf[10] = (byte)b;
                    uint crc = Crc32.Bt(buf, 74);
                    buf[74] = (byte)crc; buf[75] = (byte)(crc >> 8); buf[76] = (byte)(crc >> 16); buf[77] = (byte)(crc >> 24);
                    len = Math.Max(outLen, 78);
                }
                else
                {
                    buf[0] = 0x05; buf[1] = flags;
                    buf[4] = small; buf[5] = large;
                    buf[6] = (byte)r; buf[7] = (byte)g; buf[8] = (byte)b;
                    len = Math.Max(outLen, 32);
                }
                int written;
                bool ok = Native.WriteFile(hWrite, buf, len, out written, IntPtr.Zero) || Native.HidD_SetOutputReport(hWrite, buf, len);
                if (ok) { LR = r; LG = g; LB = b; }
                return ok;
            }
        }

        // DualSense output packet: USB — 0x02, BT — 0x31 + sequence + tag 0x10 + CRC32; the common part starts at o.
        // The first packet turns off the blue connect animation (otherwise the color won't apply), then — light bar, player, rumble.
        static readonly byte[] PlayerLeds = { 0x04, 0x0A, 0x15, 0x1B, 0x1F };   // player 1..5, as on the PS5
        public static bool PlayerLedsOn = true;
        bool ds5Released; int ds5Seq;

        int Ds5Packet(byte[] buf, int r, int g, int b)
        {
            int o;
            if (Bluetooth) { buf[0] = 0x31; buf[1] = (byte)(ds5Seq << 4); ds5Seq = (ds5Seq + 1) & 0x0F; buf[2] = 0x10; o = 3; }
            else { buf[0] = 0x02; o = 1; }
            buf[o] = (byte)(Virtual || rumbleTest ? 0x03 : 0x00);   // compatible rumble + motor select
            buf[o + 1] = (byte)(0x04 | (PlayerLedsOn ? 0x10 : 0));  // light bar, player LEDs
            buf[o + 2] = small; buf[o + 3] = large;
            byte[] tr = trigR, tl = trigL;                          // adaptive triggers: ValidFlag0 bit2/bit3, FFB at o+10 / o+21
            if (tr != null) { buf[o] |= 0x04; Array.Copy(tr, 0, buf, o + 10, 11); }
            if (tl != null) { buf[o] |= 0x08; Array.Copy(tl, 0, buf, o + 21, 11); }
            if (!ds5Released) { buf[o + 38] = 0x02; buf[o + 41] = 0x02; ds5Released = true; }
            int p = Player;
            buf[o + 43] = PlayerLedsOn && p > 0 ? PlayerLeds[Math.Min(p, PlayerLeds.Length) - 1] : (byte)0;
            buf[o + 44] = (byte)r; buf[o + 45] = (byte)g; buf[o + 46] = (byte)b;
            if (!Bluetooth) return Math.Max(outLen, 48);
            uint crc = Crc32.Bt(buf, 74);
            buf[74] = (byte)crc; buf[75] = (byte)(crc >> 8); buf[76] = (byte)(crc >> 16); buf[77] = (byte)(crc >> 24);
            return Math.Max(outLen, 78);
        }

        public void Dispose()
        {
            alive = false;
            if (hRead != null) { Native.CancelIoEx(hRead, IntPtr.Zero); }
            if (reader != null) reader.Join(500);
            if (hRead != null) hRead.Dispose();
            if (pad != null) DisconnectVirtual();
            lock (writeLock) { hWrite.Dispose(); }
        }
    }

    internal static class Crc32
    {
        static readonly uint[] Table = Make();

        static uint[] Make()
        {
            uint[] t = new uint[256];
            for (uint i = 0; i < 256; i++)
            {
                uint c = i;
                for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
                t[i] = c;
            }
            return t;
        }

        public static uint Bt(byte[] data, int len)
        {
            uint c = 0xFFFFFFFF;
            c = Table[(c ^ 0xA2) & 0xFF] ^ (c >> 8);   // BT output seed byte
            for (int i = 0; i < len; i++) c = Table[(c ^ data[i]) & 0xFF] ^ (c >> 8);
            return ~c;
        }
    }
}
