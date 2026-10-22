// One Euro filter (Casiez et al., 2012): strong smoothing when a signal moves slowly, little lag when it moves fast.
// Used for the touchpad cursor and (Pro) gyro aiming.
using System;

namespace Glowstick
{
    internal sealed class OneEuro
    {
        double x, dx; bool init;

        public void Reset() { init = false; }

        // v — the new sample, dt — seconds since the last one; minCutoff (Hz) — smoothing when still, beta — how fast it opens up with speed
        public double Step(double v, double dt, double minCutoff, double beta)
        {
            if (!init) { init = true; x = v; dx = 0; return v; }
            double d = (v - x) / dt;
            dx += Alpha(1.0, dt) * (d - dx);
            double cutoff = minCutoff + beta * Math.Abs(dx);
            x += Alpha(cutoff, dt) * (v - x);
            return x;
        }

        static double Alpha(double cutoff, double dt) { double tau = 1 / (2 * Math.PI * cutoff); return 1 / (1 + tau / dt); }
    }
}
