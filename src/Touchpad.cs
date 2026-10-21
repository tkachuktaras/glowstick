// Controller touchpad as a mouse: one finger — cursor, two fingers — scroll, touchpad click — left button (with two fingers — right).
// Called from the controller's read thread after the report has been forwarded to the virtual pad.
using System;
using System.Runtime.InteropServices;

namespace Glowstick
{
    internal class Touchpad
    {
        [StructLayout(LayoutKind.Sequential)]
        struct INPUT { public int type; public MOUSEINPUT mi; }

        [StructLayout(LayoutKind.Sequential)]
        struct MOUSEINPUT { public int dx, dy, mouseData, dwFlags, time; public IntPtr extra; }   // the largest member of the INPUT union

        [DllImport("user32.dll")] static extern int SendInput(int n, INPUT[] inputs, int size);

        const int MOVE = 0x1, LEFTDOWN = 0x2, LEFTUP = 0x4, RIGHTDOWN = 0x8, RIGHTUP = 0x10, WHEEL = 0x800;

        bool had1, had2, clickDown, clickRight;
        byte id1;
        int x1, y1, scrollY;
        double accX, accY, accWheel;

        public void Process(ref Pad p)
        {
            bool click = (p.Buttons & Btn.Touch) != 0;
            if (click != clickDown)
            {
                clickDown = click;
                if (click) clickRight = p.Touch2;   // the button is chosen at the moment of the press
                Send(0, 0, 0, click ? (clickRight ? RIGHTDOWN : LEFTDOWN) : (clickRight ? RIGHTUP : LEFTUP));
            }

            if (p.Touch1 && p.Touch2)
            {
                int y = (p.T1Y + p.T2Y) / 2;
                if (had2)
                {
                    accWheel += (scrollY - y) * 2.5 * Engine.TouchSens;
                    int w = (int)accWheel;
                    if (w != 0) { accWheel -= w; Send(0, 0, w, WHEEL); }
                }
                scrollY = y; had2 = true; had1 = false;
                return;
            }
            had2 = false; accWheel = 0;

            if (p.Touch1)
            {
                if (had1 && p.T1Id == id1)
                {
                    int dx = p.T1X - x1, dy = p.T1Y - y1;
                    double speed = Math.Sqrt(dx * dx + dy * dy);
                    double k = Engine.TouchSens * (0.35 + Math.Min(1.6, speed / 14));   // acceleration: slow — precise, fast — far
                    accX += dx * k; accY += dy * k;
                    int mx = (int)accX, my = (int)accY;
                    if (mx != 0 || my != 0) { accX -= mx; accY -= my; Send(mx, my, 0, MOVE); }
                }
                else { accX = accY = 0; }
                x1 = p.T1X; y1 = p.T1Y; id1 = p.T1Id; had1 = true;
            }
            else had1 = false;
        }

        // Release the button if the mode was turned off mid-click
        public void Reset()
        {
            if (clickDown) Send(0, 0, 0, clickRight ? RIGHTUP : LEFTUP);
            clickDown = had1 = had2 = false;
        }

        static void Send(int dx, int dy, int data, int flags)
        {
            INPUT[] inp = { new INPUT { type = 0, mi = new MOUSEINPUT { dx = dx, dy = dy, mouseData = data, dwFlags = flags } } };
            SendInput(1, inp, Marshal.SizeOf(typeof(INPUT)));
        }
    }
}
