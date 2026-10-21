// WinAPI: HID, SetupAPI, Configuration Manager, file I/O.
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace Glowstick
{
    internal class HidDeviceEntry { public string Path; public string InstanceId; public int DevInst; }

    internal static class Native
    {
        [StructLayout(LayoutKind.Sequential)]
        public struct SP_DEVICE_INTERFACE_DATA { public int cbSize; public Guid InterfaceClassGuid; public int Flags; public IntPtr Reserved; }

        [StructLayout(LayoutKind.Sequential)]
        public struct SP_DEVINFO_DATA { public int cbSize; public Guid ClassGuid; public int DevInst; public IntPtr Reserved; }

        [StructLayout(LayoutKind.Sequential)]
        public struct HIDP_CAPS
        {
            public ushort Usage, UsagePage, InputReportByteLength, OutputReportByteLength, FeatureReportByteLength;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 17)] public ushort[] Reserved;
            public ushort NumberLinkCollectionNodes, NumberInputButtonCaps, NumberInputValueCaps, NumberInputDataIndices,
                NumberOutputButtonCaps, NumberOutputValueCaps, NumberOutputDataIndices,
                NumberFeatureButtonCaps, NumberFeatureValueCaps, NumberFeatureDataIndices;
        }

        public const uint GENERIC_READ = 0x80000000, GENERIC_WRITE = 0x40000000;
        const int DIGCF_PRESENT = 0x02, DIGCF_DEVICEINTERFACE = 0x10, CM_DRP_SERVICE = 0x05;

        [DllImport("hid.dll")] public static extern void HidD_GetHidGuid(out Guid g);
        [DllImport("hid.dll", SetLastError = true)] public static extern bool HidD_GetPreparsedData(SafeFileHandle h, out IntPtr data);
        [DllImport("hid.dll")] public static extern bool HidD_FreePreparsedData(IntPtr data);
        [DllImport("hid.dll")] public static extern int HidP_GetCaps(IntPtr data, ref HIDP_CAPS caps);
        [DllImport("hid.dll", SetLastError = true)] public static extern bool HidD_SetOutputReport(SafeFileHandle h, byte[] buf, int len);
        [DllImport("hid.dll", SetLastError = true)] public static extern bool HidD_SetNumInputBuffers(SafeFileHandle h, int n);
        [DllImport("hid.dll", SetLastError = true)] public static extern bool HidD_GetFeature(SafeFileHandle h, byte[] buf, int len);
        [DllImport("setupapi.dll", SetLastError = true)] static extern IntPtr SetupDiGetClassDevs(ref Guid g, IntPtr enumerator, IntPtr parent, int flags);
        [DllImport("setupapi.dll", SetLastError = true)] static extern bool SetupDiEnumDeviceInterfaces(IntPtr set, IntPtr devInfo, ref Guid g, int index, ref SP_DEVICE_INTERFACE_DATA data);
        [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Unicode)] static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr set, ref SP_DEVICE_INTERFACE_DATA data, IntPtr detail, int size, out int required, ref SP_DEVINFO_DATA devInfo);
        [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Unicode)] static extern bool SetupDiGetDeviceInstanceId(IntPtr set, ref SP_DEVINFO_DATA devInfo, StringBuilder id, int size, out int required);
        [DllImport("setupapi.dll")] static extern bool SetupDiDestroyDeviceInfoList(IntPtr set);
        [DllImport("cfgmgr32.dll")] static extern int CM_Get_Parent(out int parent, int devInst, int flags);
        [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode, EntryPoint = "CM_Get_DevNode_Registry_PropertyW")]
        static extern int CM_Get_DevNode_Registry_Property(int devInst, int prop, out int type, byte[] buf, ref int len, int flags);
        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)] public static extern SafeFileHandle CreateFile(string name, uint access, uint share, IntPtr sec, uint disposition, uint flags, IntPtr template);
        [DllImport("kernel32.dll", SetLastError = true)] public static extern bool WriteFile(SafeFileHandle h, byte[] buf, int len, out int written, IntPtr overlapped);
        [DllImport("kernel32.dll", SetLastError = true)] public static extern bool ReadFile(SafeFileHandle h, byte[] buf, int len, out int read, IntPtr overlapped);
        [DllImport("kernel32.dll", SetLastError = true)] public static extern bool CancelIoEx(SafeFileHandle h, IntPtr overlapped);

        public static SafeFileHandle Open(string path, uint access)
        {
            return CreateFile(path, access, 3 /* FILE_SHARE_READ | FILE_SHARE_WRITE */, IntPtr.Zero, 3 /* OPEN_EXISTING */, 0, IntPtr.Zero);
        }

        public static bool GetCaps(SafeFileHandle h, out int inLen, out int outLen)
        {
            int featLen;
            return GetCaps(h, out inLen, out outLen, out featLen);
        }

        public static bool GetCaps(SafeFileHandle h, out int inLen, out int outLen, out int featLen)
        {
            inLen = outLen = featLen = 0; IntPtr pd;
            if (!HidD_GetPreparsedData(h, out pd)) return false;
            HIDP_CAPS caps = new HIDP_CAPS(); caps.Reserved = new ushort[17];
            HidP_GetCaps(pd, ref caps);
            HidD_FreePreparsedData(pd);
            inLen = caps.InputReportByteLength; outLen = caps.OutputReportByteLength; featLen = caps.FeatureReportByteLength;
            return true;
        }

        public static List<HidDeviceEntry> EnumerateHid()
        {
            List<HidDeviceEntry> res = new List<HidDeviceEntry>();
            Guid g; HidD_GetHidGuid(out g);
            IntPtr set = SetupDiGetClassDevs(ref g, IntPtr.Zero, IntPtr.Zero, DIGCF_PRESENT | DIGCF_DEVICEINTERFACE);
            if (set == IntPtr.Zero || set == new IntPtr(-1)) return res;
            try
            {
                for (int idx = 0; ; idx++)
                {
                    SP_DEVICE_INTERFACE_DATA d = new SP_DEVICE_INTERFACE_DATA(); d.cbSize = Marshal.SizeOf(d);
                    if (!SetupDiEnumDeviceInterfaces(set, IntPtr.Zero, ref g, idx, ref d)) break;
                    SP_DEVINFO_DATA info = new SP_DEVINFO_DATA(); info.cbSize = Marshal.SizeOf(info);
                    int req;
                    SetupDiGetDeviceInterfaceDetail(set, ref d, IntPtr.Zero, 0, out req, ref info);
                    if (req <= 0) continue;
                    IntPtr buf = Marshal.AllocHGlobal(req);
                    try
                    {
                        Marshal.WriteInt32(buf, IntPtr.Size == 8 ? 8 : 6);
                        if (!SetupDiGetDeviceInterfaceDetail(set, ref d, buf, req, out req, ref info)) continue;
                        HidDeviceEntry e = new HidDeviceEntry();
                        e.Path = Marshal.PtrToStringUni(new IntPtr(buf.ToInt64() + 4));
                        e.DevInst = info.DevInst;
                        StringBuilder sb = new StringBuilder(512);
                        if (SetupDiGetDeviceInstanceId(set, ref info, sb, sb.Capacity, out req)) e.InstanceId = sb.ToString();
                        res.Add(e);
                    }
                    finally { Marshal.FreeHGlobal(buf); }
                }
            }
            finally { SetupDiDestroyDeviceInfoList(set); }
            return res;
        }

        // Whether the device hangs (through its ancestors) off the ViGEmBus bus — i.e. it's our own virtual controller.
        public static bool IsOnViGEmBus(int devInst)
        {
            int cur = devInst;
            for (int depth = 0; depth < 8; depth++)
            {
                string svc = GetService(cur);
                if (svc != null && svc.Equals("ViGEmBus", StringComparison.OrdinalIgnoreCase)) return true;
                int parent;
                if (CM_Get_Parent(out parent, cur, 0) != 0) break;
                cur = parent;
            }
            return false;
        }

        static string GetService(int devInst)
        {
            byte[] buf = new byte[512]; int len = buf.Length, type;
            if (CM_Get_DevNode_Registry_Property(devInst, CM_DRP_SERVICE, out type, buf, ref len, 0) != 0) return null;
            return Encoding.Unicode.GetString(buf, 0, Math.Max(0, len)).TrimEnd('\0');
        }
    }
}
