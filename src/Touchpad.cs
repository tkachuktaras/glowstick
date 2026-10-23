// Controller touchpad as a mouse: one finger — cursor, two fingers — scroll, touchpad click — left button (with two fingers — right).
// Optional (TouchConfig): tap to click, double-tap drag, trackball glide, jitter compensation, absolute mode, inverted axes.
// Called from the controller's read thread after the report has been forwarded to the virtual pad.
using System;
using System.Collections.Generic;

namespace Glowstick
{
    // How the touchpad drives the mouse. The global one is Engine.Touch; a Pro input profile can bring its own.
    public class TouchConfig
    {
        public bool Absolute;              // the pad maps to the whole screen instead of moving the cursor relatively
        public double Sens = 1.0;          // 0.2 .. 4
        public bool Accel = true;          // slow — precise, fast — far
        public double AccelMax = 1.5;      // 1 .. 4 — the multiplier for the fastest movements
        public bool Jitter = true;         // smooth out the sensor's 1-unit wobble
        public bool TapClick;              // a short tap = left click (two-finger tap = right click)
        public bool TapDrag;               // tap, then touch again and move = drag with the left button held
        public bool Scroll = true;         // two fingers scroll (vertically and horizontally)
        public double ScrollSens = 1.0;    // 0.2 .. 4
        public bool NaturalScroll;         // content follows the fingers
        public bool Trackball;             // the cursor keeps gliding after a flick
        public double Friction = 0.5;      // 0 .. 1 — how quickly the glide stops
        public bool InvertX, InvertY;
        public bool RightHalfClick;        // clicking the right half of the pad = right button
        public bool Unaccelerated = true;  // move the cursor by exact pixels: Windows' pointer speed / "Enhance pointer precision" left out
        public bool EdgeSwipe;             // swipe in from the left / right edge — back / forward; down from the top — desktop; up from the bottom — task view
        public bool EdgeScroll;            // a finger sliding along the right edge scrolls
        public bool MediaSwipe;            // (Pro) a quick two-finger swipe left / right — previous / next track
        public bool HapticClick = true;    // a faint rumble on a tap-click and a gesture, like a real touchpad

        public TouchConfig Clone() { return (TouchConfig)MemberwiseClone(); }

        public Dictionary<string, object> Save()
        {
            return new Dictionary<string, object>
            {
                { "Absolute", Absolute }, { "Sens", Sens }, { "Accel", Accel }, { "AccelMax", AccelMax }, { "Jitter", Jitter }, { "TapClick", TapClick }, { "TapDrag", TapDrag },
                { "Scroll", Scroll }, { "ScrollSens", ScrollSens }, { "NaturalScroll", NaturalScroll }, { "Trackball", Trackball }, { "Friction", Friction },
                { "InvertX", InvertX }, { "InvertY", InvertY }, { "RightHalfClick", RightHalfClick }, { "Unaccelerated", Unaccelerated },
                { "EdgeSwipe", EdgeSwipe }, { "EdgeScroll", EdgeScroll }, { "MediaSwipe", MediaSwipe }, { "HapticClick", HapticClick }
            };
        }

        public static TouchConfig Load(object o)
        {
            TouchConfig c = new TouchConfig();
            Dictionary<string, object> d = o as Dictionary<string, object>;
            if (d == null) return c;
            object v;
            try
            {
                if (d.TryGetValue("Absolute", out v)) c.Absolute = Convert.ToBoolean(v);
                if (d.TryGetValue("Sens", out v)) c.Sens = Clamp(Convert.ToDouble(v), 0.2, 4);
                if (d.TryGetValue("Accel", out v)) c.Accel = Convert.ToBoolean(v);
                if (d.TryGetValue("AccelMax", out v)) c.AccelMax = Clamp(Convert.ToDouble(v), 1, 4);
                if (d.TryGetValue("Jitter", out v)) c.Jitter = Convert.ToBoolean(v);
                if (d.TryGetValue("TapClick", out v)) c.TapClick = Convert.ToBoolean(v);
                if (d.TryGetValue("TapDrag", out v)) c.TapDrag = Convert.ToBoolean(v);
                if (d.TryGetValue("Scroll", out v)) c.Scroll = Convert.ToBoolean(v);
                if (d.TryGetValue("ScrollSens", out v)) c.ScrollSens = Clamp(Convert.ToDouble(v), 0.2, 4);
                if (d.TryGetValue("NaturalScroll", out v)) c.NaturalScroll = Convert.ToBoolean(v);
                if (d.TryGetValue("Trackball", out v)) c.Trackball = Convert.ToBoolean(v);
                if (d.TryGetValue("Friction", out v)) c.Friction = Clamp(Convert.ToDouble(v), 0, 1);
                if (d.TryGetValue("InvertX", out v)) c.InvertX = Convert.ToBoolean(v);
                if (d.TryGetValue("InvertY", out v)) c.InvertY = Convert.ToBoolean(v);
                if (d.TryGetValue("RightHalfClick", out v)) c.RightHalfClick = Convert.ToBoolean(v);
                if (d.TryGetValue("Unaccelerated", out v)) c.Unaccelerated = Convert.ToBoolean(v);
                if (d.TryGetValue("EdgeSwipe", out v)) c.EdgeSwipe = Convert.ToBoolean(v);
                if (d.TryGetValue("EdgeScroll", out v)) c.EdgeScroll = Convert.ToBoolean(v);
                if (d.TryGetValue("MediaSwipe", out v)) c.MediaSwipe = Convert.ToBoolean(v);
                if (d.TryGetValue("HapticClick", out v)) c.HapticClick = Convert.ToBoolean(v);
            }
            catch { }
            return c;
        }

