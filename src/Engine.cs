// Engine: finding controllers, light bar animation, game mode (virtual DS4 + hiding the real ones),
// plus everything layered on top of a preset: notification flashes, battery display, idle dimming, hotkeys, BT auto-off.
// Controller order and per-controller settings (name, own preset) are tied to the MAC — they don't change depending on
// whether the controller was connected over USB or BT, or in what sequence.
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Threading;
using Nefarius.ViGEm.Client;

namespace Glowstick
{
    public class ControllerInfo
    {
        public string Id, Kind, Model, Name, OwnPreset, Warning, Output; public Color Color; public bool Virtual;
        public int Rate, Battery; public bool Charging; public double HoursLeft, Brightness;
    }

    // Settings of a specific controller (by MAC): a name, its own preset ("" — same as everyone), and the virtual pad kind
    public class PadConfig { public string Name = "", Preset = "", Output = ""; public double Brightness = 1.0; }

    public static class Engine
    {
        public static readonly List<Preset> BuiltIn = PresetLib.Build();
        static volatile List<Preset> presets = new List<Preset>(BuiltIn);
        // Custom + built-in presets. The list is never changed in place — it's replaced as a whole (ReplacePresets)
        public static List<Preset> Presets { get { return presets; } }
        // Changed by the UI, the hotkeys (light bar thread) and the local API; read every frame — hence Volatile
        static double brightness = 1.0, speed = 1.0;
        public static double Brightness { get { return Volatile.Read(ref brightness); } set { Volatile.Write(ref brightness, value); } }
        public static double Speed { get { return Volatile.Read(ref speed); } set { Volatile.Write(ref speed, value); } }
        public static volatile int PresetIndex;
        public static int BtPollMs = 0;
        public static bool IsAdmin;

        // Controller order (IDs; also remembers ones not connected right now) and per-controller settings. Under Lock.
        public static readonly List<string> Order = new List<string>();
        public static readonly Dictionary<string, PadConfig> Pads = new Dictionary<string, PadConfig>();

        // Settings from the Settings window
        public static bool NotifyFlash = true;
        public static volatile bool Hotkeys = true, TouchMouse;
        public static TouchConfig Touch = new TouchConfig();   // touchpad-as-mouse behaviour (a Pro input profile can bring its own)
        public static int IdleDimMin = 10, IdleOffMin = 30;   // 0 — off

        static readonly Stopwatch clock = Stopwatch.StartNew();
        public static long Ms { get { return clock.ElapsedMilliseconds; } }
        static readonly ConcurrentQueue<KeyValuePair<Controller, HotAction>> hotkeys = new ConcurrentQueue<KeyValuePair<Controller, HotAction>>();

        // Minimum interval between light bar packets so the Bluetooth link isn't flooded (rumble goes without delay)
        const int MinSendMsUsb = 15, MinSendMsBt = 40, KeepAliveMs = 1500;

        static readonly List<Controller> Devices = new List<Controller>();   // sorted by Order
        internal static readonly object Lock = new object();
        static Thread worker;
        static volatile bool running, rescanRequested, modeDirty;
        static double T;
        static ViGEmClient client;
        static bool clientTried;
        static bool notLoaded;   // the drivers are installed but not loaded (just installed — waiting for a reboot)
        static volatile string status = "";

        public static string Status { get { return status; } }

        // Game mode is always on; it works only with administrator rights and both drivers
        static bool GameModeActive { get { return IsAdmin && client != null && HidHide.Installed; } }
        // Game mode works (the UI shows a warning card otherwise); "unavailable" states are visible in Status
        public static bool GameModeWorking { get { return GameModeActive && !notLoaded; } }

        public static void Start()
        {
            if (running) return;
            running = true;
            worker = new Thread(Loop); worker.IsBackground = true; worker.Name = "lights"; worker.Start();
            Fullscreen.Start();
        }

