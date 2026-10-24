// Game mode drivers (ViGEmBus, HidHide): the official installers are embedded in the exe and installed with one button.
using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;
using System.ServiceProcess;
using Microsoft.Win32;

namespace Glowstick
{
    internal static class Drivers
    {
        class Package { public string Name, Service, Resource, DisplayName; }

        static readonly Package[] Packages =
        {
            new Package { Name = "ViGEmBus", Service = "ViGEmBus", Resource = "ViGEmBus_setup.exe", DisplayName = "ViGEm Bus Driver" },
            new Package { Name = "HidHide", Service = "HidHide", Resource = "HidHide_setup.exe", DisplayName = "HidHide" },
        };

        // The driver service is marked for deletion (after uninstall) — until a reboot the driver neither works nor installs
        static bool PendingDelete(string service)
        {
            using (RegistryKey k = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\" + service))
                return k != null && Convert.ToInt32(k.GetValue("DeleteFlag", 0)) != 0;
        }

        // Installed = listed in “Programs and Features” and not pending deletion.
        // (The service entry itself proves nothing: after uninstall it stays until a reboot.)
        static bool IsInstalled(Package p) { return FindProductCodes(p.DisplayName).Count > 0 && !PendingDelete(p.Service); }

        public static bool AllInstalled
        {
            get { foreach (Package p in Packages) if (!IsInstalled(p)) return false; return HidHide.Installed; }
        }

        public static bool RebootPending
        {
            get { foreach (Package p in Packages) if (PendingDelete(p.Service)) return true; return false; }
        }

        // The driver is loaded right now. Right after installing, a driver can be installed but not loaded until a reboot.
        // HidHide is a filter: it starts when it attaches to the first device stack (our controller is restarted after hiding).
        public static bool Running(string service)
        {
            try { using (ServiceController sc = new ServiceController(service)) return sc.Status == ServiceControllerStatus.Running; }
            catch { return false; }   // no such service
        }

        // Whether the installers are embedded (an exe built without them can't install drivers)
        public static bool Bundled
        {
            get
            {
                foreach (Package p in Packages)
                    if (Assembly.GetExecutingAssembly().GetManifestResourceInfo(p.Resource) == null) return false;
                return true;
            }
        }

        // Installs the missing drivers (the app already runs as administrator). Returns an error text or null.
        public static string InstallMissing(out bool rebootNeeded)
        {
            rebootNeeded = false;
            if (RebootPending) { rebootNeeded = true; return Lang.T("the drivers are still being removed — restart the computer first, then install again"); }
            string dir = Path.Combine(Path.GetTempPath(), "Glowstick_drivers");
            Directory.CreateDirectory(dir);
            try
            {
                foreach (Package p in Packages)
                {
                    if (IsInstalled(p) && (p.Service != "HidHide" || HidHide.Installed)) continue;
                    string file = Path.Combine(dir, p.Resource);
                    using (Stream s = Assembly.GetExecutingAssembly().GetManifestResourceStream(p.Resource))
                    {
                        if (s == null) return Lang.F("the {0} installer is not embedded in this exe", p.Name);
                        using (FileStream fs = File.Create(file)) s.CopyTo(fs);
                    }
                    // Held open (readable, not writable by others) from the signature check until the installer runs: what was
                    // checked in %TEMP% can't be swapped for something else before it starts, this app being an administrator
                    using (new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read))
                    {
                    if (!SignedByNefarius(file)) return Lang.F("the {0} installer signature doesn't match", p.Name);

                    // Silent: no installer windows — the app's own button shows the progress (UAC was already accepted on start)
                    ProcessStartInfo psi = new ProcessStartInfo(file, "/quiet /norestart");
                    psi.UseShellExecute = false;
                    using (Process proc = Process.Start(psi))
                    {
                        proc.WaitForExit();
                        if (proc.ExitCode == 3010 || proc.ExitCode == 1641) rebootNeeded = true;
                        else if (proc.ExitCode != 0) return Lang.F("{0}: the installer exited with code {1}", p.Name, proc.ExitCode);
                    }
                    }
                }
                return null;
            }
            catch (Exception e) { return e.Message; }
            finally { try { Directory.Delete(dir, true); } catch { } }
        }

