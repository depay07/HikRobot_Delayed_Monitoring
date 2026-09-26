using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Windows.Forms;
using SC6000DelayedMonitor;

internal static class BufferTests
{
    static void Check(bool value, string why) { if (!value) throw new Exception(why); }
    static InspectionResult Result(int sequence)
    {
        using (var borrowed = new Bitmap(12, 8))
        {
            borrowed.SetPixel(0, 0, Color.FromArgb(sequence % 255, 20, 30));
            return new InspectionResult(sequence, new Bitmap(borrowed), sequence % 2 == 0);
        }
    }
    [STAThread] static void Main()
    {
        try
        {
            foreach (int delay in new[] { 0, 1, 14 })
            {
                using (var queue = new InspectionBuffer(delay))
                {
                    for (int current = 1; current <= 1000; current++)
                    {
                        queue.Push(Result(current));
                        Check(queue.Count == Math.Min(current, delay), "Queue size");
                        using (var shown = queue.Take())
                        {
                            if (current <= delay) Check(shown == null, "Premature display");
                            else
                            {
                                Check(shown != null && shown.SequenceNo == current - delay, "Off-by-one");
                                Check(shown.IsOK == ((current - delay) % 2 == 0), "Result/image mismatch");
                                Check(shown.Image.GetPixel(0, 0).R == (current - delay) % 255, "Image changed after source dispose");
                            }
                        }
                    }
                }
                Console.WriteLine("PASS delay=" + delay + ": 1000 inspections, exact sequence/result/image");
            }
            using (var queue = new InspectionBuffer(14))
            {
                var all = new List<InspectionResult>();
                // Do not consume UI updates: memory must remain delay+1, not 1000 images.
                for (int i = 1; i <= 1000; i++) { var r = Result(i); all.Add(r); queue.Push(r); }
                Check(queue.Count == 14, "Unbounded queue");
                int retained = 0; foreach (var r in all) if (r.Image != null) retained++;
                Check(retained == 15, "Pending UI snapshots accumulated");
                using (var latest = queue.Take()) Check(latest.SequenceNo == 986, "UI coalescing changed delay");
                queue.Reset();
                foreach (var r in all) Check(r.Image == null, "Reset leaked image");
                queue.Push(Result(1)); Check(queue.Take() == null, "Reset mixed sessions");
            }
            Console.WriteLine("PASS bounded UI backlog and reset/dispose ownership");
            string dir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            foreach (string input in new[] { "14", "0", "-1", "invalid", "2147483648", "" })
            {
                string file = Path.Combine(dir, "config-test.ini");
                File.WriteAllText(file, "CAMERAS=1\nDELAY_COUNT=" + input + "\n[CAMERA1]\nIP=192.0.2.10\n");
                var config = IniConfig.Load(file);
                Check(config.DelayCount == (input == "14" ? 14 : 0), "Config fallback " + input);
                Check(config.GetCamera(1).DelayCount == config.DelayCount, "Camera delay propagation");
            }
            Console.WriteLine("PASS invalid/missing/negative DELAY_COUNT -> 0");
            using (var pane = new DelayedPane("CAMERA 1", 14))
            {
                var r = Result(12345); pane.ShowResult(r);
                pane.ShowStatus("Disconnected", true); Check(r.Image == null, "UI retained old session image");
                var r2 = Result(12346); pane.ShowResult(r2); pane.Dispose();
                Check(r2.Image == null, "UI dispose leak");
            }
            Console.WriteLine("PASS displayed image disposed on reset and close");
            // Exercise resets with a real SDK singleton, but no remote connection or Run call.
            VM.Core.VmSolution.SetControlMode(VM.Core.ControlModeType.REMOTE);
            using (var dispatcher = new Form())
            using (var pane = new DelayedPane("TEST", 14))
            {
                var handle = dispatcher.Handle;
                using (var session = new InspectionSession(new CameraSettings { DelayCount = 14 }, dispatcher, pane))
                {
                    var flags = BindingFlags.Instance | BindingFlags.NonPublic;
                    var buffer = (InspectionBuffer)typeof(InspectionSession).GetField("_buffer", flags).GetValue(session);
                    var held = Result(1); buffer.Push(held);
                    var previous = Result(2); pane.ShowResult(previous);
                    session.Reset("Disconnected");
                    Application.DoEvents();
                    Check(held.Image == null && previous.Image == null, "Session reset retained old images");
                    Check(buffer.Count == 0, "Session queue not empty");
                    session.Bind(); Application.DoEvents(); // Blank mapping must not try a guessed Procedure.
                    Check(typeof(InspectionSession).GetField("_procedure", flags).GetValue(session) == null, "Guessed Procedure");
                }
                Application.DoEvents(); // A queued drain after Dispose must safely do nothing.
            }
            Console.WriteLine("PASS session reset, stale UI work after dispose, and missing mapping");
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); Environment.ExitCode = 1; }
    }
}