        static double Clamp(double v, double lo, double hi) { return Math.Max(lo, Math.Min(hi, v)); }
    }

    internal class Touchpad
    {
        internal const int MOVE = InputOut.MOVE, LEFTDOWN = InputOut.LEFTDOWN, LEFTUP = InputOut.LEFTUP, RIGHTDOWN = InputOut.RIGHTDOWN, RIGHTUP = InputOut.RIGHTUP,
            WHEEL = InputOut.WHEEL, HWHEEL = InputOut.HWHEEL, ABSOLUTE = InputOut.ABSOLUTE, VIRTUALDESK = InputOut.VIRTUALDESK;
        const int TapMs = 200, TapMove = 60, DragWindowMs = 250;
        // The sensor updates slower than the controller reports (DS4 over Bluetooth: a new point every ~4th report, ~7 ms);
        // a finger that hasn't moved for StillMs counts as a new, unmoved point. A point's movement is spread over at most MaxSpreadMs.
        const double StillMs = 20, MaxSpreadMs = 12;

        // Where the mouse events go — InputOut (SendInput); tests swap it for a recorder (dx, dy, data, flags)
        internal Action<int, int, int, int> Output = InputOut.Mouse;
        // Where the cursor lands for an unaccelerated move (absolute coordinates) — InputOut.CursorTarget; tests swap it
        internal Func<int, int, int[]> Target = InputOut.CursorTarget;
        internal Action Haptic;   // a faint rumble pulse (the controller sets it)
        internal readonly TouchGestures Gestures = new TouchGestures();
        bool unaccelerated;   // TouchConfig.Unaccelerated of the report being processed
        bool resync;          // a gesture held the cursor: the next move starts from where the finger is, no jump

        bool had1, had2, clickDown, clickRight, dragDown;
        byte id1;
        int x1, y1, scrollX, scrollY;
        double sx, sy;                     // the filtered finger position
        readonly OneEuro fx = new OneEuro(), fy = new OneEuro();
        double accX, accY, accWheel, accHWheel;
        // the movement of the last sensor point not yet sent to the mouse, and over how many ms the rest goes out
        double pendX, pendY, spread, lastSample;
        double speed;                      // finger speed for the acceleration, smoothed over ~25 ms
        // taps
        double touchStart, lastTapEnd = -100000, lastMs;
        bool started;
        int tapTravel, maxFingers;
        bool tapCandidate;
        // trackball glide (mouse px per ms)
        double velX, velY;
        bool gliding;

