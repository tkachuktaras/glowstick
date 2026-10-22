// Glowstick runs elevated (game mode needs it), but what it starts for the user — a link from the About box, a program
// from a "launch" special action — must not inherit administrator rights. So such launches go through the desktop's own
// Explorer process: we ask the shell view of the desktop for its Shell.Application object and call its ShellExecute, which
// runs inside Explorer, with the user's normal rights. (The technique Raymond Chen describes in "How can I launch an
// unelevated process from my elevated process and vice versa?")
using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Glowstick
{
    internal static class Shell
    {
        // Starts file (a program, a document or a URL) with arguments as the normal user. Falls back to explorer.exe "file"
        // (also unelevated, but without arguments); never to an elevated start.
        public static bool RunAsUser(string file, string args)
        {
            if (string.IsNullOrEmpty(file)) return false;
            if (System.Threading.Thread.CurrentThread.GetApartmentState() != System.Threading.ApartmentState.STA)
            {
                bool result = false;   // the shell objects want an STA thread
                System.Threading.Thread th = new System.Threading.Thread(() => result = RunAsUser(file, args)) { IsBackground = true, Name = "shell start" };
                th.SetApartmentState(System.Threading.ApartmentState.STA);
                th.Start();
                th.Join(10000);
                return result;
            }
            try
            {
                dynamic app = DesktopApplication();
                app.ShellExecute(file, args ?? "", "", "open", 1);
                return true;
            }
            catch (Exception e)
            {
                Log.Error("unelevated start", e);
                if (!string.IsNullOrEmpty(args)) return false;
                try { Process.Start(new ProcessStartInfo("explorer.exe", "\"" + file.Replace("\"", "") + "\"") { UseShellExecute = false }); return true; }
                catch (Exception e2) { Log.Error("explorer start", e2); return false; }
            }
        }

        // "C:\Some Dir\app.exe" -x  /  app.exe -x  /  https://…  →  (file, args)
        public static void Split(string command, out string file, out string args)
        {
            command = (command ?? "").Trim();
            file = command; args = "";
            if (command.StartsWith("\""))
            {
                int end = command.IndexOf('"', 1);
                if (end > 0) { file = command.Substring(1, end - 1); args = command.Substring(end + 1).Trim(); }
                return;
            }
            // Unquoted: the first space ends the program — unless the whole thing is an existing path with spaces
            if (System.IO.File.Exists(command) || command.Contains("://")) return;
            int sp = command.IndexOf(' ');
            if (sp > 0) { file = command.Substring(0, sp); args = command.Substring(sp + 1).Trim(); }
        }

        const int SWC_DESKTOP = 8, SWFO_NEEDDISPATCH = 1, SVGIO_BACKGROUND = 0;
        static readonly Guid SID_STopLevelBrowser = new Guid("4C96BE40-915C-11CF-99D3-00AA004AE837");

        static object DesktopApplication()
        {
            IShellWindows windows = (IShellWindows)Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("9BA05972-F6A8-11CF-A442-00A0C90A8F39")));
            object loc = 0, root = Missing();   // CSIDL_DESKTOP
            int hwnd;
            object disp = windows.FindWindowSW(ref loc, ref root, SWC_DESKTOP, out hwnd, SWFO_NEEDDISPATCH);
            IServiceProvider sp = (IServiceProvider)disp;
            Guid sid = SID_STopLevelBrowser, iid = typeof(IShellBrowser).GUID;
            IShellBrowser browser = (IShellBrowser)sp.QueryService(ref sid, ref iid);
            IShellView view = browser.QueryActiveShellView();
            Guid dispIid = new Guid("00020400-0000-0000-C000-000000000046");   // IDispatch → IShellFolderViewDual
            dynamic folderView = view.GetItemObject(SVGIO_BACKGROUND, ref dispIid);
            return folderView.Application;
        }

        static object Missing() { return System.Reflection.Missing.Value; }

        // ---- the COM interfaces, declared only up to the methods used (earlier slots keep the vtable order) ----
        [ComImport, Guid("85CB6900-4D95-11CF-960C-0080C7F4EE85"), InterfaceType(ComInterfaceType.InterfaceIsIDispatch)]
        interface IShellWindows
        {
            [DispId(0x60020008)]
            [return: MarshalAs(UnmanagedType.IDispatch)]
            object FindWindowSW([In] ref object pvarLoc, [In] ref object pvarLocRoot, int swClass, out int pHWND, int swfwOptions);
        }

        [ComImport, Guid("6D5140C1-7436-11CE-8034-00AA006009FA"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IServiceProvider
        {
            [return: MarshalAs(UnmanagedType.IUnknown)]
            object QueryService(ref Guid guidService, ref Guid riid);
        }

        [ComImport, Guid("000214E2-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IShellBrowser
        {
            void GetWindow(); void ContextSensitiveHelp(); void InsertMenusSB(); void SetMenuSB(); void RemoveMenusSB();
            void SetStatusTextSB(); void EnableModelessSB(); void TranslateAcceleratorSB(); void BrowseObject();
            void GetViewStateStream(); void GetControlWindow(); void SendControlMsg();
            IShellView QueryActiveShellView();
        }

        [ComImport, Guid("000214E3-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IShellView
        {
            void GetWindow(); void ContextSensitiveHelp(); void TranslateAccelerator(); void EnableModeless(); void UIActivate();
            void Refresh(); void CreateViewWindow(); void DestroyViewWindow(); void GetCurrentInfo(); void AddPropertySheetPages();
            void SaveViewState(); void SelectItem();
            [return: MarshalAs(UnmanagedType.Interface)]
            object GetItemObject(uint uItem, ref Guid riid);
        }
    }
}