        public static void Stop()
        {
            running = false;
            Fullscreen.Stop();
            if (worker != null) worker.Join(3000);
            ReleaseAll();
            if (IsAdmin && HidHide.Installed) HidHide.Release();
            if (client != null) { try { client.Dispose(); } catch { } client = null; }
        }

        public static void Rescan() { rescanRequested = true; }

        // After installing drivers — reconnect to ViGEmBus and restart game mode
        public static void DriversChanged() { clientTried = false; modeDirty = true; }

        public static void SetPreset(int index)
        {
            List<Preset> ps = Presets;
            if (index >= 0 && index < ps.Count && ps[index].Locked) return;   // a Pro preset without Pro is shown, never applied
            lock (Lock) { apiGlobal = null; foreach (Controller d in Devices) { d.ApiPreset = null; if (d.OwnPreset == null) { d.FadeFrom = d.Current; d.Fade = 0; } } }
            PresetIndex = index;
        }

        // ---------- live override through the local API (Pro): a solid color or an effect, not saved as a preset ----------
        static volatile Preset apiGlobal;   // for every controller without its own API override; null — none

        // id null/"" — all controllers (global); otherwise just that one. preset null — clear the override (back to presets).
        public static void SetApiPreset(string id, Preset preset)
        {
            lock (Lock)
            {
                if (string.IsNullOrEmpty(id)) { apiGlobal = preset; foreach (Controller d in Devices) { d.ApiPreset = null; d.FadeFrom = d.Current; d.Fade = 0; } }
                else foreach (Controller d in Devices) if (d.Id == id) { d.ApiPreset = preset; d.FadeFrom = d.Current; d.Fade = 0; }
            }
        }

        // A new preset list (a custom one was added/changed/deleted). The preset named select stays selected.
        public static void ReplacePresets(List<Preset> list, string select)
        {
            int idx = list.FindIndex(p => p.Name == select);
            presets = list;
            if (idx >= 0) PresetIndex = idx; else SetPreset(0);
        }

        // ---------- order and per-controller settings ----------
        static PadConfig Config(string id)
        {
            PadConfig c;
            if (!Pads.TryGetValue(id, out c)) { c = new PadConfig(); Pads[id] = c; }
            return c;
        }

        static void SortDevices()
        {
            Devices.Sort((x, y) => Order.IndexOf(x.Id).CompareTo(Order.IndexOf(y.Id)));
            for (int k = 0; k < Devices.Count; k++) Devices[k].Player = k + 1;
        }

        // Drag and drop: put a controller at place `index` among the connected ones
        public static void MoveTo(string id, int index)
        {
            lock (Lock)
            {
                List<string> connected = Devices.ConvertAll(d => d.Id);
                if (!Reorder(Order, connected, id, index)) return;
                SortDevices();
            }
            Settings.Save();
        }

        // The connected controllers keep the slots they have in `order` (ones not connected now stay where they are);
        // within those slots `id` moves to place `index`. False — nothing changed.
        internal static bool Reorder(List<string> order, List<string> connected, string id, int index)
        {
            int k = connected.IndexOf(id);
            if (k < 0) return false;
            index = Math.Max(0, Math.Min(connected.Count - 1, index));
            if (index == k) return false;
            List<int> slots = connected.ConvertAll(c => order.IndexOf(c));
            if (slots.Contains(-1)) return false;
            slots.Sort();
            List<string> moved = new List<string>(connected);
            moved.RemoveAt(k);
            moved.Insert(index, id);
            for (int j = 0; j < slots.Count; j++) order[slots[j]] = moved[j];
            return true;
        }

        public static void SetPadName(string id, string name)
        {
            lock (Lock) Config(id).Name = (name ?? "").Trim();
            Settings.Save();
        }

