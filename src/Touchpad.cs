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

        public TouchConfig Clone() { return (TouchConfig)MemberwiseClone(); }

        public Dictionary<string, object> Save()
        {
            return new Dictionary<string, object>
            {
                { "Absolute", Absolute }, { "Sens", Sens }, { "Accel", Accel }, { "Jitter", Jitter }, { "TapClick", TapClick }, { "TapDrag", TapDrag },
                { "Scroll", Scroll }, { "ScrollSens", ScrollSens }, { "NaturalScroll", NaturalScroll }, { "Trackball", Trackball }, { "Friction", Friction },
                { "InvertX", InvertX }, { "InvertY", InvertY }, { "RightHalfClick", RightHalfClick }
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

        // Where the mouse events go — InputOut (SendInput); tests swap it for a recorder (dx, dy, data, flags)
        internal Action<int, int, int, int> Output = InputOut.Mouse;

        bool had1, had2, clickDown, clickRight, dragDown;
        byte id1;
        int x1, y1, scrollX, scrollY;
        double sx, sy;                     // the filtered finger position
        readonly OneEuro fx = new OneEuro(), fy = new OneEuro();
        double accX, accY, accWheel, accHWheel;
        // taps
        long touchStart, lastTapEnd = -100000, lastMs;
        int tapTravel, maxFingers;
        bool tapCandidate;
        // trackball glide (mouse px per ms)
        double velX, velY;
        bool gliding;

        public void Process(ref Pad p, TouchConfig c, long now)
        {
            long dt = lastMs == 0 ? 4 : Math.Max(1, Math.Min(50, now - lastMs));
            lastMs = now;
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
                }
                if (c.Trackball && !c.Absolute && had1 && Math.Abs(velX) + Math.Abs(velY) > 0.15) gliding = true;
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
                else if (had1 && p.T1Id == id1)
                {
                    // The finger position through a One Euro filter: steady when moving slowly, no lag when moving fast.
                    // (No dead threshold: it made a slow finger stick and then jump.)
                    double dts = dt / 1000.0;
                    double nx = fx.Step(p.T1X, dts, c.Jitter ? 1.2 : 4.0, 0.004), ny = fy.Step(p.T1Y, dts, c.Jitter ? 1.2 : 4.0, 0.004);
                    double dx = nx - sx, dy = ny - sy;
                    sx = nx; sy = ny;
                    tapTravel += Math.Abs(p.T1X - x1) + Math.Abs(p.T1Y - y1);
                    double k = c.Sens * (c.Accel ? Gain(Math.Sqrt(dx * dx + dy * dy) / dt) : 0.8);
                    double mx0 = dx * k * (c.InvertX ? -1 : 1), my0 = dy * k * (c.InvertY ? -1 : 1);
                    velX = velX * 0.6 + mx0 / dt * 0.4; velY = velY * 0.6 + my0 / dt * 0.4;
                    accX += mx0; accY += my0;
                    int mx = (int)accX, my = (int)accY;
                    if (mx != 0 || my != 0) { accX -= mx; accY -= my; Output(mx, my, 0, MOVE); }
                }
                else { accX = accY = 0; sx = p.T1X; sy = p.T1Y; velX = velY = 0; fx.Reset(); fy.Reset(); fx.Step(p.T1X, 0.004, 1, 0); fy.Step(p.T1Y, 0.004, 1, 0); }
                x1 = p.T1X; y1 = p.T1Y; id1 = p.T1Id; had1 = true;
            }
            else
            {
                had1 = false;
                if (gliding) Glide(c, dt);
            }
        }

        // Pointer acceleration from the finger speed (touchpad units per ms): a smooth curve from 0.45× (slow, precise)
        // to 2× (fast, far) — no jumps between neighbouring speeds
        internal static double Gain(double speed)
        {
            double t = Math.Max(0, Math.Min(1, (speed - 0.05) / 2.95));
            return 0.45 + 1.55 * t * t * (3 - 2 * t);
        }

        void Glide(TouchConfig c, long dt)
        {
            double decay = Math.Exp(-dt / 1000.0 * (1 + c.Friction * 9));
            velX *= decay; velY *= decay;
            if (Math.Abs(velX) + Math.Abs(velY) < 0.02) { gliding = false; return; }
            accX += velX * dt; accY += velY * dt;
            int mx = (int)accX, my = (int)accY;
            if (mx != 0 || my != 0) { accX -= mx; accY -= my; Output(mx, my, 0, MOVE); }
        }

        // Release the buttons if the mode was turned off mid-click
        public void Reset()
        {
            if (clickDown) Output(0, 0, 0, clickRight ? RIGHTUP : LEFTUP);
            if (dragDown) Output(0, 0, 0, LEFTUP);
            clickDown = had1 = had2 = dragDown = gliding = false;
        }
    }
}
