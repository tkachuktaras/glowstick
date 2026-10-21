// DualSense adaptive-trigger effects as the 11-byte FFB blocks the controller's output report expects (see Controller.Ds5Packet).
// Byte 0 is the effect type; the rest are its parameters. These are the simple "native" effects; exact feel is tuned on hardware.
namespace Glowstick
{
    internal static class DsTrigger
    {
        public static readonly byte[] Off = new byte[11];   // type 0x00 — no resistance

        // Uniform resistance from `start` (0–255 along the pull) with `force` (0–255)
        public static byte[] Resistance(byte start, byte force)
        {
            return new byte[] { 0x01, start, force, 0, 0, 0, 0, 0, 0, 0, 0 };
        }

        // A resistant section between `start` and `end` that gives way (like a trigger), with `force`
        public static byte[] Weapon(byte start, byte end, byte force)
        {
            return new byte[] { 0x02, start, end, force, 0, 0, 0, 0, 0, 0, 0 };
        }

        // Vibrating resistance from `pos` with `amplitude` at `frequency` (Hz) — e.g. an automatic weapon
        public static byte[] Vibration(byte pos, byte amplitude, byte frequency)
        {
            return new byte[] { 0x06, pos, amplitude, frequency, 0, 0, 0, 0, 0, 0, 0 };
        }
    }
}