        // now — ms with a fraction (reports come every ~2 ms, whole ms would make every speed jitter)
        public void Process(ref Pad p, TouchConfig c, double now)
        {
            double dt = started ? Math.Max(0.1, Math.Min(50, now - lastMs)) : 4;
            unaccelerated = c.Unaccelerated;
            lastMs = now; started = true;
            int fingers = (p.Touch1 ? 1 : 0) + (p.Touch2 ? 1 : 0);

            // Physical click
            bool click = (p.Buttons & Btn.Touch) != 0;
            if (click != clickDown)
            {
                clickDown = click;
                if (click) clickRight = p.Touch2 || (c.RightHalfClick && p.Touch1 && p.T1X > Pad.TouchW / 2);   // the button is chosen at the moment of the press
                Output(0, 0, 0, click ? (clickRight ? RIGHTDOWN : LEFTDOWN) : (clickRight ? RIGHTUP : LEFTUP));
                tapCandidate = false;   // a real click is not a tap
            }

            // Taps: a short touch that barely moved
            if (fingers > 0 && !had1 && !had2)
            {
                touchStart = now; tapTravel = 0; maxFingers = fingers; tapCandidate = c.TapClick && !click;
                gliding = false; velX = velY = 0;
                if (c.TapDrag && c.TapClick && now - lastTapEnd < DragWindowMs && fingers == 1 && !dragDown) { dragDown = true; Output(0, 0, 0, LEFTDOWN); tapCandidate = false; }
            }
            if (fingers > maxFingers) maxFingers = fingers;
            if (fingers == 0 && (had1 || had2))
            {
                if (dragDown) { dragDown = false; Output(0, 0, 0, LEFTUP); }
                else if (tapCandidate && now - touchStart <= TapMs && tapTravel < TapMove)
                {
                    bool right = maxFingers >= 2;
                    Output(0, 0, 0, right ? RIGHTDOWN : LEFTDOWN);
                    Output(0, 0, 0, right ? RIGHTUP : LEFTUP);
                    if (!right) lastTapEnd = now;
                    if (c.HapticClick && Haptic != null) Haptic();
                }
                if (c.Trackball && !c.Absolute && had1 && Math.Abs(velX) + Math.Abs(velY) > 0.15) gliding = true;
            }

            // Gestures (edge swipes, edge scroll, two-finger media swipe): while one runs the cursor stays put
            if (!c.Absolute && Gestures.Step(p, c, now, Output))
            {
                if (Gestures.Fired && c.HapticClick && Haptic != null) Haptic();
                if (Gestures.Busy) tapCandidate = false;
                x1 = p.T1X; y1 = p.T1Y; id1 = p.T1Id; had1 = p.Touch1; had2 = false; pendX = pendY = 0; resync = true;
                return;
            }

            if (p.Touch1 && p.Touch2)
            {
                int y = (p.T1Y + p.T2Y) / 2, x = (p.T1X + p.T2X) / 2;
                if (had2 && c.Scroll)
                {
                    double dir = c.NaturalScroll ? -1 : 1;
                    accWheel += (scrollY - y) * 2.5 * c.ScrollSens * dir;
                    accHWheel += (x - scrollX) * 2.5 * c.ScrollSens * dir;
                    tapTravel += Math.Abs(scrollY - y) + Math.Abs(x - scrollX);
                    int w = (int)accWheel, h = (int)accHWheel;
                    if (w != 0) { accWheel -= w; Output(0, 0, w, WHEEL); }
                    if (h != 0) { accHWheel -= h; Output(0, 0, h, HWHEEL); }
                }
                scrollY = y; scrollX = x; had2 = true; had1 = false;
                return;
            }
            had2 = false; accWheel = accHWheel = 0;

            if (p.Touch1)
            {
                if (c.Absolute)
                {
                    int ax = c.InvertX ? Pad.TouchW - 1 - p.T1X : p.T1X, ay = c.InvertY ? Pad.TouchH - 1 - p.T1Y : p.T1Y;
                    Output(ax * 65535 / (Pad.TouchW - 1), ay * 65535 / (Pad.TouchH - 1), 0, MOVE | ABSOLUTE | VIRTUALDESK);
                }
                else if (had1 && p.T1Id == id1 && !resync)
                {
                    // Reports that repeat the last point carry nothing new: counting them as "stopped" made the speed (and
                    // the acceleration) flicker and the cursor move in steps — one jump per point, then nothing.
                    double since = now - lastSample;
                    if (p.T1X != x1 || p.T1Y != y1 || since >= StillMs)
                    {
                        // The finger position through a One Euro filter: steady when moving slowly, no lag when moving fast.
                        // (No dead threshold: it made a slow finger stick and then jump.)
                        double nx = fx.Step(p.T1X, since / 1000.0, c.Jitter ? 1.2 : 4.0, 0.004), ny = fy.Step(p.T1Y, since / 1000.0, c.Jitter ? 1.2 : 4.0, 0.004);
                        double dx = nx - sx, dy = ny - sy;
                        sx = nx; sy = ny;
                        tapTravel += Math.Abs(p.T1X - x1) + Math.Abs(p.T1Y - y1);
                        // The speed is smoothed: point to point it wobbles, and the multiplier followed it — the cursor surged and slowed
                        speed += (Math.Sqrt(dx * dx + dy * dy) / since - speed) * (1 - Math.Exp(-since / 25));
                        double k = c.Sens * (c.Accel ? Gain(speed, c.AccelMax) : 0.8);
                        double mx0 = dx * k * (c.InvertX ? -1 : 1), my0 = dy * k * (c.InvertY ? -1 : 1);
                        velX = velX * 0.6 + mx0 / since * 0.4; velY = velY * 0.6 + my0 / since * 0.4;
                        // Sent out evenly until the next point is due (it usually comes after the same gap)
                        pendX += mx0; pendY += my0; spread = Math.Min(since, MaxSpreadMs);
                        lastSample = now;
                    }
                }
                else
                {
                    resync = false;
                    accX = accY = pendX = pendY = spread = speed = 0; sx = p.T1X; sy = p.T1Y; velX = velY = 0; lastSample = now;
                    fx.Reset(); fy.Reset(); fx.Step(p.T1X, 0.004, 1, 0); fy.Step(p.T1Y, 0.004, 1, 0);
                }
                x1 = p.T1X; y1 = p.T1Y; id1 = p.T1Id; had1 = true;
                Spend(dt);
            }
            else
            {
                had1 = false;
                Spend(dt);
                if (gliding) Glide(c, dt);
            }
        }