        // What games see this controller as: "Xbox" — virtual Xbox 360, else virtual DualShock 4. Switches the virtual pad live.
        public static void SetPadOutput(string id, string kind)
        {
            kind = kind == "Xbox" ? "Xbox" : "DS4";
            lock (Lock)
            {
                Config(id).Output = kind == "Xbox" ? "Xbox" : "";
                foreach (Controller d in Devices) if (d.Id == id) d.SwitchOutput(kind);
            }
            Settings.Save();
        }

        // A controller's own brightness multiplier (0.1 .. 1.0), on top of the global brightness — Pro
        public static void SetPadBrightness(string id, double value)
        {
            value = Math.Max(0.1, Math.Min(1.0, value));
            lock (Lock)
            {
                Config(id).Brightness = value;
                foreach (Controller d in Devices) if (d.Id == id) d.PadBrightness = value;
            }
            Settings.Save();
        }

        // A controller's own preset; null or "" — same as everyone
        public static void SetPadPreset(string id, string preset)
        {
            Preset p = string.IsNullOrEmpty(preset) ? null : Presets.Find(x => x.Name == preset);
            if (p != null && p.Locked) return;   // shown with a lock in the menus, never applied
            lock (Lock)
            {
                Config(id).Preset = preset ?? "";
                foreach (Controller d in Devices)
                    if (d.Id == id) { d.FadeFrom = d.Current; d.Fade = 0; d.ApiPreset = null; d.OwnPreset = string.IsNullOrEmpty(preset) ? null : preset; }
            }
            Settings.Save();
        }

        public static ControllerInfo[] Snapshot()
        {
            lock (Lock)
            {
                int n = Devices.Count; ControllerInfo[] r = new ControllerInfo[n];
                for (int k = 0; k < n; k++)
                {
                    Controller d = Devices[k];
                    PadConfig cfg;
                    Pads.TryGetValue(d.Id, out cfg);
                    r[k] = new ControllerInfo
                    {
                        Id = d.Id, Kind = d.Bluetooth ? "Bluetooth" : "USB", Model = d.Model, Name = cfg != null ? cfg.Name : "",
                        OwnPreset = d.OwnPreset, Output = d.OutputKind, Color = d.Out.ToColor(), Virtual = d.Virtual, Rate = d.ReportRate,
                        Battery = d.Battery, Charging = d.Charging, HoursLeft = d.HoursLeft, Brightness = d.PadBrightness,
                        Warning = d.NoVirtual ? Lang.T("hidden, but the virtual pad wasn't created — games don't see it")
                            : !GameModeActive || d.Hidden ? null
                            : Lang.T("games see it directly (couldn't hide it)")
                    };
                }
                return r;
            }
        }

        // In the same order as the tiles in the window
        internal static Controller[] Controllers()
        {
            lock (Lock) return Devices.ToArray();
        }

        // Display name: its own or “Controller N”
        public static string DisplayName(ControllerInfo c, int index)
        {
            return c != null && !string.IsNullOrEmpty(c.Name) ? c.Name : Lang.F("Controller {0}", index + 1);
        }

        // Called from the controller's read thread — the action itself runs in the light bar loop
        internal static void Hotkey(Controller c, HotAction a) { hotkeys.Enqueue(new KeyValuePair<Controller, HotAction>(c, a)); }

        // A few short color flashes (notification)
        internal static void Flash(Controller c, Rgb color, int blinks)
        {
            if (!NotifyFlash) return;
            c.NotifyColor = color; c.NotifyStart = Ms; c.NotifyUntil = Ms + blinks * 400;
        }

        public static void Flash(string id, Rgb color, int blinks)
        {
            foreach (Controller c in Controllers()) if (c.Id == id) Flash(c, color, blinks);
        }

        public static void ShowBattery(string id)
        {
            foreach (Controller c in Controllers()) if (c.Id == id) c.BatteryShowUntil = Ms + 3000;
        }

        public static void Disconnect(string id)
        {
            foreach (Controller c in Controllers())
                if (c.Id == id && c.Bluetooth) { ulong addr = c.BtAddress; ThreadPool.QueueUserWorkItem(_ => BtRadio.Disconnect(addr)); }
        }

