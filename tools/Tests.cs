// Automated tests without a controller: presets for any number of controllers, custom presets, DualSense report parsing,
// translation completeness. Run: powershell -ExecutionPolicy Bypass -File tools\test.ps1
//   --keys <file> — write a JSON template with all translation keys (for a new language).
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;

namespace Glowstick
{
    static class Tests
    {
        static int failed, passed;

        static void Check(bool ok, string what)
        {
            if (ok) passed++;
            else { failed++; Console.WriteLine("FAIL: " + what); }
        }

        static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            AppDomain.CurrentDomain.AssemblyResolve += Program.ResolveEmbedded;   // the ViGEm library is embedded, as in the app
            string root = args.FirstOrDefault(a => !a.StartsWith("--")) ?? ".";
            int ki = Array.IndexOf(args, "--keys");
            if (ki >= 0)
            {
                // New language template: one key per line, empty values
                JavaScriptSerializer js = new JavaScriptSerializer();
                List<string> lines = AllKeys(root).Select(k => js.Serialize(k) + ": \"\"").ToList();
                File.WriteAllText(args[ki + 1], "{\n" + string.Join(",\n", lines) + "\n}\n", new UTF8Encoding(false));
                Console.WriteLine("Template: " + args[ki + 1] + " (" + lines.Count + " keys)");
                return 0;
            }
            Lang.Init("en");
            Presets();
            DualSense();
            Translations(root);
            BatteryLevels();
            PadLines();
            TileLayout();
            Swatches();
            CategoriesOrder();
            PadActionsOnMissingId();
            HsvRoundTrip();
            TrayTooltip();
            TestButtons();
            LifetimeGuard();
            DuplicateConnections();
            UpdateParsing();
            UpdateSwap();
            FirstRunWhen();
            DragOrder();
            Hotkeys();
            TouchMouse();
            Hardening();
            PadPieces();
            ProServerAnswers();
            Sta(UiTests);
            Console.WriteLine(string.Format("{0} passed, {1} failed", passed, failed));
            return failed == 0 ? 0 : 1;
        }

        static bool Bad(Rgb c) { return double.IsNaN(c.R) || double.IsNaN(c.G) || double.IsNaN(c.B) || double.IsInfinity(c.R + c.G + c.B); }

        // Every built-in preset: 1..8 controllers, different points in time — no exceptions and no NaN
        static void Presets()
        {
            HashSet<string> names = new HashSet<string>();
            foreach (Preset p in Engine.BuiltIn)
            {
                Check(names.Add(p.Name), "duplicate preset name: " + p.Name);
                bool ok = true;
                for (int n = 1; n <= 8; n++)
                    for (int i = 0; i < n; i++)
                        for (double t = 0; t < 30; t += 0.37)
                            try { if (Bad(p.Fn(t, i, n))) ok = false; } catch { ok = false; }
                Check(ok, "preset " + p.Name + " gives NaN or throws");
            }
        }



        // DualSense report (USB, without ID) → DS4 layout
        static void DualSense()
        {
            byte[] b = new byte[80], raw = new byte[63];
            int o = 1;
            b[0] = 0x01;
            b[o] = 10; b[o + 1] = 20; b[o + 2] = 30; b[o + 3] = 40;   // sticks
            b[o + 4] = 200; b[o + 5] = 100;                           // L2, R2
            b[o + 7] = 0x24;                                          // ✕ + d-pad "down" (4)
            b[o + 8] = 0x21;                                          // L1 + Options
            b[o + 9] = 0x03;                                          // PS + touchpad click
            b[o + 15] = 0x34; b[o + 16] = 0x12;                       // gyro X = 0x1234
            b[o + 32] = 0x05;                                         // finger 1: touching (bit 7 = 0), id 5
            b[o + 33] = 0x80; b[o + 34] = 0x07 | (0x8 << 4); b[o + 35] = 0x21;   // x = 0x780 (1920), y = 0x218 (536)
            b[o + 36] = 0x80;                                         // finger 2 — none
            b[o + 52] = 0x15;                                         // charging, level 5
            int counter = 0, pct; bool chg;
            DualSenseWire.ToDs4(b, o, raw, ref counter, out pct, out chg);
            Pad p = Pad.Parse(raw);
            Check(p.LX == 10 && p.LY == 20 && p.RX == 30 && p.RY == 40, "DS5 sticks");
            Check(p.L2 == 200 && p.R2 == 100, "DS5 triggers");
            Check((p.Buttons & Btn.Cross) != 0 && (p.Buttons & Btn.Down) != 0 && (p.Buttons & Btn.Up) == 0, "DS5 cross + d-pad");
            Check((p.Buttons & Btn.L1) != 0 && (p.Buttons & Btn.Options) != 0, "DS5 L1 + Options");
            Check((p.Buttons & Btn.PS) != 0 && (p.Buttons & Btn.Touch) != 0, "DS5 PS + touch click");
            Check(p.GyroX == 0x1234, "DS5 gyro");
            Check(p.Touch1 && p.T1Id == 5 && p.T1X == 1920 && p.T1Y == 536 * Pad.TouchH / 1080, "DS5 touch point 1 (y scaled)");
            Check(!p.Touch2, "DS5 touch point 2 absent");
            Check(chg && pct == 55, "DS5 battery 55% charging");
            Check((raw[29] & 0x10) != 0, "DS5 → DS4 cable bit");
        }


