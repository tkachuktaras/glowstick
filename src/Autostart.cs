// Runs with administrator rights without UAC — through Task Scheduler tasks (Task Scheduler COM API):
//   "Glowstick"     — autostart at Windows logon (in the tray), toggled by a checkbox;
//   "Glowstick UI"  — no trigger; a normal (non-admin) launch of the exe restarts itself elevated through it.
// Task priority is normal (Windows defaults to lowered) and there are no battery or run-time limits.
// The tasks start the protected copy in Program Files (Installer), never an exe a normal user could replace.
using System;
using System.IO;
using System.Security;
using System.Security.Principal;
using System.Windows.Forms;

namespace Glowstick
{
    internal static class Autostart
    {
        public const string LogonTask = "Glowstick", UiTask = "Glowstick UI";
        const int TASK_CREATE_OR_UPDATE = 6, TASK_LOGON_INTERACTIVE_TOKEN = 3;

        // The exe the tasks start: the protected copy. Set by the elevated start (Installer.Ensure); null — it couldn't be
        // made, so no task is created.
        public static string Target = Installer.Exe;

        static dynamic Folder()
        {
            dynamic svc = Activator.CreateInstance(Type.GetTypeFromProgID("Schedule.Service"));
            svc.Connect();
            return svc.GetFolder("\\");
        }

        static dynamic Get(string name)
        {
            try { return Folder().GetTask(name); } catch { return null; }
        }

        public static bool Exists(string name) { return Get(name) != null; }

        // The task exists and points to the protected copy.
        public static bool PointsHere(string name)
        {
            dynamic t = Get(name);
            if (t == null || Target == null) return false;
            try
            {
                string xml = t.Xml;
                return xml.IndexOf("<Command>" + SecurityElement.Escape(Target) + "</Command>", StringComparison.OrdinalIgnoreCase) >= 0;
            }
            catch { return false; }
        }

        public static bool Create(string name, bool atLogon, string args)
        {
            if (Target == null) return false;
            try
            {
                string user = WindowsIdentity.GetCurrent().User.Value;
                string trigger = atLogon ? "<LogonTrigger><UserId>" + user + "</UserId><Delay>PT3S</Delay></LogonTrigger>" : "";
                string xml =
                    "<?xml version=\"1.0\" encoding=\"UTF-16\"?>\r\n" +
                    "<Task version=\"1.2\" xmlns=\"http://schemas.microsoft.com/windows/2004/02/mit/task\">" +
                    "<RegistrationInfo><Description>Glowstick — DualShock 4 / DualSense light bar</Description></RegistrationInfo>" +
                    "<Triggers>" + trigger + "</Triggers>" +
                    "<Principals><Principal id=\"Author\"><UserId>" + user + "</UserId><LogonType>InteractiveToken</LogonType><RunLevel>HighestAvailable</RunLevel></Principal></Principals>" +
                    "<Settings><MultipleInstancesPolicy>Parallel</MultipleInstancesPolicy>" +
                    "<DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries><StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>" +
                    "<AllowHardTerminate>true</AllowHardTerminate><StartWhenAvailable>false</StartWhenAvailable>" +
                    "<IdleSettings><StopOnIdleEnd>false</StopOnIdleEnd><RestartOnIdle>false</RestartOnIdle></IdleSettings>" +
                    "<AllowStartOnDemand>true</AllowStartOnDemand><Enabled>true</Enabled><Hidden>false</Hidden>" +
                    "<ExecutionTimeLimit>PT0S</ExecutionTimeLimit><Priority>4</Priority></Settings>" +
                    "<Actions Context=\"Author\"><Exec><Command>" + SecurityElement.Escape(Target) + "</Command>" +
                    (string.IsNullOrEmpty(args) ? "" : "<Arguments>" + SecurityElement.Escape(args) + "</Arguments>") +
                    "</Exec></Actions></Task>";
                Folder().RegisterTask(name, xml, TASK_CREATE_OR_UPDATE, null, null, TASK_LOGON_INTERACTIVE_TOKEN, null);
                return true;
            }
            catch { return false; }
        }

        public static void Delete(string name) { try { Folder().DeleteTask(name, 0); } catch { } }

        public static bool RunTask(string name)
        {
            dynamic t = Get(name);
            if (t == null) return false;
            try { t.Run(null); return true; } catch { return false; }
        }

        // The app's former name is DS4 Colors: its tasks and autostart shortcut are moved to the new ones.
        public const string OldLogonTask = "DS4 Colors", OldUiTask = "DS4 Colors UI";

        public static void MigrateOld()
        {
            bool wasAuto = Exists(OldLogonTask);
            string lnk = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Startup), "DS4 Colors.lnk");
            if (File.Exists(lnk)) { wasAuto = true; try { File.Delete(lnk); } catch { } }
            if (wasAuto && !Exists(LogonTask)) Create(LogonTask, true, "-Tray");
            Delete(OldLogonTask);
            Delete(OldUiTask);
        }
    }
}
