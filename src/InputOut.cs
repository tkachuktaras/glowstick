// Keyboard and mouse output for everything Glowstick injects (touchpad as mouse; Pro remap, macros, gyro, stick → mouse).
// SendInput with hardware scan codes: games that read DirectInput / Raw Input see real key presses (a virtual-key-only
// event has scan code 0, and many games ignore it). The sinks are swappable for tests.
using System;
using System.Runtime.InteropServices;

namespace Glowstick
{
    internal static class InputOut
    {
        // Mouse button codes used next to virtual keys (key / macro outputs)
        public const int MouseLeft = 1001, MouseRight = 1002, MouseMiddle = 1003, MouseX1 = 1004, MouseX2 = 1005;
        public const int MOVE = 0x1, LEFTDOWN = 0x2, LEFTUP = 0x4, RIGHTDOWN = 0x8, RIGHTUP = 0x10, MIDDLEDOWN = 0x20, MIDDLEUP = 0x40,
            XDOWN = 0x80, XUP = 0x100, WHEEL = 0x800, HWHEEL = 0x1000, VIRTUALDESK = 0x4000, ABSOLUTE = 0x8000;

        public static Action<int, bool> KeySink = SendKey;               // code (virtual key or Mouse…), down
        public static Action<int, int, int, int> MouseSink = SendMouse;  // dx, dy, data, flags

        public static void Key(int code, bool down) { KeySink(code, down); }
        public static void Mouse(int dx, int dy, int data, int flags) { MouseSink(dx, dy, data, flags); }
        public static void Move(int dx, int dy) { MouseSink(dx, dy, 0, MOVE); }
        public static void Wheel(int delta, bool horizontal) { MouseSink(0, 0, delta, horizontal ? HWHEEL : WHEEL); }

        // The cursor moved by (dx, dy) pixels as an absolute position (SendInput's 0..65535 over the whole desktop): Windows
        // applies its pointer speed and "Enhance pointer precision" only to relative moves. null — the cursor is unknown.
        public static int[] CursorTarget(int dx, int dy)
        {
            POINT p;
            if (!GetCursorPos(out p)) return null;
            int vx = GetSystemMetrics(76), vy = GetSystemMetrics(77), vw = GetSystemMetrics(78), vh = GetSystemMetrics(79);
            if (vw <= 1 || vh <= 1) return null;
            int x = Math.Max(vx, Math.Min(vx + vw - 1, p.X + dx)), y = Math.Max(vy, Math.Min(vy + vh - 1, p.Y + dy));
            return new[] { ToAbsolute(x - vx, vw), ToAbsolute(y - vy, vh) };
        }

        // Windows maps an absolute coordinate back as pixel = n * size / 65536 (rounded down): the smallest n that lands on it
        internal static int ToAbsolute(int pixel, int size) { return Math.Min(65535, (int)Math.Ceiling(pixel * 65536.0 / size)); }

        [StructLayout(LayoutKind.Sequential)] struct POINT { public int X, Y; }
        [DllImport("user32.dll")] static extern bool GetCursorPos(out POINT p);
        [DllImport("user32.dll")] static extern int GetSystemMetrics(int index);

        // ---------- SendInput ----------
        [StructLayout(LayoutKind.Sequential)] struct MOUSEINPUT { public int dx, dy, mouseData, dwFlags, time; public IntPtr extra; }
        [StructLayout(LayoutKind.Sequential)] struct KEYBDINPUT { public ushort wVk, wScan; public int dwFlags, time; public IntPtr extra; }
        [StructLayout(LayoutKind.Explicit)] struct UNION { [FieldOffset(0)] public MOUSEINPUT mi; [FieldOffset(0)] public KEYBDINPUT ki; }
        [StructLayout(LayoutKind.Sequential)] struct INPUT { public int type; public UNION u; }   // the union is pointer-aligned on x64

        [DllImport("user32.dll", SetLastError = true)] static extern uint SendInput(uint n, INPUT[] inputs, int size);
        [DllImport("user32.dll")] static extern uint MapVirtualKey(uint code, uint mapType);

        const int KEYEVENTF_EXTENDEDKEY = 0x1, KEYEVENTF_KEYUP = 0x2, KEYEVENTF_SCANCODE = 0x8;
        const uint MAPVK_VK_TO_VSC_EX = 4;
        static readonly int Size = Marshal.SizeOf(typeof(INPUT));

        // Scan code and flags for a virtual key: (scan, extended); scan 0 — the key has none (sent by virtual key)
        internal static void ScanOf(int vk, out ushort scan, out bool extended)
        {
            uint sc = MapVirtualKey((uint)vk, MAPVK_VK_TO_VSC_EX);
            extended = (sc & 0xFF00) == 0xE000 || (sc & 0xFF00) == 0xE100;
            scan = (ushort)(sc & 0xFF);
            // MapVirtualKey doesn't flag these as extended, but they are (the numpad versions aren't)
            if (vk >= 0x21 && vk <= 0x28 || vk == 0x2D || vk == 0x2E || vk == 0x5B || vk == 0x5C || vk == 0x5D || vk == 0x6F || vk == 0xA3 || vk == 0xA5) extended = true;
        }

        static void SendKey(int code, bool down)
        {
            if (code >= MouseLeft)
            {
                int flags, data = 0;
                switch (code)
                {
                    case MouseLeft: flags = down ? LEFTDOWN : LEFTUP; break;
                    case MouseRight: flags = down ? RIGHTDOWN : RIGHTUP; break;
                    case MouseMiddle: flags = down ? MIDDLEDOWN : MIDDLEUP; break;
                    default: flags = down ? XDOWN : XUP; data = code == MouseX1 ? 1 : 2; break;
                }
                SendMouse(0, 0, data, flags);
                return;
            }
            ushort scan; bool ext;
            ScanOf(code, out scan, out ext);
            INPUT[] i = new INPUT[1];
            i[0].type = 1;
            i[0].u.ki.wVk = (ushort)code;
            i[0].u.ki.wScan = scan;
            i[0].u.ki.dwFlags = (down ? 0 : KEYEVENTF_KEYUP) | (scan != 0 ? KEYEVENTF_SCANCODE : 0) | (ext ? KEYEVENTF_EXTENDEDKEY : 0);
            SendInput(1, i, Size);
        }

        static void SendMouse(int dx, int dy, int data, int flags)
        {
            INPUT[] i = new INPUT[1];
            i[0].type = 0;
            i[0].u.mi.dx = dx; i[0].u.mi.dy = dy; i[0].u.mi.mouseData = data; i[0].u.mi.dwFlags = flags;
            SendInput(1, i, Size);
        }
    }
}