        // ---------- WPF UI ----------
        // WPF objects belong to the thread that created them: all WPF tests run on one STA thread
        static void Sta(Action a)
        {
            Exception err = null;
            System.Threading.Thread t = new System.Threading.Thread(() => { try { a(); } catch (Exception e) { err = e; } });
            t.SetApartmentState(System.Threading.ApartmentState.STA); t.Start(); t.Join();
            Check(err == null, "WPF tests threw: " + err);
        }

        static void UiTests()
        {
            UiTheme();
            TileFitsLongName();
            GridSurvivesShrinkingList();
            PresetCardFitsLongTitle();
            PadMenuMissingId();
            ModalOverlay();
        }

        static System.Windows.Media.Color Hex(string s) { return (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(s); }

        static void UiTheme()
        {
            Theme.Load();
            foreach (string k in new[] { "Bg", "Side", "Card", "CardBorder", "Accent", "SelCard", "Text", "Muted", "Warn", "BatOk", "BatLow", "BatCritical", "StatusOn", "StatusOff", "StatusBad" })
                Check(Theme.Dict.Contains(k), "theme brush: " + k);
            Check(((System.Windows.Media.SolidColorBrush)Theme.Brush("Accent")).Color == Hex("#60A5FA"), "accent color");
            Check(((System.Windows.Media.SolidColorBrush)Theme.Brush("Bg")).Color == Hex("#202024"), "window color");
            foreach (string k in new[] { "NavItem", "CardBox", "Chip", "PageTitle", "MutedText" })
                Check(Theme.Style(k) != null, "theme style: " + k);
        }


        static void BatteryLevels()
        {
            Check(UiFormat.Level(-1) == BatteryLevel.Unknown, "battery -1");
            Check(UiFormat.Level(0) == BatteryLevel.Critical && UiFormat.Level(24) == BatteryLevel.Critical, "battery <25");
            Check(UiFormat.Level(25) == BatteryLevel.Low && UiFormat.Level(49) == BatteryLevel.Low, "battery 25..49");
            Check(UiFormat.Level(50) == BatteryLevel.Ok && UiFormat.Level(100) == BatteryLevel.Ok, "battery >=50");
        }

        static void PadLines()
        {
            Check(UiFormat.PadLine(new ControllerInfo { Battery = 82, Kind = "USB" }) == "82% · USB", "pad line USB");
            Check(UiFormat.PadLine(new ControllerInfo { Battery = 82, Charging = true, Kind = "USB", HoursLeft = 2 }) == "82% ⚡ · USB", "pad line charging");
            Check(UiFormat.PadLine(new ControllerInfo { Battery = 45, Kind = "Bluetooth", HoursLeft = 3.25 }) == "45% · BT · ~3 h 15 min", "pad line hours");
            Check(UiFormat.PadLine(new ControllerInfo { Battery = -1, Kind = "Bluetooth" }) == "BT", "pad line unknown");
            Check(UiFormat.Hours(0) == "", "no hours");
            Check(UiFormat.Hours(0.004) == "" && UiFormat.Hours(0.008) == "", "under a minute → no estimate");
            Check(UiFormat.Hours(0.02) == "~1 min" && UiFormat.Hours(1.999) == "~2 h" && UiFormat.Hours(4.42) == "~4 h 25 min", "battery time with its units");
            Check(UiFormat.PadLine(new ControllerInfo { Battery = 1, Kind = "Bluetooth", HoursLeft = 0.004 }) == "1% · BT", "pad line without ~0:00");

            // Preview animation time: a speed change doesn't jump, a long pause moves at most 0.2 s
            AnimClock ac = new AnimClock();
            double a1 = ac.Advance(0.1, 1), a2 = ac.Advance(0.2, 1), a3 = ac.Advance(0.25, 4);
            Check(Math.Abs(a2 - 0.2) < 1e-9 && Math.Abs(a3 - 0.4) < 1e-9, "speed ×4 scales the next step, no jump");
            Check(Math.Abs(ac.Advance(60, 1) - 0.6) < 1e-9, "pause → +0.2 s at most");
        }

        static void TileLayout()
        {
            Check(UiFormat.TileColumns(1) == 4 && UiFormat.TileColumns(4) == 4, "tiles <=4 in one row");
            Check(UiFormat.TileColumns(5) == 4 && UiFormat.TileColumns(8) == 4 && UiFormat.TileColumns(10) == 5, "tiles >4 in two rows");
        }

        static void TileFitsLongName()
        {
            ControllerTile t = new ControllerTile();
            t.Update(new ControllerInfo { Id = "x", Name = new string('W', 60), Battery = 45, Kind = "Bluetooth", Warning = new string('W', 120), OwnPreset = "Fire" }, 0);
            t.Measure(new System.Windows.Size(190, double.PositiveInfinity));
            Check(t.DesiredSize.Width <= 190, "tile with a long name fits 190 DIP");
        }


        static void Swatches()
        {
            Rgb[] c = UiFormat.SwatchColors(Engine.BuiltIn.Find(p => p.Name == "Players: even rainbow").Fn, 1.0, 4);
            Check(c.Length == 4, "swatch: 4 stripes");
            Check(Math.Abs(c[0].R - c[2].R) + Math.Abs(c[0].G - c[2].G) + Math.Abs(c[0].B - c[2].B) > 1, "per-controller stripes differ");
            Rgb[] bad = UiFormat.SwatchColors((t, i, n) => { throw new Exception(); }, 0, 4);
            Check(bad.All(x => x.R == 0 && x.G == 0 && x.B == 0), "throwing preset → black");
            Rgb[] nan = UiFormat.SwatchColors((t, i, n) => new Rgb(double.NaN, 300, -5), 0, 2);
            Check(nan.All(x => !Bad(x) && x.G <= 255 && x.B >= 0), "NaN/out of range → clamped");
        }

        static void CategoriesOrder()
        {
            List<string> c = UiFormat.Categories(Engine.BuiltIn);
            Check(c[0] == Pro.MyPresets, "\"My presets\" first (locked without Pro)");
            Check(c.Distinct().Count() == c.Count, "categories unique");
            // The free version: static presets and exactly three usable animations, one controller; the Pro presets are
            // there only as recorded previews — all locked
            Check(Engine.BuiltIn.FindAll(p => p.Category == "Animations" && !p.Locked).Count == 3, "free build: three animations");
            Check(!Pro.Present && !Pro.Unlocked, "free build: no Pro module");
            Check(Pro.MaxControllers == 1, "free build: one controller");
            List<Preset> previews = Engine.BuiltIn.FindAll(p => p.Pro);
            Check(previews.Count >= 30 && previews.TrueForAll(p => p.Locked), "free build: Pro presets come as locked previews (" + previews.Count + ")");
            Check(c.Contains("Gradients") && c.Contains("Live (react to game & PC)"), "free build: Pro categories are shown");
            Check(Engine.BuiltIn.TrueForAll(p => p.Pro == p.Locked), "free build: exactly the Pro presets are locked");

            // A locked preset is never applied: not by a click / tray / menu (SetPreset), nor by PS + ←/→
            int locked = Engine.BuiltIn.FindIndex(p => p.Locked), before = Engine.PresetIndex;
            Engine.SetPreset(locked);
            Check(Engine.PresetIndex == before, "SetPreset ignores a locked preset");
            int last = Engine.BuiltIn.FindLastIndex(p => !p.Locked);
            int next = Engine.NextUnlocked(Engine.BuiltIn, last, 1);
            Check(!Engine.BuiltIn[next].Locked && next < last, "PS + → skips the locked presets (wraps to the first usable)");
            Rgb[] a = UiFormat.SwatchColors(previews[0].Fn, 0, 4), b = UiFormat.SwatchColors(previews[0].Fn, 0.05, 4);
            Check(a[0].R == b[0].R && a[0].G == b[0].G, "a preview plays recorded frames (10 per second)");
        }

        static void GridSurvivesShrinkingList()
        {
            PresetsPage page = new PresetsPage();
            Engine.PresetIndex = Engine.Presets.Count - 1;
            Engine.ReplacePresets(new List<Preset>(Engine.BuiltIn.Take(5)), null);
            page.Rebuild(); page.SyncSelection();
            Check(Engine.PresetIndex < Engine.Presets.Count, "index clamped after shrink");
            Engine.ReplacePresets(new List<Preset>(Engine.BuiltIn), null);
        }

        static void PresetCardFitsLongTitle()
        {
            PresetCard c = new PresetCard(0, new Preset { Name = new string('W', 80), Category = "x", Fn = Engine.BuiltIn[0].Fn, Custom = new object() });
            c.Measure(new System.Windows.Size(170, double.PositiveInfinity));
            Check(c.DesiredSize.Width <= 170, "preset card with a long title fits");
        }


        // Engine's per-controller setters save settings.json, so the tests only check the UI guard every action goes through
        static void PadActionsOnMissingId()
        {
            bool ran = false;
            Check(!PadMenu.IfPresent("nope", () => ran = true) && !ran, "action on a missing controller is skipped");
        }

        static void PadMenuMissingId() { Check(PadMenu.Build(null, "nope").Items.Count == 0, "menu for a missing controller is empty"); }


        // The color picker works in HSV: converting there and back (PresetLib.Hsv) must give the same color
        static void HsvRoundTrip()
        {
            foreach (int hex in new[] { 0xFF0000, 0x00FF00, 0x0000FF, 0xFFFFFF, 0x000000, 0x60A5FA, 0xFFB060, 0x808080 })
            {
                Rgb c = Rgb.Hex(hex); double h, s, v;
                ColorMath.ToHsv(c, out h, out s, out v);
                Check(h >= 0 && h < 1 && s >= 0 && s <= 1 && v >= 0 && v <= 1, "HSV in range " + hex.ToString("X6"));
                Rgb back = PresetLib.Hsv(h, s, v);
                Check(Math.Abs(back.R - c.R) < 1 && Math.Abs(back.G - c.G) < 1 && Math.Abs(back.B - c.B) < 1, "HSV round trip " + hex.ToString("X6"));
            }
        }


        static void TrayTooltip()
        {
            ControllerInfo[] one = { new ControllerInfo { Battery = 82, HoursLeft = 3.25 } };
            Check(TrayText.Tooltip(one) == "Glowstick · 1: 82% ~3 h 15 min", "tray text one controller");
            ControllerInfo[] many = Enumerable.Range(0, 8).Select(i => new ControllerInfo { Battery = 50 + i, Charging = i % 2 == 0, HoursLeft = 4 }).ToArray();
            Check(TrayText.Tooltip(many).Length <= 63, "tray text ≤ 63 chars with 8 controllers");
            Check(TrayText.Tooltip(new ControllerInfo[0]) == "Glowstick", "tray text no controllers");
            Check(TrayText.Tooltip(new[] { new ControllerInfo { Battery = -1 } }) == "Glowstick", "tray text battery unknown");
        }


        // Controller test page: which button labels light up for a report (DS4 layout, as the read thread gives it)
        static void TestButtons()
        {
            byte[] raw = new byte[63];
            raw[4] = 0x08;   // d-pad released, no face buttons
            Check(TestLayout.PressedButtons(raw).Length == 0, "neutral report → nothing pressed");
            raw[4] = 0x08 | 0x20;   // ✕
            Check(string.Join(",", TestLayout.PressedButtons(raw)) == "✕", "cross → ✕");
            raw[4] = 0x00;   // d-pad up
            Check(string.Join(",", TestLayout.PressedButtons(raw)) == "▲", "d-pad 0 → ▲");
            raw[4] = 0x08; raw[5] = 0x01; raw[6] = 0x01;   // L1 + PS
            Check(string.Join(",", TestLayout.PressedButtons(raw)) == "L1,PS", "L1 + PS");
        }


        // Shutdown cleanup runs exactly once, and not while the full removal is in progress
        static void LifetimeGuard()
        {
            Lifetime l = new Lifetime();
            Check(l.TryBegin() && !l.TryBegin(), "cleanup runs once");
            Lifetime r = new Lifetime { Removing = true };
            Check(!r.TryBegin(), "no exit while removing");
            r.Removing = false;
            Check(r.TryBegin(), "exit allowed after removing");
        }

        // The "removing…" overlay can't be dismissed with Esc, and closing a specific dialog leaves the others alone
        static void ModalOverlay()
        {
            MainWindow w = new MainWindow();
            System.Windows.Controls.TextBlock a = new System.Windows.Controls.TextBlock(), b = new System.Windows.Controls.TextBlock();
            w.ShowOverlay(a, null, true);
            Check(!w.OnEscape() && w.OverlayTop == a, "Esc doesn't close a modal overlay");
            w.ShowOverlay(b);
            w.CloseOverlay(a);
            Check(w.OverlayTop == b, "closing a specific dialog keeps the one on top");
            Check(w.OnEscape() && w.OverlayTop == null, "Esc closes a normal dialog");
        }


        // One controller over USB and Bluetooth at once: only the USB connection is used
        static void DuplicateConnections()
        {
            Check(Engine.Duplicate(true, false) == Engine.Dup.Replace, "BT active, USB appears → USB replaces it");
            Check(Engine.Duplicate(false, true) == Engine.Dup.Skip, "USB active, BT appears → BT waits");
            Check(Engine.Duplicate(false, false) == Engine.Dup.Skip && Engine.Duplicate(true, true) == Engine.Dup.Skip, "same kind → keep the first");
        }




        // ---------- updates ----------
        const string ReleaseJson = "{\"tag_name\":\"v1.3.0\",\"html_url\":\"https://github.com/o/r/releases/tag/v1.3.0\",\"assets\":[" +
            "{\"name\":\"Glowstick.exe\",\"browser_download_url\":\"https://github.com/o/r/releases/download/v1.3.0/Glowstick.exe\"}," +
            "{\"name\":\"SHA256.txt\",\"browser_download_url\":\"https://github.com/o/r/releases/download/v1.3.0/SHA256.txt\"}]}";

        static void UpdateParsing()
        {
            UpdateInfo u = Updater.Parse(ReleaseJson, new Version(1, 2, 1, 0));
            Check(u != null && u.Version == "1.3.0", "newer release found");
            Check(u != null && u.ExeUrl.EndsWith("/Glowstick.exe") && u.ShaUrl.EndsWith("/SHA256.txt") && u.PageUrl.EndsWith("/v1.3.0"), "release links");
            Check(Updater.Parse(ReleaseJson, new Version(1, 3, 0, 0)) == null, "same version → no update");
            Check(Updater.Parse("{\"tag_name\":\"v9.0.0\",\"html_url\":\"x\",\"assets\":[]}", new Version(1, 0)) == null, "no exe in the release → no update");
            Check(Updater.ExpectedHash("abc123  Glowstick.exe\r\n", "Glowstick.exe") == "abc123", "hash from SHA256.txt");
            Check(Updater.ExpectedHash("ABC123 *Glowstick.exe", "glowstick.exe") == "abc123", "hash: binary marker, case");
            Check(Updater.ExpectedHash("abc123  Other.exe", "Glowstick.exe") == null, "hash for another file → none");
        }

        // Swapping the running exe: old → .old, new → its place; any failure puts the old one back
        static void UpdateSwap()
        {
            string dir = Path.Combine(Path.GetTempPath(), "glowstick-test-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                string exe = Path.Combine(dir, "Glowstick.exe"), fresh = Path.Combine(dir, "new.exe");
                File.WriteAllText(exe, "old"); File.WriteAllText(fresh, "new"); File.WriteAllText(exe + ".old", "older");
                Check(Updater.Swap(exe, fresh) == null && File.ReadAllText(exe) == "new" && File.ReadAllText(exe + ".old") == "old", "swap: new in place, old kept as .old");
                Check(Updater.Rollback(exe) && File.ReadAllText(exe) == "old" && !File.Exists(exe + ".old"), "rollback: the old exe is back");
                File.WriteAllText(exe, "current");
                Check(Updater.Swap(exe, Path.Combine(dir, "missing.exe")) != null && File.ReadAllText(exe) == "current", "swap failure → the current exe stays");
            }
            finally { try { Directory.Delete(dir, true); } catch { } }
        }


        // ---------- first run ----------
        static void FirstRunWhen()
        {
            Check(FirstRun.Needed(false, false), "no settings yet, normal start → setup");
            Check(!FirstRun.Needed(false, true), "no settings, start in the tray (autostart) → no setup window");
            Check(!FirstRun.Needed(true, false), "settings exist → no setup");
            Check(FirstRun.Steps == 2, "two steps: language, autostart");
        }

        // ---------- drag and drop order ----------
        static void DragOrder()
        {
            // Order remembers X (not connected now) between B and C; connected: A, B, C, D
            List<string> order = new List<string> { "A", "B", "X", "C", "D" };
            List<string> conn = new List<string> { "A", "B", "C", "D" };
            Check(Engine.Reorder(order, conn, "D", 0) && string.Join(",", order) == "D,A,X,B,C", "last → first, X keeps its slot");
            order = new List<string> { "A", "B", "X", "C", "D" };
            Check(Engine.Reorder(order, conn, "A", 2) && string.Join(",", order) == "B,C,X,A,D", "first → third");
            order = new List<string> { "A", "B", "X", "C", "D" };
            Check(Engine.Reorder(order, conn, "B", 99) && string.Join(",", order) == "A,C,X,D,B", "index past the end → last");
            order = new List<string> { "A", "B" };
            Check(!Engine.Reorder(order, new List<string> { "A", "B" }, "A", 0) && string.Join(",", order) == "A,B", "same place → no change");
            Check(!Engine.Reorder(order, new List<string> { "A", "B" }, "Z", 1), "unknown id → no change");
        }

        // ---------- Pro server answers ----------
        static void ProServerAnswers()
        {
            string sha = new string('a', 64);
            Version cur = new Version(1, 0, 0, 0);
            UpdateInfo u = ProServer.Parse("{\"version\":\"1.2.0\",\"sha256\":\"" + sha.ToUpperInvariant() + "\"}", cur);
            Check(u != null && u.Version == "1.2.0" && u.Sha256 == sha && u.FromProServer && u.PageUrl == null, "newer Pro → update with its hash");
            Check(ProServer.Parse("{\"version\":\"1.0.0\",\"sha256\":\"" + sha + "\"}", cur) == null, "same version → nothing");
            Check(ProServer.Parse("{\"version\":\"1.0.0\",\"sha256\":\"" + sha + "\"}", null) != null, "free → Pro: any version is offered");
            Check(ProServer.Parse("{\"version\":\"1.2.0\",\"sha256\":\"short\"}", cur) == null, "a bad hash → nothing");
            Check(ProServer.Parse("{\"version\":\"x\",\"sha256\":\"" + sha + "\"}", cur) == null, "a bad version → nothing");
        }

        // ---------- PS hotkeys ----------
        // What Controller is made of: PS hotkeys, the output packets, the Xbox stick mapping
        static void PadPieces()
        {
            HotkeyMap.Reset();
            List<HotAction> fired = new List<HotAction>();
            PsHotkeys hk = new PsHotkeys(a => fired.Add(a));
            byte[] raw = new byte[63];
            Pad.WriteButtons(raw, Btn.PS);
            Check(hk.Update(Btn.PS, 0) && (hk.Mask(raw, 0)[6] & 0x01) == 0, "PS held: hidden from the game");
            Check(hk.Update(Btn.PS | Btn.Right, 50) && fired.Count == 1 && fired[0] == HotAction.NextPreset, "PS + → fires the next preset");
            byte[] masked = hk.Mask(raw, 50);
            Check((masked[4] & 0x0F) == 0x08, "… and the d-pad doesn't reach the game");
            hk.Update(Btn.None, 100);
            Check(!hk.Update(Btn.None, 120) || (hk.Mask(raw, 120)[6] & 0x01) == 0, "released after a combo: no PS tap for the game");

            PsHotkeys tap = new PsHotkeys(a => { });
            tap.Update(Btn.PS, 0);
            Check(tap.Update(Btn.None, 80) && (tap.Mask(new byte[63], 80)[6] & 0x01) != 0, "a short PS alone: the game gets a PS tap");
            Check(!tap.Update(Btn.None, 300), "… that ends");
            PsHotkeys hold = new PsHotkeys(a => { });
            hold.Update(Btn.PS, 0);
            Check(!hold.Update(Btn.PS, 500), "PS held 0.5 s without a combo goes to the game (Steam long press)");

            byte[] usb = new byte[32];
            int len = DualShock4Wire.Output(usb, false, 32, 0, true, 10, 20, 255, 128, 0);
            Check(len == 32 && usb[0] == 0x05 && usb[1] == 0x07 && usb[4] == 10 && usb[5] == 20 && usb[6] == 255 && usb[7] == 128 && usb[8] == 0, "DS4 USB packet: rumble + color");
            byte[] bt = new byte[78];
            DualShock4Wire.Output(bt, true, 78, 4, false, 0, 0, 1, 2, 3);
            uint crc = Crc32.Bt(bt, 74);
            Check(bt[0] == 0x11 && bt[1] == (0xC0 | 4) && bt[3] == 0x06 && bt[8] == 1 && bt[74] == (byte)crc && bt[77] == (byte)(crc >> 24), "DS4 BT packet: no rumble outside game mode, CRC at the end");
            byte[] ds = new byte[78];
            int seq = 0; bool released = false;
            DualSenseWire.Output(ds, true, 78, ref seq, ref released, true, 5, 6, null, new byte[11], true, 2, 9, 8, 7);
            Check(ds[0] == 0x31 && seq == 1 && released && ds[3] == (0x03 | 0x04) && ds[3 + 41] == 0x02 && ds[3 + 43] == 0x0A && ds[3 + 44] == 9, "DualSense BT packet: rumble, trigger, connect animation off, player 2, color");

            Check(VirtualPad.Axis(0, false) == -32768 && VirtualPad.Axis(255, false) == 32767 && VirtualPad.Axis(128, false) == 0, "Xbox stick: both full deflections reach the maximum");
            Check(VirtualPad.Axis(0, true) == 32767 && VirtualPad.Axis(255, true) == -32768 && VirtualPad.Axis(128, true) == 0, "Xbox stick: Y inverted, full both ways");
        }

        // Security / robustness pieces: the protected copy for the elevated tasks, unelevated launches, scan codes
        static void Hardening()
        {
            Version v12 = new Version(1, 2, 0), v13 = new Version(1, 3, 0);
            Check(Installer.ShouldCopy(v12, "a", null, null), "no protected copy yet → copy");
            Check(Installer.ShouldCopy(v13, "a", v12, "b"), "newer exe → replaces the protected copy");
            Check(!Installer.ShouldCopy(v12, "a", v13, "b"), "older exe never replaces a newer protected copy");
            Check(Installer.ShouldCopy(v12, "a", v12, "b") && !Installer.ShouldCopy(v12, "a", v12, "a"), "same version: copied only if it's a different build");
            Check(Installer.TaskMayRun(v12, "a", v13, "b") && Installer.TaskMayRun(v12, "a", v12, "a"), "the task may start an equal or newer protected copy");
            Check(!Installer.TaskMayRun(v13, "a", v12, "b") && !Installer.TaskMayRun(v12, "a", v12, "b") && !Installer.TaskMayRun(v12, "a", null, null),
                "an older / different / missing copy → UAC instead of the task");
            Check(Installer.Dir.StartsWith(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), StringComparison.OrdinalIgnoreCase), "the protected copy lives in Program Files");

            string file, args;
            Shell.Split("\"C:\\Program Files\\OBS\\obs64.exe\" --startrecording", out file, out args);
            Check(file == @"C:\Program Files\OBS\obs64.exe" && args == "--startrecording", "launch: quoted path + arguments");
            Shell.Split(@"notepad.exe C:\notes.txt", out file, out args);
            Check(file == "notepad.exe" && args == @"C:\notes.txt", "launch: program + argument");
            Shell.Split("https://example.com/a b", out file, out args);
            Check(file == "https://example.com/a b" && args == "", "launch: a web address stays whole");

            ushort sc; bool ext;
            InputOut.ScanOf(0x57, out sc, out ext);   // W
            Check(sc == 0x11 && !ext, "scan code: W = 0x11");
            InputOut.ScanOf(0x25, out sc, out ext);   // ←
            Check(sc == 0x4B && ext, "scan code: ← = E0 4B (extended)");
            InputOut.ScanOf(0x64, out sc, out ext);   // numpad 4
            Check(sc == 0x4B && !ext, "scan code: numpad 4 = 4B (not extended)");
        }

