// Pro (experimental): a real Xbox controller (or any XInput one) as a source — read through XInput, hidden from games with
// HidHide, and shown to them as a virtual DualShock 4 that goes through the same mapping as the PlayStation pads (remap,
// macros, sticks, profiles): PlayStation button prompts and Glowstick's input features for an Xbox pad. No light bar.
// Two guards against reading our own virtual Xbox 360 pads (they are XInput too): the XInput slots they hold are skipped,
// and no more slots are read than physical Xbox devices are found outside ViGEmBus. The output is always a DualShock 4,
// so a pad read here can never be read back. Runs on its own thread at ~250 Hz, only in game mode, only when turned on.
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using Nefarius.ViGEm.Client;

namespace Glowstick
{
    internal static class XboxSource
    {
        public static volatile bool Enabled;   // settings.json "XboxSource"
        public static volatile int Count;      // Xbox controllers being read now

        sealed class Slot { public VirtualPad Pad; public uint Packet = uint.MaxValue; }
        static readonly Slot[] slots = new Slot[4];
        static Thread worker;
        static volatile bool running;
        static ViGEmClient client;

        // Engine: every scan (game mode on, Pro unlocked) — on or off as the setting says
        public static void Ensure(ViGEmClient c, bool on)
        {
            if (on && !running && c != null) { client = c; running = true; worker = new Thread(Loop) { IsBackground = true, Name = "Glowstick Xbox", Priority = ThreadPriority.AboveNormal }; worker.Start(); }
            else if (!on && running) Stop();
        }

        public static void Stop()
        {
            running = false;
            Thread w = worker;
            if (w != null) w.Join(1000);
            worker = null;
        }

        static void Loop()
        {
            long scanAt = -100000;
            int physical = 0;
            byte[] raw = new byte[63];
            try
            {
                while (running)
                {
                    long now = Engine.Ms;
                    if (now - scanAt > 2000) { scanAt = now; physical = HideAll(); }
                    HashSet<int> own = Engine.OwnXInputSlots();
                    int read = 0;
                    for (int i = 0; i < 4; i++)
                    {
                        XState st = default(XState);
                        bool there = !own.Contains(i) && read < physical && Get(i, out st);
                        if (!there) { Drop(i); continue; }
                        read++;
                        Slot s = slots[i] ?? (slots[i] = Open(i));
                        if (st.Packet == s.Packet) continue;
                        s.Packet = st.Packet;
                        ToDs4(st.Pad, raw);
                        byte[] send = Pro.MapPad != null ? Pro.MapPad("xbox" + i, raw, raw, now) : raw;
                        if (s.Pad != null) s.Pad.Submit(send);
                    }
                    Count = read;
                    Thread.Sleep(4);
                }
            }
            catch (Exception e) { Log.Error("Xbox source", e); }
            finally { for (int i = 0; i < 4; i++) Drop(i); Count = 0; running = false; }
        }

        static Slot Open(int i)
        {
            Slot s = new Slot();
            int slot = i;
            try { s.Pad = new VirtualPad(client, "DS4", (l, sm) => Rumble(slot, l, sm), "xbox" + i); }
            catch (Exception e) { Log.Error("Xbox source pad", e); }
            Log.Info("Xbox controller in XInput slot " + i + " → virtual DualShock 4");
            return s;
        }

        static void Drop(int i)
        {
            Slot s = slots[i];
            if (s == null) return;
            slots[i] = null;
            if (s.Pad != null) s.Pad.Disconnect();
            if (Pro.ControllerGone != null) try { Pro.ControllerGone("xbox" + i); } catch { }
            Log.Info("Xbox controller in XInput slot " + i + " gone");
        }

        // The game's rumble on the virtual DS4 → the Xbox controller's motors
        static void Rumble(int i, byte large, byte small)
        {
            XVibration v = new XVibration { Left = (ushort)(large * 257), Right = (ushort)(small * 257) };
            try { XInputSetState(i, ref v); } catch { }
        }

