// BtRadio — turns a Bluetooth controller off (like holding PS for 10 s): IOCTL_BTH_DISCONNECT_DEVICE on every BT adapter.
using System;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Win32.SafeHandles;

namespace Glowstick
{
    internal static class BtRadio
    {
        [StructLayout(LayoutKind.Sequential)] struct FIND_RADIO_PARAMS { public int dwSize; }

        [DllImport("bthprops.cpl", SetLastError = true)] static extern IntPtr BluetoothFindFirstRadio(ref FIND_RADIO_PARAMS p, out IntPtr radio);
        [DllImport("bthprops.cpl", SetLastError = true)] static extern bool BluetoothFindNextRadio(IntPtr find, out IntPtr radio);
        [DllImport("bthprops.cpl")] static extern bool BluetoothFindRadioClose(IntPtr find);
        [DllImport("kernel32.dll", SetLastError = true)] static extern bool DeviceIoControl(IntPtr h, uint code, ref ulong inBuf, int inSize, IntPtr outBuf, int outSize, out int returned, IntPtr overlapped);
        [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr h);
        [DllImport("hid.dll", CharSet = CharSet.Unicode)] static extern bool HidD_GetSerialNumberString(SafeFileHandle h, StringBuilder buf, int size);
        [DllImport("cfgmgr32.dll")] static extern int CM_Get_Parent(out int parent, int devInst, int flags);
        [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)] static extern int CM_Get_Device_ID(int devInst, StringBuilder buf, int len, int flags);

        const uint IOCTL_BTH_DISCONNECT_DEVICE = 0x41000C;

        // Controller MAC: over BT the DS4 reports it as the serial number; fallback — the parent BT device ID
        public static ulong FindAddress(SafeFileHandle h, int devInst)
        {
            StringBuilder sb = new StringBuilder(128);
            if (HidD_GetSerialNumberString(h, sb, sb.Capacity * 2))
            {
                ulong a = Parse(sb.ToString());
                if (a != 0) return a;
            }
            int cur = devInst;
            for (int depth = 0; depth < 6; depth++)
            {
                int parent;
                if (CM_Get_Parent(out parent, cur, 0) != 0) break;
                cur = parent;
                StringBuilder id = new StringBuilder(512);
                if (CM_Get_Device_ID(cur, id, id.Capacity, 0) != 0) continue;
                Match m = Regex.Match(id.ToString(), @"(?:DEV_|&)([0-9A-Fa-f]{12})(?:_|$)");
                if (m.Success) return Convert.ToUInt64(m.Groups[1].Value, 16);
            }
            return 0;
        }

        static ulong Parse(string s)
        {
            string hex = Regex.Replace(s, "[^0-9A-Fa-f]", "");
            return hex.Length == 12 ? Convert.ToUInt64(hex, 16) : 0;
        }

        public static bool Disconnect(ulong address)
        {
            if (address == 0) return false;
            FIND_RADIO_PARAMS p = new FIND_RADIO_PARAMS { dwSize = Marshal.SizeOf(typeof(FIND_RADIO_PARAMS)) };
            IntPtr radio;
            IntPtr find = BluetoothFindFirstRadio(ref p, out radio);
            if (find == IntPtr.Zero) return false;
            bool ok = false;
            try
            {
                do
                {
                    int ret; ulong a = address;
                    if (DeviceIoControl(radio, IOCTL_BTH_DISCONNECT_DEVICE, ref a, 8, IntPtr.Zero, 0, out ret, IntPtr.Zero)) ok = true;
                    CloseHandle(radio);
                } while (BluetoothFindNextRadio(find, out radio));
            }
            finally { BluetoothFindRadioClose(find); }
            return ok;
        }
    }
}