        static void ReleaseAll()
        {
            Controller[] arr;
            lock (Lock) { arr = Devices.ToArray(); Devices.Clear(); }
            foreach (Controller d in arr) d.Dispose();
        }

        static void UpdateStatus()
        {
            if (!IsAdmin) status = Lang.T("Game mode unavailable: the app is running without administrator rights.");
            else if (Drivers.RebootPending) status = Lang.T("Game mode unavailable: drivers are still being removed — restart the computer.");
            else if (notLoaded || client == null && Drivers.AllInstalled)
                status = Lang.T("Game mode unavailable: restart the computer to finish installing the drivers.");
            else if (client == null) status = Lang.T("Game mode unavailable: the ViGEmBus driver was not found.");
            else if (!HidHide.Installed) status = Lang.T("Game mode unavailable: the HidHide driver was not found.");
            else status = Lang.F("Game mode: games see a virtual {0}, the real controller is hidden.", "DualShock 4");
            Log.Info("status: " + status);
        }

        static void ConnectClient()
        {
            clientTried = true;
            try { client = new ViGEmClient(); }
            catch (Exception e) { client = null; Log.Error("ViGEmClient", e); }
        }

        static void Loop()
        {
            if (IsAdmin && !clientTried) ConnectClient();
            UpdateStatus();

            Stopwatch sw = Stopwatch.StartNew();
            double last = 0, lastScan = -100, lastRate = 0;
            while (running)
            {
                double now = sw.Elapsed.TotalSeconds, dt = Math.Min(0.2, now - last); last = now;
                if (modeDirty)
                {
                    modeDirty = false;
                    notLoaded = false;
                    ReleaseAll();
                    if (IsAdmin && client == null && !clientTried) ConnectClient();
                    if (!GameModeActive && IsAdmin && HidHide.Installed) HidHide.Release();
                    UpdateStatus();
                    lastScan = -100;
                }
                if (rescanRequested || now - lastScan > 2)
                {
                    rescanRequested = false; lastScan = now;
                    try { DoRescan(); } catch (Exception e) { Log.Error("rescan", e); }
                }
                if (now - lastRate >= 1)
                {
                    lastRate = now;
                    lock (Lock) foreach (Controller d in Devices) d.TickRate();
                }
                T += dt * Speed;

                KeyValuePair<Controller, HotAction> hk;
                while (hotkeys.TryDequeue(out hk)) try { DoHotkey(hk.Key, hk.Value); } catch (Exception e) { Log.Error("hotkey", e); }

                Controller[] arr; lock (Lock) arr = Devices.ToArray();
                int n = arr.Length;
                List<Preset> ps = Presets;
                Preset global = ps[Math.Max(0, Math.Min(PresetIndex, ps.Count - 1))];
                long ms = sw.ElapsedMilliseconds, nowMs = Ms;
                for (int i = 0; i < n; i++)
                {
                    Controller d = arr[i];
                    if (d.Dead) { Remove(d); continue; }
                    Live.Cur = d;
                    Live.Pad = d.CopyLast(lastRaw) ? Pad.Parse(lastRaw) : new Pad { LX = 128, LY = 128, RX = 128, RY = 128 };
                    // most specific wins: this pad's API override, then its own preset, then a global API override, then the chosen preset
                    string own = d.OwnPreset;
                    Preset named = own != null ? ps.Find(x => x.Name == own) : null;
                    Preset pr = d.ApiPreset ?? named ?? apiGlobal ?? global;
                    if (pr.Locked) pr = ps.Find(x => !x.Locked) ?? pr;   // e.g. saved by Pro, then Pro was locked
                    Rgb c;
                    try { c = pr.Fn(T, i, n).Clamp(); } catch { c = new Rgb(0, 0, 0); }
                    d.Fade = Math.Min(1, d.Fade + dt / 0.4);
                    if (d.Fade < 1) c = Rgb.Lerp(d.FadeFrom, c, PresetLib.Smooth(d.Fade));
                    d.Current = c;
                    c = Overlays(d, c, nowMs, dt);
                    Rgb o = c.Mul(Brightness * d.Dim * d.PadBrightness).Clamp(); d.Out = o;
                    int r = (int)Math.Round(o.R), g = (int)Math.Round(o.G), b = (int)Math.Round(o.B);
                    long since = ms - d.LastSendMs;
                    bool changed = r != d.LR || g != d.LG || b != d.LB;
                    if ((changed && since >= (d.Bluetooth ? MinSendMsBt : MinSendMsUsb)) || since > KeepAliveMs)
                    {
                        if (d.Send(r, g, b)) { d.LastSendMs = ms; d.Fails = 0; }
                        else if (++d.Fails > 20) { Log.Info("controller stopped answering: " + d.Id); Remove(d); }
                    }
                }
                Live.Cur = null;
                Thread.Sleep(10);
            }
        }