        // Touchpad as a mouse: taps, drag, scroll direction, absolute mode, glide, settings round trip
        static void TouchMouse()
        {
            List<int[]> ev = new List<int[]>();
            Touchpad t = new Touchpad();
            t.Output = (dx, dy, data, flags) => ev.Add(new[] { dx, dy, data, flags });
            TouchConfig c = new TouchConfig { TapClick = true, TapDrag = true };
            Func<bool, int, int, Pad> one = (down, x, y) => new Pad { Touch1 = down, T1X = x, T1Y = y, T1Id = 1 };
            Func<int, bool> has = f => ev.Exists(e => (e[3] & f) == f);

            long now = 1000;
            P(t, one(true, 500, 400), c, now); P(t, one(false, 500, 400), c, now += 80);
            Check(has(Touchpad.LEFTDOWN) && has(Touchpad.LEFTUP), "tap → left click");
            ev.Clear();
            P(t, one(true, 500, 400), c, now += 600); P(t, one(false, 500, 400), c, now += 500);
            Check(!has(Touchpad.LEFTDOWN), "a long touch is not a tap");

            ev.Clear();   // tap, then touch again quickly and slide → drag
            P(t, one(true, 500, 400), c, now += 1000); P(t, one(false, 500, 400), c, now += 60);
            ev.Clear();
            P(t, one(true, 500, 400), c, now += 100);
            Check(has(Touchpad.LEFTDOWN) && !has(Touchpad.LEFTUP), "double tap → button held for a drag");
            for (int k = 1; k <= 10; k++) P(t, one(true, 500 + k * 20, 400), c, now += 8);
            Check(ev.Exists(e => e[3] == Touchpad.MOVE && e[0] > 0), "drag moves the cursor");
            P(t, one(false, 700, 400), c, now += 8);
            Check(has(Touchpad.LEFTUP), "lifting ends the drag");

            ev.Clear();   // two fingers up → wheel; natural scrolling flips it
            Func<int, Pad> two = y => new Pad { Touch1 = true, Touch2 = true, T1X = 400, T2X = 600, T1Y = y, T2Y = y, T1Id = 2, T2Id = 3 };
            TouchConfig sc = new TouchConfig();
            P(t, two(600), sc, now += 8); P(t, two(500), sc, now += 8);
            Check(ev.Exists(e => e[3] == Touchpad.WHEEL && e[2] > 0), "fingers up → wheel up");
            ev.Clear(); t.Reset();
            sc.NaturalScroll = true;
            P(t, two(600), sc, now += 100); P(t, two(500), sc, now += 8);
            Check(ev.Exists(e => e[3] == Touchpad.WHEEL && e[2] < 0), "natural scrolling → wheel down");

            ev.Clear(); t.Reset();
            TouchConfig ab = new TouchConfig { Absolute = true };
            P(t, one(true, Pad.TouchW - 1, 0), ab, now += 100);
            Check(ev.Exists(e => (e[3] & Touchpad.ABSOLUTE) != 0 && e[0] == 65535 && e[1] == 0), "absolute: right edge → right of the screen");

            ev.Clear(); t.Reset();   // a flick with trackball on keeps moving after the finger lifts
            TouchConfig tb = new TouchConfig { Trackball = true, Friction = 0.2 };
            P(t, one(true, 100, 400), tb, now += 100);
            for (int k = 1; k <= 8; k++) P(t, one(true, 100 + k * 60, 400), tb, now += 4);
            P(t, one(false, 580, 400), tb, now += 4);
            int before = ev.Count;
            for (int k = 0; k < 10; k++) P(t, one(false, 580, 400), tb, now += 4);
            Check(ev.Count > before, "trackball: glides after lifting");

            // Smoothness: a slow, steady finger moves the cursor steadily (no stick-and-jump); a still finger's wobble doesn't move it
            ev.Clear(); t.Reset();
            TouchConfig smooth = new TouchConfig();
            int[] steps = new int[60];
            for (int k = 0; k < 60; k++) { int before2 = ev.Count; P(t, one(true, 300 + k * 2, 400), smooth, now += 4); steps[k] = ev.Skip(before2).Where(e => e[3] == Touchpad.MOVE).Sum(e => e[0]); }
            int moving = steps.Skip(20).Count(s => s > 0), jump = steps.Skip(20).Max();
            Check(moving >= 30 && jump <= 3, "slow finger: the cursor moves steadily (" + moving + "/40 reports, biggest step " + jump + ")");
            ev.Clear(); t.Reset();
            for (int k = 0; k < 80; k++) P(t, one(true, 500 + (k % 2), 400 + (k % 3 == 0 ? 1 : 0)), smooth, now += 4);
            Check(Math.Abs(ev.Where(e => e[3] == Touchpad.MOVE).Sum(e => e[0])) <= 2, "a resting finger's 1-unit wobble barely moves the cursor");
            // DS4 over Bluetooth: a report every ~1.8 ms, a new touch point only every 4th — the cursor still moves on most
            // reports in even steps, not one jump per point
            ev.Clear(); t.Reset();
            double tn = now + 100;
            int[] per = new int[200];
            for (int k = 0; k < 200; k++) { int b3 = ev.Count; P(t, one(true, 300 + (k / 4) * 6, 400), smooth, tn += 1.8); per[k] = ev.Skip(b3).Where(e => e[3] == Touchpad.MOVE).Sum(e => e[0]); }
            int moved = per.Skip(40).Count(s => s > 0), top = per.Skip(40).Max(), total = per.Skip(40).Sum();
            Check(moved >= 120 && top <= total / 160.0 * 2 + 1,"sensor slower than reports: the cursor moves evenly (" + moved + "/160 reports, biggest step " + top + ", total " + total + ")");
            Check(Touchpad.Gain(0, 2) < Touchpad.Gain(0.5, 2) && Touchpad.Gain(0.5, 2) < Touchpad.Gain(2, 2) && Math.Abs(Touchpad.Gain(10, 2) - 2) < 1e-9, "acceleration rises smoothly to the set maximum");
            Check(Touchpad.Gain(0, 3) == Touchpad.Gain(0, 1.5) && Touchpad.Gain(1, 1.5) < Touchpad.Gain(1, 3), "the acceleration strength changes fast movements, not slow ones");

            TouchConfig r = TouchConfig.Load(new TouchConfig { Sens = 2.5, Trackball = true, InvertY = true, Friction = 0.3, AccelMax = 2.5 }.Save());
            Check(r.Sens == 2.5 && r.Trackball && r.InvertY && r.Friction == 0.3 && r.AccelMax == 2.5, "touch settings round trip");
            Check(TouchConfig.Load(new Dictionary<string, object> { { "AccelMax", 0.2 } }).AccelMax == 1, "acceleration strength clamped");
            Check(TouchConfig.Load(new Dictionary<string, object> { { "Sens", 99.0 } }).Sens == 4, "touch sensitivity clamped");
        }

