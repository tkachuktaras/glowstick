// How a controller is doing, from its own input reports: is the Bluetooth link dropping reports, does a stick drift.
// Both are fed on the read thread and read by the UI (Engine.Snapshot); plain classes so the tests can drive them.
using System;

namespace Glowstick
{
    // Late reports: a controller reports every few ms all the time (idle too), so a long silence between two reports means
    // the radio link lost some. A second with a gap over BadGapMs counts as bad; 3 bad of the last 5 — a weak link, which
    // clears after QuietSeconds without a bad one (so it doesn't flicker on and off).
    internal sealed class LinkMeter
    {
        public const int BadGapMs = 60, QuietSeconds = 10;
        int history, quiet;
        public bool Weak { get; private set; }
        public int WorstGapMs { get; private set; }   // the longest silence since the link turned weak

        // Once a second, with the longest gap between two reports in it
        public void Second(int gapMs)
        {
            bool badNow = gapMs > BadGapMs;
            history = (history << 1 | (badNow ? 1 : 0)) & 0x1F;
            quiet = badNow ? 0 : quiet + 1;   // seconds since the last bad one
            int bad = 0;
            for (int h = history; h != 0; h >>= 1) bad += h & 1;
            if (bad >= 3 && !Weak) { Weak = true; WorstGapMs = 0; }
            else if (Weak && quiet >= QuietSeconds) Weak = false;
            if (Weak) WorstGapMs = Math.Max(WorstGapMs, gapMs);
        }
    }

    // A stick that doesn't come back to the center. Only moments when the controller is put down count: no button or trigger
    // held and the stick not moving for StillMs; where it rests then (if not pushed on purpose) is its offset — smoothed over
    // several rests. Offset is a fraction of the full throw (0 .. 1), -1 until the stick has rested once.
    internal sealed class DriftMeter
    {
        public const double StillMs = 1500, Pushed = 0.35, Shown = 0.06;
        const int Wobble = 2;   // a resting stick still wobbles by a unit or two
        int ax = -100, ay = -100;
        long since;
        double offset = -1;

        public double Offset { get { return offset; } }

        public void Add(byte x, byte y, bool idle, long now)
        {
            if (!idle || Math.Abs(x - ax) > Wobble || Math.Abs(y - ay) > Wobble) { ax = x; ay = y; since = now; return; }
            if (now - since < StillMs) return;
            since = now;   // the next reading after another StillMs
            double dx = (ax - 127.5) / 127.5, dy = (ay - 127.5) / 127.5, rest = Math.Sqrt(dx * dx + dy * dy);
            if (rest >= Pushed) return;
            offset = offset < 0 ? rest : offset * 0.7 + rest * 0.3;
        }

        // The dead zone that hides the drift, rounded up to a whole percent with a little room
        public static double DeadzoneFor(double offset) { return Math.Min(0.5, Math.Ceiling((offset + 0.03) * 100) / 100); }
    }
}