        static readonly byte[] lastRaw = new byte[63];

        // On top of the preset: battery display (PS + ✕), button flashes, notifications; idle dimming and auto-off
        static Rgb Overlays(Controller d, Rgb c, long now, double dt)
        {
            if (now < d.BatteryShowUntil) c = PresetLib.BatteryColor(d.Battery, d.Charging, T);
            if (now < d.NotifyUntil && ((now - d.NotifyStart) / 200) % 2 == 0) c = d.NotifyColor;

            long idle = now - d.LastActiveMs;
            bool dim = IdleDimMin > 0 && idle > IdleDimMin * 60000L && now >= d.NotifyUntil;
            d.Dim = dim ? Math.Max(0, d.Dim - dt / 1.5) : Math.Min(1, d.Dim + dt / 0.15);
            if (d.Bluetooth && IdleOffMin > 0 && idle > IdleOffMin * 60000L && !d.IdleOff)
            {
                d.IdleOff = true;   // the controller will disappear — DoRescan removes it
                ulong addr = d.BtAddress;
                ThreadPool.QueueUserWorkItem(_ => BtRadio.Disconnect(addr));
            }
            return c;
        }

        // The next / previous preset that isn't locked (PS + ←/→ skips Pro presets without Pro)
        internal static int NextUnlocked(List<Preset> ps, int from, int step)
        {
            int k = from;
            for (int tries = 0; tries < ps.Count; tries++)
            {
                k = ((k + step) % ps.Count + ps.Count) % ps.Count;
                if (!ps[k].Locked) return k;
            }
            return from;
        }

        static void DoHotkey(Controller d, HotAction a)
        {
            if (a == HotAction.PrevPreset || a == HotAction.NextPreset)
            {
                int step = a == HotAction.NextPreset ? 1 : -1;
                List<Preset> ps = Presets;
                string own = d.OwnPreset;
                int cur = own != null ? ps.FindIndex(p => p.Name == own) : -1;
                int next = NextUnlocked(ps, cur >= 0 ? cur : PresetIndex, step);
                if (cur >= 0) SetPadPreset(d.Id, ps[next].Name);
                else { SetPreset(next); Settings.Save(); }
            }
            if (a == HotAction.Brighter || a == HotAction.Dimmer)
            {
                double v = Math.Round(Brightness * 10 + (a == HotAction.Brighter ? 1 : -1)) / 10;
                Brightness = Math.Max(0.1, Math.Min(1, v));
                Settings.Save();
            }
            if (a == HotAction.ShowBattery) d.BatteryShowUntil = Ms + 3000;
            if (a == HotAction.TouchMouse)
            {
                TouchMouse = !TouchMouse;
                d.NotifyColor = TouchMouse ? Rgb.Hex(0x00FF40) : Rgb.Hex(0xFF0000);   // confirmation — always, even with notifications off
                d.NotifyStart = Ms; d.NotifyUntil = Ms + 800;
                Settings.Save();
            }
            if (a == HotAction.TurnOff && d.Bluetooth)
            {
                ulong addr = d.BtAddress;
                ThreadPool.QueueUserWorkItem(_ => BtRadio.Disconnect(addr));
            }
        }