        // Physical Xbox devices (XUSB, and XInput HID over Bluetooth) outside ViGEmBus — hidden from games; how many there are
        static int HideAll()
        {
            int n = 0;
            List<HidDeviceEntry> found = Native.EnumerateInterface(XusbInterface);
            foreach (HidDeviceEntry e in Native.EnumerateHid())
                if (e.Path.IndexOf("vid_045e", StringComparison.OrdinalIgnoreCase) >= 0 && e.Path.IndexOf("&ig_", StringComparison.OrdinalIgnoreCase) >= 0) found.Add(e);
            foreach (HidDeviceEntry e in found)
            {
                if (Native.IsOnViGEmBus(e.DevInst) || e.InstanceId == null) continue;
                n++;
                if (HidHide.Installed && HidHide.Hide(e.InstanceId) == HidHide.NewlyHidden) HidHide.RestartDevice(e.InstanceId);
            }
            return n;
        }

        static readonly Guid XusbInterface = new Guid("EC87F1E3-C13B-4100-B5F7-8B84D54260CB");

        // ---------- XInput → the DS4 report layout ----------
        internal const ushort Up = 0x1, Down = 0x2, Left = 0x4, Right = 0x8, Start = 0x10, Back = 0x20, LThumb = 0x40, RThumb = 0x80,
            LB = 0x100, RB = 0x200, Guide = 0x400, A = 0x1000, B = 0x2000, X = 0x4000, Y = 0x8000;

        internal static void ToDs4(XPad g, byte[] raw)
        {
            Array.Clear(raw, 0, raw.Length);
            raw[0] = Axis(g.LX, false); raw[1] = Axis(g.LY, true); raw[2] = Axis(g.RX, false); raw[3] = Axis(g.RY, true);
            raw[7] = g.LT; raw[8] = g.RT;
            Btn b = Btn.None;
            if ((g.Buttons & A) != 0) b |= Btn.Cross;
            if ((g.Buttons & B) != 0) b |= Btn.Circle;
            if ((g.Buttons & X) != 0) b |= Btn.Square;
            if ((g.Buttons & Y) != 0) b |= Btn.Triangle;
            if ((g.Buttons & Up) != 0) b |= Btn.Up;
            if ((g.Buttons & Down) != 0) b |= Btn.Down;
            if ((g.Buttons & Left) != 0) b |= Btn.Left;
            if ((g.Buttons & Right) != 0) b |= Btn.Right;
            if ((g.Buttons & LB) != 0) b |= Btn.L1;
            if ((g.Buttons & RB) != 0) b |= Btn.R1;
            if ((g.Buttons & Back) != 0) b |= Btn.Share;
            if ((g.Buttons & Start) != 0) b |= Btn.Options;
            if ((g.Buttons & LThumb) != 0) b |= Btn.L3;
            if ((g.Buttons & RThumb) != 0) b |= Btn.R3;
            if ((g.Buttons & Guide) != 0) b |= Btn.PS;
            if (g.LT > 30) b |= Btn.L2;
            if (g.RT > 30) b |= Btn.R2;
            Pad.WriteButtons(raw, b);
            raw[21] = 0x20;   // accelerometer Y = 8192 (1 g): a controller lying flat, not a falling one
            raw[34] = 0x80; raw[38] = 0x80;   // no finger on the (missing) touchpad
        }

        // XInput short (Y up = +) → DS4 byte (Y up = 0)
        internal static byte Axis(short v, bool invert)
        {
            int b = (v + 32768) >> 8;
            return (byte)(invert ? 255 - b : b);
        }

        // ---------- XInput ----------
        [StructLayout(LayoutKind.Sequential)] internal struct XPad { public ushort Buttons; public byte LT, RT; public short LX, LY, RX, RY; }
        [StructLayout(LayoutKind.Sequential)] struct XState { public uint Packet; public XPad Pad; }
        [StructLayout(LayoutKind.Sequential)] struct XVibration { public ushort Left, Right; }

        [DllImport("xinput1_4.dll", EntryPoint = "#100")] static extern int XInputGetStateEx(int index, out XState state);   // + the Guide button
        [DllImport("xinput1_4.dll")] static extern int XInputGetState(int index, out XState state);
        [DllImport("xinput1_4.dll")] static extern int XInputSetState(int index, ref XVibration v);

        static bool exUnavailable;

        internal static bool Probe(int i) { XState s; return Get(i, out s); }   // tests: XInput loads and answers

        static bool Get(int i, out XState s)
        {
            if (!exUnavailable)
                try { return XInputGetStateEx(i, out s) == 0; } catch (EntryPointNotFoundException) { exUnavailable = true; }
            return XInputGetState(i, out s) == 0;
        }
    }
}
