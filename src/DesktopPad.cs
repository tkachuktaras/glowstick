// Desktop mode: with no game in front (nothing fullscreen, no game profile running) the controller drives the PC —
// left stick moves the cursor, right stick scrolls, ✕ Enter, ○ Esc, □ Backspace, △ Space, the d-pad arrow keys, R2 / L2 left /
// right click, L1 / R1 switch windows (Alt + Tab, held while they're pressed in a row), Options the Start menu. The game
// sees a controller at rest meanwhile. Turned on in Settings or with PS + Share (Engine.DesktopMode). One per controller,
// run on its read thread; everything it holds is let go when it stops.
using System;
using System.Collections.Generic;

namespace Glowstick
{
    internal sealed class DesktopPad
    {
        public const double StickDead = 0.15, CursorPxPerMs = 1.6, ScrollPerMs = 0.84;   // full deflection: 1600 px/s, ~7 wheel notches (120 each) a second
        public const long AltHoldMs = 900;   // Alt stays down this long after the last L1 / R1, so the next one moves on in the switcher
        const int VK_RETURN = 0x0D, VK_ESCAPE = 0x1B, VK_BACK = 0x08, VK_SPACE = 0x20, VK_LEFT = 0x25, VK_UP = 0x26, VK_RIGHT = 0x27, VK_DOWN = 0x28,
            VK_LWIN = 0x5B, VK_MENU = 0x12, VK_TAB = 0x09, VK_SHIFT = 0x10;

        // Where the events go — InputOut; tests swap them
        internal Action<int, bool> Key = InputOut.Key;
        internal Action<int, int, int, int> Mouse = InputOut.Mouse;

        static readonly KeyValuePair<Btn, int>[] Keys =
        {
            new KeyValuePair<Btn, int>(Btn.Cross, VK_RETURN), new KeyValuePair<Btn, int>(Btn.Circle, VK_ESCAPE),
            new KeyValuePair<Btn, int>(Btn.Square, VK_BACK), new KeyValuePair<Btn, int>(Btn.Triangle, VK_SPACE),
            new KeyValuePair<Btn, int>(Btn.Up, VK_UP), new KeyValuePair<Btn, int>(Btn.Down, VK_DOWN),
            new KeyValuePair<Btn, int>(Btn.Left, VK_LEFT), new KeyValuePair<Btn, int>(Btn.Right, VK_RIGHT),
            new KeyValuePair<Btn, int>(Btn.Options, VK_LWIN),
        };

        readonly HashSet<int> held = new HashSet<int>();
        Btn prev;
        bool left, right, alt;
        long altUntil;
        double ax, ay, wheel, hwheel, last = -1;

        public bool Busy { get { return held.Count > 0 || left || right || alt; } }

        // One report. buttons — with PS held nothing is taken (those are hotkeys)
        public void Process(Pad p, long now)
        {
            double dt = last < 0 ? 4 : Math.Max(1, Math.Min(50, now - last));
            last = now;
            Btn b = (p.Buttons & Btn.PS) != 0 ? Btn.None : p.Buttons;

            // Sticks: cursor and scroll, a curve so small deflections are precise
            double cx = Curve(p.LX), cy = Curve(p.LY);
            ax += cx * CursorPxPerMs * dt; ay += cy * CursorPxPerMs * dt;
            int mx = (int)ax, my = (int)ay;
            if (mx != 0 || my != 0) { ax -= mx; ay -= my; Mouse(mx, my, 0, InputOut.MOVE); }
            wheel += -Curve(p.RY) * ScrollPerMs * dt; hwheel += Curve(p.RX) * ScrollPerMs * dt;
            int w = (int)wheel, h = (int)hwheel;
            if (w != 0) { wheel -= w; Mouse(0, 0, w, InputOut.WHEEL); }
            if (h != 0) { hwheel -= h; Mouse(0, 0, h, InputOut.HWHEEL); }

            // Clicks on the triggers
            bool l = p.L2 > 128, r = p.R2 > 128;
            if (r != left) { left = r; Mouse(0, 0, 0, r ? InputOut.LEFTDOWN : InputOut.LEFTUP); }
            if (l != right) { right = l; Mouse(0, 0, 0, l ? InputOut.RIGHTDOWN : InputOut.RIGHTUP); }

            // Window switching: Alt held, Tab (or Shift + Tab) for each L1 / R1; ✕ while it's open picks the window
            Btn pressed = b & ~prev;
            if ((pressed & (Btn.L1 | Btn.R1)) != 0)
            {
                if (!alt) { alt = true; Key(VK_MENU, true); }
                bool back = (pressed & Btn.L1) != 0;
                if (back) Key(VK_SHIFT, true);
                Key(VK_TAB, true); Key(VK_TAB, false);
                if (back) Key(VK_SHIFT, false);
                altUntil = now + AltHoldMs;
            }
            bool pick = alt && (pressed & Btn.Cross) != 0;
            if (alt && (pick || now >= altUntil && (b & (Btn.L1 | Btn.R1)) == 0)) { alt = false; Key(VK_MENU, false); }

            // The rest are keys held while their button is
            foreach (KeyValuePair<Btn, int> k in Keys)
            {
                bool down = (b & k.Key) != 0 && !(pick && k.Key == Btn.Cross);
                if (down && !held.Contains(k.Value) && (pressed & k.Key) != 0) { held.Add(k.Value); Key(k.Value, true); }
                else if (!down && held.Remove(k.Value)) Key(k.Value, false);
            }
            prev = b;
        }

        // Stopped (turned off, a game came up, the controller went away): nothing stays pressed
        public void Release()
        {
            foreach (int k in held) Key(k, false);
            held.Clear();
            if (alt) { alt = false; Key(VK_MENU, false); }
            if (left) { left = false; Mouse(0, 0, 0, InputOut.LEFTUP); }
            if (right) { right = false; Mouse(0, 0, 0, InputOut.RIGHTUP); }
            prev = Btn.None; ax = ay = wheel = hwheel = 0; last = -1;
        }

        // A stick byte → −1 .. 1 past the dead zone, squared for precision near the center
        internal static double Curve(byte v)
        {
            double x = (v - 127.5) / 127.5, a = Math.Abs(x);
            if (a <= StickDead) return 0;
            double t = Math.Min(1, (a - StickDead) / (1 - StickDead));
            return Math.Sign(x) * t * t;
        }

        // What a game sees while desktop mode has the controller: at rest (sticks centered, nothing pressed, triggers up)
        public static byte[] AtRest(byte[] raw, byte[] buf)
        {
            Buffer.BlockCopy(raw, 0, buf, 0, 63);
            buf[0] = buf[1] = buf[2] = buf[3] = 128;
            buf[7] = buf[8] = 0;
            Pad.WriteButtons(buf, Btn.None);
            return buf;
        }
    }
}