        static void Remove(Controller d)
        {
            lock (Lock) { Devices.Remove(d); SortDevices(); }
            d.Dispose();
        }

        static bool IsSupported(string p)
        {
            p = p.ToLowerInvariant();
            if (!p.Contains("vid_054c") && !p.Contains("vid&0002054c")) return false;
            foreach (string id in DualShock4Wire.Pids) if (p.Contains("pid_" + id) || p.Contains("pid&" + id)) return true;
            foreach (string id in Controller.DualSensePids) if (p.Contains("pid_" + id) || p.Contains("pid&" + id)) return true;
            return false;
        }

        public enum Dup { Replace, Skip }

        // The same controller connected twice (a Bluetooth-paired pad plugged in with a cable): USB wins.
        // Replace — the new connection replaces the active one; Skip — the new one waits.
        public static Dup Duplicate(bool activeBluetooth, bool newBluetooth) { return activeBluetooth && !newBluetooth ? Dup.Replace : Dup.Skip; }

        // Second connections of controllers already in use (device path → MAC). Not opened while the other one is there;
        // when it goes away (cable unplugged), the waiting one is opened on the next scan.
        static readonly Dictionary<string, string> standby = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        // Connected controllers the free version leaves alone (paths); shown on the tiles as "more controllers — in Pro"
        static readonly HashSet<string> overLimit = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        public static int OverLimit { get { return overLimit.Count; } }