        // The share of the pending movement due in the last dt ms
        void Spend(double dt)
        {
            if (pendX == 0 && pendY == 0) return;
            double f = spread <= dt ? 1 : dt / spread;
            spread = Math.Max(0, spread - dt);
            double ox = pendX * f, oy = pendY * f;
            pendX -= ox; pendY -= oy;
            if (f == 1) pendX = pendY = 0;
            accX += ox; accY += oy;
            int mx = (int)accX, my = (int)accY;
            if (mx != 0 || my != 0) { accX -= mx; accY -= my; Move(mx, my); }
        }

        // A relative cursor move — sent as the absolute position it ends at when Windows' own acceleration is to be left out
        void Move(int mx, int my)
        {
            int[] at = unaccelerated ? Target(mx, my) : null;
            if (at != null) Output(at[0], at[1], 0, MOVE | ABSOLUTE | VIRTUALDESK);
            else Output(mx, my, 0, MOVE);
        }

        // Pointer acceleration from the finger speed (touchpad units per ms): a smooth curve from 0.45× (slow, precise)
        // to max× (fast, far; TouchConfig.AccelMax) — no jumps between neighbouring speeds
        internal static double Gain(double speed, double max)
        {
            double t = Math.Max(0, Math.Min(1, (speed - 0.05) / 2.95));
            return 0.45 + (max - 0.45) * t * t * (3 - 2 * t);
        }

        void Glide(TouchConfig c, double dt)
        {
            double decay = Math.Exp(-dt / 1000.0 * (1 + c.Friction * 9));
            velX *= decay; velY *= decay;
            if (Math.Abs(velX) + Math.Abs(velY) < 0.02) { gliding = false; return; }
            accX += velX * dt; accY += velY * dt;
            int mx = (int)accX, my = (int)accY;
            if (mx != 0 || my != 0) { accX -= mx; accY -= my; Move(mx, my); }
        }

        // Release the buttons if the mode was turned off mid-click
        public void Reset()
        {
            if (clickDown) Output(0, 0, 0, clickRight ? RIGHTUP : LEFTUP);
            if (dragDown) Output(0, 0, 0, LEFTUP);
            clickDown = had1 = had2 = dragDown = gliding = false;
            pendX = pendY = spread = 0;
        }
    }

    // Touchpad gestures on top of the mouse. One finger: a swipe in from the left / right edge — browser back / forward,
    // down from the top edge — show the desktop, up from the bottom edge — task view; sliding along the right edge scrolls.
    // Two fingers (Pro): a quick swipe left / right — previous / next track. A finger that lands at an edge holds the cursor
    // until it's clear whether it's a gesture; one that doesn't go inward is an ordinary finger again.
    internal sealed class TouchGestures
    {
        const double EdgeX = 0.05, EdgeY = 0.1, SwipeMs = 700, MediaMs = 450;
        const int Decide = 80, SwipeX = 450, SwipeY = 230, MediaDist = 650;
        internal const int VK_BROWSER_BACK = 0xA6, VK_BROWSER_FORWARD = 0xA7, VK_MEDIA_NEXT = 0xB0, VK_MEDIA_PREV = 0xB1, VK_LWIN = 0x5B, VK_TAB = 0x09, VK_D = 0x44;
        enum Mode { None, Undecided, Swipe, EdgeScroll, Ordinary }