        static void P(Touchpad t, Pad p, TouchConfig c, double now) { t.Process(ref p, c, now); }

        static void Hotkeys()
        {
            HotkeyMap.Reset();
            Check(HotkeyMap.Get(HotAction.NextPreset) == Btn.Right && (HotkeyMap.All & Btn.Options) != 0, "defaults: → next preset, Options in use");
            HotkeyMap.Set(HotAction.ShowBattery, Btn.Triangle);
            Check(HotkeyMap.Get(HotAction.ShowBattery) == Btn.Triangle && (HotkeyMap.All & Btn.Cross) == 0, "✕ freed, △ shows the battery");
            HotkeyMap.Set(HotAction.TurnOff, Btn.Right);
            Check(HotkeyMap.Get(HotAction.TurnOff) == Btn.Right && HotkeyMap.Get(HotAction.NextPreset) == Btn.None, "a button moves: → now turns off, next preset is off");
            // Fired: the combo just becoming fully held fires; already-held doesn't
            List<HotAction> acts = HotkeyMap.Fired(Btn.PS | Btn.Right | Btn.Triangle, Btn.PS);
            Check(acts.Count == 2 && acts.Contains(HotAction.TurnOff) && acts.Contains(HotAction.ShowBattery), "newly held buttons → their actions");
            Check(HotkeyMap.Fired(Btn.PS | Btn.Right, Btn.PS | Btn.Right).Count == 0, "already-held combo doesn't fire again");
            HotkeyMap.Set(HotAction.Dimmer, Btn.PS);
            Check(HotkeyMap.Get(HotAction.Dimmer) == Btn.Down, "PS itself can't be part of a combo");

            // Combos: L1 + R1 for one action, lone L1 for another — the superset wins when both are held
            HotkeyMap.Reset();
            HotkeyMap.Set(HotAction.Brighter, Btn.L1 | Btn.R1);
            HotkeyMap.Set(HotAction.Dimmer, Btn.L1);
            List<HotAction> both = HotkeyMap.Fired(Btn.L1 | Btn.R1, Btn.None);
            Check(both.Count == 1 && both.Contains(HotAction.Brighter), "L1 + R1 held together → only the combo, not lone L1");
            Check(HotkeyMap.ComboName(Btn.L1 | Btn.R1) == "L1 + R1", "combo name");

            Dictionary<string, object> saved = HotkeyMap.Save();
            HotkeyMap.Reset();
            HotkeyMap.Load(saved);
            Check(HotkeyMap.Get(HotAction.Brighter) == (Btn.L1 | Btn.R1), "settings round trip keeps the combo");
            HotkeyMap.Load(new Dictionary<string, object> { { "PrevPreset", "L1" }, { "NextPreset", "L1" }, { "Brighter", "Nonsense" } });
            Check(HotkeyMap.Get(HotAction.PrevPreset) == Btn.L1 && HotkeyMap.Get(HotAction.NextPreset) == Btn.None, "a combo twice in the file — the later action loses it");
            HotkeyMap.Reset();
        }


