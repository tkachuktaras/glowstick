// PS hotkeys on one controller (which buttons — HotkeyMap). Runs on the controller's read thread.
// While PS is held, neither PS nor combo buttons reach the game; PS + a combo fires the action. Released without a combo —
// the game gets a short PS; held longer than 0.4 s without a combo — PS goes to the game right away (long press for Steam etc.).
using System;
using System.Collections.Generic;

namespace Glowstick
{
    internal sealed class PsHotkeys
    {
        const long PassAfterMs = 400, TapMs = 100;

        readonly Action<HotAction> fire;
        readonly byte[] fwd = new byte[63];
        bool held, combo, pass;
        long downMs, tapUntil;
        Btn prev;

        public PsHotkeys(Action<HotAction> fire) { this.fire = fire; }

        // True if the report for the game must be adjusted (PS is held or a PS "tap" is still being sent)
        public bool Update(Btn b, long now)
        {
            bool ps = (b & Btn.PS) != 0;
            if (ps && !held) { held = true; combo = false; pass = false; downMs = now; }
            if (held)
            {
                if (!ps)
                {
                    held = false;
                    if (!combo && !pass) tapUntil = now + TapMs;
                }
                else if (!pass)
                {
                    List<HotAction> fired = HotkeyMap.Fired(b, prev);
                    if (fired.Count > 0) { combo = true; foreach (HotAction a in fired) fire(a); }
                    else if (!combo && now - downMs > PassAfterMs) pass = true;
                }
            }
            prev = b;
            return (held && !pass) || now < tapUntil;
        }

        // The report the game gets while Update says so: PS and the hotkey buttons released, or the short PS tap.
        // Returns a buffer of its own (raw is untouched).
        public byte[] Mask(byte[] raw, long now)
        {
            Buffer.BlockCopy(raw, 0, fwd, 0, 63);
            if (held && !pass)
            {
                uint m = (uint)HotkeyMap.All;
                fwd[4] = 0x08;                               // d-pad neutral, □✕○△ released
                fwd[5] &= (byte)~(m >> 8);                   // shoulder buttons, Share, Options, L3/R3 used as hotkeys
                fwd[6] &= (byte)~(0x01 | (m >> 16) & 0x02); // PS and, if it's a hotkey, the touchpad click
                if ((m & (uint)Btn.L2) != 0) fwd[7] = 0;     // the triggers' analog values too
                if ((m & (uint)Btn.R2) != 0) fwd[8] = 0;
            }
            else if (now < tapUntil) fwd[6] |= 0x01;
            return fwd;
        }
    }
}