        internal Action<int, bool> Key = InputOut.Key;   // tests swap it
        Mode mode;
        int edge, x0, y0, lastY;   // edge: 0 left, 1 right, 2 top, 3 bottom, -1 none
        double t0, wheel;
        bool done, two, twoDone;
        int tx0, ty0; double tt0;

        public bool Fired { get; private set; }   // this report fired a gesture (for the haptic pulse)
        public bool Busy { get { return mode == Mode.Swipe || mode == Mode.EdgeScroll; } }

        // true — this report is the gesture's: no cursor movement, no scrolling
        public bool Step(Pad p, TouchConfig c, double now, Action<int, int, int, int> mouse)
        {
            Fired = false;
            if (p.Touch1 && p.Touch2)
            {
                int x = (p.T1X + p.T2X) / 2, y = (p.T1Y + p.T2Y) / 2;
                if (!two) { two = true; tx0 = x; ty0 = y; tt0 = now; twoDone = false; }
                else if (c.MediaSwipe && Pro.Unlocked && !twoDone && now - tt0 <= MediaMs && Math.Abs(x - tx0) >= MediaDist && Math.Abs(x - tx0) > 2 * Math.Abs(y - ty0))
                {
                    twoDone = true;
                    Press(x > tx0 ? VK_MEDIA_NEXT : VK_MEDIA_PREV);
                }
                mode = Mode.Ordinary;   // a second finger ends any one-finger gesture
                return false;
            }
            two = false;
            if (!p.Touch1) { mode = Mode.None; return false; }
            if (mode == Mode.None)
            {
                x0 = p.T1X; y0 = p.T1Y; lastY = y0; t0 = now; done = false; wheel = 0;
                edge = x0 < Pad.TouchW * EdgeX ? 0 : x0 > Pad.TouchW * (1 - EdgeX) ? 1 : y0 < Pad.TouchH * EdgeY ? 2 : y0 > Pad.TouchH * (1 - EdgeY) ? 3 : -1;
                mode = edge >= 0 && (c.EdgeSwipe || c.EdgeScroll && edge == 1) ? Mode.Undecided : Mode.Ordinary;
                return mode == Mode.Undecided;
            }
            int dx = p.T1X - x0, dy = p.T1Y - y0;
            if (mode == Mode.Undecided)
            {
                if (Math.Max(Math.Abs(dx), Math.Abs(dy)) < Decide) return true;
                bool across = Math.Abs(dx) > Math.Abs(dy);
                bool inward = edge == 0 ? across && dx > 0 : edge == 1 ? across && dx < 0 : edge == 2 ? !across && dy > 0 : !across && dy < 0;
                if (c.EdgeSwipe && inward) mode = Mode.Swipe;
                else if (c.EdgeScroll && edge == 1 && !across) { mode = Mode.EdgeScroll; lastY = p.T1Y; }
                else { mode = Mode.Ordinary; return false; }
            }
            if (mode == Mode.Swipe)
            {
                int travel = edge <= 1 ? Math.Abs(dx) : Math.Abs(dy);
                if (!done && travel >= (edge <= 1 ? SwipeX : SwipeY) && now - t0 <= SwipeMs)
                {
                    done = true;
                    if (edge == 0) Press(VK_BROWSER_BACK);
                    else if (edge == 1) Press(VK_BROWSER_FORWARD);
                    else if (edge == 2) Combo(VK_LWIN, VK_D);
                    else Combo(VK_LWIN, VK_TAB);
                }
                return true;
            }
            if (mode == Mode.EdgeScroll)
            {
                wheel += (lastY - p.T1Y) * 2.5 * c.ScrollSens * (c.NaturalScroll ? -1 : 1);
                lastY = p.T1Y;
                int w = (int)wheel;
                if (w != 0) { wheel -= w; mouse(0, 0, w, InputOut.WHEEL); }
                return true;
            }
            return false;
        }

        void Press(int vk) { Key(vk, true); Key(vk, false); Fired = true; }
        void Combo(int mod, int vk) { Key(mod, true); Key(vk, true); Key(vk, false); Key(mod, false); Fired = true; }
    }
}