        // Slow things (HidHide, device restart, pad connection) — outside the lock so the UI doesn't stall.
        static void DoRescan()
        {
            List<HidDeviceEntry> found = new List<HidDeviceEntry>();
            foreach (HidDeviceEntry e in Native.EnumerateHid())
                if (IsSupported(e.Path) && !Native.IsOnViGEmBus(e.DevInst)) found.Add(e);
            found.Sort((x, y) => string.Compare(x.Path, y.Path, StringComparison.OrdinalIgnoreCase));

            List<Controller> gone = new List<Controller>();
            HashSet<string> have = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            lock (Lock)
            {
                for (int k = Devices.Count - 1; k >= 0; k--)
                {
                    bool present = false;
                    foreach (HidDeviceEntry e in found) if (e.Path == Devices[k].Path) present = true;
                    if (!present) { gone.Add(Devices[k]); Devices.RemoveAt(k); }
                    else have.Add(Devices[k].Path);
                }
                if (gone.Count > 0) SortDevices();
            }
            foreach (Controller d in gone) { Log.Info("disconnected: " + d.Model + " " + d.Id); d.Dispose(); }

            // Forget waiting connections that disappeared or whose controller is no longer connected otherwise
            List<string> drop = new List<string>();
            foreach (KeyValuePair<string, string> kv in standby)
            {
                bool there = found.Exists(e => string.Equals(e.Path, kv.Key, StringComparison.OrdinalIgnoreCase));
                bool active;
                lock (Lock) active = Devices.Exists(d => d.Id == kv.Value);
                if (!there || !active) drop.Add(kv.Key);
            }
            foreach (string p in drop) standby.Remove(p);
            // Controllers over the free limit: forget the ones that disappeared, and all of them once there's room again
            int active0;
            lock (Lock) active0 = Devices.Count;
            if (active0 < Pro.MaxControllers) overLimit.Clear();
            else foreach (string p in new List<string>(overLimit)) if (!found.Exists(e => string.Equals(e.Path, p, StringComparison.OrdinalIgnoreCase))) overLimit.Remove(p);

            bool virt = GameModeActive;
            foreach (HidDeviceEntry e in found)
            {
                if (have.Contains(e.Path) || standby.ContainsKey(e.Path) || overLimit.Contains(e.Path)) continue;
                // Free version: one controller. A further one is left alone — not lit, not hidden from games — unless it's
                // the same controller over the other connection (USB + Bluetooth), which the usual duplicate rule handles.
                int activeNow;
                lock (Lock) activeNow = Devices.Count;
                if (activeNow >= Pro.MaxControllers)
                {
                    Controller probe = Controller.Open(e);
                    if (probe == null) continue;
                    Controller same;
                    lock (Lock) same = Devices.Find(d => d.Id == probe.Id);
                    bool replaces = same != null && Duplicate(same.Bluetooth, probe.Bluetooth) == Dup.Replace;
                    probe.Dispose();
                    if (!replaces)
                    {
                        if (same != null) standby[e.Path] = probe.Id;
                        else { overLimit.Add(e.Path); Log.Info("controller " + probe.Id + " left alone: the free version drives one controller"); }
                        continue;
                    }
                }
                bool hiddenOk = false;
                if (virt && e.InstanceId != null)
                {
                    int h = HidHide.Hide(e.InstanceId);
                    if (h == HidHide.NewlyHidden) { HidHide.RestartDevice(e.InstanceId); continue; }   // will reappear already hidden
                    hiddenOk = h == HidHide.AlreadyHidden;
                    // The device was restarted, but the HidHide filter didn't attach — games would see both the real controller
                    // and the virtual one (double input). No virtual pad until a reboot.
                    if (hiddenOk && !Drivers.Running("HidHide"))
                    {
                        hiddenOk = false;
                        if (!notLoaded) { notLoaded = true; Log.Info("HidHide is installed but not loaded"); UpdateStatus(); }
                    }
                }
                Controller c = Controller.Open(e);
                if (c == null) continue;
                Controller twin;
                lock (Lock) twin = Devices.Find(d => d.Id == c.Id);
                if (twin != null)
                {
                    if (Duplicate(twin.Bluetooth, c.Bluetooth) == Dup.Skip)
                    {
                        standby[e.Path] = c.Id;
                        Log.Info("second connection of " + c.Id + " (" + (c.Bluetooth ? "BT" : "USB") + ") waits — the other one is in use");
                        c.Dispose();
                        continue;
                    }
                    lock (Lock) { Devices.Remove(twin); SortDevices(); }
                    standby[twin.Path] = twin.Id;
                    Log.Info(twin.Id + ": USB connected — Bluetooth connection waits");
                    twin.Dispose();
                }
                c.BtPollMs = BtPollMs;
                lock (Lock) { PadConfig oc; if (Pads.TryGetValue(c.Id, out oc)) { if (oc.Output == "Xbox") c.OutputKind = "Xbox"; c.PadBrightness = oc.Brightness; } }
                try { c.Start(hiddenOk ? client : null); } catch (Exception ex) { Log.Error("start " + c.Id, ex); }
                c.Hidden = hiddenOk;
                c.NoVirtual = hiddenOk && !c.Virtual;
                Log.Info("connected: " + c.Model + " " + (c.Bluetooth ? "BT" : "USB") + " " + c.Id + (c.Virtual ? " (virtual pad)" : "") + (c.NoVirtual ? " (NO virtual pad)" : ""));
                lock (Lock)
                {
                    if (!Order.Contains(c.Id)) Order.Add(c.Id);
                    PadConfig cfg;
                    if (Pads.TryGetValue(c.Id, out cfg) && cfg.Preset != "") c.OwnPreset = cfg.Preset;
                    Devices.Add(c);
                    SortDevices();
                }
                c.Send(0, 0, 0);   // first output packet: a BT controller switches to full reports, a DualSense stops its blue animation
                Flash(c, Rgb.Hex(0xFFFFFF), 3);   // a freshly connected controller blinks white
            }
        }
    }
}
