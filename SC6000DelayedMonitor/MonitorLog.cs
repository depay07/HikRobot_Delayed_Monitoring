using System;
using System.Diagnostics;
using System.IO;

namespace SC6000DelayedMonitor
{
    internal static class MonitorLog
    {
        private static readonly object Gate = new object();
        public static void Write(string message)
        {
            // Separate files per viewer avoid file-lock contention. Never log passwords.
            lock (Gate)
            {
                try
                {
                    string dir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs");
                    Directory.CreateDirectory(dir);
                    string path = Path.Combine(dir, "monitor-" + Process.GetCurrentProcess().Id + ".log");
                    if (File.Exists(path) && new FileInfo(path).Length > 1024 * 1024)
                        File.WriteAllText(path, "Log rotated\r\n");
                    File.AppendAllText(path, DateTime.Now.ToString("s") + " " + message + Environment.NewLine);
                }
                catch (Exception ex) { Trace.WriteLine(ex.Message); }
            }
        }
    }
}