        // Removes the drivers through their MSI uninstallers (found by “Programs and Features” entries).
        // Stop the engine first — to turn hiding off and disconnect the virtual controllers.
        public static string UninstallAll(out bool rebootNeeded)
        {
            rebootNeeded = false;
            string err = null;
            System.Collections.Generic.List<string> codes = new System.Collections.Generic.List<string>();
            foreach (Package p in Packages) codes.AddRange(FindProductCodes(p.DisplayName));
            foreach (string code in codes)
            {
                try
                {
                    ProcessStartInfo psi = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "msiexec.exe"), "/x " + code + " /quiet /norestart");
                    psi.UseShellExecute = false;
                    using (Process proc = Process.Start(psi))
                    {
                        proc.WaitForExit();
                        if (proc.ExitCode == 3010 || proc.ExitCode == 1641) rebootNeeded = true;
                        else if (proc.ExitCode != 0 && proc.ExitCode != 1605 /* already not installed */) err = Lang.F("the uninstaller exited with code {0}", proc.ExitCode);
                    }
                }
                catch (Exception e) { err = e.Message; }
            }
            if (RebootPending) rebootNeeded = true;   // MSI often returns 0 although the service is removed only after a reboot
            return err;
        }

        // MSI product codes by Nefarius whose name starts with displayName (“Programs and Features”)
        static System.Collections.Generic.List<string> FindProductCodes(string displayName)
        {
            System.Collections.Generic.List<string> codes = new System.Collections.Generic.List<string>();
            foreach (string root in new[] { @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall", @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall" })
            {
                using (RegistryKey k = Registry.LocalMachine.OpenSubKey(root))
                {
                    if (k == null) continue;
                    foreach (string sub in k.GetSubKeyNames())
                    {
                        if (!sub.StartsWith("{") || codes.Contains(sub)) continue;
                        using (RegistryKey e = k.OpenSubKey(sub))
                        {
                            string name = e == null ? null : e.GetValue("DisplayName") as string;
                            string pub = e == null ? null : e.GetValue("Publisher") as string;
                            if (name == null || pub == null || pub.IndexOf("Nefarius", StringComparison.OrdinalIgnoreCase) < 0) continue;
                            if (name.StartsWith(displayName, StringComparison.OrdinalIgnoreCase)) codes.Add(sub);
                        }
                    }
                }
            }
            return codes;
        }

        static bool SignedByNefarius(string file) { return SignedBy(file, "Nefarius Software Solutions"); }

        // The signature is valid (as Windows itself checks it, timestamp included) and issued to a subject containing `who`
        internal static bool SignedBy(string file, string who)
        {
            try
            {
                X509Certificate cert = X509Certificate.CreateFromSignedFile(file);
                if (cert.Subject.IndexOf(who, StringComparison.OrdinalIgnoreCase) < 0) return false;
            }
            catch { return false; }

            WINTRUST_FILE_INFO fi = new WINTRUST_FILE_INFO();
            fi.cbStruct = Marshal.SizeOf(fi); fi.pcwszFilePath = file;
            IntPtr pFile = Marshal.AllocHGlobal(fi.cbStruct);
            try
            {
                Marshal.StructureToPtr(fi, pFile, false);
                WINTRUST_DATA wd = new WINTRUST_DATA();
                wd.cbStruct = Marshal.SizeOf(wd); wd.dwUIChoice = 2 /* WTD_UI_NONE */; wd.dwUnionChoice = 1 /* WTD_CHOICE_FILE */; wd.pFile = pFile;
                Guid action = new Guid("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");   // WINTRUST_ACTION_GENERIC_VERIFY_V2
                return WinVerifyTrust(IntPtr.Zero, ref action, ref wd) == 0;
            }
            finally { Marshal.DestroyStructure(pFile, typeof(WINTRUST_FILE_INFO)); Marshal.FreeHGlobal(pFile); }
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct WINTRUST_FILE_INFO { public int cbStruct; [MarshalAs(UnmanagedType.LPWStr)] public string pcwszFilePath; public IntPtr hFile, pgKnownSubject; }

        [StructLayout(LayoutKind.Sequential)]
        struct WINTRUST_DATA
        {
            public int cbStruct; public IntPtr pPolicyCallbackData, pSIPClientData;
            public int dwUIChoice, fdwRevocationChecks, dwUnionChoice; public IntPtr pFile;
            public int dwStateAction; public IntPtr hWVTStateData, pwszURLReference;
            public int dwProvFlags, dwUIContext; public IntPtr pSignatureSettings;
        }

        [DllImport("wintrust.dll", CharSet = CharSet.Unicode)]
        static extern int WinVerifyTrust(IntPtr hwnd, ref Guid action, ref WINTRUST_DATA data);
    }
}
