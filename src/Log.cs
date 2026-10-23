// Error log: %APPDATA%\Glowstick\log.txt (up to 1 MB, then — log.old.txt). Users attach it to issues.
// We write only what's worth knowing when investigating problems: errors, connects/disconnects, game mode.
using System;
using System.IO;
using System.Text;
using System.Threading;

namespace Glowstick
{
    internal static class Log
    {
        static readonly object lk = new object();
        static string Dir { get { return Showcase.On ? Showcase.LogDir : Settings.Dir; } }   // the showcase never writes into the real log
        public static string FilePath { get { return Path.Combine(Dir, "log.txt"); } }

        public static void Info(string msg) { Write(msg); }

        public static void Error(string where, Exception e)
        {
            Write("ERROR " + where + ": " + (e == null ? "" : e.GetType().Name + ": " + e.Message + Environment.NewLine + e.StackTrace));
        }

        static void Write(string msg)
        {
            lock (lk)
            {
                try
                {
                    Directory.CreateDirectory(Dir);
                    string path = FilePath;
                    FileInfo fi = new FileInfo(path);
                    if (fi.Exists && fi.Length > 1024 * 1024)
                    {
                        string old = Path.Combine(Dir, "log.old.txt");
                        if (File.Exists(old)) File.Delete(old);
                        File.Move(path, old);
                    }
                    File.AppendAllText(path, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + " [" +
                        (Thread.CurrentThread.Name ?? Thread.CurrentThread.ManagedThreadId.ToString()) + "] " + msg + Environment.NewLine, Encoding.UTF8);
                }
                catch { }   // the log must never crash the app
            }
        }
    }
}
