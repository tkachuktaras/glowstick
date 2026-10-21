// HidHide: hides the real controllers from games/Steam; only this exe can see them.
// Handles opened BEFORE hiding keep working, so after hiding the device is restarted
// (pnputil /restart-device) — otherwise Steam/the game would still see the real controller next to the virtual one.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace Glowstick
{
    internal static class HidHide
    {
        static readonly string Cli = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            @"Nefarius Software Solutions\HidHide\x64\HidHideCLI.exe");

        static readonly HashSet<string> hidden = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        static bool appRegistered;
        public static bool CloakActive { get; private set; }

        public static bool Installed { get { return File.Exists(Cli); } }

        public const int Failed = -1, AlreadyHidden = 0, NewlyHidden = 1;

        // Hides a device. NewlyHidden — just hidden, the device must be restarted.
        public static int Hide(string instanceId)
        {
            if (hidden.Contains(instanceId) && CloakActive) return AlreadyHidden;
            StringBuilder args = new StringBuilder();
            if (!appRegistered) args.Append("--app-reg ").Append(Quote(Application.ExecutablePath)).Append(' ');
            args.Append("--dev-hide ").Append(Quote(instanceId)).Append(" --cloak-on");
            if (Run(Cli, args.ToString()) != 0) return Failed;
            appRegistered = true; CloakActive = true;
            hidden.Add(instanceId);
            return NewlyHidden;
        }

        // Turn hiding off and restart the devices so games see the real controllers right away.
        public static void Release()
        {
            if (!CloakActive) return;
            Run(Cli, "--cloak-off");
            CloakActive = false;
            foreach (string id in hidden) RestartDevice(id);
            hidden.Clear();
        }

        public static void RestartDevice(string instanceId)
        {
            Run(Path.Combine(Environment.SystemDirectory, "pnputil.exe"), "/restart-device " + Quote(instanceId));
        }

        static string Quote(string s) { return "\"" + s + "\""; }

        static int Run(string exe, string args)
        {
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo(exe, args);
                psi.UseShellExecute = false; psi.CreateNoWindow = true;
                psi.RedirectStandardOutput = true; psi.RedirectStandardError = true;
                using (Process p = Process.Start(psi))
                {
                    p.StandardOutput.ReadToEnd(); p.StandardError.ReadToEnd();
                    if (!p.WaitForExit(15000)) { try { p.Kill(); } catch { } return -1; }
                    return p.ExitCode;
                }
            }
            catch { return -1; }
        }
    }
}
