// Animation time for previews in the window. Speed scales the time step, not the whole elapsed time —
// otherwise moving the speed slider jumps every animation (elapsed × new speed is a different point in time).
// The engine does the same for the light bars (Engine.Loop: T += dt * Speed).
using System;
using System.Diagnostics;

namespace Glowstick
{
    internal class AnimClock
    {
        readonly Stopwatch sw = Stopwatch.StartNew();
        double last, t;

        public double Now(double speed) { return Advance(sw.Elapsed.TotalSeconds, speed); }

        // now — seconds since start. A long pause (window hidden) moves the animation by at most 0.2 s.
        internal double Advance(double now, double speed)
        {
            double dt = Math.Max(0, Math.Min(0.2, now - last));
            last = now;
            t += dt * speed;
            return t;
        }
    }
}
