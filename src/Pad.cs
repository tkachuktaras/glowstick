// Parsing a DS4 input report (63 bytes without the ID, USB layout): buttons, sticks, triggers, touchpad, gyro.
using System;

namespace Glowstick
{
    [Flags]
    internal enum Btn : uint
    {
        None = 0, Up = 1, Down = 2, Left = 4, Right = 8,
        Square = 0x10, Cross = 0x20, Circle = 0x40, Triangle = 0x80,
        L1 = 0x100, R1 = 0x200, L2 = 0x400, R2 = 0x800, Share = 0x1000, Options = 0x2000, L3 = 0x4000, R3 = 0x8000,
        PS = 0x10000, Touch = 0x20000
    }

    internal struct Pad
    {
        public Btn Buttons;
        public byte LX, LY, RX, RY, L2, R2;
        public short GyroX, GyroY, GyroZ, AccelX, AccelY, AccelZ;
        public bool Touch1, Touch2;
        public int T1X, T1Y, T2X, T2Y;
        public byte T1Id, T2Id;

        public const int TouchW = 1920, TouchH = 943;

        static readonly Btn[] Hat = { Btn.Up, Btn.Up | Btn.Right, Btn.Right, Btn.Down | Btn.Right, Btn.Down, Btn.Down | Btn.Left, Btn.Left, Btn.Up | Btn.Left };

        public static Btn ReadButtons(byte[] raw)
        {
            int b4 = raw[4], b5 = raw[5], b6 = raw[6], hat = b4 & 0x0F;
            Btn b = hat < 8 ? Hat[hat] : Btn.None;
            b |= (Btn)(b4 & 0xF0);              // □ ✕ ○ △
            b |= (Btn)((uint)b5 << 8);          // L1 R1 L2 R2 Share Options L3 R3
            b |= (Btn)((uint)(b6 & 0x03) << 16); // PS, touchpad click
            return b;
        }

        public static Pad Parse(byte[] raw)
        {
            Pad p = new Pad();
            p.Buttons = ReadButtons(raw);
            p.LX = raw[0]; p.LY = raw[1]; p.RX = raw[2]; p.RY = raw[3]; p.L2 = raw[7]; p.R2 = raw[8];
            p.GyroX = S(raw, 12); p.GyroY = S(raw, 14); p.GyroZ = S(raw, 16);
            p.AccelX = S(raw, 18); p.AccelY = S(raw, 20); p.AccelZ = S(raw, 22);
            p.Touch1 = (raw[34] & 0x80) == 0; p.T1Id = (byte)(raw[34] & 0x7F);
            p.T1X = raw[35] | (raw[36] & 0x0F) << 8; p.T1Y = raw[36] >> 4 | raw[37] << 4;
            p.Touch2 = (raw[38] & 0x80) == 0; p.T2Id = (byte)(raw[38] & 0x7F);
            p.T2X = raw[39] | (raw[40] & 0x0F) << 8; p.T2Y = raw[40] >> 4 | raw[41] << 4;
            return p;
        }

        static short S(byte[] raw, int i) { return (short)(raw[i] | raw[i + 1] << 8); }

        // Right stick angle in turns (0..1) and deflection (0..1)
        public double RightAngle { get { return Angle(RX, RY); } }
        public double RightAmount { get { return Amount(RX, RY); } }
        public static double Angle(byte x, byte y) { double a = Math.Atan2(-(y - 127.5), x - 127.5) / (2 * Math.PI); return a - Math.Floor(a); }
        public static double Amount(byte x, byte y) { double dx = (x - 127.5) / 127.5, dy = (y - 127.5) / 127.5; return Math.Min(1, Math.Sqrt(dx * dx + dy * dy)); }
    }
}
