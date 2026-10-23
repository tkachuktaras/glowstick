// The PC's microphone mute (the default recording device, and the default communications one if it's another):
// what the DualSense Mute light can show and its Mute button can switch (Pro). Core Audio's IAudioEndpointVolume —
// the same switch as Windows' own; Discord, Teams and games see the mic go silent. Called from the light bar thread.
using System;
using System.Runtime.InteropServices;

namespace Glowstick
{
    internal static class Mic
    {
        static bool muted;
        static long readAt = -100000;

        // Read at most twice a second
        public static bool Muted
        {
            get
            {
                long now = Engine.Ms;
                if (now - readAt >= 500) { readAt = now; bool m; if (Get(0, out m)) muted = m; }
                return muted;
            }
        }

        public static void Toggle()
        {
            bool m;
            if (!Get(0, out m)) return;
            Set(0, !m);
            Set(2, !m);   // the communications device, if it's a different one (setting the same twice is harmless)
            muted = !m; readAt = Engine.Ms;
            Log.Info("microphone " + (muted ? "muted" : "unmuted"));
        }

        static bool Get(int role, out bool mute)
        {
            mute = false;
            IAudioEndpointVolume v = Volume(role);
            if (v == null) return false;
            try { return v.GetMute(out mute) == 0; } catch { return false; } finally { Marshal.ReleaseComObject(v); }
        }

        static void Set(int role, bool mute)
        {
            IAudioEndpointVolume v = Volume(role);
            if (v == null) return;
            try { Guid ctx = Guid.Empty; v.SetMute(mute, ref ctx); } catch { } finally { Marshal.ReleaseComObject(v); }
        }

        // role: 0 — console (the default device), 2 — communications
        static IAudioEndpointVolume Volume(int role)
        {
            try
            {
                IMMDeviceEnumerator en = (IMMDeviceEnumerator)Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")));   // by CLSID — see AudioCapture.Enumerator
                IMMDevice dev;
                if (en.GetDefaultAudioEndpoint(1 /* eCapture */, role, out dev) != 0 || dev == null) return null;
                Guid iid = typeof(IAudioEndpointVolume).GUID;
                object o;
                if (dev.Activate(ref iid, 1 /* CLSCTX_INPROC_SERVER */, IntPtr.Zero, out o) != 0) return null;
                return (IAudioEndpointVolume)o;
            }
            catch { return null; }
        }


        [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IMMDeviceEnumerator
        {
            [PreserveSig] int EnumAudioEndpoints(int dataFlow, int stateMask, out IntPtr devices);
            [PreserveSig] int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice endpoint);
        }

        [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IMMDevice
        {
            [PreserveSig] int Activate(ref Guid iid, int clsCtx, IntPtr activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object iface);
        }

        [ComImport, Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IAudioEndpointVolume
        {
            [PreserveSig] int RegisterControlChangeNotify(IntPtr notify);
            [PreserveSig] int UnregisterControlChangeNotify(IntPtr notify);
            [PreserveSig] int GetChannelCount(out int count);
            [PreserveSig] int SetMasterVolumeLevel(float level, ref Guid ctx);
            [PreserveSig] int SetMasterVolumeLevelScalar(float level, ref Guid ctx);
            [PreserveSig] int GetMasterVolumeLevel(out float level);
            [PreserveSig] int GetMasterVolumeLevelScalar(out float level);
            [PreserveSig] int SetChannelVolumeLevel(int channel, float level, ref Guid ctx);
            [PreserveSig] int SetChannelVolumeLevelScalar(int channel, float level, ref Guid ctx);
            [PreserveSig] int GetChannelVolumeLevel(int channel, out float level);
            [PreserveSig] int GetChannelVolumeLevelScalar(int channel, out float level);
            [PreserveSig] int SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, ref Guid ctx);
            [PreserveSig] int GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);
        }
    }
}