        // ---------- translations ----------
        static readonly Regex LiteralKey = new Regex(@"Lang\.[TF]\(""((?:[^""\\]|\\.)*)""");

        // All keys: Lang.T/Lang.F literals in src, preset names and categories, effect names and hints
        static SortedSet<string> AllKeys(string root)
        {
            SortedSet<string> keys = new SortedSet<string>(StringComparer.Ordinal);
            foreach (string file in Directory.GetFiles(Path.Combine(root, "src"), "*.cs", SearchOption.AllDirectories))
                foreach (Match m in LiteralKey.Matches(File.ReadAllText(file, Encoding.UTF8)))
                    keys.Add(Regex.Unescape(m.Groups[1].Value));
            foreach (Preset p in Engine.BuiltIn) { keys.Add(p.Name); keys.Add(p.Category); }
            return keys;
        }

        static int Placeholders(string s) { return Regex.Matches(s, @"\{\d+(:[^}]*)?\}").Cast<Match>().Select(m => m.Value).Distinct().Count(); }

        static void Translations(string root)
        {
            SortedSet<string> keys = AllKeys(root);
            Check(keys.Count > 100, "found translation keys: " + keys.Count);
            foreach (string[] l in Lang.Languages)
            {
                if (l[0] == "en") continue;
                string file = Path.Combine(Path.Combine(root, "lang"), l[0] + ".json");
                Check(File.Exists(file), "lang file exists: " + l[0]);
                if (!File.Exists(file)) continue;
                Dictionary<string, object> d = null;
                try { d = new JavaScriptSerializer { MaxJsonLength = int.MaxValue }.Deserialize<Dictionary<string, object>>(File.ReadAllText(file, Encoding.UTF8)); }
                catch (Exception e) { Check(false, l[0] + ".json is valid JSON: " + e.Message); continue; }
                List<string> missing = keys.Where(k => !d.ContainsKey(k) || string.IsNullOrEmpty(d[k] as string)).ToList();
                List<string> stale = d.Keys.Where(k => !keys.Contains(k)).ToList();
                foreach (string k in missing) Console.WriteLine("  " + l[0] + " missing: " + k.Replace("\n", "\\n"));
                foreach (string k in stale) Console.WriteLine("  " + l[0] + " unused: " + k.Replace("\n", "\\n"));
                Check(missing.Count == 0, l[0] + ": " + missing.Count + " missing translations");
                Check(stale.Count == 0, l[0] + ": " + stale.Count + " unused keys");
                foreach (KeyValuePair<string, object> kv in d)
                    if (kv.Value is string && keys.Contains(kv.Key))
                        Check(Placeholders(kv.Key) == Placeholders((string)kv.Value), l[0] + ": placeholders differ in “" + kv.Key + "”");
                Lang.Init(l[0]);
                Check(Lang.Code == l[0], "language loads from resources: " + l[0]);
            }
            Lang.Init("uk");
            Check(Lang.FromUk("Червоний + Синій") == "Red + Blue", "old Ukrainian preset names migrate");
            Lang.Init("en");
        }
    }
}
