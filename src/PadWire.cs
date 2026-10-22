// The controllers' wire formats, separate from the device handling in Controller:
//   • DualSense input → the DS4 report layout everything else reads (virtual pad, test page, touchpad, presets);
//   • the output packets — light bar, rumble, player LEDs, adaptive triggers — of both models over USB and Bluetooth;
//   • the CRC32 a Bluetooth output packet ends with.
using System;

namespace Glowstick
{
    internal static class DualSenseWire
    {
        // Product IDs of DualSense and DualSense Edge
        public static readonly string[] Pids = { "0ce6", "0df2" };

        // DualSense report (after the ID; over BT also after a sequence byte — o = 2): 0-3 sticks, 4-5 L2/R2, 7-9 buttons
        // (7 and 8 are bitwise the same as DS4), 15-26 gyro/accelerometer, 27-30 sensor time (×0.33 µs), 32-39 touchpad, 52 — battery.
        public static void ToDs4(byte[] b, int o, byte[] raw, ref int counter, out int pct, out bool charging)
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

        static readonly byte[] PlayerLeds = { 0x04, 0x0A, 0x15, 0x1B, 0x1F };   // player 1..5, as on the PS5

        // Output packet: USB — 0x02, BT — 0x31 + sequence + tag 0x10 + CRC32; the common part starts at o. The first packet
        // turns off the blue connect animation (otherwise the color won't apply), then — light bar, player LEDs, rumble,
        // adaptive triggers (11-byte FFB each, null — none). Returns the length to write; buf must be cleared.
        public static int Output(byte[] buf, bool bluetooth, int outLen, ref int seq, ref bool released, bool rumble, byte small, byte large,
            byte[] trigL, byte[] trigR, bool playerLeds, int player, int r, int g, int b)
        {
            int o;
            if (bluetooth) { buf[0] = 0x31; buf[1] = (byte)(seq << 4); seq = (seq + 1) & 0x0F; buf[2] = 0x10; o = 3; }
            else { buf[0] = 0x02; o = 1; }
            buf[o] = (byte)(rumble ? 0x03 : 0x00);                    // compatible rumble + motor select
            buf[o + 1] = (byte)(0x04 | (playerLeds ? 0x10 : 0));      // light bar, player LEDs
            buf[o + 2] = small; buf[o + 3] = large;
            if (trigR != null) { buf[o] |= 0x04; Array.Copy(trigR, 0, buf, o + 10, 11); }   // ValidFlag0 bit2 / bit3, FFB at o+10 / o+21
            if (trigL != null) { buf[o] |= 0x08; Array.Copy(trigL, 0, buf, o + 21, 11); }
            if (!released) { buf[o + 38] = 0x02; buf[o + 41] = 0x02; released = true; }
            buf[o + 43] = playerLeds && player > 0 ? PlayerLeds[Math.Min(player, PlayerLeds.Length) - 1] : (byte)0;
            buf[o + 44] = (byte)r; buf[o + 45] = (byte)g; buf[o + 46] = (byte)b;
            if (!bluetooth) return Math.Max(outLen, 48);
            Crc32.Seal(buf);
            return Math.Max(outLen, 78);
        }
    }

    internal static class DualShock4Wire
    {
        public static readonly string[] Pids = { "05c4", "09cc", "0ba0" };

        // Output packet: USB — 0x05, BT — 0x11 + poll rate + CRC32. Flags: 0x01 rumble (only in game mode, so we don't override
        // someone else's otherwise), 0x02 light bar, 0x04 blink. Returns the length to write; buf must be cleared.
        public static int Output(byte[] buf, bool bluetooth, int outLen, int btPollMs, bool rumble, byte small, byte large, int r, int g, int b)
        {
            byte flags = (byte)(rumble ? 0x07 : 0x06);
            if (bluetooth)
            {
                buf[0] = 0x11; buf[1] = (byte)(0xC0 | (btPollMs & 0x3F)); buf[3] = flags;
                buf[6] = small; buf[7] = large;
                buf[8] = (byte)r; buf[9] = (byte)g; buf[10] = (byte)b;
                Crc32.Seal(buf);
                return Math.Max(outLen, 78);
            }
            buf[0] = 0x05; buf[1] = flags;
            buf[4] = small; buf[5] = large;
            buf[6] = (byte)r; buf[7] = (byte)g; buf[8] = (byte)b;
            return Math.Max(outLen, 32);
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

        // A 78-byte Bluetooth output packet: the CRC32 of bytes 0..73 goes into 74..77
        public static void Seal(byte[] buf)
        {
            uint crc = Bt(buf, 74);
            buf[74] = (byte)crc; buf[75] = (byte)(crc >> 8); buf[76] = (byte)(crc >> 16); buf[77] = (byte)(crc >> 24);
        }
    }
}
